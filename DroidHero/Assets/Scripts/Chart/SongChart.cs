
using System;
using System.Collections.Generic;

namespace DroidHero.Chart
{
    public static class LaneInfo
    {
        public const int Count = 5;
    }

    public struct TempoChange
    {
        public int Tick;
        public float Bpm;
        public float SecondsAtTick;
        public float SecondsPerTick;
    }

    public struct ChartNote
    {
        public int Tick;
        public float Time;
        public float Duration;
        public int Lane;
        public bool StarPower;
        public bool Hopo;
        public bool Tap;
        public bool ForcedHopo;
        public bool ForcedStrum;

        public float EndTime => Time + Duration;
        public bool IsSustain => Duration > 0.0001f;
    }

    public struct StarPowerPhrase
    {
        public int StartTick;
        public int EndTick;
        public float Start;
        public float End;
    }

    /// <summary>
    /// Timing-accurate song chart. Ticks are the authored unit; <see cref="FinalizeChart"/>
    /// resolves them into seconds so gameplay never has to convert during playback.
    /// </summary>
    public class SongChart
    {
        public string Name = "Unknown";
        public string Artist = "Unknown";
        public string Album = "";
        public string Charter = "";
        public string Genre = "";
        public string Year = "";
        public string Difficulty = "Expert";
        public string AudioFile = "";

        public float Offset;
        public int Resolution = 192;

        const float MinSustainSeconds = 0.02f;

        public readonly List<TempoChange> Tempos = new List<TempoChange>();
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<StarPowerPhrase> StarPower = new List<StarPowerPhrase>();

        /// <summary>
        /// Onsets of the drum chart, in seconds, used only to drive the light show.
        ///
        /// Charts that ship no drum track fall back to the guitar onsets, so the lights still
        /// pulse in time with the music instead of staying dark for the whole song.
        /// </summary>
        public readonly List<float> DrumHits = new List<float>();

        float _songLength = -1f;

        /// <summary>Length of the charted notes in seconds; cached because the HUD asks each frame.</summary>
        public float SongLength
        {
            get
            {
                if (_songLength >= 0f) return _songLength;

                float max = 0f;
                for (int i = 0; i < Notes.Count; i++)
                {
                    float end = Notes[i].EndTime;
                    if (end > max) max = end;
                }

                _songLength = max;
                return max;
            }
        }

        public void AddTempo(int tick, float bpm)
        {
            Tempos.Add(new TempoChange { Tick = tick, Bpm = bpm });
        }

        public void AddNote(int tick, int lane, int sustainTicks, bool starPower, int flags)
        {
            Notes.Add(new ChartNote
            {
                Tick = tick,
                Lane = lane,
                Duration = sustainTicks,
                StarPower = starPower,
                ForcedHopo = flags == 1,
                ForcedStrum = flags == 2,
                Tap = flags == 3,
            });
        }

        /// <summary>Registers a drum onset tick, later resolved into <see cref="DrumHits"/>.</summary>
        public void AddDrumTick(int tick) => _drumTicks.Add(tick);

        readonly List<int> _drumTicks = new List<int>();

        public void RebuildTiming()
        {
            if (Tempos.Count == 0)
            {
                Tempos.Add(new TempoChange { Tick = 0, Bpm = 120f });
            }

            Tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));

            // MIDI charts repeat the tempo map in every track, so collapse events sharing a tick.
            for (int i = Tempos.Count - 1; i > 0; i--)
            {
                if (Tempos[i].Tick == Tempos[i - 1].Tick) Tempos.RemoveAt(i - 1);
            }

            double seconds = 0;
            int prevTick = Tempos[0].Tick;
            float prevBpm = Tempos[0].Bpm;

