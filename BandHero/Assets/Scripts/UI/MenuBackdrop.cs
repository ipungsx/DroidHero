using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// Animated stage behind the menus: gradient sky, drifting spotlights and a beat pulse.
    ///
    /// Built from the same procedural sprites the gameplay uses, so the menu and the stage share
    /// one look and the app still ships no bitmap art. Every sprite is created once; each frame
    /// only writes colours and scales.
    /// </summary>
    public class MenuBackdrop : MonoBehaviour
    {
        const int BeamCount = 7;

        readonly SpriteRenderer[] _beams = new SpriteRenderer[BeamCount];
        readonly float[] _beamPhase = new float[BeamCount];
        readonly SpriteRenderer[] _sparkles = new SpriteRenderer[18];
        readonly float[] _sparkleSpeed = new float[18];

        SpriteRenderer _sky;
        SpriteRenderer _glow;
        SpriteRenderer _horizon;
        Camera _camera;

        float _time;
        float _pulse;

        public void Build()
        {
            _camera = Camera.main;

            var root = new GameObject("MenuBackdrop");
            root.transform.SetParent(transform, false);

            _sky = Sprite(root.transform, "Sky", ProceduralSprites.VerticalFade, -100);
            _glow = Sprite(root.transform, "Glow", ProceduralSprites.Glow, -99);
            _horizon = Sprite(root.transform, "Horizon", ProceduralSprites.VerticalFade, -98);

            for (int i = 0; i < BeamCount; i++)
            {
                _beams[i] = Sprite(root.transform, "Beam" + i, ProceduralSprites.Beam, -97);
                _beamPhase[i] = i * 0.9f;
            }

            for (int i = 0; i < _sparkles.Length; i++)
            {
                _sparkles[i] = Sprite(root.transform, "Spark" + i, ProceduralSprites.Glow, -96);
                _sparkleSpeed[i] = 0.25f + (i % 7) * 0.09f;
            }

            ApplyTheme();
            Layout();
        }

        SpriteRenderer Sprite(Transform parent, string name, Sprite sprite, int order)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, 0f);

            return renderer;
        }

        /// <summary>Re-tints the backdrop for the selected theme, so the menu previews it.</summary>
        public void ApplyTheme()
        {
            GameplayTheme theme = LaneTheme.Current;

            _sky.color = theme.Background;
            _horizon.color = new Color(theme.BackgroundGlow.r, theme.BackgroundGlow.g,
                                       theme.BackgroundGlow.b, 0.55f);

            for (int i = 0; i < _beams.Length; i++)
            {
                Color color = i % 2 == 0 ? theme.BeamA : theme.BeamB;
                color.a = 0.10f;
                _beams[i].color = color;
            }

            for (int i = 0; i < _sparkles.Length; i++)
            {
                Color color = i % 3 == 0 ? theme.BeamB : theme.Accent;
                color.a = 0.30f;
                _sparkles[i].color = color;
            }
        }

        void Layout()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;

            _sky.transform.position = new Vector3(0f, 0f, 0f);
            _sky.transform.localScale = new Vector3(halfWidth * 2.2f, halfHeight * 2.2f, 1f);

            _glow.transform.position = new Vector3(0f, -halfHeight * 0.15f, 0f);
            _glow.transform.localScale = new Vector3(halfWidth * 1.7f, halfHeight * 1.1f, 1f);

            _horizon.transform.position = new Vector3(0f, -halfHeight * 0.55f, 0f);
            _horizon.transform.localScale = new Vector3(halfWidth * 2.2f, halfHeight * 0.9f, 1f);

            for (int i = 0; i < _beams.Length; i++)
            {
                float t = BeamCount > 1 ? i / (float)(BeamCount - 1) : 0.5f;
                _beams[i].transform.position = new Vector3(
                    Mathf.Lerp(-halfWidth * 0.95f, halfWidth * 0.95f, t), halfHeight * 0.25f, 0f);
                _beams[i].transform.localScale = new Vector3(halfWidth * 0.32f, halfHeight * 1.5f, 1f);
            }

            for (int i = 0; i < _sparkles.Length; i++)
            {
                float x = Mathf.PerlinNoise(i * 3.1f, 1.7f) * 2f - 1f;
                float y = Mathf.PerlinNoise(2.3f, i * 2.9f) * 2f - 1f;
                _sparkles[i].transform.position = new Vector3(x * halfWidth, y * halfHeight, 0f);
                _sparkles[i].transform.localScale = Vector3.one * (0.10f + (i % 5) * 0.045f);
            }
        }

        void Update()
        {
            _time += Time.unscaledDeltaTime;
            _pulse = Mathf.Max(0f, _pulse - Time.unscaledDeltaTime * 2f);

            Layout();

            // A slow sine keeps the beams sweeping; the pulse is reserved for real input.
            for (int i = 0; i < _beams.Length; i++)
            {
                float sweep = Mathf.Sin(_time * (0.34f + i * 0.07f) + _beamPhase[i]);
                _beams[i].transform.rotation = Quaternion.Euler(0f, 0f, sweep * 13f);

                Color color = _beams[i].color;
                color.a = 0.07f + 0.05f * Mathf.Abs(sweep) + _pulse * 0.10f;
                _beams[i].color = color;
            }

            for (int i = 0; i < _sparkles.Length; i++)
            {
                float twinkle = 0.5f + 0.5f * Mathf.Sin(_time * (0.8f + _sparkleSpeed[i] * 2f) + i);
                Color color = _sparkles[i].color;
                color.a = 0.06f + twinkle * 0.22f;
                _sparkles[i].color = color;

                Vector3 position = _sparkles[i].transform.position;
                position.y += Mathf.Sin(_time * 0.3f + i) * 0.0015f;
                _sparkles[i].transform.position = position;
            }

            if (_glow != null)
            {
                Color glow = LaneTheme.BackgroundGlowColor;
                glow.a = 0.14f + 0.06f * Mathf.Sin(_time * 1.1f) + _pulse * 0.16f;
                _glow.color = glow;
            }
        }

        /// <summary>Fires the menu's own light pulse, used when a button is pressed.</summary>
        public void Pulse() => _pulse = 1f;
    }
}
