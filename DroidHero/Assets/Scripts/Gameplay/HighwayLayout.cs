using UnityEngine;
using DroidHero.Chart;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// Maps song time to screen space for the note highway.
    ///
    /// The highway is a plane running away from the player, so a note's screen position is a
    /// perspective projection, not a linear one. <see cref="ApproachSeconds"/> controls how far
    /// ahead the player can see, which is the single most important readability setting for a
    /// rhythm game.
    /// </summary>
    public class HighwayLayout
    {
        public float ApproachSeconds = 1.6f;
        public float HitLineY = -3.4f;
        public float TopY = 4.6f;
        public float LaneWidth = 1.1f;
        public float NoteHeight = 0.34f;

        /// <summary>
        /// How much further away the far end of the highway is than the strike line, as a
        /// depth ratio. Notes move at a constant speed through the world, so their on-screen
        /// speed comes from this: the projection divides by depth, which makes a note crawl
        /// near the horizon and rush at the player. A linear time-to-Y ramp instead reads as
        /// notes sliding down a flat wall.
        /// </summary>
        public float DepthRatio = 2.6f;

        /// <summary>
        /// Width of the highway at the top, as a fraction of its width at the strike line.
        ///
        /// Notes travel from the far end of a stage toward the player, so the far end has to be
        /// narrower. This is the whole 2D perspective effect: one lerp on the lane centre.
        /// </summary>
        public float PerspectiveScale = 0.52f;

        public float HighwayHeight => TopY - HitLineY;

        /// <summary>Lane width multiplier at a given height; 1 at the strike line, tapering upward.</summary>
        public float WidthScaleAt(float y)
        {
            float t = Mathf.Clamp01((y - HitLineY) / Mathf.Max(0.0001f, HighwayHeight));
            return Mathf.Lerp(1f, PerspectiveScale, t);
        }

        /// <summary>Lane centre at the strike line.</summary>
        public float LaneCenterX(int lane) => LaneCenterXAt(lane, HitLineY);

        /// <summary>Lane centre at an arbitrary height, following the perspective taper.</summary>
        public float LaneCenterXAt(int lane, float y)
        {
            float total = LaneWidth * LaneInfo.Count;
            float left = -total * 0.5f + LaneWidth * 0.5f;
            float hitLineX = left + lane * LaneWidth;
            return hitLineX * WidthScaleAt(y);
        }

        /// <summary>Half-width of a lane at an arbitrary height, used to size note heads.</summary>
        public float LaneWidthAt(float y) => LaneWidth * WidthScaleAt(y);

        public float HighwayLeftX => LaneCenterX(0) - LaneWidth * 0.5f;
        public float HighwayRightX => LaneCenterX(LaneInfo.Count - 1) + LaneWidth * 0.5f;

        public float HighwayLeftXAt(float y) => LaneCenterXAt(0, y) - LaneWidthAt(y) * 0.5f;
        public float HighwayRightXAt(float y) => LaneCenterXAt(LaneInfo.Count - 1, y) + LaneWidthAt(y) * 0.5f;

        /// <summary>
        /// Reciprocal depth of a note <paramref name="delta"/> seconds from the strike line,
        /// normalised so the strike line is 0 and the far end is 1. This is the projection
        /// itself; everything else here is that value scaled onto the highway.
        /// </summary>
        float ScreenFraction(float delta)
        {
            float u = delta / Mathf.Max(0.05f, ApproachSeconds);

            // A note that has just been struck is behind the near plane, where the reciprocal
            // blows up and would fling its hit flash off the bottom of the screen. Past notes
            // are held to a short ramp instead, so the flash stays where the player hit it.
            if (u < 0f) return Mathf.Max(-0.15f, u * 0.9f);

            float depth = 1f + (DepthRatio - 1f) * u;
            float reciprocal = 1f / depth;

            float near = 1f;
            float far = 1f / DepthRatio;
            return (reciprocal - near) / (far - near);
        }

        /// <summary>
        /// Y position for a note at the given time. Notes at the hit line land exactly on
        /// <see cref="HitLineY"/>; notes further in the future sit further up the highway,
        /// converging on <see cref="TopY"/> as they approach the horizon.
        /// </summary>
        public float TimeToY(float noteTime, float songTime) =>
            HitLineY + ScreenFraction(noteTime - songTime) * HighwayHeight;

        /// <summary>Inverse of <see cref="TimeToY"/>, used for hit-testing taps.</summary>
        public float YToTime(float y, float songTime)
        {
            float fraction = (y - HitLineY) / HighwayHeight;

            float near = 1f;
            float far = 1f / DepthRatio;
            float reciprocal = near + fraction * (far - near);

            if (reciprocal <= 0.0001f) return songTime + ApproachSeconds * 8f;

            float depth = 1f / reciprocal;
            float u = (depth - 1f) / (DepthRatio - 1f);
            return songTime + u * ApproachSeconds;
        }

        /// <summary>
        /// True while a note is on the highway. The far end is a horizon, so anything past it
        /// is clipped: a note that is allowed to appear above the horizon looks like it is
        /// falling in from the sky instead of arriving from the front.
        /// </summary>
        public bool IsVisible(float y) => y >= HitLineY - NoteHeight && y <= TopY + 0.001f;

        /// <summary>
        /// Smallest orthographic size that still shows the whole highway at the given aspect.
        ///
        /// A fixed size clips the outer lanes on tall phones: the visible half-width is
        /// size * aspect, and a 1080x2520 screen at size 6.4 only reaches 2.74 units where the
        /// highway needs 2.75.
        /// </summary>
        public float RequiredOrthographicSize(float aspect, float horizontalMargin = 0.2f)
        {
            float halfWidth = Mathf.Max(Mathf.Abs(HighwayLeftX), Mathf.Abs(HighwayRightX));
            if (aspect <= 0.01f) return TopY;

            return (halfWidth + horizontalMargin) / aspect;
        }

        /// <summary>Lane under a world-space point at the strike line, or -1 when outside.</summary>
        public int LaneAtX(float x) => LaneAtXAt(x, HitLineY);

        /// <summary>
        /// Lane under a point at a given height. Because the highway narrows toward the top, the
        /// same screen X can be lane 0 near the strike line and outside the highway far above it.
        /// </summary>
        public int LaneAtXAt(float x, float y)
        {
            float left = HighwayLeftXAt(y);
            float width = LaneWidthAt(y);
            if (x < left || x > left + width * LaneInfo.Count) return -1;

            int lane = Mathf.FloorToInt((x - left) / width);
            return Mathf.Clamp(lane, 0, LaneInfo.Count - 1);
        }
    }
}