            for (int i = 0; i < Tempos.Count; i++)
            {
                TempoChange t = Tempos[i];
                if (i > 0)
                {
                    seconds += (t.Tick - prevTick) / (prevBpm * Resolution / 60.0);
                    prevTick = t.Tick;
                    prevBpm = t.Bpm;
                }

                t.SecondsAtTick = (float)seconds;
                t.SecondsPerTick = 60f / (Math.Max(t.Bpm, 1f) * Resolution);
                Tempos[i] = t;
            }
        }

        public float TickToSeconds(int tick)
        {
            if (Tempos.Count == 0) RebuildTiming();

            int index = 0;
            for (int i = 0; i < Tempos.Count; i++)
            {
                if (Tempos[i].Tick <= tick) index = i;
                else break;
            }

            TempoChange seg = Tempos[index];
            return seg.SecondsAtTick + (tick - seg.Tick) * seg.SecondsPerTick;
        }

        public float TicksToSecondsDuration(int tick, int ticks)
        {
            return TickToSeconds(tick + ticks) - TickToSeconds(tick);
        }

        public float BpmAt(int tick)
        {
            if (Tempos.Count == 0) RebuildTiming();

            float bpm = Tempos[0].Bpm;
            for (int i = 0; i < Tempos.Count; i++)
            {
                if (Tempos[i].Tick <= tick) bpm = Tempos[i].Bpm;
                else break;
            }
            return bpm;
        }

        /// <summary>Converts ticks to seconds, sorts notes and resolves HOPO/tap flags.</summary>
        public void FinalizeChart(float audioOffsetSeconds = 0f)
        {
            RebuildTiming();

            for (int i = 0; i < Notes.Count; i++)
            {
                ChartNote n = Notes[i];
                n.Time = TickToSeconds(n.Tick) + Offset + audioOffsetSeconds;
                n.Duration = TicksToSecondsDuration(n.Tick, (int)n.Duration);

                // MIDI charts close a non-sustained note one tick later; treat anything
                // shorter than a strum as a plain note rather than a one-frame sustain.
                if (n.Duration < MinSustainSeconds) n.Duration = 0f;

                Notes[i] = n;
            }

            Notes.Sort((a, b) =>
            {
                int c = a.Time.CompareTo(b.Time);
                return c != 0 ? c : a.Lane.CompareTo(b.Lane);
            });

            for (int i = 0; i < StarPower.Count; i++)
            {
                StarPowerPhrase p = StarPower[i];
                p.Start = TickToSeconds(p.StartTick) + Offset + audioOffsetSeconds;
                p.End = TickToSeconds(p.EndTick) + Offset + audioOffsetSeconds;
                StarPower[i] = p;
            }

            StarPower.Sort((a, b) => a.Start.CompareTo(b.Start));
            ResolveHopos();
            ResolveDrumHits(audioOffsetSeconds);
            _songLength = -1f;
        }

        /// <summary>
        /// Converts the drum ticks to seconds, collapsing onsets that land within a few
        /// milliseconds so a flam does not fire two light pulses on the same frame.
        /// </summary>
        void ResolveDrumHits(float audioOffsetSeconds)
        {
            DrumHits.Clear();

            // Charts without a drum track fall back to the guitar onsets, which still land on
            // the beat. Chords share a tick, so the fallback needs the same collapsing below.
            var seconds = new List<float>(_drumTicks.Count > 0 ? _drumTicks.Count : Notes.Count);

            if (_drumTicks.Count == 0)
            {
                for (int i = 0; i < Notes.Count; i++) seconds.Add(Notes[i].Time);
            }
            else
            {
                for (int i = 0; i < _drumTicks.Count; i++)
                {
                    seconds.Add(TickToSeconds(_drumTicks[i]) + Offset + audioOffsetSeconds);
                }
            }

            seconds.Sort();

            const float MinGap = 0.02f;
            float last = float.MinValue;
            for (int i = 0; i < seconds.Count; i++)
            {
                if (seconds[i] - last < MinGap) continue;
                DrumHits.Add(seconds[i]);
                last = seconds[i];
            }
        }

        /// <summary>
        /// HOPO resolution follows the Clone Hero convention: a note can be played without
        /// strumming when it is a single note, the previous event was also a single note, the gap
        /// is within the current BPM's eighth-note window, and it is not explicitly forced to strum.
        /// </summary>
        void ResolveHopos()
        {
            int previousTick = -1;
            int previousGroupSize = 0;

            int i = 0;
            while (i < Notes.Count)
            {
                int groupStart = i;
                int tick = Notes[i].Tick;
                int groupSize = 0;
                while (i < Notes.Count && Notes[i].Tick == tick)
                {
                    groupSize++;
                    i++;
                }

                bool isChord = groupSize > 1;
                int hopoWindow = Math.Max(1, (int)(Resolution * 0.5f));

                for (int g = groupStart; g < groupStart + groupSize; g++)
                {
                    ChartNote n = Notes[g];
                    if (n.IsSustain) continue;
                    if (n.Tap) continue;

                    if (n.ForcedHopo)
                    {
                        n.Hopo = true;
                    }
                    else if (n.ForcedStrum || isChord)
                    {
                        n.Hopo = false;
                    }
                    else
                    {
                        bool closeEnough = previousTick >= 0 && (tick - previousTick) <= hopoWindow;
                        n.Hopo = closeEnough && previousGroupSize == 1;
                    }

                    Notes[g] = n;
                }

                previousTick = tick;
                previousGroupSize = groupSize;
            }
        }
    }
}
