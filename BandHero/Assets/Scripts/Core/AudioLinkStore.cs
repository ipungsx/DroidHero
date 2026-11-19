using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BandHero.Core
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

        void Load()
        {
            if (!File.Exists(_file)) return;

            try
            {
                foreach (string line in File.ReadAllLines(_file))
                {
                    if (line.Length == 0) continue;

                    int separator = line.IndexOf('\t');
                    if (separator <= 0) continue;

                    string chartPath = Unescape(line.Substring(0, separator));
                    string audioPath = Unescape(line.Substring(separator + 1));
                    if (chartPath.Length > 0 && audioPath.Length > 0) _links[chartPath] = audioPath;
                }
            }
            catch (Exception)
            {
                // A corrupt link file must not stop the library from loading; the player simply
                // re-attaches their audio.
                _links.Clear();
            }
        }

        void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(_file);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                foreach (var pair in _links)
                {
                    builder.Append(Escape(pair.Key)).Append('\t').Append(Escape(pair.Value)).Append('\n');
                }

                File.WriteAllText(_file, builder.ToString());
            }
            catch (Exception)
            {
                // Losing the link list is recoverable; crashing here would not be.
            }
        }

        // Paths can contain tabs and newlines on exotic filesystems, which would break the
        // one-pair-per-line format.
        static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        static string Unescape(string value)
        {
            var builder = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(value[i]);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case 't': builder.Append('\t'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    default: builder.Append(next); break;
                }
            }

            return builder.ToString();
        }
    }
}
