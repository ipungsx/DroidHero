using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace BandHero.Core
{
    /// <summary>One finished run, kept for the leaderboard.</summary>
    public class ScoreRecord
    {
        public string Song = "";
        public string Artist = "";
        public string Difficulty = "";
        public int Score;
        public int MaxCombo;
        public float Accuracy;
        public int Stars;
        public long PlayedAtUtc;

        /// <summary>Stable key used to group records for one song at one difficulty.</summary>
        public string Key => EntryKey(Song, Difficulty);

        public static string EntryKey(string song, string difficulty) =>
            (song ?? "").Trim().ToLowerInvariant() + "|" + (difficulty ?? "").Trim().ToLowerInvariant();

        /// <summary>Local timestamp of the run, for leaderboard rows.</summary>
        public string PlayedAtText => PlayedAtUtc > 0
            ? DateTimeOffset.FromUnixTimeSeconds(PlayedAtUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "-";

        public string Describe()
        {
            string when = PlayedAtUtc > 0
                ? DateTimeOffset.FromUnixTimeSeconds(PlayedAtUtc).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "-";

            return Score.ToString("N0") + "\t" + Accuracy.ToString("F1", CultureInfo.InvariantCulture) + "%\t" +
                   "x" + MaxCombo + "\t" + Difficulty + "\t" + when;
        }
    }

    /// <summary>
    /// Local leaderboard: the best runs, grouped by song and difficulty.
    ///
    /// Everything is local by design, so the game needs no account, no server and no network
    /// permission to show a ranking. Records are stored one per line and re-read on launch.
    /// </summary>
    public class Leaderboard
    {
        public const int MaxEntriesPerGroup = 10;

        readonly List<ScoreRecord> _records = new List<ScoreRecord>();
        readonly string _path;
        bool _dirty;

        public Leaderboard(string root)
        {
            _path = Path.Combine(root, "leaderboard.txt");
            Load();
        }

        public IReadOnlyList<ScoreRecord> Records => _records;

        public int Count => _records.Count;

        /// <summary>Adds a run and returns true when it earned a place in its group.</summary>
        public bool Submit(ScoreRecord record)
        {
            if (record == null || record.Score <= 0) return false;

            if (record.PlayedAtUtc <= 0)
            {
                record.PlayedAtUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            }

            _records.Add(record);
            Prune(record.Key);
            _dirty = true;
            Save();

            return IsRanked(record);
        }

        /// <summary>True when the record survived pruning for its own song and difficulty.</summary>
        public bool IsRanked(ScoreRecord record)
        {
            if (record == null) return false;

            string key = record.Key;
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i] == record) return true;
            }
            return false;
        }

        /// <summary>Best-first records for one song and difficulty.</summary>
        public List<ScoreRecord> Top(string song, string difficulty, int limit = MaxEntriesPerGroup)
        {
            string key = ScoreRecord.EntryKey(song, difficulty);

            var matches = new List<ScoreRecord>();
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Key == key) matches.Add(_records[i]);
            }

            matches.Sort(Compare);
            if (matches.Count > limit) matches.RemoveRange(limit, matches.Count - limit);
            return matches;
        }

        /// <summary>
        /// One entry per song, its best run across every difficulty, best first.
        ///
        /// Grouping by song rather than by song and difficulty keeps the "all songs" list from
        /// showing the same song four times; the row still names the difficulty it was set on.
        /// </summary>
        public List<ScoreRecord> BestPerSong()
        {
            var best = new Dictionary<string, ScoreRecord>(StringComparer.Ordinal);

            for (int i = 0; i < _records.Count; i++)
            {
                ScoreRecord record = _records[i];
                string key = (record.Song ?? "").Trim().ToLowerInvariant();

                if (!best.TryGetValue(key, out ScoreRecord current) || Compare(record, current) < 0)
                {
                    best[key] = record;
                }
            }

            var list = new List<ScoreRecord>(best.Values);
            list.Sort(Compare);
            return list;
        }

        public int BestScore(string song, string difficulty)
        {
            List<ScoreRecord> top = Top(song, difficulty, 1);
            return top.Count > 0 ? top[0].Score : 0;
        }

        /// <summary>Wipes the board, either entirely or for one song/difficulty group.</summary>
        public void Clear(string song = null, string difficulty = null)
        {
            if (string.IsNullOrEmpty(song))
            {
                _records.Clear();
            }
            else
            {
                string key = ScoreRecord.EntryKey(song, difficulty);
                _records.RemoveAll(r => r.Key == key);
            }

            _dirty = true;
            Save();
        }

        /// <summary>Higher score first, then accuracy, then combo. Never returns 0 for distinct runs.</summary>
        static int Compare(ScoreRecord a, ScoreRecord b)
        {
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0) return byScore;

            int byAccuracy = b.Accuracy.CompareTo(a.Accuracy);
            if (byAccuracy != 0) return byAccuracy;

            int byCombo = b.MaxCombo.CompareTo(a.MaxCombo);
            if (byCombo != 0) return byCombo;

            return a.PlayedAtUtc.CompareTo(b.PlayedAtUtc);
        }

        void Prune(string key)
        {
            var group = new List<ScoreRecord>();
            for (int i = 0; i < _records.Count; i++)
            {
                if (_records[i].Key == key) group.Add(_records[i]);
            }

            if (group.Count <= MaxEntriesPerGroup) return;

            group.Sort(Compare);
            var keep = new HashSet<ScoreRecord>(group.GetRange(0, MaxEntriesPerGroup));
            _records.RemoveAll(r => r.Key == key && !keep.Contains(r));
        }

        void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;

                foreach (string raw in File.ReadAllLines(_path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    ScoreRecord record = ParseLine(line);
                    if (record != null) _records.Add(record);
                }
            }
            catch (Exception)
            {
                _records.Clear();
            }
        }

        public void Save()
        {
            if (!_dirty) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
                var lines = new List<string>(_records.Count + 1) { "# Band Hero leaderboard" };

                for (int i = 0; i < _records.Count; i++)
                {
                    lines.Add(FormatLine(_records[i]));
                }

                File.WriteAllLines(_path, lines);
                _dirty = false;
            }
            catch (Exception)
            {
                // Keep the in-memory board usable even when the file cannot be written.
            }
        }

        // Tabs are safe here because song names come from chart metadata and are stripped below.
        static string FormatLine(ScoreRecord r)
        {
            return string.Join("\t", new[]
            {
                Clean(r.Song),
                Clean(r.Artist),
                Clean(r.Difficulty),
                r.Score.ToString(CultureInfo.InvariantCulture),
                r.MaxCombo.ToString(CultureInfo.InvariantCulture),
                r.Accuracy.ToString("F2", CultureInfo.InvariantCulture),
                r.Stars.ToString(CultureInfo.InvariantCulture),
                r.PlayedAtUtc.ToString(CultureInfo.InvariantCulture),
            });
        }

        static ScoreRecord ParseLine(string line)
        {
            string[] parts = line.Split('\t');
            if (parts.Length < 8) return null;

            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int score)) return null;

            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int combo);
            float.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float accuracy);
            int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int stars);
            long.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out long playedAt);

            return new ScoreRecord
            {
                Song = parts[0],
                Artist = parts[1],
                Difficulty = parts[2],
                Score = score,
                MaxCombo = combo,
                Accuracy = accuracy,
                Stars = stars,
                PlayedAtUtc = playedAt,
            };
        }

        static string Clean(string value) =>
            string.IsNullOrEmpty(value) ? "" : value.Replace('\t', ' ').Replace('\n', ' ').Trim();
    }
}
