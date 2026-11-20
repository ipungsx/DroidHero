using UnityEngine;

namespace DroidHero.UI
{
    /// <summary>
    /// Drawing helpers and type styles for the menus.
    ///
    /// Menus are IMGUI because they are static screens: a GUI pass per frame is cheap when
    /// nothing is playing, and it needs no prefabs, canvas or imported art. Gameplay uses sprites
    /// and world text instead, where the cost actually matters.
    /// </summary>
    public static class UiSkin
    {
        public static readonly Color Panel = new Color(0.09f, 0.09f, 0.13f, 0.96f);
        public static readonly Color PanelLight = new Color(0.15f, 0.15f, 0.20f, 0.96f);
        public static readonly Color PanelTranslucent = new Color(0.08f, 0.08f, 0.12f, 0.72f);
        public static readonly Color Accent = new Color(0.72f, 0.44f, 1f);
        public static readonly Color AccentDim = new Color(0.36f, 0.24f, 0.52f);
        public static readonly Color TextPrimary = new Color(0.96f, 0.96f, 0.99f);
        public static readonly Color TextMuted = new Color(0.64f, 0.66f, 0.74f);
        public static readonly Color Danger = new Color(0.95f, 0.35f, 0.42f);
        public static readonly Color Good = new Color(0.45f, 0.92f, 0.55f);
        public static readonly Color Gold = new Color(1f, 0.84f, 0.32f);

        static Texture2D _panelTex;
        static Texture2D _panelLightTex;
        static Texture2D _accentTex;
        static Texture2D _accentDimTex;
        static Texture2D _roundedTex;
        static Texture2D _roundedLightTex;
        static Texture2D _roundedAccentTex;
        static Texture2D _roundedAccentDimTex;
        static Texture2D _roundedGhostTex;
        static Texture2D _chipTex;
        static Texture2D _whiteTex;
        static Font _font;

        static GUIStyle _title;
        static GUIStyle _heading;
        static GUIStyle _body;
        static GUIStyle _small;
        static GUIStyle _tiny;
        static GUIStyle _button;
        static GUIStyle _buttonAccent;
        static GUIStyle _buttonGhost;
        static GUIStyle _textField;
        static GUIStyle _textFieldPrompt;
        static GUIStyle _center;
        static GUIStyle _centerBold;
        static GUIStyle _songTitle;
        static GUIStyle _songTitleCentered;
        static GUIStyle _titleFit;

        public static Texture2D PanelTex => _panelTex != null ? _panelTex : (_panelTex = Solid(Panel));
        public static Texture2D PanelLightTex => _panelLightTex != null ? _panelLightTex : (_panelLightTex = Solid(PanelLight));
        public static Texture2D AccentTex => _accentTex != null ? _accentTex : (_accentTex = Solid(Accent));
        public static Texture2D AccentDimTex => _accentDimTex != null ? _accentDimTex : (_accentDimTex = Solid(AccentDim));

        public static Texture2D WhiteTex => _whiteTex != null ? _whiteTex : (_whiteTex = Solid(Color.white));

        /// <summary>Rounded card background used for song rows, panels and buttons.</summary>
        public static Texture2D RoundedTex
        {
            get
            {
                if (_roundedTex == null) _roundedTex = Rounded(24, Panel);
                return _roundedTex;
            }
        }

        public static Texture2D RoundedLightTex
        {
            get
            {
                if (_roundedLightTex == null) _roundedLightTex = Rounded(24, PanelLight);
                return _roundedLightTex;
            }
        }

        /// <summary>Rounded fill for the selected/accent state, so a chip and a button match.</summary>
        public static Texture2D RoundedAccentTex
        {
            get
            {
                if (_roundedAccentTex == null) _roundedAccentTex = Rounded(24, Accent);
                return _roundedAccentTex;
            }
        }

        public static Texture2D RoundedAccentDimTex
        {
            get
            {
                if (_roundedAccentDimTex == null) _roundedAccentDimTex = Rounded(24, AccentDim);
                return _roundedAccentDimTex;
            }
        }

        public static Texture2D RoundedGhostTex
        {
            get
            {
                if (_roundedGhostTex == null) _roundedGhostTex = Rounded(24, PanelTranslucent);
                return _roundedGhostTex;
            }
        }

        /// <summary>
        /// Chip fill: white so callers tint it, with a small radius. A pill would round the
        /// selected chip into a lozenge and make it read as a different control than its
        /// neighbours, so the corners stay only slightly turned.
        /// </summary>
        public static Texture2D ChipTex
        {
            get
            {
                if (_chipTex == null) _chipTex = Rounded(9, Color.white);
                return _chipTex;
            }
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Rounded rectangle with a soft border, built once and stretched by IMGUI. The border
        /// stays crisp because it is part of the texture rather than a drawn outline.
        /// </summary>
        static Texture2D Rounded(int radius, Color fill)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            Color border = new Color(
                Mathf.Min(1f, fill.r * 2.4f + 0.06f),
                Mathf.Min(1f, fill.g * 2.4f + 0.06f),
                Mathf.Min(1f, fill.b * 2.4f + 0.08f),
                fill.a);

            var pixels = new Color[size * size];
            float r = radius;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = 0f, dy = 0f;
                    if (x < r) dx = r - x;
                    else if (x > size - 1 - r) dx = x - (size - 1 - r);
                    if (y < r) dy = r - y;
                    else if (y > size - 1 - r) dy = y - (size - 1 - r);

                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);

