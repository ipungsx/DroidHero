using UnityEngine;

namespace DroidHero.UI
{
    /// <summary>
    /// A complete visual identity for the highway: lane colours, backdrop, light beams and the
    /// accent used for chrome. Swapping themes must not touch gameplay, so everything here is
    /// presentation only.
    /// </summary>
    public class GameplayTheme
    {
        public string Id;
        public string Name;
        public string Blurb;

        /// <summary>Five fret colours, green to orange by convention.</summary>
        public Color[] Lanes;

        public Color Background;
        public Color BackgroundGlow;
        public Color Highway;
        public Color HighwayEdge;
        public Color HitLine;
        public Color Accent;
        public Color BeamA;
        public Color BeamB;
        public Color Crowd;
        public Color StarPower;

        /// <summary>Lane colours dimmed to the level used for an unpressed fret button.</summary>
        public Color Dim(int lane)
        {
            Color c = Lanes[Mathf.Clamp(lane, 0, Lanes.Length - 1)];
            return new Color(c.r * 0.34f, c.g * 0.34f, c.b * 0.34f, 1f);
        }

        public Color Lane(int lane) => Lanes[Mathf.Clamp(lane, 0, Lanes.Length - 1)];

        public static readonly GameplayTheme[] All =
        {
            new GameplayTheme
            {
                Id = "neon",
                Name = "Neon Stage",
                Blurb = "Classic five-fret colours on a violet club stage.",
                Lanes = new[]
                {
                    new Color(0.30f, 0.92f, 0.42f),
                    new Color(0.96f, 0.26f, 0.36f),
                    new Color(0.99f, 0.86f, 0.24f),
                    new Color(0.32f, 0.58f, 0.99f),
                    new Color(0.98f, 0.55f, 0.16f),
                },
                Background = new Color(0.045f, 0.035f, 0.09f),
                BackgroundGlow = new Color(0.24f, 0.10f, 0.46f),
                Highway = new Color(0.07f, 0.06f, 0.12f, 0.94f),
                HighwayEdge = new Color(0.62f, 0.40f, 1f, 0.55f),
                HitLine = new Color(1f, 1f, 1f, 0.92f),
                Accent = new Color(0.72f, 0.44f, 1f),
                BeamA = new Color(0.72f, 0.35f, 1f),
                BeamB = new Color(0.25f, 0.75f, 1f),
                Crowd = new Color(0.05f, 0.04f, 0.10f),
                StarPower = new Color(0.66f, 0.88f, 1f),
            },
            new GameplayTheme
            {
                Id = "sunset",
                Name = "Sunset Drive",
                Blurb = "Warm retro gradients over a synthwave skyline.",
                Lanes = new[]
                {
                    new Color(0.42f, 0.95f, 0.72f),
                    new Color(1.00f, 0.42f, 0.52f),
                    new Color(1.00f, 0.90f, 0.42f),
                    new Color(0.60f, 0.62f, 1.00f),
                    new Color(1.00f, 0.62f, 0.32f),
                },
                Background = new Color(0.10f, 0.045f, 0.13f),
                BackgroundGlow = new Color(0.85f, 0.30f, 0.35f),
                Highway = new Color(0.12f, 0.05f, 0.14f, 0.93f),
                HighwayEdge = new Color(1f, 0.55f, 0.55f, 0.55f),
                HitLine = new Color(1f, 0.94f, 0.80f, 0.94f),
                Accent = new Color(1f, 0.55f, 0.60f),
                BeamA = new Color(1f, 0.45f, 0.55f),
                BeamB = new Color(1f, 0.78f, 0.35f),
                Crowd = new Color(0.10f, 0.03f, 0.11f),
                StarPower = new Color(1f, 0.86f, 0.62f),
            },
            new GameplayTheme
            {
                Id = "frost",
                Name = "Frostbite",
                Blurb = "Cold cyan light with a glassy highway.",
                Lanes = new[]
                {
                    new Color(0.55f, 1.00f, 0.85f),
                    new Color(0.45f, 0.85f, 1.00f),
                    new Color(0.85f, 0.95f, 1.00f),
                    new Color(0.60f, 0.70f, 1.00f),
                    new Color(0.42f, 0.95f, 0.98f),
                },
                Background = new Color(0.02f, 0.07f, 0.12f),
                BackgroundGlow = new Color(0.10f, 0.45f, 0.65f),
                Highway = new Color(0.04f, 0.11f, 0.18f, 0.92f),
                HighwayEdge = new Color(0.55f, 0.90f, 1f, 0.55f),
                HitLine = new Color(0.90f, 1f, 1f, 0.95f),
                Accent = new Color(0.55f, 0.92f, 1f),
                BeamA = new Color(0.40f, 0.85f, 1f),
                BeamB = new Color(0.70f, 1f, 0.95f),
                Crowd = new Color(0.02f, 0.06f, 0.10f),
                StarPower = new Color(0.80f, 1f, 1f),
            },
            new GameplayTheme
            {
                Id = "inferno",
                Name = "Inferno",
                Blurb = "Molten red stage lights and ember haze.",
                Lanes = new[]
                {
                    new Color(1.00f, 0.85f, 0.35f),
                    new Color(1.00f, 0.35f, 0.28f),
                    new Color(1.00f, 0.62f, 0.20f),
                    new Color(0.95f, 0.30f, 0.55f),
                    new Color(1.00f, 0.45f, 0.15f),
                },
                Background = new Color(0.10f, 0.02f, 0.02f),
                BackgroundGlow = new Color(0.75f, 0.16f, 0.05f),
                Highway = new Color(0.13f, 0.03f, 0.03f, 0.94f),
                HighwayEdge = new Color(1f, 0.48f, 0.22f, 0.55f),
                HitLine = new Color(1f, 0.92f, 0.75f, 0.95f),
                Accent = new Color(1f, 0.50f, 0.25f),
                BeamA = new Color(1f, 0.35f, 0.12f),
                BeamB = new Color(1f, 0.72f, 0.25f),
                Crowd = new Color(0.08f, 0.01f, 0.01f),
                StarPower = new Color(1f, 0.85f, 0.55f),
            },
            new GameplayTheme
            {
                Id = "mono",
                Name = "Monochrome",
                Blurb = "High-contrast greys, easiest to read in daylight.",
                Lanes = new[]
                {
                    new Color(0.95f, 0.95f, 0.95f),
                    new Color(0.78f, 0.78f, 0.80f),
                    new Color(1.00f, 1.00f, 1.00f),
                    new Color(0.62f, 0.64f, 0.68f),
                    new Color(0.86f, 0.86f, 0.90f),
                },
                Background = new Color(0.06f, 0.06f, 0.07f),
                BackgroundGlow = new Color(0.30f, 0.31f, 0.34f),
                Highway = new Color(0.11f, 0.11f, 0.12f, 0.94f),
                HighwayEdge = new Color(0.85f, 0.85f, 0.90f, 0.45f),
                HitLine = new Color(1f, 1f, 1f, 0.95f),
                Accent = new Color(0.88f, 0.88f, 0.92f),
                BeamA = new Color(0.75f, 0.75f, 0.80f),
                BeamB = new Color(0.95f, 0.95f, 1.00f),
                Crowd = new Color(0.04f, 0.04f, 0.05f),
                StarPower = new Color(0.92f, 0.92f, 1f),
            },
        };

        public static int Count => All.Length;

        /// <summary>Theme for an index, wrapped so a stale saved index can never go out of range.</summary>
        public static GameplayTheme At(int index)
        {
            if (All.Length == 0) return null;

            int wrapped = index % All.Length;
            if (wrapped < 0) wrapped += All.Length;
            return All[wrapped];
        }

        public static GameplayTheme ById(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return All[i];
            }
            return All[0];
        }

        public static int IndexOfId(string id)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Id, id, System.StringComparison.OrdinalIgnoreCase)) return i;
            }
            return 0;
        }
    }
}
