using UnityEngine;
using BandHero.Chart;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Maps song time to screen space for the note highway.
    ///
    /// The highway is drawn in world units with the hit line at a fixed height, and notes
    /// approach from the top. <see cref="ApproachSeconds"/> controls how far ahead the player
    /// can see, which is the single most important readability setting for a rhythm game.
    /// </summary>
    public class HighwayLayout
    {
        public float ApproachSeconds = 1.6f;
        public float HitLineY = -3.4f;
        public float TopY = 4.6f;
        public float LaneWidth = 1.1f;
        public float NoteHeight = 0.34f;

        public float HighwayHeight => TopY - HitLineY;

        public float LaneCenterX(int lane)
        {
            float total = LaneWidth * LaneInfo.Count;
            float left = -total * 0.5f + LaneWidth * 0.5f;
            return left + lane * LaneWidth;
        }

        public float HighwayLeftX => LaneCenterX(0) - LaneWidth * 0.5f;
        public float HighwayRightX => LaneCenterX(LaneInfo.Count - 1) + LaneWidth * 0.5f;

        /// <summary>
        /// Y position for a note at the given time. Notes at the hit line land exactly on
        /// <see cref="HitLineY"/>; notes further in the future sit higher up the screen.
        /// </summary>
        public float TimeToY(float noteTime, float songTime)
        {
            float delta = noteTime - songTime;
            float normalized = delta / Mathf.Max(0.05f, ApproachSeconds);
            return HitLineY + normalized * HighwayHeight;
        }

        /// <summary>Inverse of <see cref="TimeToY"/>, used for hit-testing taps.</summary>
        public float YToTime(float y, float songTime)
        {
            float normalized = (y - HitLineY) / HighwayHeight;
            return songTime + normalized * ApproachSeconds;
        }

        public bool IsVisible(float y) => y >= HitLineY - NoteHeight && y <= TopY + NoteHeight;

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

        /// <summary>Lane under a world-space point, or -1 when the point is outside the highway.</summary>
        public int LaneAtX(float x)
        {
            if (x < HighwayLeftX || x > HighwayRightX) return -1;

            int lane = Mathf.FloorToInt((x - HighwayLeftX) / LaneWidth);
            return Mathf.Clamp(lane, 0, LaneInfo.Count - 1);
        }
    }
}
