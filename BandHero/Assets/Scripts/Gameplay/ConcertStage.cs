using System.Collections.Generic;
using UnityEngine;
using BandHero.Chart;
using BandHero.UI;

namespace BandHero.Gameplay
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
        const int MaxLights = 14;
        const int BeamCount = 6;

        readonly List<float> _hits = new List<float>();
        readonly float[] _lightEnergy = new float[MaxLights];
        readonly float[] _lightPhase = new float[MaxLights];
        readonly SpriteRenderer[] _lights = new SpriteRenderer[MaxLights];
        readonly SpriteRenderer[] _beams = new SpriteRenderer[BeamCount];

        readonly List<SpriteRenderer> _crowd = new List<SpriteRenderer>(64);

        int _hitCursor;
        bool _beatPending;
        int _lightsEnabled = MaxLights;
        bool _beamsEnabled = true;

        SpriteRenderer _backdrop;
        SpriteRenderer _glow;
        SpriteRenderer _floor;

        float _flash;
        int _flashSide = -1;
        float _songTime;

        public bool LightShowEnabled { get; set; } = true;

        /// <summary>Turned off by the frame budget on slow devices; the lights stay on.</summary>
        public bool BeamShowEnabled
        {
            get => _beamsEnabled;
            set
            {
                _beamsEnabled = value;
                for (int i = 0; i < _beams.Length; i++)
                {
                    if (_beams[i] != null) _beams[i].enabled = value;
                }
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
            BuildCrowd();
            BuildLights();
            BuildBeams();
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
            _backdrop.transform.localScale = new Vector3(46f, 15f, 1f);

            var glow = new GameObject("StageGlow");
            glow.transform.SetParent(transform, false);
            _glow = glow.AddComponent<SpriteRenderer>();
            _glow.sprite = ProceduralSprites.Glow;
            _glow.color = new Color(1f, 1f, 1f, 0.10f);
            _glow.sortingOrder = -59;
            _glow.transform.position = new Vector3(0f, 0.4f, 0f);
            _glow.transform.localScale = new Vector3(20f, 12f, 1f);

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
                renderer.color = new Color(1f, 1f, 1f, 0.05f);

                float t = BeamCount > 1 ? i / (float)(BeamCount - 1) : 0.5f;
                renderer.transform.position = new Vector3(Mathf.Lerp(-7f, 7f, t), 3.4f, 0f);
                renderer.transform.localScale = new Vector3(1.5f, 8f, 1f);

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

        /// <summary>Keeps the show in step with the audio clock, not with frame time.</summary>
        public void Tick(float songTime, float deltaTime)
        {
            _songTime = songTime;

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

                float idle = 0.16f + 0.06f * Mathf.Sin(songTime * 1.7f + _lightPhase[i]);
                float energy = Mathf.Max(_lightEnergy[i], idle);

                Color color = i % 2 == 0 ? LaneTheme.BeamColorA : LaneTheme.BeamColorB;
                _lights[i].color = new Color(color.r, color.g, color.b, energy * 0.85f);
                _lights[i].transform.localScale = Vector3.one * (2.2f + energy * 2.6f);
            }

            UpdateBeams(songTime);
            UpdateBackdrop(deltaTime);
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
                color.a = 0.06f + 0.05f * Mathf.Abs(sweep) + _flash * 0.16f;
                _beams[i].color = color;
            }
        }

        void UpdateBackdrop(float deltaTime)
        {
            if (_glow != null)
            {
                Color glow = LaneTheme.BackgroundGlowColor;
                glow.a = 0.08f + _flash * 0.22f;
                _glow.color = glow;
                _glow.transform.localScale = new Vector3(20f + _flash * 3f, 12f + _flash * 2f, 1f);
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

            for (int i = 0; i < _lightEnergy.Length; i++) _lightEnergy[i] = 0f;
        }

        public float SongTime => _songTime;
    }
}
