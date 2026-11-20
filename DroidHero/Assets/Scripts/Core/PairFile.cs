using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DroidHero.Core
{
    /// <summary>
    /// One key/value pair per line, tab separated and escaped.
    ///
    /// Several small preferences are stored this way (attached audio, chosen difficulty), and
    /// each needs the same escaping and the same tolerance for a damaged file, so the format
    /// lives in one place.
    /// </summary>
    public static class PairFile
    {
        public static void Load(string path, IDictionary<string, string> into)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    if (line.Length == 0) continue;

                    int separator = line.IndexOf('\t');
                    if (separator <= 0) continue;

                    string key = Unescape(line.Substring(0, separator));
                    string value = Unescape(line.Substring(separator + 1));
                    if (key.Length > 0 && value.Length > 0) into[key] = value;
                }
            }
            catch (Exception)
            {
                // A corrupt preferences file must not stop the app from starting; the player
                // simply re-picks whatever it held.
                into.Clear();
            }
        }

        public static void Save(string path, IEnumerable<KeyValuePair<string, string>> pairs)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                var builder = new StringBuilder();
                foreach (var pair in pairs)
                {
                    builder.Append(Escape(pair.Key)).Append('\t').Append(Escape(pair.Value)).Append('\n');
                }

                File.WriteAllText(path, builder.ToString());
            }
            catch (Exception)
            {
                // Losing a preference is recoverable; crashing here would not be.
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
