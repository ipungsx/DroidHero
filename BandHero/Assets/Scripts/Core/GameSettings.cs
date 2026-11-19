using System;
using System.Globalization;
using System.IO;

namespace BandHero.Core
{
    /// <summary>
    /// Player preferences that outlive a run: theme, difficulty and the light show toggle.
    ///
    /// Stored as a handful of "key=value" lines rather than JSON so a half-written or
    /// hand-edited file degrades to defaults instead of throwing on launch.
    /// </summary>
    public class GameSettings
    {
        public const int DefaultDifficultyIndex = 3; // Expert

        public int ThemeIndex;
        public int DifficultyIndex = DefaultDifficultyIndex;
        public bool LightShow = true;
        public bool ShowLyrics;

        string _path;

        public string Difficulty => Chart.ChartDifficulty.At(DifficultyIndex);

        public static GameSettings Load(string root)
        {
            var settings = new GameSettings { _path = FilePath(root) };

            try
            {
                if (!File.Exists(settings._path)) return settings;

                foreach (string raw in File.ReadAllLines(settings._path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "theme":
                            settings.ThemeIndex = ParseInt(value, settings.ThemeIndex);
                            break;
                        case "difficulty":
                            settings.DifficultyIndex = Chart.ChartDifficulty.IndexOf(value);
                            break;
                        case "lightShow":
                            settings.LightShow = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "lyrics":
                            settings.ShowLyrics = value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // A corrupt settings file must never stop the game from starting.
            }

            return settings;
        }

        public void Save(string root)
        {
            _path = FilePath(root);

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllLines(_path, new[]
                {
                    "# Band Hero settings",
                    "theme=" + ThemeIndex.ToString(CultureInfo.InvariantCulture),
                    "difficulty=" + Difficulty,
                    "lightShow=" + (LightShow ? "1" : "0"),
                    "lyrics=" + (ShowLyrics ? "1" : "0"),
                });
            }
            catch (Exception)
            {
                // Losing a preference is not worth interrupting play.
            }
        }

        static string FilePath(string root) => Path.Combine(root, "settings.txt");

        static int ParseInt(string value, int fallback) =>
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    }
}
