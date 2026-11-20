using System;
using System.Globalization;
using System.IO;

namespace DroidHero.Core
{
    /// <summary>
    /// How far the chart audio sits from the chart notes, in milliseconds.
    ///
    /// A negative value makes notes arrive later, which is what a player needs when their track
    /// starts slightly ahead of the chart. Local files carry encoder delay that chart-embedded
    /// audio does not, so this has to be adjustable rather than fixed.
    /// </summary>
    public static class AudioCalibration
    {
        public const int MinMilliseconds = -300;
        public const int MaxMilliseconds = 300;
        public const int StepMilliseconds = 5;

        static string FilePath(string root) => Path.Combine(root, "calibration.txt");

        public static int Clamp(int milliseconds) =>
            Math.Max(MinMilliseconds, Math.Min(MaxMilliseconds, milliseconds));

        public static float ToSeconds(int milliseconds) => milliseconds / 1000f;

        /// <summary>Reads the saved offset, falling back to zero when unset or unreadable.</summary>
        public static int Load(string root)
        {
            try
            {
                string file = FilePath(root);
                if (!File.Exists(file)) return 0;

                string text = File.ReadAllText(file).Trim();
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? Clamp(value)
                    : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static void Save(string root, int milliseconds)
        {
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(FilePath(root),
                                  Clamp(milliseconds).ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // An unwritable settings file must not stop a song from starting.
            }
        }

        public static string Describe(int milliseconds)
        {
            if (milliseconds == 0) return "0 ms";
            return (milliseconds > 0 ? "+" : "") + milliseconds + " ms";
        }
    }
}
