using System;
using System.Collections.Generic;
using System.IO;

namespace BandHero.Core
{
    /// <summary>
    /// Remembers the difficulty the player last chose for each song.
    ///
    /// Difficulty belongs to a chart, not to the app: two charts can carry different sections,
    /// so a single global setting would silently fall back whenever the next song did not have
    /// the remembered one. The app-wide setting survives only as the default for a song the
    /// player has never opened.
    /// </summary>
    public class SongDifficultyStore
    {
        readonly string _file;
        readonly Dictionary<string, string> _choices = new Dictionary<string, string>(StringComparer.Ordinal);

        public SongDifficultyStore(string filePath)
        {
            _file = filePath;
            PairFile.Load(_file, _choices);
        }

        public int Count => _choices.Count;

        /// <summary>Difficulty last chosen for a chart, or null when it has never been played.</summary>
        public string Get(string chartPath)
        {
            if (string.IsNullOrEmpty(chartPath)) return null;
            return _choices.TryGetValue(chartPath, out string difficulty) ? difficulty : null;
        }

        /// <summary>
        /// Difficulty to use for a chart: the player's last choice when the chart still carries
        /// it, otherwise the requested default resolved against what the chart really has.
        /// </summary>
        public string Resolve(string chartPath, string fallback, IList<string> available)
        {
            string remembered = Get(chartPath);
            return Chart.ChartDifficulty.Resolve(
                string.IsNullOrEmpty(remembered) ? fallback : remembered, available);
        }

        public void Set(string chartPath, string difficulty)
        {
            if (string.IsNullOrEmpty(chartPath)) return;

            if (string.IsNullOrEmpty(difficulty))
            {
                if (!_choices.Remove(chartPath)) return;
            }
            else if (_choices.TryGetValue(chartPath, out string existing) &&
                     string.Equals(existing, difficulty, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            else
            {
                _choices[chartPath] = difficulty;
            }

            PairFile.Save(_file, _choices);
        }

        public void Clear()
        {
            if (_choices.Count == 0) return;
            _choices.Clear();
            PairFile.Save(_file, _choices);
        }
    }
}
