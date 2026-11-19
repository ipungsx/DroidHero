using System.Collections.Generic;
using UnityEngine;
using BandHero.Chart;
using BandHero.UI;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Draws the 2D note highway: lane backdrop, hit line, frets and every visible note.
    ///
    /// Rendering is stateless with respect to gameplay: it reads the tracker and redraws from
    /// scratch each frame, so a dropped frame can never desync the visuals from the music.
    /// </summary>
    public class HighwayRenderer : MonoBehaviour
    {
        public HighwayLayout Layout = new HighwayLayout();

        readonly List<ActiveNote> _visible = new List<ActiveNote>(256);
        readonly SpriteRenderer[] _laneBackdrops = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _laneFlashes = new SpriteRenderer[LaneInfo.Count];
        readonly SpriteRenderer[] _fretButtons = new SpriteRenderer[LaneInfo.Count];

        NoteViewPool _pool;
        NoteTracker _tracker;
        Transform _noteRoot;

        SpriteRenderer _hitLine;
        SpriteRenderer _starPowerOverlay;

        float _flashTimer;
        int _flashLane = -1;

        public void Initialize(NoteTracker tracker)
        {
            _tracker = tracker;
            BuildStaticElements();
            _pool = new NoteViewPool(_noteRoot, 64, 20);
        }

        void BuildStaticElements()
        {
            var backdrop = new GameObject("Highway");
            backdrop.transform.SetParent(transform, false);

            // A single dark quad behind the lanes keeps the notes readable over any background.
            var highway = new GameObject("HighwayBackdrop");
            highway.transform.SetParent(backdrop.transform, false);
            var highwayRenderer = highway.AddComponent<SpriteRenderer>();
            highwayRenderer.sprite = ProceduralSprites.White;
            highwayRenderer.color = LaneTheme.HighwayColor;
            highwayRenderer.sortingOrder = 0;
            highwayRenderer.transform.position = new Vector3(
                (Layout.HighwayLeftX + Layout.HighwayRightX) * 0.5f,
                (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
            highwayRenderer.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX,
                Layout.TopY - Layout.HitLineY, 1f);

            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                var strip = new GameObject("Lane" + lane);
                strip.transform.SetParent(backdrop.transform, false);
                var stripRenderer = strip.AddComponent<SpriteRenderer>();
                stripRenderer.sprite = ProceduralSprites.White;
                stripRenderer.color = lane % 2 == 0
                    ? new Color(1f, 1f, 1f, 0.03f)
                    : new Color(0f, 0f, 0f, 0.10f);
                stripRenderer.sortingOrder = 1;
                stripRenderer.transform.position = new Vector3(
                    Layout.LaneCenterX(lane),
                    (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
                stripRenderer.transform.localScale = new Vector3(
                    Layout.LaneWidth * 0.96f, Layout.TopY - Layout.HitLineY, 1f);
            }

            var hitLine = new GameObject("HitLine");
            hitLine.transform.SetParent(backdrop.transform, false);
            _hitLine = hitLine.AddComponent<SpriteRenderer>();
            _hitLine.sprite = ProceduralSprites.White;
            _hitLine.color = LaneTheme.HitLineColor;
            _hitLine.sortingOrder = 30;
            _hitLine.transform.position = new Vector3(
                (Layout.HighwayLeftX + Layout.HighwayRightX) * 0.5f, Layout.HitLineY, 0f);
            _hitLine.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, 0.055f, 1f);

            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                var flash = new GameObject("LaneFlash" + lane);
                flash.transform.SetParent(backdrop.transform, false);
                var flashRenderer = flash.AddComponent<SpriteRenderer>();
                flashRenderer.sprite = ProceduralSprites.White;
                flashRenderer.color = new Color(1f, 1f, 1f, 0f);
                flashRenderer.sortingOrder = 2;
                flashRenderer.transform.position = new Vector3(
                    Layout.LaneCenterX(lane),
                    (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
                flashRenderer.transform.localScale = new Vector3(
                    Layout.LaneWidth * 0.96f, Layout.TopY - Layout.HitLineY, 1f);
                _laneFlashes[lane] = flashRenderer;

                var fret = new GameObject("Fret" + lane);
                fret.transform.SetParent(backdrop.transform, false);
                var fretRenderer = fret.AddComponent<SpriteRenderer>();
                fretRenderer.sprite = ProceduralSprites.RoundedNote;
                fretRenderer.color = LaneTheme.DimForLane(lane);
                fretRenderer.sortingOrder = 25;
                fretRenderer.transform.position = new Vector3(Layout.LaneCenterX(lane), Layout.HitLineY, 0f);
                fretRenderer.transform.localScale = new Vector3(
                    Layout.LaneWidth * 0.74f, Layout.LaneWidth * 0.74f, 1f);
                _fretButtons[lane] = fretRenderer;
            }

            var overlay = new GameObject("StarPowerOverlay");
            overlay.transform.SetParent(backdrop.transform, false);
            _starPowerOverlay = overlay.AddComponent<SpriteRenderer>();
            _starPowerOverlay.sprite = ProceduralSprites.White;
            _starPowerOverlay.color = new Color(0f, 0f, 0f, 0f);
            _starPowerOverlay.sortingOrder = 3;
            _starPowerOverlay.transform.position = new Vector3(
                (Layout.HighwayLeftX + Layout.HighwayRightX) * 0.5f,
                (Layout.HitLineY + Layout.TopY) * 0.5f, 0f);
            _starPowerOverlay.transform.localScale = new Vector3(
                Layout.HighwayRightX - Layout.HighwayLeftX, Layout.TopY - Layout.HitLineY, 1f);

            var noteRoot = new GameObject("Notes");
            noteRoot.transform.SetParent(transform, false);
            _noteRoot = noteRoot.transform;
        }

        /// <summary>Pulses a lane to acknowledge a hit.</summary>
        public void FlashLane(int lane)
        {
            if (lane < 0 || lane >= _laneFlashes.Length) return;
            _flashLane = lane;
            _flashTimer = 0.12f;
        }

        public void SetStarPowerActive(bool active)
        {
            if (_starPowerOverlay == null) return;
            _starPowerOverlay.color = active
                ? new Color(LaneTheme.StarPowerColor.r, LaneTheme.StarPowerColor.g, LaneTheme.StarPowerColor.b, 0.12f)
                : new Color(0f, 0f, 0f, 0f);
        }

        public void Render(float songTime, IList<bool> heldLanes)
        {
            if (_tracker == null || _pool == null) return;

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
            float laneX = Layout.LaneCenterX(note.Lane);

            _pool.Acquire(out SpriteRenderer body, out SpriteRenderer glow, out SpriteRenderer sustain);

            bool hit = note.State == NoteState.Hit;
            Color color = LaneTheme.ForLane(note.Lane);

            if (hit) color = Color.Lerp(color, Color.white, 0.55f);

            float width = Layout.LaneWidth * 0.82f;
            float height = Layout.NoteHeight;

            if (note.Note.Hopo && !hit)
            {
                // HOPO notes are drawn slimmer with a bright cap, the usual visual cue.
                height *= 0.8f;
                body.sprite = ProceduralSprites.CreateCircle(64);
            }
            else
            {
                body.sprite = ProceduralSprites.RoundedNote;
            }

            body.color = color;
            body.transform.position = new Vector3(laneX, headY, 0f);
            body.transform.localScale = new Vector3(width, height, 1f);

            if (note.Note.StarPower && !hit)
            {
                glow.enabled = true;
                glow.color = new Color(LaneTheme.StarPowerColor.r, LaneTheme.StarPowerColor.g,
                                       LaneTheme.StarPowerColor.b, 0.55f);
                glow.transform.position = new Vector3(laneX, headY, 0f);
                glow.transform.localScale = new Vector3(width * 1.28f, height * 1.9f, 1f);
            }

            if (note.IsSustain && !note.SustainBroken)
            {
                float tailY = Layout.TimeToY(note.EndTime, songTime);
                float sustainHeight = Mathf.Max(0f, tailY - headY);
                float visibleTop = Mathf.Min(tailY, Layout.TopY + height);
                sustainHeight = Mathf.Max(0f, visibleTop - headY);

                if (sustainHeight > 0.02f)
                {
                    sustain.enabled = true;
                    Color sustainColor = color;
                    sustainColor.a = hit ? 0.85f : 0.55f;
                    sustain.color = sustainColor;
                    sustain.transform.position = new Vector3(laneX, headY + sustainHeight * 0.5f, 0f);
                    sustain.transform.localScale = new Vector3(width * 0.42f, sustainHeight, 1f);
                }
            }
        }

        void UpdateLaneFeedback(IList<bool> heldLanes)
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float alpha = Mathf.Clamp01(_flashTimer / 0.12f) * 0.35f;

                for (int lane = 0; lane < _laneFlashes.Length; lane++)
                {
                    if (_laneFlashes[lane] == null) continue;
                    Color c = _laneFlashes[lane].color;
                    c.a = lane == _flashLane ? alpha : Mathf.Lerp(c.a, 0f, 0.5f);
                    _laneFlashes[lane].color = c;
                }
            }

            if (heldLanes == null) return;

            for (int lane = 0; lane < _fretButtons.Length && lane < heldLanes.Count; lane++)
            {
                if (_fretButtons[lane] == null) continue;

                bool held = heldLanes[lane];
                _fretButtons[lane].color = held
                    ? Color.Lerp(LaneTheme.ForLane(lane), Color.white, 0.35f)
                    : LaneTheme.DimForLane(lane);

                float scale = held ? Layout.LaneWidth * 0.82f : Layout.LaneWidth * 0.74f;
                _fretButtons[lane].transform.localScale = new Vector3(scale, scale, 1f);
            }
        }
    }
}
