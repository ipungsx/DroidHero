namespace BandHero.Chart
{
    public static class ChartDifficulty
    {
        public static readonly string[] All = { "Easy", "Medium", "Hard", "Expert" };

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
            return All.Length - 1;
        }
    }
}
