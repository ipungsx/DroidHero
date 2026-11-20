using System;
using System.Collections.Generic;
using System.Text;

namespace DroidHero.Chart
{
    /// <summary>
    /// Parser for Rock Band style "notes.mid" files.
    ///
    /// Guitar notes live on the PART GUITAR / PART LEAD / T1 GEMS track. Each difficulty owns a
    /// 12-semitone block: Easy 60-64, Medium 72-76, Hard 84-88, Expert 96-100. Note 116 marks a
    /// star power phrase, and note 103 is the star power fill activator.
    /// </summary>
    public static class MidiParser
    {
        static readonly string[] GuitarTracks = { "PART GUITAR", "PART LEAD", "T1 GEMS" };
        static readonly string[] DrumTracks = { "PART DRUMS", "PART DRUM" };

        const int EasyBase = 60;
        const int SemitonesPerDifficulty = 12;
        const int StarPowerNote = 116;

        public static SongChart Parse(byte[] data, string difficulty = "Expert")
        {
            if (data == null || data.Length < 14) throw new ArgumentException("MIDI data too short.");

            int pos = 0;
            if (ReadTag(data, ref pos) != "MThd") throw new ArgumentException("Not a MIDI file (missing MThd).");

            int headerLength = ReadInt32BE(data, ref pos);
            int headerEnd = pos + headerLength;
            ReadInt16BE(data, ref pos);                       // format
            int trackCount = ReadInt16BE(data, ref pos);
            int division = ReadInt16BE(data, ref pos);
            pos = headerEnd;

            var chart = new SongChart { Resolution = Math.Max(1, division) };
            chart.Difficulty = difficulty;

            int noteBase = EasyBase + ChartDifficulty.IndexOf(difficulty) * SemitonesPerDifficulty;

            for (int t = 0; t < trackCount; t++)
            {
                if (pos + 8 > data.Length) break;

                string tag = ReadTag(data, ref pos);
                int length = ReadInt32BE(data, ref pos);
                int trackEnd = Math.Min(pos + length, data.Length);

                if (tag == "MTrk")
                {
                    ParseTrack(data, pos, trackEnd, chart, noteBase);
                }

                pos = trackEnd;
            }

            if (chart.Tempos.Count == 0) chart.AddTempo(0, 120f);

            ApplyStarPower(chart);
            chart.FinalizeChart();
            return chart;
        }

