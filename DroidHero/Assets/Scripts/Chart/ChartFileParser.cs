using System;
using System.Collections.Generic;
using System.Globalization;

namespace DroidHero.Chart
{
    /// <summary>
    /// Parser for the ".chart" text format used by Clone Hero / Feedback charts.
    ///
    /// The file is a list of [Section] blocks of "key = value" lines. Note sections are
    /// named after difficulty plus track, e.g. [ExpertSingle], and hold tick-indexed events
    /// such as "768 = N 2 0" (note) or "3456 = S 2 1344" (star power phrase).
    /// </summary>
    public static class ChartFileParser
    {
        sealed class Section
        {
            public string Name;
            public readonly List<KeyValuePair<string, string>> Lines = new List<KeyValuePair<string, string>>();
        }

        public static SongChart Parse(string text, string difficulty = "Expert")
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            List<Section> sections = SplitSections(text);
            var chart = new SongChart();

            Section song = FindSection(sections, "Song");
            if (song != null)
            {
                chart.Name = GetString(song, "Name", chart.Name);
                chart.Artist = GetString(song, "Artist", chart.Artist);
                chart.Album = GetString(song, "Album", chart.Album);
                chart.Charter = GetString(song, "Charter", chart.Charter);
                chart.Genre = GetString(song, "Genre", chart.Genre);
                chart.Year = GetString(song, "Year", chart.Year);
                chart.Offset = GetFloat(song, "Offset", 0f) / 1000f;
                chart.Resolution = Math.Max(1, (int)GetFloat(song, "Resolution", 192f));
                chart.AudioFile = GetString(song, "MusicStream", "");
            }

            chart.Difficulty = difficulty;

            Section sync = FindSection(sections, "SyncTrack");
            if (sync != null)
            {
                foreach (var line in sync.Lines)
                {
                    if (!TryParseTick(line.Key, out int tick)) continue;

                    string[] parts = line.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || parts[0] != "B") continue;

                    float bpm = ParseFloat(parts[1]) / 1000f; // stored as milli-BPM
                    if (bpm > 0f) chart.AddTempo(tick, bpm);
                }
            }

            Section notes = FindTrackSection(sections, difficulty);
            if (notes != null)
            {
                foreach (var line in notes.Lines)
                {
                    if (!TryParseTick(line.Key, out int tick)) continue;

                    string[] parts = line.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    string kind = parts[0];
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lane)) continue;

                    if (kind == "N")
                    {
                        if (lane < 0 || lane >= LaneInfo.Count) continue; // open/6-fret/drum markers
                        int sustain = parts.Length > 2 ? ParseInt(parts[2]) : 0;
                        int flags = parts.Length > 3 ? ParseInt(parts[3]) : 0;
                        chart.AddNote(tick, lane, sustain, false, flags);
                    }
                    else if (kind == "S" && lane == 2)
                    {
                        int length = parts.Length > 2 ? ParseInt(parts[2]) : 0;
                        chart.StarPower.Add(new StarPowerPhrase { StartTick = tick, EndTick = tick + length });
                    }
                }
            }

            ParseDrumTrack(sections, chart, difficulty);

            ApplyStarPower(chart);
            chart.FinalizeChart();
            return chart;
        }

        /// <summary>
        /// Collects the drum onsets for the light show. Drum sections use the same
        /// "tick = N lane" note lines as guitar sections, so only the tick matters here.
        /// </summary>
        static void ParseDrumTrack(List<Section> sections, SongChart chart, string difficulty)
        {
            Section drums = FindSection(sections, difficulty + "Drums");
            if (drums == null) return;

            foreach (var line in drums.Lines)
            {
                if (!TryParseTick(line.Key, out int tick)) continue;

                string[] parts = line.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] != "N") continue;

                chart.AddDrumTick(tick);
            }
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

        static List<Section> SplitSections(string text)
        {
            var sections = new List<Section>();
            Section current = null;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;

                if (line[0] == '[')
                {
                    int close = line.IndexOf(']');
                    if (close > 0)
                    {
                        current = new Section { Name = line.Substring(1, close - 1).Trim() };
                        sections.Add(current);
                        continue;
                    }
                }

                if (current == null || line == "{" || line == "}") continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                current.Lines.Add(new KeyValuePair<string, string>(
                    line.Substring(0, eq).Trim(),
                    line.Substring(eq + 1).Trim()));
            }

            return sections;
        }

        /// <summary>
        /// Resolves a difficulty to a note section, accepting both the bare name ([Expert]) and
        /// the track-qualified names enchor/Rock Band exports use ([ExpertSingle], [ExpertGuitar]).
        /// </summary>
        static Section FindTrackSection(List<Section> sections, string difficulty)
        {
            string[] suffixes = { "", "Single", "Guitar", "Lead" };
            for (int s = 0; s < suffixes.Length; s++)
            {
                Section found = FindSection(sections, difficulty + suffixes[s]);
                if (found != null && found.Lines.Count > 0) return found;
            }
            return null;
        }

        static Section FindSection(List<Section> sections, string name)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (string.Equals(sections[i].Name, name, StringComparison.OrdinalIgnoreCase)) return sections[i];
            }
            return null;
        }

        static bool TryParseTick(string key, out int tick) =>
            int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out tick);

        static string GetString(Section section, string key, string fallback)
        {
            for (int i = 0; i < section.Lines.Count; i++)
            {
                if (!string.Equals(section.Lines[i].Key, key, StringComparison.OrdinalIgnoreCase)) continue;

                string value = section.Lines[i].Value;
                if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                {
                    value = value.Substring(1, value.Length - 2);
                }
                return value;
            }
            return fallback;
        }

        static float GetFloat(Section section, string key, float fallback)
        {
            for (int i = 0; i < section.Lines.Count; i++)
            {
                if (!string.Equals(section.Lines[i].Key, key, StringComparison.OrdinalIgnoreCase)) continue;

                if (float.TryParse(section.Lines[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    return value;
                }
            }
            return fallback;
        }

        static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

        static int ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }
}
