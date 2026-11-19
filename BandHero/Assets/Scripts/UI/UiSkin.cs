using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// Small helper set for drawing a consistent dark interface with IMGUI, so the game needs
    /// no imported UI assets, fonts or prefabs to run on a device.
    /// </summary>
    public static class UiSkin
    {
        public static readonly Color Panel = new Color(0.09f, 0.09f, 0.13f, 0.96f);
        public static readonly Color PanelLight = new Color(0.15f, 0.15f, 0.20f, 0.96f);
        public static readonly Color Accent = new Color(0.35f, 0.78f, 1f);
        public static readonly Color AccentDim = new Color(0.20f, 0.42f, 0.55f);
        public static readonly Color TextPrimary = new Color(0.95f, 0.96f, 0.98f);
        public static readonly Color TextMuted = new Color(0.62f, 0.65f, 0.72f);
        public static readonly Color Danger = new Color(0.95f, 0.35f, 0.35f);
        public static readonly Color Good = new Color(0.40f, 0.90f, 0.45f);

        static Texture2D _panelTex;
        static Texture2D _panelLightTex;
        static Texture2D _accentTex;
        static Texture2D _accentDimTex;
        static Font _font;
        static GUIStyle _title;
        static GUIStyle _heading;
        static GUIStyle _body;
        static GUIStyle _small;
        static GUIStyle _button;
        static GUIStyle _buttonAccent;
        static GUIStyle _textField;

        public static Texture2D PanelTex => _panelTex != null ? _panelTex : (_panelTex = Solid(Panel));
        public static Texture2D PanelLightTex => _panelLightTex != null ? _panelLightTex : (_panelLightTex = Solid(PanelLight));
        public static Texture2D AccentTex => _accentTex != null ? _accentTex : (_accentTex = Solid(Accent));
        public static Texture2D AccentDimTex => _accentDimTex != null ? _accentDimTex : (_accentDimTex = Solid(AccentDim));

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Unity renamed the built-in font, so try the current name first and fall back for
        /// older editor versions.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null)
                {
                    var fonts = Font.GetOSInstalledFontNames();
                    if (fonts != null && fonts.Length > 0) _font = Font.CreateDynamicFontFromOSFont(fonts[0], 16);
                }

                return _font;
            }
        }

        static int ScaledSize(int baseSize) =>
            Mathf.RoundToInt(baseSize * Mathf.Max(1f, Screen.height / 1080f));

        public static GUIStyle Title => _title ?? (_title = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(46),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextPrimary }
        });

        public static GUIStyle Heading => _heading ?? (_heading = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(26),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = TextPrimary }
        });

        public static GUIStyle Body => _body ?? (_body = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(19),
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            normal = { textColor = TextPrimary }
        });

        public static GUIStyle Small => _small ?? (_small = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(16),
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = TextMuted }
        });

        public static GUIStyle Button => _button ?? (_button = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(20),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { background = PanelLightTex, textColor = TextPrimary },
            hover = { background = AccentDimTex, textColor = TextPrimary },
            active = { background = AccentTex, textColor = Color.black }
        });

        public static GUIStyle ButtonAccent => _buttonAccent ?? (_buttonAccent = new GUIStyle(Button)
        {
            normal = { background = AccentTex, textColor = Color.black },
            hover = { background = AccentTex, textColor = Color.black }
        });

        public static GUIStyle TextField => _textField ?? (_textField = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(20),
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(ScaledSize(12), ScaledSize(12), ScaledSize(8), ScaledSize(8)),
            normal = { background = PanelLightTex, textColor = TextPrimary },
            focused = { background = PanelLightTex, textColor = TextPrimary }
        });

        /// <summary>Draws a flat colour quad; used for separators and highlights.</summary>
        public static void Fill(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public static void PanelRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, PanelTex);
            GUI.color = previous;
        }

        public static void Label(Rect rect, string text, GUIStyle style)
        {
            GUI.Label(rect, text, style);
        }
    }
}
