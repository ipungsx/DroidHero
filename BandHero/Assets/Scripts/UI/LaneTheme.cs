using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// The active palette for the highway.
    ///
    /// This is a thin view over <see cref="GameplayTheme"/> so rendering code can ask for "the
    /// colour of lane 2" without caring which theme the player picked. Set <see cref="Current"/>
    /// once before building the highway; the renderer reads it every frame.
    /// </summary>
    public static class LaneTheme
    {
        static GameplayTheme _current = GameplayTheme.All[0];

        public static GameplayTheme Current
        {
            get => _current;
            set => _current = value ?? GameplayTheme.All[0];
        }

        public static void Select(int index) => _current = GameplayTheme.At(index);

        public static Color ForLane(int lane) => _current.Lane(lane);

        public static Color DimForLane(int lane) => _current.Dim(lane);

        public static Color StarPowerColor => _current.StarPower;
        public static Color BackgroundColor => _current.Background;
        public static Color BackgroundGlowColor => _current.BackgroundGlow;
        public static Color HighwayColor => _current.Highway;
        public static Color HighwayEdgeColor => _current.HighwayEdge;
        public static Color HitLineColor => _current.HitLine;
        public static Color AccentColor => _current.Accent;
        public static Color BeamColorA => _current.BeamA;
        public static Color BeamColorB => _current.BeamB;
        public static Color CrowdColor => _current.Crowd;
    }
}
