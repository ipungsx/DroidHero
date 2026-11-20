using System;
using System.Collections.Generic;
using System.Text;

namespace DroidHero.Chart
{
    /// <summary>
    /// Reports which difficulties a chart actually contains notes for.
    ///
    /// Charts are not guaranteed to carry every difficulty: a charter may author only Expert,
    /// and enchor.us metadata only rates the chart (diff_guitar) rather than listing the
    /// sections inside the package. The menu therefore reads the real sections out of the file
    /// so a player can never pick a difficulty that would fail to load.
    /// </summary>
    public static class ChartDifficultyScanner
    {
        /// <summary>
        /// Difficulties present in a .chart file, in canonical order (Easy..Expert).
        ///
        /// A section counts only when it holds at least one note event; empty or
        /// section-less tracks are ignored so the menu does not offer a difficulty that would
        /// load zero notes.
        /// </summary>
        public static List<string> FromChartText(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;

            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentSection = null;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();

                if (line.Length == 0 || line[0] == ';' || line[0] == '/') continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    currentSection = close > 1 ? line.Substring(1, close - 1).Trim() : null;
                    continue;
                }

                if (currentSection == null) continue;
                if (line[0] == '{' || line[0] == '}') continue;

                int equals = line.IndexOf('=');
                if (equals <= 0) continue;

                // Only "N" note events count; "S" star power and "E" events do not.
                string value = line.Substring(equals + 1).Trim();
                if (value.Length < 2 || value[0] != 'N') continue;

                string difficulty = DifficultyOfSection(currentSection);
                if (difficulty != null) present.Add(difficulty);
            }

            for (int i = 0; i < ChartDifficulty.All.Length; i++)
            {
                if (present.Contains(ChartDifficulty.All[i])) found.Add(ChartDifficulty.All[i]);
            }

            return found;
        }

        /// <summary>Maps a note section name to its difficulty, or null when it is not a guitar track.</summary>
        static string DifficultyOfSection(string section)
        {
            for (int i = 0; i < ChartDifficulty.SectionNames.Length; i++)
            {
                string name = ChartDifficulty.SectionNames[i];

                if (string.Equals(section, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(section, name + "Single", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(section, name + "Guitar", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(section, name + "Lead", StringComparison.OrdinalIgnoreCase))
                {
                    return ChartDifficulty.All[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Difficulties present in a Rock Band style notes.mid.
        ///
        /// Each difficulty owns a 12-semitone block on the guitar track (Easy 60, Medium 72,
        /// Hard 84, Expert 96), so the presence of any note inside a block means that
        /// difficulty was authored.
        /// </summary>
        public static List<string> FromMidi(byte[] data)
        {
            var found = new List<string>();
            if (data == null || data.Length < 14) return found;

            var present = new bool[ChartDifficulty.All.Length];
            int pos = 0;

            if (ReadTag(data, ref pos) != "MThd") return found;

            int headerLength = ReadInt32BE(data, ref pos);
            int headerEnd = Math.Min(pos + headerLength, data.Length);
            ReadInt16BE(data, ref pos);                    // format
            int trackCount = ReadInt16BE(data, ref pos);
            pos = headerEnd;

            for (int t = 0; t < trackCount && pos + 8 <= data.Length; t++)
            {
                string tag = ReadTag(data, ref pos);
                int length = ReadInt32BE(data, ref pos);
                int trackEnd = Math.Min(pos + length, data.Length);

                if (tag == "MTrk") ScanTrack(data, pos, trackEnd, present);

                pos = trackEnd;
            }

            for (int i = 0; i < present.Length; i++)
            {
                if (present[i]) found.Add(ChartDifficulty.All[i]);
            }

            return found;
        }

        static void ScanTrack(byte[] data, int start, int end, bool[] present)
        {
            int pos = start;
            int runningStatus = 0;
            string trackName = null;

            // Notes are buffered because the track-name event can appear anywhere in the track.
            var notes = new List<int>();

            while (pos < end)
            {
                ReadVarInt(data, ref pos);
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

                    if (type == 0x90 && velocity > 0) notes.Add(note);
                    continue;
                }

                pos += (type == 0xC0 || type == 0xD0) ? 1 : 2;
            }

            if (!IsGuitarTrack(trackName)) return;

            const int EasyBase = 60;
            const int SemitonesPerDifficulty = 12;

            for (int i = 0; i < notes.Count; i++)
            {
                int offset = notes[i] - EasyBase;
                if (offset < 0) continue;

                int difficulty = offset / SemitonesPerDifficulty;
                if (difficulty >= 0 && difficulty < present.Length && offset % SemitonesPerDifficulty < 5)
                {
                    present[difficulty] = true;
                }
            }
        }

        static bool IsGuitarTrack(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return string.Equals(name, "PART GUITAR", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "PART LEAD", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "T1 GEMS", StringComparison.OrdinalIgnoreCase);
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
