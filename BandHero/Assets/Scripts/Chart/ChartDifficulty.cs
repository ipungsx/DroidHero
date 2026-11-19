namespace BandHero.Chart
{
    public static class ChartDifficulty
    {
        /// <summary>
        /// Difficulty names, spelled the way enchor.us and Rock Band label them. The chart
        /// sections themselves still use the Guitar Hero spelling, which the parsers accept.
        /// </summary>
        public static readonly string[] All = { "Easy", "Normal", "Hard", "Expert" };

        /// <summary>Name used inside .chart sections, where Medium is the authored spelling.</summary>
        public static readonly string[] SectionNames = { "Easy", "Medium", "Hard", "Expert" };

        public static string SectionName(int index)
        {
            if (index < 0) index = 0;
            if (index >= SectionNames.Length) index = SectionNames.Length - 1;
            return SectionNames[index];
        }

        /// <summary>
        /// Maps any spelling of a difficulty to the name the chart sections use. Charts and the
        /// enchor API disagree here ("Medium" against "Normal"), and both have to work.
        /// </summary>
        public static string ToSectionName(string difficulty)
        {
            if (string.IsNullOrEmpty(difficulty)) return SectionNames[SectionNames.Length - 1];

            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i], difficulty, System.StringComparison.OrdinalIgnoreCase))
                {
                    return SectionNames[i];
                }
            }

            for (int i = 0; i < SectionNames.Length; i++)
            {
                if (string.Equals(SectionNames[i], difficulty, System.StringComparison.OrdinalIgnoreCase))
                {
                    return SectionNames[i];
                }
            }

            return difficulty;
        }

        public static string At(int index)
        {
            if (index < 0) index = 0;
            if (index >= All.Length) index = All.Length - 1;
            return All[index];
        }

        public static int IndexOf(string name)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            for (int i = 0; i < SectionNames.Length; i++)
            {
                if (string.Equals(SectionNames[i], name, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            return All.Length - 1;
        }
    }
}
