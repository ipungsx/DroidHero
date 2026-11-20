using System.Collections.Generic;
using UnityEngine;
using DroidHero.Chart;
using DroidHero.UI;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// The stage behind the highway: crowd silhouette, truss lights and sweeping beams.
    ///
    /// The lights are driven by the chart's drum onsets, so a fill or a snare roll makes the
    /// stage blink in time with the music rather than on a fixed timer. When the chart carries no
    /// drum track the onsets fall back to the guitar notes, which still lands on the beat.
    /// </summary>
    public class ConcertStage : MonoBehaviour
    {
        const int MaxLights = 22;
        const int BeamCount = 10;
        const int ConfettiCount = 40;
        const int BackRowLights = 12;

        readonly List<float> _hits = new List<float>();
        readonly float[] _lightEnergy = new float[MaxLights];
        readonly float[] _lightPhase = new float[MaxLights];
        readonly SpriteRenderer[] _lights = new SpriteRenderer[MaxLights];
        readonly SpriteRenderer[] _beams = new SpriteRenderer[BeamCount];
        readonly SpriteRenderer[] _confetti = new SpriteRenderer[ConfettiCount];
        readonly SpriteRenderer[] _backRow = new SpriteRenderer[BackRowLights];
        readonly float[] _confettiPhase = new float[ConfettiCount];
        readonly float[] _confettiSpeed = new float[ConfettiCount];
        readonly float[] _confettiSpin = new float[ConfettiCount];

        readonly List<SpriteRenderer> _crowd = new List<SpriteRenderer>(64);

        int _hitCursor;
        bool _beatPending;
        int _lightsEnabled = MaxLights;
        bool _beamsEnabled = true;
        bool _confettiEnabled = true;

        SpriteRenderer _backdrop;
        SpriteRenderer _glow;
        SpriteRenderer _floor;
        SpriteRenderer _guitar;
        SpriteRenderer _guitarGlow;
        SpriteRenderer _spotWash;

        float _flash;
        int _flashSide = -1;
        float _songTime;
        float _overdrive;
        float _overdriveTarget;
        float _overdrivePulse;

        bool _lightShowEnabled = true;

        /// <summary>
        /// Turns the whole show on or off.
        ///
        /// Switching it off has to hide the decorative renderers outright, not just stop
        /// animating them: confetti and beams are positioned by their tick, so freezing them
        /// leaves the shards in the row they were built in, straight across the middle of the
        /// screen. That frozen row is what looked like a rendering glitch with the show off.
        /// </summary>
        public bool LightShowEnabled
        {
            get => _lightShowEnabled;
            set
            {
                _lightShowEnabled = value;
                ApplyShowVisibility();
            }
        }

        /// <summary>Turned off by the frame budget on slow devices; the lights stay on.</summary>
        public bool BeamShowEnabled
        {
            get => _beamsEnabled;
            set
            {
                _beamsEnabled = value;
                ApplyShowVisibility();
            }
        }

        /// <summary>Confetti is the cheapest thing to drop first: it is pure decoration.</summary>
        public bool ConfettiEnabled
        {
            get => _confettiEnabled;
            set
            {
                _confettiEnabled = value;
                ApplyShowVisibility();
            }
        }

        /// <summary>
        /// Number of lights currently drawn. The frame-budget guard lowers this on slow devices
        /// instead of dropping the whole show.
        /// </summary>
        public int ActiveLightCount
        {
            get => _lightsEnabled;
            set
            {
                _lightsEnabled = Mathf.Clamp(value, 0, MaxLights);
                ApplyLightVisibility();
            }
        }

        public void Build(SongChart chart)
        {
            _hits.Clear();
            if (chart != null) _hits.AddRange(chart.DrumHits);

            BuildBackdrop();
            BuildGuitarProp();
            BuildCrowd();
            BuildLights();
            BuildBackLights();
            BuildBeams();
            BuildConfetti();

            // The show switch is set before Build, when there was nothing to hide yet.
            ApplyShowVisibility();
        }

        /// <summary>
        /// A guitar standing behind the highway, lit by a warm wash. It gives the playfield a
        /// subject to scroll in front of, which is what makes the 2D perspective read as depth
        /// rather than as a flat column of notes.
        /// </summary>
        void BuildGuitarProp()
        {
            var root = new GameObject("GuitarProp");
            root.transform.SetParent(transform, false);

            var glow = new GameObject("GuitarGlow");
            glow.transform.SetParent(root.transform, false);
            _guitarGlow = glow.AddComponent<SpriteRenderer>();
            _guitarGlow.sprite = ProceduralSprites.Glow;
            _guitarGlow.sortingOrder = -57;
            _guitarGlow.transform.position = new Vector3(0f, 0.4f, 0f);
            _guitarGlow.transform.localScale = new Vector3(6f, 8f, 1f);

            var guitar = new GameObject("Guitar");
            guitar.transform.SetParent(root.transform, false);
            _guitar = guitar.AddComponent<SpriteRenderer>();
            _guitar.sprite = ProceduralSprites.Guitar;
            _guitar.sortingOrder = -56;
            // The sprite is 256x512 at 100 pixels per unit, so a scale of one already stands
            // 5.1 world units tall. The camera shows 12.8, and the prop has to sit behind the
            // highway rather than fill the screen, hence the fraction below.
            _guitar.transform.position = new Vector3(0f, 0.1f, 0f);
            _guitar.transform.localScale = new Vector3(0.92f, 0.92f, 1f);
            _guitar.transform.rotation = Quaternion.Euler(0f, 0f, -9f);
        }

        /// <summary>
        /// Falling confetti, drawn behind the highway so it never competes with a note.
        /// Positions come from a per-shard phase, so the fall loops without allocation.
        /// </summary>
        void BuildConfetti()
        {
            var root = new GameObject("Confetti");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < ConfettiCount; i++)
            {
                var shard = new GameObject("Shard" + i);
                shard.transform.SetParent(root.transform, false);

                var renderer = shard.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Confetti;
                renderer.sortingOrder = -55;

                float t = i / (float)Mathf.Max(1, ConfettiCount - 1);
                renderer.transform.position = new Vector3(Mathf.Lerp(-10f, 10f, t), 0f, 0f);
                renderer.transform.localScale = Vector3.one * (0.10f + (i % 4) * 0.03f);

                _confetti[i] = renderer;
                _confettiPhase[i] = (i * 0.37f) % 1f;
                _confettiSpeed[i] = 0.045f + (i % 5) * 0.012f;
                _confettiSpin[i] = (i % 2 == 0 ? 1f : -1f) * (35f + (i % 7) * 12f);
            }
        }

        void BuildBackdrop()
        {
            var backdrop = new GameObject("StageBackdrop");
            backdrop.transform.SetParent(transform, false);

            _backdrop = backdrop.AddComponent<SpriteRenderer>();
            _backdrop.sprite = ProceduralSprites.VerticalFade;
            _backdrop.color = LaneTheme.BackgroundGlowColor;
            _backdrop.sortingOrder = -60;
            _backdrop.transform.position = new Vector3(0f, -1.2f, 0f);
            _backdrop.transform.localScale = new Vector3(46f, 17f, 1f);

            var glow = new GameObject("StageGlow");
            glow.transform.SetParent(transform, false);
            _glow = glow.AddComponent<SpriteRenderer>();
            _glow.sprite = ProceduralSprites.Glow;
            _glow.color = new Color(1f, 1f, 1f, 0.34f);
            _glow.sortingOrder = -59;
            _glow.transform.position = new Vector3(0f, 0.4f, 0f);
            _glow.transform.localScale = new Vector3(20f, 12f, 1f);

            // A wide, soft wash from the truss keeps the stage bright without hiding the notes.
            var wash = new GameObject("SpotWash");
            wash.transform.SetParent(transform, false);
            _spotWash = wash.AddComponent<SpriteRenderer>();
            _spotWash.sprite = ProceduralSprites.Beam;
            _spotWash.color = new Color(1f, 0.96f, 0.86f, 0.16f);
            _spotWash.sortingOrder = -59;
            _spotWash.transform.position = new Vector3(0f, 2.6f, 0f);
            _spotWash.transform.localScale = new Vector3(22f, 16f, 1f);

            var floor = new GameObject("StageFloor");
            floor.transform.SetParent(transform, false);
            _floor = floor.AddComponent<SpriteRenderer>();
            _floor.sprite = ProceduralSprites.VerticalFade;
            _floor.color = new Color(LaneTheme.CrowdColor.r, LaneTheme.CrowdColor.g,
                                     LaneTheme.CrowdColor.b, 0.85f);
            _floor.sortingOrder = -58;
            _floor.transform.position = new Vector3(0f, -6.4f, 0f);
            _floor.transform.localScale = new Vector3(46f, 7f, 1f);
        }

        /// <summary>
        /// A silhouette of heads and raised hands. Drawn once at build time: animating a crowd
        /// per frame would cost more than the notes themselves.
        /// </summary>
        void BuildCrowd()
        {
            var root = new GameObject("Crowd");
            root.transform.SetParent(transform, false);

            const int heads = 26;
            for (int i = 0; i < heads; i++)
            {
                float t = i / (float)(heads - 1);
                float x = Mathf.Lerp(-11f, 11f, t);
                float size = 0.55f + Mathf.PerlinNoise(i * 3.7f, 0.5f) * 0.45f;
                float y = -6.1f - Mathf.PerlinNoise(i * 1.3f, 2.5f) * 0.5f;

                var head = new GameObject("Head" + i);
                head.transform.SetParent(root.transform, false);

                var renderer = head.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Circle;
                renderer.color = new Color(LaneTheme.CrowdColor.r * 0.6f, LaneTheme.CrowdColor.g * 0.6f,
                                           LaneTheme.CrowdColor.b * 0.6f, 0.95f);
                renderer.sortingOrder = -50;
                renderer.transform.position = new Vector3(x, y, 0f);
                renderer.transform.localScale = Vector3.one * size;
                _crowd.Add(renderer);
            }
        }

        void BuildLights()
        {
            var root = new GameObject("TrussLights");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < MaxLights; i++)
            {
                var light = new GameObject("Light" + i);
                light.transform.SetParent(root.transform, false);

                var renderer = light.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Glow;
                renderer.sortingOrder = -40;

                float t = MaxLights > 1 ? i / (float)(MaxLights - 1) : 0.5f;
                renderer.transform.position = new Vector3(Mathf.Lerp(-8.5f, 8.5f, t), 6.6f, 0f);
                renderer.transform.localScale = Vector3.one * 2.4f;

                _lights[i] = renderer;
                _lightPhase[i] = i * 0.7f;
                _lightEnergy[i] = 0f;
            }

            ApplyLightVisibility();
        }

        /// <summary>
        /// A second, denser row of small lights high on the back wall. They never blink with the
        /// beat: their job is to keep the top of the frame bright and busy so the stage reads as
        /// a packed arena instead of a dark column behind the notes.
        /// </summary>
        void BuildBackLights()
        {
            var root = new GameObject("BackRowLights");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BackRowLights; i++)
            {
                var light = new GameObject("BackLight" + i);
                light.transform.SetParent(root.transform, false);

                var renderer = light.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Glow;
                renderer.sortingOrder = -41;

                float t = BackRowLights > 1 ? i / (float)(BackRowLights - 1) : 0.5f;
                renderer.transform.position = new Vector3(Mathf.Lerp(-10.5f, 10.5f, t), 8.1f, 0f);
                renderer.transform.localScale = Vector3.one * 1.5f;

                _backRow[i] = renderer;
            }
        }

        void BuildBeams()
        {
            var root = new GameObject("Beams");
            root.transform.SetParent(transform, false);

            for (int i = 0; i < BeamCount; i++)
            {
                var beam = new GameObject("Beam" + i);
                beam.transform.SetParent(root.transform, false);

                var renderer = beam.AddComponent<SpriteRenderer>();
                renderer.sprite = ProceduralSprites.Beam;
                renderer.sortingOrder = -39;
                renderer.color = new Color(1f, 1f, 1f, 0.09f);

                float t = BeamCount > 1 ? i / (float)(BeamCount - 1) : 0.5f;
                renderer.transform.position = new Vector3(Mathf.Lerp(-9f, 9f, t), 3.4f, 0f);
                renderer.transform.localScale = new Vector3(1.7f, 9.5f, 1f);

                _beams[i] = renderer;
            }
        }

        void ApplyLightVisibility()
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null) _lights[i].enabled = i < _lightsEnabled;
            }
        }

        /// <summary>
        /// Reconciles every decorative renderer with both the show switch and the frame-budget
        /// flags, so turning the show back on cannot resurrect something the budget dropped.
        /// </summary>
        void ApplyShowVisibility()
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null) _lights[i].enabled = _lightShowEnabled && i < _lightsEnabled;
            }

            for (int i = 0; i < _backRow.Length; i++)
            {
                if (_backRow[i] != null) _backRow[i].enabled = _lightShowEnabled;
            }

            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] != null) _beams[i].enabled = _lightShowEnabled && _beamsEnabled;
            }

            for (int i = 0; i < _confetti.Length; i++)
            {
                if (_confetti[i] != null) _confetti[i].enabled = _lightShowEnabled && _confettiEnabled;
            }
        }

        /// <summary>
        /// Flares the whole rig for a score milestone. The steady glow that follows is driven by
        /// <see cref="SetOverdriveActive"/>, so the burst is only the moment of arrival.
        /// </summary>
        public void OverdriveBurst()
        {
            _overdrivePulse = 1f;
            _overdrive = Mathf.Max(_overdrive, 0.75f);

            for (int i = 0; i < _lightEnergy.Length; i++) _lightEnergy[i] = 1f;
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] != null) _beams[i].color = new Color(1f, 0.88f, 0.55f, 0.42f);
            }

            _flash = 1.6f;
        }

        /// <summary>Holds the rig at its brighter Overdrive level for as long as the reward runs.</summary>
        public void SetOverdriveActive(bool active) => _overdriveTarget = active ? 1f : 0f;

        /// <summary>
        /// The extra row of back-wall lights. They breathe on a slow, detuned sine so the top of
        /// the frame never goes flat, and they warm toward gold during Overdrive.
        /// </summary>
        void UpdateBackLights(float songTime)
        {
            for (int i = 0; i < _backRow.Length; i++)
            {
                if (_backRow[i] == null) continue;

                float pulse = 0.55f + 0.45f * Mathf.Sin(songTime * (1.1f + i * 0.07f) + i * 0.9f);
                Color color = i % 3 == 0 ? LaneTheme.BeamColorA
                            : i % 3 == 1 ? LaneTheme.BeamColorB
                            : LaneTheme.AccentColor;

                if (_overdrive > 0f) color = Color.Lerp(color, new Color(1f, 0.80f, 0.35f), _overdrive);

                color.a = (0.20f + pulse * 0.30f) * (1f + _overdrive * 0.6f);
                _backRow[i].color = color;
                _backRow[i].transform.localScale = Vector3.one * (1.35f + pulse * 0.45f);
            }
        }

        /// <summary>Keeps the show in step with the audio clock, not with frame time.</summary>
        public void Tick(float songTime, float deltaTime)
        {
            _songTime = songTime;

            _overdrive = Mathf.MoveTowards(_overdrive, _overdriveTarget, deltaTime * 1.6f);
            _overdrivePulse = Mathf.Max(0f, _overdrivePulse - deltaTime * 2.2f);

            if (!LightShowEnabled)
            {
                FadeOut(deltaTime);
                return;
            }

            FireDueHits(songTime);

            float decay = Mathf.Clamp01(deltaTime * 6.5f);

            for (int i = 0; i < _lights.Length; i++)
            {
                _lightEnergy[i] = Mathf.Max(0f, _lightEnergy[i] - decay);

                if (_lights[i] == null) continue;

                float idle = 0.24f + 0.08f * Mathf.Sin(songTime * 1.7f + _lightPhase[i]);
                float energy = Mathf.Max(_lightEnergy[i], idle) + _overdrive * 0.25f + _overdrivePulse * 0.35f;

                Color color = i % 2 == 0 ? LaneTheme.BeamColorA : LaneTheme.BeamColorB;
                _lights[i].color = new Color(color.r, color.g, color.b, energy * 0.85f);
                _lights[i].transform.localScale = Vector3.one * (2.2f + energy * 2.6f);
            }

            UpdateBackLights(songTime);
            UpdateBeams(songTime);
            UpdateConfetti(songTime);
            UpdateGuitarProp(songTime);
            UpdateBackdrop(deltaTime);
        }

        /// <summary>
        /// Loops the confetti downward. Each shard's height is a sawtooth of its own phase, so
        /// the field never empties and no allocation or respawn bookkeeping is needed.
        /// </summary>
        void UpdateConfetti(float songTime)
        {
            if (!_confettiEnabled) return;

            for (int i = 0; i < _confetti.Length; i++)
            {
                if (_confetti[i] == null) continue;

                float t = Mathf.Repeat(_confettiPhase[i] + songTime * _confettiSpeed[i], 1f);
                float y = Mathf.Lerp(8.5f, -7.5f, t);

                // A slow sway across the width stops the fall from looking like rain. X is
                // derived from the shard's index each frame, so it can never drift.
                float baseX = Mathf.Lerp(-10f, 10f, i / (float)Mathf.Max(1, ConfettiCount - 1));
                float sway = Mathf.Sin(songTime * 0.8f + i * 1.7f) * 0.55f;

                _confetti[i].transform.position = new Vector3(baseX + sway, y, 0f);
                _confetti[i].transform.rotation = Quaternion.Euler(0f, 0f, songTime * _confettiSpin[i] + i * 23f);

                Color color = i % 3 == 0 ? LaneTheme.BeamColorA
                            : i % 3 == 1 ? LaneTheme.BeamColorB
                            : LaneTheme.AccentColor;

                // Fade at both ends of the loop so a shard never pops into existence.
                float fade = Mathf.Clamp01(Mathf.Min(t * 6f, (1f - t) * 6f));
                color.a = 0.55f * fade + _flash * 0.25f;
                _confetti[i].color = color;
            }
        }

        /// <summary>Breathes the guitar prop with the beat, as if it were being strummed.</summary>
        void UpdateGuitarProp(float songTime)
        {
            if (_guitar == null) return;

            float sway = Mathf.Sin(songTime * 0.7f) * 2.4f;
            _guitar.transform.rotation = Quaternion.Euler(0f, 0f, -9f + sway + _flash * 1.6f);

            Color tint = LaneTheme.HighwayEdgeColor;
            _guitar.color = new Color(
                Mathf.Lerp(0.30f, tint.r, 0.55f) + _flash * 0.22f,
                Mathf.Lerp(0.30f, tint.g, 0.55f) + _flash * 0.22f,
                Mathf.Lerp(0.34f, tint.b, 0.55f) + _flash * 0.22f,
                0.92f);

            if (_guitarGlow != null)
            {
                Color glow = LaneTheme.BackgroundGlowColor;
                glow.a = 0.16f + _flash * 0.20f;
                _guitarGlow.color = glow;
            }
        }

        void FireDueHits(float songTime)
        {
            // The cursor only moves forward, so a long song costs one comparison per frame.
            while (_hitCursor < _hits.Count && _hits[_hitCursor] <= songTime)
            {
                Fire();
                _hitCursor++;
            }

            // A restart or a seek backwards rewinds the cursor so the show keeps working.
            if (_hitCursor > 0 && _hitCursor <= _hits.Count && songTime < _hits[_hitCursor - 1] - 1f)
            {
                _hitCursor = 0;
            }
        }

        void Fire()
        {
            _flash = 1f;
            _beatPending = true;
            _flashSide = 1 - _flashSide;

            // Alternate sides so consecutive hits trade between the left and right truss.
            for (int i = 0; i < _lights.Length; i++)
            {
                bool sameSide = (_flashSide == 0) == (i < _lights.Length / 2);
                _lightEnergy[i] = Mathf.Max(_lightEnergy[i], sameSide ? 1f : 0.55f);
            }
        }

        void UpdateBeams(float songTime)
        {
            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null) continue;

                // Each beam sweeps on its own phase, which reads as a moving rig.
                float sweep = Mathf.Sin(songTime * (0.55f + i * 0.11f) + _lightPhase[i] * 2f);
                float angle = sweep * 16f;
                _beams[i].transform.rotation = Quaternion.Euler(0f, 0f, angle);

                Color color = i % 2 == 0 ? LaneTheme.BeamColorA : LaneTheme.BeamColorB;
                color.a = 0.11f + 0.07f * Mathf.Abs(sweep) + _flash * 0.18f + _overdrive * 0.10f;
                _beams[i].color = color;
            }
        }

        void UpdateBackdrop(float deltaTime)
        {
            if (_glow != null)
            {
                Color glow = LaneTheme.BackgroundGlowColor;
                glow.a = 0.18f + _flash * 0.24f;
                _glow.color = glow;
                _glow.transform.localScale = new Vector3(20f + _flash * 3f, 12f + _flash * 2f, 1f);
            }

            if (_spotWash != null)
            {
                Color wash = _spotWash.color;
                wash.a = 0.10f + _flash * 0.16f;
                _spotWash.color = wash;
            }

            _flash = Mathf.Max(0f, _flash - deltaTime * 4.5f);
        }

        void FadeOut(float deltaTime)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] == null) continue;

                Color color = _lights[i].color;
                color.a = Mathf.MoveTowards(color.a, 0f, deltaTime * 3f);
                _lights[i].color = color;
            }

            for (int i = 0; i < _beams.Length; i++)
            {
                if (_beams[i] == null) continue;

                Color color = _beams[i].color;
                color.a = Mathf.MoveTowards(color.a, 0f, deltaTime * 2f);
                _beams[i].color = color;
            }

            for (int i = 0; i < _backRow.Length; i++)
            {
                if (_backRow[i] == null) continue;

                Color color = _backRow[i].color;
                color.a = Mathf.MoveTowards(color.a, 0f, deltaTime * 3f);
                _backRow[i].color = color;
            }

            _flash = Mathf.Max(0f, _flash - deltaTime * 4f);
        }

        /// <summary>
        /// True once per drum hit. The highway reads this to pulse the strike line, so the beat
        /// is something the player sees as well as hears.
        /// </summary>
        public bool ConsumeBeat()
        {
            if (!_beatPending) return false;

            _beatPending = false;
            return true;
        }

        /// <summary>Restarts the show for a fresh run.</summary>
        public void ResetShow()
        {
            _hitCursor = 0;
            _flash = 0f;
            _beatPending = false;
            _overdrive = 0f;
            _overdriveTarget = 0f;
            _overdrivePulse = 0f;

            for (int i = 0; i < _lightEnergy.Length; i++) _lightEnergy[i] = 0f;
        }

        public float SongTime => _songTime;
    }
}