                    bool onBorder = distance > r - 1.6f && distance <= r + 0.5f;
                    Color color = onBorder ? border : fill;
                    color.a *= alpha;

                    pixels[y * size + x] = color;
                }
            }

            texture.SetPixels(pixels);
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
            fontSize = ScaledSize(52),
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
            wordWrap = true,
            normal = { textColor = TextMuted }
        });

        public static GUIStyle Tiny => _tiny ?? (_tiny = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(13),
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = TextMuted }
        });

        public static GUIStyle Center => _center ?? (_center = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(20),
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = TextPrimary }
        });

        public static GUIStyle CenterBold => _centerBold ?? (_centerBold = new GUIStyle(Center)
        {
            fontSize = ScaledSize(24),
            fontStyle = FontStyle.Bold
        });

        /// <summary>
        /// A song title that has to survive an arbitrary name. Chart names routinely run past
        /// forty characters, so the label wraps instead of overflowing the row, and clips rather
        /// than bleeding into the artist line underneath.
        /// </summary>
        public static GUIStyle SongTitle => _songTitle ?? (_songTitle = new GUIStyle(CenterBold)
        {
            fontSize = ScaledSize(18),
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            clipping = TextClipping.Clip
        });

        /// <summary>Song title inside the detail panel, centred over the artist line.</summary>
        public static GUIStyle SongTitleCentered => _songTitleCentered ?? (_songTitleCentered = new GUIStyle(SongTitle)
        {
            alignment = TextAnchor.UpperCenter
        });

        /// <summary>
        /// Draws a bold label shrunk until it fits its box.
        ///
        /// Chart names are arbitrary and often long, and a fixed font size lets them overflow
        /// into the artist line or the button beside them. Measuring first and scaling the font
        /// down keeps every row the same shape whatever the name is.
        /// </summary>
        public static void FittedTitle(Rect rect, string text, TextAnchor anchor = TextAnchor.MiddleLeft,
                                 int maxSize = 24, int minSize = 11)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (_titleFit == null) _titleFit = new GUIStyle(CenterBold);

            _titleFit.alignment = anchor;
            _titleFit.wordWrap = false;
            _titleFit.clipping = TextClipping.Clip;
            _titleFit.fontSize = ScaledSize(maxSize);

            Vector2 measured = _titleFit.CalcSize(new GUIContent(text));
            float scale = 1f;
            if (measured.x > rect.width && measured.x > 0f) scale = rect.width / measured.x;
            if (measured.y > rect.height && measured.y > 0f) scale = Mathf.Min(scale, rect.height / measured.y);

            _titleFit.fontSize = Mathf.Max(ScaledSize(minSize), Mathf.RoundToInt(_titleFit.fontSize * scale));
            GUI.Label(rect, text, _titleFit);
        }

        /// <summary>
        /// Drag-to-scroll for a list, driven by the finger.
        ///
        /// IMGUI's scroll view only moves for a mouse wheel or a drag on its scrollbar, and a
        /// phone offers neither, so any list taller than its view was stuck. This returns the
        /// scroll position the finger asks for; the caller still passes it to
        /// <see cref="GUI.BeginScrollView"/> so clipping and the wheel keep working.
        /// </summary>
        public sealed class DragScroller
        {
            int _finger = -1;
            Vector2 _last;
            bool _dragging;

            public Vector2 Apply(Rect view, Vector2 scroll, float contentHeight)
            {
                float limit = Mathf.Max(0f, contentHeight - view.height);
                float travel = 0f;

                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);

                    if (touch.phase == TouchPhase.Began)
                    {
                        // GUI rects measure from the top, touches from the bottom.
                        var point = new Vector2(touch.position.x, Screen.height - touch.position.y);
                        if (_finger < 0 && view.Contains(point))
                        {
                            _finger = touch.fingerId;
                            _last = touch.position;
                        }
                    }
                    else if (touch.fingerId == _finger)
                    {
                        if (touch.phase == TouchPhase.Moved)
                        {
                            travel += touch.position.y - _last.y;
                            _last = touch.position;
                        }
                        else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                        {
                            _finger = -1;
                        }
                    }
                }

                // The editor sends no touches, so a mouse drag stands in for a finger there.
                if (Input.touchCount == 0)
                {
                    var point = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

                    if (Input.GetMouseButtonDown(0) && view.Contains(point))
                    {
                        _dragging = true;
                        _last = Input.mousePosition;
                    }
                    else if (_dragging && Input.GetMouseButton(0))
                    {
                        travel += Input.mousePosition.y - _last.y;
                        _last = Input.mousePosition;
                    }
                    else if (_dragging && !Input.GetMouseButton(0))
                    {
                        _dragging = false;
                    }
                }

                scroll.y = Mathf.Clamp(scroll.y + travel, 0f, limit);
                return scroll;
            }
        }

        public static GUIStyle Button => _button ?? (_button = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(20),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(ScaledSize(16), ScaledSize(16), ScaledSize(10), ScaledSize(10)),
            border = new RectOffset(24, 24, 24, 24),
            normal = { background = RoundedLightTex, textColor = TextPrimary },
            hover = { background = RoundedAccentDimTex, textColor = TextPrimary },
            active = { background = RoundedAccentTex, textColor = Color.black }
        });

        public static GUIStyle ButtonAccent => _buttonAccent ?? (_buttonAccent = new GUIStyle(Button)
        {
            normal = { background = RoundedAccentTex, textColor = Color.black },
            hover = { background = RoundedAccentTex, textColor = Color.black },
            active = { background = RoundedAccentTex, textColor = Color.black }
        });

        /// <summary>Low-emphasis button for secondary actions such as BACK.</summary>
        public static GUIStyle ButtonGhost => _buttonGhost ?? (_buttonGhost = new GUIStyle(Button)
        {
            fontSize = ScaledSize(18),
            normal = { background = RoundedGhostTex, textColor = TextMuted },
            hover = { background = RoundedLightTex, textColor = TextPrimary },
            active = { background = RoundedAccentDimTex, textColor = TextPrimary }
        });

        static Texture2D _panelTranslucentTex;
        public static Texture2D PanelTranslucentTex =>
            _panelTranslucentTex != null ? _panelTranslucentTex : (_panelTranslucentTex = Solid(PanelTranslucent));

        public static GUIStyle TextField => _textField ?? (_textField = new GUIStyle
        {
            font = Font,
            fontSize = ScaledSize(20),
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(ScaledSize(14), ScaledSize(14), ScaledSize(8), ScaledSize(8)),
            border = new RectOffset(24, 24, 24, 24),
            normal = { background = RoundedLightTex, textColor = TextPrimary },
            focused = { background = RoundedLightTex, textColor = TextPrimary }
        });

        /// <summary>Same box as <see cref="TextField"/>, muted while the field is still empty.</summary>
        public static GUIStyle TextFieldPrompt => _textFieldPrompt ?? (_textFieldPrompt = new GUIStyle(TextField)
        {
            normal = { background = RoundedLightTex, textColor = TextMuted },
            focused = { background = RoundedLightTex, textColor = TextMuted }
        });

        /// <summary>Draws a flat colour quad; used for separators, bars and highlights.</summary>
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

        /// <summary>Card with rounded corners and a soft border.</summary>
        public static void Card(Rect rect, Color tint)
        {
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, RoundedTex, ScaleMode.StretchToFill, true);
            GUI.color = previous;
        }

        /// <summary>
        /// Vertical gradient. IMGUI has no gradient primitive, so this stacks thin quads; eight
        /// bands is enough to look smooth at phone resolution and costs almost nothing.
        /// </summary>
        public static void VerticalGradient(Rect rect, Color top, Color bottom, int bands = 8)
        {
            float bandHeight = rect.height / bands;
            for (int i = 0; i < bands; i++)
            {
                float t = i / (float)(bands - 1);
                Fill(new Rect(rect.x, rect.y + i * bandHeight, rect.width, bandHeight + 1f),
                     Color.Lerp(top, bottom, t));
            }
        }

        public static void Label(Rect rect, string text, GUIStyle style)
        {
            GUI.Label(rect, text, style);
        }

        /// <summary>Rounded pill used for difficulty, theme and filter chips.</summary>
        public static bool Chip(Rect rect, string label, bool selected, Color accent)
        {
            Color previous = GUI.color;
            GUI.color = selected ? accent : new Color(1f, 1f, 1f, 0.10f);
            GUI.DrawTexture(rect, ChipTex, ScaleMode.StretchToFill, true);
            GUI.color = previous;

            var style = new GUIStyle(CenterBold)
            {
                fontSize = ScaledSize(17),
                normal = { textColor = selected ? Color.black : TextPrimary }
            };

            GUI.Label(rect, label, style);
            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        /// <summary>Thin progress bar; used for download and load feedback.</summary>
        public static void ProgressBar(Rect rect, float fraction, Color color)
        {
            Fill(rect, new Color(0f, 0f, 0f, 0.45f));
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        /// <summary>Star row drawn as text, avoiding a sprite per star.</summary>
        public static void Stars(Rect rect, int count, int max = 5)
        {
            var style = new GUIStyle(CenterBold)
            {
                fontSize = ScaledSize(20),
                normal = { textColor = Gold }
            };

            GUI.Label(rect, new string('*', Mathf.Clamp(count, 0, max)), style);
        }
    }
}
