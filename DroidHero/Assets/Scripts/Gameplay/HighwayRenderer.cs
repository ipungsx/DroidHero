using System.Collections.Generic;
using UnityEngine;
using DroidHero.Chart;
using DroidHero.UI;

namespace DroidHero.Gameplay
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

        /// <summary>
        /// Fret button size at rest, as a fraction of the lane width. Pressing pops the cap up
        /// and toward the player, so a tap reads as a physical button being struck rather than
        /// as a colour change.
        /// </summary>
        const float FretRestScale = 0.86f;
        const float FretPressedScale = 1.00f;

        /// <summary>How far a pressed fret lifts toward the camera, in world units.</summary>
        const float FretLift = 0.16f;

        /// <summary>Spring constants for the press pop: stiff enough to snap, damped to settle.</summary>
        const float FretSpringStiffness = 420f;
        const float FretSpringDamping = 21f;

        /// <summary>
        /// Largest step the press spring may take. An explicit Euler step on a spring of
        /// stiffness k is stable only while delta &lt; 2/sqrt(k); at 420 that is 0.098 s, so a
        /// single slow frame would otherwise diverge and freeze the cap at a huge scale.
        /// </summary>
        const float MaxSpringStep = 0.02f;

        public HighwayLayout Layout = new HighwayLayout();

        readonly List<ActiveNote> _visible = new List<ActiveNote>(256);
        readonly bool[] _fretCustomArt = new bool[LaneInfo.Count];
        readonly SpriteRenderer[] _laneBackdrops = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _laneFlashes = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _laneEdges = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _fretButtons = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _fretGlows = new SpriteRenderer[LaneInfo.Count];

        readonly float[] _laneFlashTimers = new float[LaneInfo.Count];
        readonly float[] _fretPressScale = new float[LaneInfo.Count];
        readonly float[] _fretPressVelocity = new float[LaneInfo.Count];
        readonly SpriteRenderer[] _fretShadows = new SpriteRenderer[LaneInfo.Count];

        NoteViewPool _pool;
        NoteTracker _tracker;
        Transform _noteRoot;

        SpriteRenderer _hitLine;
        SpriteRenderer _hitLineGlow;
        SpriteRenderer _starPowerOverlay;
        SpriteRenderer _overdriveOverlay;
        SpriteRenderer _strikePulse;

        float _pulseTimer;
        float _beatTimer;
        float _songTime;
        float _overdrivePulse;
        bool _overdriveActive;

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

            // The stage behind the highway is deliberately bright, so the highway itself has to be
            // a dark surface or the gems stop reading against it. This opaque floor is the single
            // biggest readability win: notes are now bright shapes on dark ground.
            Color floorColor = LaneTheme.HighwayColor;
            floorColor.a = 1f;

            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                BuildLaneColumn(backdrop.transform, "Lane" + lane, floorColor, 0, 1f);
            }

            // Alternating lane stripes sit on the floor to separate the five lanes without
            // needing a visible outline on every one of them.
            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                Color laneColor = lane % 2 == 0
                    ? new Color(1f, 1f, 1f, 0.055f)
                    : new Color(0f, 0f, 0f, 0.22f);

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

            BuildLaneSeparators(backdrop.transform);

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

                // A dark shadow disc under the cap. It is what sells the button as sitting on
                // top of the highway, and it grows as the cap sinks, so the press has depth.
                var shadow = new GameObject("FretShadow" + lane);
                shadow.transform.SetParent(backdrop.transform, false);
                var shadowRenderer = shadow.AddComponent<SpriteRenderer>();
                shadowRenderer.sprite = ProceduralSprites.Circle;
                shadowRenderer.color = new Color(0f, 0f, 0f, 0.55f);
                shadowRenderer.sortingOrder = 24;
                shadowRenderer.transform.position = new Vector3(Layout.LaneCenterX(lane),
                                                                Layout.HitLineY - FretLift * 0.35f, 0f);
                shadowRenderer.transform.localScale = Vector3.one * (Layout.LaneWidth * FretRestScale * 1.04f);
                _fretShadows[lane] = shadowRenderer;

                var fret = new GameObject("Fret" + lane);
                fret.transform.SetParent(backdrop.transform, false);
                var fretRenderer = fret.AddComponent<SpriteRenderer>();
                fretRenderer.sprite = ProceduralSprites.FretArt(lane);
                _fretCustomArt[lane] = ProceduralSprites.HasCustomFretArt(lane);
                fretRenderer.color = _fretCustomArt[lane]
                    ? Color.white
                    : LaneTheme.DimForLane(lane);
                fretRenderer.sortingOrder = 25;
                fretRenderer.transform.position = new Vector3(Layout.LaneCenterX(lane), Layout.HitLineY, 0f);
                fretRenderer.transform.localScale = Vector3.one * (Layout.LaneWidth * FretRestScale);
                _fretButtons[lane] = fretRenderer;
                _fretPressScale[lane] = FretRestScale;
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

            // Overdrive tint is its own overlay above the star power one, so both can be on
            // without either having to remember the other's colour.
            var od = new GameObject("OverdriveOverlay");
            od.transform.SetParent(backdrop.transform, false);
            _overdriveOverlay = od.AddComponent<SpriteRenderer>();
            _overdriveOverlay.sprite = ProceduralSprites.White;
            _overdriveOverlay.color = new Color(0f, 0f, 0f, 0f);
            _overdriveOverlay.sortingOrder = 4;
            _overdriveOverlay.transform.position = new Vector3(0f, (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
            _overdriveOverlay.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, Layout.HighwayHeight, 1f);

            var noteRoot = new GameObject("Notes");
            noteRoot.transform.SetParent(transform, false);
            _noteRoot = noteRoot.transform;
        }

        /// <summary>
        /// Builds one lane as a stack of quads, each sized and positioned for the perspective at
        /// its own height, so the lane edges converge toward the far end of the stage.
        /// </summary>
        SpriteRenderer BuildLaneColumn(Transform parent, string name, Color color, int order,
                                       float inset = 0.94f)
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
                    Layout.LaneWidthAt(midY) * inset, segmentHeight * 1.06f, 1f);

                if (first == null) first = renderer;
            }

            return first;
        }

        /// <summary>
        /// Draws the four lane separators as tapered strips, so the lane boundaries converge with
        /// the highway instead of running parallel to a perspective that is not there.
        /// </summary>
        void BuildLaneSeparators(Transform parent)
        {
            const int segments = 10;
            float segmentHeight = Layout.HighwayHeight / segments;

            Color separator = LaneTheme.HighwayEdgeColor;
            separator.a = 0.30f;

            for (int boundary = 1; boundary < LaneInfo.Count; boundary++)
            {
                var line = new GameObject("Separator" + boundary);
                line.transform.SetParent(parent, false);

                for (int i = 0; i < segments; i++)
                {
                    float midY = Layout.HitLineY + i * segmentHeight + segmentHeight * 0.5f;
                    float x = (Layout.LaneCenterXAt(boundary - 1, midY) +
                               Layout.LaneCenterXAt(boundary, midY)) * 0.5f;

                    var segment = new GameObject("Seg" + i);
                    segment.transform.SetParent(line.transform, false);

                    var renderer = segment.AddComponent<SpriteRenderer>();
                    renderer.sprite = ProceduralSprites.White;
                    renderer.color = separator;
                    renderer.sortingOrder = 2;
                    renderer.transform.position = new Vector3(x, midY, 0f);
                    renderer.transform.localScale = new Vector3(
                        Mathf.Max(0.012f, 0.028f * Layout.WidthScaleAt(midY)),
                        segmentHeight * 1.06f, 1f);
                }
            }
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

        /// <summary>
        /// Paints the highway gold while Overdrive is running. It is the same overlay the star
        /// power uses, so the two never fight: Overdrive wins because it is the rarer reward.
        /// </summary>
        public void SetOverdriveActive(bool active)
        {
            if (_overdriveOverlay == null) return;

            _overdriveActive = active;

            _overdriveOverlay.color = active
                ? new Color(1f, 0.72f, 0.18f, 0.13f)
                : new Color(0f, 0f, 0f, 0f);

            if (active && _overdrivePulse <= 0f) _overdrivePulse = 0.5f;
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
                ActiveNote note = _visible[i];

                // The far end of the highway is a horizon, so a note is only drawn once it has
                // come over it. Without this the gem appears in empty sky above the stage.
                // Notes behind the strike line still draw: a miss has to be seen falling past
                // the line, and a hit note's flash is anchored where the player struck it.
                if (Layout.TimeToY(note.Time, songTime) > Layout.TopY + 0.001f) continue;

                DrawNote(note, songTime);
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
                HighwayLayout.TailQuad tail = Layout.SustainTail(
                    headY, Layout.TimeToY(note.EndTime, songTime), note.Lane);

                if (tail.Visible)
                {
                    Color sustainColor = color;
                    sustainColor.a = hit ? 0.85f : 0.60f;

                    // Normalise against the sprite's own bounds so the quad covers exactly the
                    // world rectangle the layout asked for, whatever the texture size is.
                    Vector3 bounds = view.Sustain.sprite.bounds.size;

                    view.Sustain.enabled = true;
                    view.Sustain.color = sustainColor;
                    view.Sustain.transform.position = new Vector3(tail.CenterX, tail.CenterY, 0f);
                    view.Sustain.transform.localScale = new Vector3(
                        tail.Width / Mathf.Max(0.0001f, bounds.x),
                        tail.Height / Mathf.Max(0.0001f, bounds.y), 1f);
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

                // A hit note is a flash, not a gem, so the sheen and rim would only muddy it.
                view.Gloss.enabled = false;
                view.Rim.enabled = false;

                if (fade <= 0.01f) view.Root.SetActive(false);
                return;
            }

            float pulse = 1f + 0.04f * Mathf.Sin((songTime * 6f) + note.Lane);

            // Halos stay tight and low-alpha. A wide bloom was what made the old highway look
            // foggy; a small halo plus a hard rim reads at a glance on a bright stage.
            if (note.Note.StarPower)
            {
                Color halo = LaneTheme.StarPowerColor;
                view.Halo.color = new Color(halo.r, halo.g, halo.b, 0.55f);
                view.Halo.transform.localScale = Vector3.one * (radius * 4.4f * pulse);
            }
            else
            {
                Color halo = color;
                view.Halo.color = new Color(halo.r, halo.g, halo.b, 0.30f);
                view.Halo.transform.localScale = Vector3.one * (radius * 3.2f * pulse);
            }

            view.Halo.transform.position = new Vector3(laneX, headY, 0f);

            // HOPO notes are drawn smaller with a brighter core, the usual visual cue.
            float hopoScale = note.Note.Hopo ? 0.78f : 1f;
            float bodyScale = radius * 2f * hopoScale * pulse;

            // The rim is a slightly larger disc in near-black, so every gem keeps a crisp edge
            // against whatever colour the stage happens to be throwing behind it.
            view.Rim.color = new Color(0.04f, 0.03f, 0.06f, 0.92f);
            view.Rim.transform.position = new Vector3(laneX, headY, 0f);
            view.Rim.transform.localScale = Vector3.one * (bodyScale + radius * 0.34f);

            view.Body.color = color;
            view.Body.transform.position = new Vector3(laneX, headY, 0f);
            view.Body.transform.localScale = Vector3.one * bodyScale;

            Color core = Color.Lerp(color, Color.white, note.Note.Hopo ? 0.85f : 0.62f);
            view.Core.color = new Color(core.r, core.g, core.b, note.Note.Hopo ? 0.95f : 0.80f);
            view.Core.transform.position = new Vector3(laneX, headY, 0f);
            view.Core.transform.localScale = Vector3.one * (radius * (note.Note.Hopo ? 1.25f : 1.0f) * pulse);

            // The sheen sits on the body only, so the note keeps a glassy highlight while the
            // halo behind it stays a flat bloom.
            view.Gloss.enabled = true;
            view.Gloss.color = new Color(1f, 1f, 1f, 0.72f);
            view.Gloss.transform.position = new Vector3(laneX, headY, 0f);
            view.Gloss.transform.localScale = Vector3.one * bodyScale;
        }

        void UpdateLaneFeedback(IList<bool> heldLanes)
        {
            float delta = Time.deltaTime;

            if (_overdriveActive || _overdrivePulse > 0f)
            {
                if (_overdrivePulse > 0f) _overdrivePulse -= delta;

                // A slow breathing gold wash while Overdrive runs, plus a bright pop right after
                // the milestone so the moment is unmistakable.
                float breathe = _overdriveActive ? 0.10f + Mathf.Sin(Time.time * 6f) * 0.03f : 0f;
                float pop = Mathf.Clamp01(_overdrivePulse / 0.5f) * 0.28f;

                _overdriveOverlay.color = new Color(1f, 0.72f, 0.18f, Mathf.Max(breathe, pop));
            }
            else if (_overdriveOverlay.color.a > 0f)
            {
                _overdriveOverlay.color = new Color(0f, 0f, 0f, 0f);
            }

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

            // The press spring is integrated explicitly, and at this stiffness an explicit step
            // diverges once a frame runs past roughly 2/sqrt(k) ~ 0.098 s. One hitch on a phone
            // is enough, and the resulting blow-up left the cap frozen at an absurd scale, which
            // reads as the fret hanging. Clamping the step keeps the spring bounded; the song
            // clock is unaffected because it comes from the audio position, not from this.
            float springDelta = Mathf.Min(delta, MaxSpringStep);

            for (int lane = 0; lane < _fretButtons.Length && lane < heldLanes.Count; lane++)
            {
                if (_fretButtons[lane] == null) continue;

                bool held = heldLanes[lane];

                // A damped spring drives the size, so the cap overshoots slightly on the way up
                // and settles back down. That overshoot is what makes the press feel like a
                // physical button popping toward the player rather than a lerped scale.
                float target = held ? FretPressedScale : FretRestScale;
                float offset = _fretPressScale[lane] - target;
                _fretPressVelocity[lane] += (-FretSpringStiffness * offset -
                                             FretSpringDamping * _fretPressVelocity[lane]) * springDelta;
                _fretPressScale[lane] += _fretPressVelocity[lane] * springDelta;

                // Belt and braces: even a clamped step can overshoot on a very slow frame, and a
                // cap stuck outside the rest/pressed range never returns to either.
                _fretPressScale[lane] = Mathf.Clamp(_fretPressScale[lane],
                                                    FretRestScale - 0.15f,
                                                    FretPressedScale + 0.15f);

                float press = Mathf.InverseLerp(FretRestScale, FretPressedScale, _fretPressScale[lane]);
                press = Mathf.Clamp01(press);

                _fretButtons[lane].color = _fretCustomArt[lane]
                    ? Color.white
                    : (held
                        ? Color.Lerp(LaneTheme.ForLane(lane), Color.white, 0.45f)
                        : LaneTheme.DimForLane(lane));

                _fretButtons[lane].transform.localScale =
                    Vector3.one * (Layout.LaneWidth * _fretPressScale[lane]);

                // Lifting the cap toward the camera reads as "timbul"; the shadow stays put and
                // spreads a little, which is what sells the height.
                _fretButtons[lane].transform.position = new Vector3(
                    Layout.LaneCenterX(lane), Layout.HitLineY + FretLift * press, 0f);

                if (_fretShadows[lane] != null)
                {
                    _fretShadows[lane].transform.localScale = Vector3.one *
                        (Layout.LaneWidth * FretRestScale * (1.04f + press * 0.22f));
                    _fretShadows[lane].color = new Color(0f, 0f, 0f, 0.55f - press * 0.18f);
                }

                Color fretGlow = LaneTheme.ForLane(lane);
                fretGlow.a = held ? 0.75f : 0.20f;
                _fretGlows[lane].color = fretGlow;
                _fretGlows[lane].transform.localScale =
                    Vector3.one * (Layout.LaneWidth * (held ? 1.9f : 1.55f));
            }
        }
    }
}
