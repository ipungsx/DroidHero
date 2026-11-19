using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// Fret colours and layout constants for the 2D highway. Colours follow the classic
    /// five-fret arrangement so the lanes are readable at a glance on a phone screen.
    /// </summary>
    public static class LaneTheme
    {
        public static readonly Color[] LaneColors =
        {
            new Color(0.29f, 0.87f, 0.31f), // green
            new Color(0.95f, 0.26f, 0.26f), // red
            new Color(0.98f, 0.85f, 0.22f), // yellow
            new Color(0.30f, 0.55f, 0.98f), // blue
            new Color(0.96f, 0.53f, 0.18f), // orange
        };

        public static readonly Color[] LaneColorsDim =
        {
            new Color(0.16f, 0.42f, 0.17f),
            new Color(0.45f, 0.14f, 0.14f),
            new Color(0.45f, 0.39f, 0.12f),
            new Color(0.16f, 0.27f, 0.45f),
            new Color(0.45f, 0.26f, 0.10f),
        };

        public static readonly Color StarPowerColor = new Color(0.62f, 0.86f, 1f);
        public static readonly Color BackgroundColor = new Color(0.05f, 0.05f, 0.08f);
        public static readonly Color HighwayColor = new Color(0.10f, 0.10f, 0.14f, 0.92f);
        public static readonly Color HitLineColor = new Color(1f, 1f, 1f, 0.85f);

        public static Color ForLane(int lane)
        {
            if (lane < 0 || lane >= LaneColors.Length) return Color.white;
            return LaneColors[lane];
        }

        public static Color DimForLane(int lane)
        {
            if (lane < 0 || lane >= LaneColorsDim.Length) return Color.gray;
            return LaneColorsDim[lane];
        }
    }
}
