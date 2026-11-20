using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DroidHero.Core
{
    /// <summary>
    /// Remembers which local audio file belongs to which chart.
    ///
    /// Attaching a track is a deliberate choice by the player, so it has to survive a restart;
    /// without this, every song would fall back to the chart's own audio on the next launch.
    /// Keys are chart paths and values are audio paths, stored one pair per line.
    /// </summary>
    public class AudioLinkStore
    {
        readonly string _file;
        readonly Dictionary<string, string> _links = new Dictionary<string, string>(StringComparer.Ordinal);

        public AudioLinkStore(string filePath)
        {
            _file = filePath;
            Load();
        }

        public int Count => _links.Count;

        /// <summary>Audio path attached to a chart, or null when the chart uses its own audio.</summary>
        public string Get(string chartPath)
        {
            if (string.IsNullOrEmpty(chartPath)) return null;
            return _links.TryGetValue(chartPath, out string audioPath) ? audioPath : null;
        }

        public void Set(string chartPath, string audioPath)
        {
            if (string.IsNullOrEmpty(chartPath)) return;

            if (string.IsNullOrEmpty(audioPath)) _links.Remove(chartPath);
            else _links[chartPath] = audioPath;

            Save();
        }

        public void Remove(string chartPath) => Set(chartPath, null);

        void Load() => PairFile.Load(_file, _links);

        void Save() => PairFile.Save(_file, _links);
    }
}