        static bool IsGuitarTrack(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < GuitarTracks.Length; i++)
            {
                if (string.Equals(name, GuitarTracks[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool IsDrumTrack(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < DrumTracks.Length; i++)
            {
                if (string.Equals(name, DrumTracks[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        struct NoteSpan
        {
            public int StartTick;
            public int EndTick;
            public int Note;
        }

        static void ParseTrack(byte[] data, int start, int end, SongChart chart, int noteBase)
        {
            int pos = start;
            int tick = 0;
            int runningStatus = 0;
            string trackName = null;

            // Notes are buffered until the track ends, because the track-name meta event can
            // appear anywhere in the track and decides whether these notes are guitar notes.
            var spans = new List<NoteSpan>();
            var openNotes = new Dictionary<int, int>();

            while (pos < end)
            {
                tick += ReadVarInt(data, ref pos);
                if (pos >= end) break;

                int status = data[pos];
                if (status < 0x80)
                {
                    status = runningStatus;
                }
                else
                {
                    pos++;
                    if (status < 0xF0) runningStatus = status;
                }

                if (status == 0xFF)
                {
                    int metaType = data[pos++];
                    int metaLength = ReadVarInt(data, ref pos);
                    int available = Math.Min(metaLength, end - pos);

                    if (metaType == 0x03 && available > 0)
                    {
                        trackName = Encoding.UTF8.GetString(data, pos, available).Trim();
                    }
                    else if (metaType == 0x51 && available == 3)
                    {
                        int microsPerQuarter = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                        if (microsPerQuarter > 0) chart.AddTempo(tick, 60000000f / microsPerQuarter);
                    }

                    pos += metaLength;
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int sysexLength = ReadVarInt(data, ref pos);
                    pos += sysexLength;
                    continue;
                }

                int type = status & 0xF0;

                if (type == 0x90 || type == 0x80)
                {
                    int note = data[pos++];
                    int velocity = data[pos++];
                    bool noteOn = type == 0x90 && velocity > 0;

                    if (noteOn)
                    {
                        openNotes[note] = tick;
                    }
                    else if (openNotes.TryGetValue(note, out int noteStart))
                    {
                        openNotes.Remove(note);
                        spans.Add(new NoteSpan { StartTick = noteStart, EndTick = tick, Note = note });
                    }
                    continue;
                }

                pos += (type == 0xC0 || type == 0xD0) ? 1 : 2;
            }

            foreach (var pair in openNotes)
            {
                spans.Add(new NoteSpan { StartTick = pair.Value, EndTick = tick, Note = pair.Key });
            }

            if (IsDrumTrack(trackName))
            {
                AddDrumHits(chart, spans);
                return;
            }

            if (!IsGuitarTrack(trackName)) return;

            for (int i = 0; i < spans.Count; i++)
            {
                NoteSpan span = spans[i];
                AddGuitarNote(chart, span.Note, span.StartTick, span.EndTick - span.StartTick, noteBase);
            }
        }

        /// <summary>
        /// Feeds drum onsets to the light show. The pad number is irrelevant, but the two
        /// authoring markers (star power and the fill activator) are not hits and are skipped.
        /// </summary>
        static void AddDrumHits(SongChart chart, List<NoteSpan> spans)
        {
            for (int i = 0; i < spans.Count; i++)
            {
                int note = spans[i].Note;
                if (note == StarPowerNote || note == 103 || note == 101) continue;
                chart.AddDrumTick(spans[i].StartTick);
            }
        }

        static void AddGuitarNote(SongChart chart, int midiNote, int tick, int sustainTicks, int noteBase)
        {
            if (midiNote == StarPowerNote)
            {
                chart.StarPower.Add(new StarPowerPhrase { StartTick = tick, EndTick = tick + Math.Max(0, sustainTicks) });
                return;
            }

            int lane = midiNote - noteBase;
            if (lane < 0 || lane >= LaneInfo.Count) return; // open notes (101) and fills (103)
            chart.AddNote(tick, lane, Math.Max(0, sustainTicks), false, 0);
        }

        static void ApplyStarPower(SongChart chart)
        {
            if (chart.StarPower.Count == 0) return;

            for (int i = 0; i < chart.Notes.Count; i++)
            {
                ChartNote note = chart.Notes[i];
                for (int p = 0; p < chart.StarPower.Count; p++)
                {
                    StarPowerPhrase phrase = chart.StarPower[p];
                    if (note.Tick >= phrase.StartTick && note.Tick <= phrase.EndTick)
                    {
                        note.StarPower = true;
                        chart.Notes[i] = note;
                        break;
                    }
                }
            }
        }

        static string ReadTag(byte[] data, ref int pos)
        {
            if (pos + 4 > data.Length) return "";
            string tag = Encoding.ASCII.GetString(data, pos, 4);
            pos += 4;
            return tag;
        }

        static int ReadInt32BE(byte[] data, ref int pos)
        {
            int value = (data[pos] << 24) | (data[pos + 1] << 16) | (data[pos + 2] << 8) | data[pos + 3];
            pos += 4;
            return value;
        }

        static int ReadInt16BE(byte[] data, ref int pos)
        {
            int value = (data[pos] << 8) | data[pos + 1];
            pos += 2;
            return value;
        }

        static int ReadVarInt(byte[] data, ref int pos)
        {
            int value = 0;
            while (pos < data.Length)
            {
                int b = data[pos++];
                value = (value << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return value;
        }
    }
}
