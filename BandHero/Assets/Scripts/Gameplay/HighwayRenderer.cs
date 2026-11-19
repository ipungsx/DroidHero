using System.Collections.Generic;
using UnityEngine;
using BandHero.Chart;
using BandHero.UI;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Draws the 2D note highway: perspective lane backdrop, strike line, frets and every note.
    ///
    /// Rendering is stateless with respect to gameplay: it reads the tracker and redraws from
    /// scratch each frame, so a dropped frame can never desync the visuals from the music.
    /// Every sprite is pooled or built once, so a frame allocates nothing.
    /// </summary>
    public class HighwayRenderer : MonoBehaviour
    {
        const float LaneSegments = 10f;

        public HighwayLayout Layout = new HighwayLayout();

        readonly List<ActiveNote> _visible = new List<ActiveNote>(256);
        readonly SpriteRenderer[] _laneBackdrops = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _laneFlashes = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _laneEdges = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _fretButtons = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _fretGlows = new SpriteRenderer[LaneInfo.Count];

        readonly float[] _laneFlashTimers = new float[LaneInfo.Count];
        readonly float[] _fretPressScale = new float[LaneInfo.Count];

        NoteViewPool _pool;
        NoteTracker _tracker;
        Transform _noteRoot;

        SpriteRenderer _hitLine;
        SpriteRenderer _hitLineGlow;
        SpriteRenderer _starPowerOverlay;
        SpriteRenderer _strikePulse;

        float _pulseTimer;
        float _beatTimer;
        float _songTime;

        public void Initialize(NoteTracker tracker)
        {
            _tracker = tracker;
            BuildStaticElements();
            _pool = new NoteViewPool(_noteRoot, 96, 20);
        }

        void BuildStaticElements()
        {
            var backdrop = new GameObject("Highway");
            backdrop.transform.SetParent(transform, false);

            // Segment quads give the highway its stage perspective: each strip is narrower than
            // the one below it, so the lanes converge toward the far end of the stage.
            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                Color laneColor = lane % 2 == 0
                    ? new Color(1f, 1f, 1f, 0.045f)
                    : new Color(0f, 0f, 0f, 0.16f);

                _laneBackdrops[lane] = BuildLaneColumn(backdrop.transform, "Lane" + lane, laneColor, 1);

                // A bright edge on the two outer lanes frames the playfield.
                if (lane == 0 || lane == LaneInfo.Count - 1)
                {
                    _laneEdges[lane] = BuildLaneEdge(backdrop.transform, lane);
                }

                var flash = new GameObject("LaneFlash" + lane);
                flash.transform.SetParent(backdrop.transform, false);
                var flashRenderer = flash.AddComponent<SpriteRenderer>();
                flashRenderer.sprite = ProceduralSprites.VerticalFade;
                flashRenderer.color = new Color(1f, 1f, 1f, 0f);
                flashRenderer.sortingOrder = 2;
                _laneFlashes[lane] = flashRenderer;
                PositionLaneQuad(flash.transform, lane, 0f, Layout.HighwayHeight);
            }

            var hitLine = new GameObject("HitLine");
            hitLine.transform.SetParent(backdrop.transform, false);
            _hitLine = hitLine.AddComponent<SpriteRenderer>();
            _hitLine.sprite = ProceduralSprites.White;
            _hitLine.color = LaneTheme.HitLineColor;
            _hitLine.sortingOrder = 30;
            _hitLine.transform.position = new Vector3(0f, Layout.HitLineY, 0f);
            _hitLine.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, 0.045f, 1f);

            var hitGlow = new GameObject("HitLineGlow");
            hitGlow.transform.SetParent(backdrop.transform, false);
            _hitLineGlow = hitGlow.AddComponent<SpriteRenderer>();
            _hitLineGlow.sprite = ProceduralSprites.White;
            _hitLineGlow.color = new Color(LaneTheme.HitLineColor.r, LaneTheme.HitLineColor.g,
                                           LaneTheme.HitLineColor.b, 0.16f);
            _hitLineGlow.sortingOrder = 29;
            _hitLineGlow.transform.position = new Vector3(0f, Layout.HitLineY, 0f);
            _hitLineGlow.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, 0.34f, 1f);

            var strike = new GameObject("StrikePulse");
            strike.transform.SetParent(backdrop.transform, false);
            _strikePulse = strike.AddComponent<SpriteRenderer>();
            _strikePulse.sprite = ProceduralSprites.White;
            _strikePulse.color = new Color(1f, 1f, 1f, 0f);
            _strikePulse.sortingOrder = 31;
            _strikePulse.transform.position = new Vector3(0f, Layout.HitLineY, 0f);
            _strikePulse.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, 0.12f, 1f);

            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                var fretGlow = new GameObject("FretGlow" + lane);
                fretGlow.transform.SetParent(backdrop.transform, false);
                var glowRenderer = fretGlow.AddComponent<SpriteRenderer>();
                glowRenderer.sprite = ProceduralSprites.Glow;
                glowRenderer.color = new Color(1f, 1f, 1f, 0.20f);
                glowRenderer.sortingOrder = 24;
                glowRenderer.transform.position = new Vector3(Layout.LaneCenterX(lane), Layout.HitLineY, 0f);
                glowRenderer.transform.localScale = Vector3.one * (Layout.LaneWidth * 1.55f);
                _fretGlows[lane] = glowRenderer;

                var fret = new GameObject("Fret" + lane);
                fret.transform.SetParent(backdrop.transform, false);
                var fretRenderer = fret.AddComponent<SpriteRenderer>();
                fretRenderer.sprite = ProceduralSprites.Circle;
                fretRenderer.color = LaneTheme.DimForLane(lane);
                fretRenderer.sortingOrder = 25;
                fretRenderer.transform.position = new Vector3(Layout.LaneCenterX(lane), Layout.HitLineY, 0f);
                fretRenderer.transform.localScale = Vector3.one * (Layout.LaneWidth * 0.70f);
                _fretButtons[lane] = fretRenderer;
                _fretPressScale[lane] = 0.70f;
            }

            var overlay = new GameObject("StarPowerOverlay");
            overlay.transform.SetParent(backdrop.transform, false);
            _starPowerOverlay = overlay.AddComponent<SpriteRenderer>();
            _starPowerOverlay.sprite = ProceduralSprites.White;
            _starPowerOverlay.color = new Color(0f, 0f, 0f, 0f);
            _starPowerOverlay.sortingOrder = 3;
            _starPowerOverlay.transform.position = new Vector3(0f, (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
            _starPowerOverlay.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, Layout.HighwayHeight, 1f);

            var noteRoot = new GameObject("Notes");
            noteRoot.transform.SetParent(transform, false);
            _noteRoot = noteRoot.transform;
        }

        /// <summary>
        /// Builds one lane as a stack of quads, each sized and positioned for the perspective at
        /// its own height, so the lane edges converge toward the far end of the stage.
        /// </summary>
        SpriteRenderer BuildLaneColumn(Transform parent, string name, Color color, int order)
        {
            var column = new GameObject(name);
            column.transform.SetParent(parent, false);

            int segments = Mathf.RoundToInt(LaneSegments);
            float segmentHeight = Layout.HighwayHeight / segments;
            SpriteRenderer first = null;

            for (int i = 0; i < segments; i++)
            {
                float bottom = i * segmentHeight;
                float midY = Layout.HitLineY + bottom + segmentHeight * 0.5f;

                var segment = new GameObject("Segment" + i);
                segment.transform.SetParent(column.transform, false);

                var renderer = segment.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.White;
                renderer.color = color;
                renderer.sortingOrder = order;

                // The quad is one unit tall and stretched; overlapping slightly hides the seams.
                renderer.transform.position = new Vector3(
                    Layout.LaneCenterXAt(LaneOf(name), midY), midY, 0f);
                renderer.transform.localScale = new Vector3(
                    Layout.LaneWidthAt(midY) * 0.94f, segmentHeight * 1.06f, 1f);

                if (first == null) first = renderer;
            }

            return first;
        }

        static int LaneOf(string laneName)
        {
            for (int i = 0; i < LaneInfo.Count; i++)
            {
                if (laneName == "Lane" + i) return i;
            }
            return 0;
        }

        SpriteRenderer BuildLaneEdge(Transform parent, int lane)
        {
            var edge = new GameObject("LaneEdge" + lane);
            edge.transform.SetParent(parent, false);

            var renderer = edge.AddComponent<SpriteRenderer>();
            renderer.sprite = ProceduralSprites.White;
            renderer.color = LaneTheme.HighwayEdgeColor;
            renderer.sortingOrder = 2;

            float x = lane == 0 ? Layout.HighwayLeftX : Layout.HighwayRightX;
            renderer.transform.position = new Vector3(x, (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
            renderer.transform.localScale = new Vector3(0.035f, Layout.HighwayHeight, 1f);

            return renderer;
        }

        /// <summary>Places a lane-wide quad between two heights, following the perspective taper.</summary>
        void PositionLaneQuad(Transform quad, int lane, float fromHeight, float toHeight)
        {
            float bottomY = Layout.HitLineY + fromHeight;
            float topY = Layout.HitLineY + toHeight;
            float midY = (bottomY + topY) * 0.5f;

            quad.position = new Vector3(Layout.LaneCenterXAt(lane, midY), midY, 0f);
            quad.localScale = new Vector3(Layout.LaneWidthAt(midY) * 0.94f, topY - bottomY, 1f);
        }

        /// <summary>Pulses a lane to acknowledge a hit.</summary>
        public void FlashLane(int lane)
        {
            if (lane < 0 || lane >= _laneFlashTimers.Length) return;
            _laneFlashTimers[lane] = 0.14f;
        }

        public void SetStarPowerActive(bool active)
        {
            if (_starPowerOverlay == null) return;

            _starPowerOverlay.color = active
                ? new Color(LaneTheme.StarPowerColor.r, LaneTheme.StarPowerColor.g,
                            LaneTheme.StarPowerColor.b, 0.10f)
                : new Color(0f, 0f, 0f, 0f);
        }

        /// <summary>Called once per drum hit so the strike line breathes with the beat.</summary>
        public void PulseStrikeLine()
        {
            _beatTimer = 0.18f;
        }

        public void Render(float songTime, IList<bool> heldLanes)
        {
            if (_tracker == null || _pool == null) return;

            _songTime = songTime;
            _pool.ReleaseAll();

            float fromTime = songTime - 0.35f;
            float toTime = songTime + Layout.ApproachSeconds + 0.35f;
            _tracker.CollectVisible(fromTime, toTime, _visible);

            for (int i = 0; i < _visible.Count; i++)
            {
                DrawNote(_visible[i], songTime);
            }

            UpdateLaneFeedback(heldLanes);
        }

        void DrawNote(ActiveNote note, float songTime)
        {
            float headY = Layout.TimeToY(note.Time, songTime);
            float laneX = Layout.LaneCenterXAt(note.Lane, headY);

            // Notes shrink toward the far end of the stage, which is what sells the depth.
            float radius = Layout.LaneWidthAt(headY) * 0.40f;

            NoteViewPool.View view = _pool.Acquire();

            bool hit = note.State == NoteState.Hit;
            Color color = LaneTheme.ForLane(note.Lane);

            if (note.IsSustain && !note.SustainBroken)
            {
                float tailY = Layout.TimeToY(note.EndTime, songTime);
                float sustainHeight = Mathf.Max(0f, Mathf.Min(tailY, Layout.TopY + radius) - headY);

                if (sustainHeight > 0.02f)
                {
                    Color sustainColor = color;
                    sustainColor.a = hit ? 0.55f : 0.32f;

                    view.Sustain.enabled = true;
                    view.Sustain.color = sustainColor;
                    view.Sustain.transform.position = new Vector3(laneX, headY + sustainHeight * 0.5f, 0f);
                    view.Sustain.transform.localScale = new Vector3(
                        radius * 1.15f, sustainHeight, 1f);
                }
            }

            if (hit)
            {
                // A hit note blooms white and rushes outward, then fades on the next frames.
                float progress = Mathf.Clamp01((songTime - note.Time) / 0.22f);
                float fade = 1f - progress;

                color = Color.Lerp(color, Color.white, 0.75f);
                color.a = fade;

                view.Body.color = color;
                view.Body.transform.position = new Vector3(laneX, headY, 0f);
                view.Body.transform.localScale = Vector3.one * (radius * 2f * (1f + progress * 0.55f));

                view.Halo.color = new Color(color.r, color.g, color.b, 0.55f * fade);
                view.Halo.transform.position = new Vector3(laneX, headY, 0f);
                view.Halo.transform.localScale = Vector3.one * (radius * 6f * (1f + progress));

                view.Core.color = new Color(1f, 1f, 1f, fade);
                view.Core.transform.position = new Vector3(laneX, headY, 0f);
                view.Core.transform.localScale = Vector3.one * (radius * 1.6f);

                view.Ripple.enabled = true;
                view.Ripple.color = new Color(color.r, color.g, color.b, 0.75f * fade);
                view.Ripple.transform.position = new Vector3(laneX, headY, 0f);
                view.Ripple.transform.localScale = Vector3.one * (radius * 4f * (1f + progress * 1.6f));

                if (fade <= 0.01f) view.Root.SetActive(false);
                return;
            }

            float pulse = 1f + 0.06f * Mathf.Sin((songTime * 6f) + note.Lane);

            if (note.Note.StarPower)
            {
                // Star power notes carry a brighter, wider halo so they stand out in a run.
                Color halo = LaneTheme.StarPowerColor;
                view.Halo.color = new Color(halo.r, halo.g, halo.b, 0.85f);
                view.Halo.transform.localScale = Vector3.one * (radius * 6.2f * pulse);
            }
            else
            {
                Color halo = color;
                view.Halo.color = new Color(halo.r, halo.g, halo.b, 0.55f);
                view.Halo.transform.localScale = Vector3.one * (radius * 4.6f * pulse);
            }

            view.Halo.transform.position = new Vector3(laneX, headY, 0f);

            view.Body.color = color;
            view.Body.transform.position = new Vector3(laneX, headY, 0f);

            // HOPO notes are drawn smaller with a brighter core, the usual visual cue.
            float hopoScale = note.Note.Hopo ? 0.78f : 1f;
            view.Body.transform.localScale = Vector3.one * (radius * 2f * hopoScale * pulse);

            Color core = Color.Lerp(color, Color.white, note.Note.Hopo ? 0.85f : 0.55f);
            view.Core.color = new Color(core.r, core.g, core.b, note.Note.Hopo ? 0.95f : 0.75f);
            view.Core.transform.position = new Vector3(laneX, headY, 0f);
            view.Core.transform.localScale = Vector3.one * (radius * (note.Note.Hopo ? 1.25f : 1.0f) * pulse);
        }

        void UpdateLaneFeedback(IList<bool> heldLanes)
        {
            float delta = Time.deltaTime;

            for (int lane = 0; lane < _laneFlashes.Length; lane++)
            {
                if (_laneFlashTimers[lane] > 0f)
                {
                    _laneFlashTimers[lane] -= delta;

                    float alpha = Mathf.Clamp01(_laneFlashTimers[lane] / 0.14f) * 0.45f;
                    Color flashColor = LaneTheme.ForLane(lane);
                    flashColor.a = alpha;
                    _laneFlashes[lane].color = flashColor;
                    PositionLaneQuad(_laneFlashes[lane].transform, lane, 0f, Layout.HighwayHeight);
                }
                else if (_laneFlashes[lane].color.a > 0f)
                {
                    Color c = _laneFlashes[lane].color;
                    c.a = 0f;
                    _laneFlashes[lane].color = c;
                }
            }

            if (_beatTimer > 0f)
            {
                _beatTimer -= delta;
                float glow = Mathf.Clamp01(_beatTimer / 0.18f);
                _hitLineGlow.color = new Color(LaneTheme.HitLineColor.r, LaneTheme.HitLineColor.g,
                                               LaneTheme.HitLineColor.b, 0.16f + glow * 0.34f);
            }

            if (_pulseTimer > 0f)
            {
                _pulseTimer -= delta;
                float t = Mathf.Clamp01(_pulseTimer / 0.2f);
                _strikePulse.color = new Color(1f, 1f, 1f, t * 0.22f);
                _strikePulse.transform.localScale = new Vector3(
                    Layout.HighwayRightX - Layout.HighwayLeftX, 0.12f + (1f - t) * 0.30f, 1f);
            }
            else if (_strikePulse.color.a > 0f)
            {
                _strikePulse.color = new Color(1f, 1f, 1f, 0f);
            }

            if (heldLanes == null) return;

            for (int lane = 0; lane < _fretButtons.Length && lane < heldLanes.Count; lane++)
            {
                if (_fretButtons[lane] == null) continue;

                bool held = heldLanes[lane];
                float target = held ? 0.86f : 0.70f;

                // Ease the press instead of snapping, so a fast run reads as motion.
                _fretPressScale[lane] = Mathf.Lerp(_fretPressScale[lane], target, delta * 18f);

                _fretButtons[lane].color = held
                    ? Color.Lerp(LaneTheme.ForLane(lane), Color.white, 0.45f)
                    : LaneTheme.DimForLane(lane);

                _fretButtons[lane].transform.localScale =
                    Vector3.one * (Layout.LaneWidth * _fretPressScale[lane]);

                Color fretGlow = LaneTheme.ForLane(lane);
                fretGlow.a = held ? 0.75f : 0.20f;
                _fretGlows[lane].color = fretGlow;
                _fretGlows[lane].transform.localScale =
                    Vector3.one * (Layout.LaneWidth * (held ? 1.9f : 1.55f));
            }
        }
    }
}
