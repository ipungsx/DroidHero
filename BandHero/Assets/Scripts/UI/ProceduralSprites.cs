using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// Generates the small set of sprites the game needs (solid quads and rounded note caps)
    /// at runtime, so the project ships without binary art assets.
    /// </summary>
    public static class ProceduralSprites
    {
        static Sprite _white;
        static Sprite _roundedNote;
        static Sprite _circle;
        static Sprite _glow;
        static Sprite _ring;
        static Sprite _softRing;
        static Sprite _verticalFade;
        static Sprite _beam;

        public static Sprite White
        {
            get
            {
                if (_white == null) _white = CreateSolid(4, 4);
                return _white;
            }
        }

        /// <summary>A quad with softly rounded corners, used for panels and buttons.</summary>
        public static Sprite RoundedNote
        {
            get
            {
                if (_roundedNote == null) _roundedNote = CreateRounded(64, 64, 14);
                return _roundedNote;
            }
        }

        /// <summary>Filled circle, the shape of every note head.</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null) _circle = CreateCircle(96);
                return _circle;
            }
        }

        /// <summary>
        /// Radial falloff, drawn additively behind a note or a stage light. Alpha falls off
        /// smoothly so a tinted quad reads as a soft bloom rather than a hard-edged disc.
        /// </summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow == null) _glow = CreateRadial(96, 1f, 0f);
                return _glow;
            }
        }

        /// <summary>Bright thin circle, used for the ripple left behind by a hit note.</summary>
        public static Sprite Ring
        {
            get
            {
                if (_ring == null) _ring = CreateRing(128, 0.62f, 0.40f);
                return _ring;
            }
        }

        /// <summary>Wide soft ring for the strike-line pulse.</summary>
        public static Sprite SoftRing
        {
            get
            {
                if (_softRing == null) _softRing = CreateRing(128, 0.42f, 0.58f);
                return _softRing;
            }
        }

        /// <summary>Vertical gradient that fades to transparent at the top, for lane trails.</summary>
        public static Sprite VerticalFade
        {
            get
            {
                if (_verticalFade == null) _verticalFade = CreateVerticalFade(8, 64);
                return _verticalFade;
            }
        }

        /// <summary>Soft-edged beam used for the stage spotlights.</summary>
        public static Sprite Beam
        {
            get
            {
                if (_beam == null) _beam = CreateBeam(32, 128);
                return _beam;
            }
        }

        public static Sprite CreateSolid(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Sprite CreateRounded(int width, int height, int radius)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[width * height];
            float r = radius;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Distance from the nearest corner circle centre.
                    float dx = 0f, dy = 0f;

                    if (x < r) dx = r - x;
                    else if (x > width - r) dx = x - (width - r);

                    if (y < r) dy = r - y;
                    else if (y > height - r) dy = y - (height - r);

                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(r - distance + 0.5f);

                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Sprite CreateCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            float center = size * 0.5f;
            float radius = center - 1f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center + 0.5f;
                    float dy = y - center + 0.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        public static Sprite FromTexture(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0) return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(imageBytes)) return null;

            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Radial falloff: solid to <paramref name="coreRadius"/>, then smooth to nothing.</summary>
        static Sprite CreateRadial(int size, float coreRadius, float edgeAlpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            float center = size * 0.5f;
            float radius = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center + 0.5f) / radius;
                    float dy = (y - center + 0.5f) / radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    // Squared falloff reads as a bloom; linear falloff looks like a flat disc.
                    float alpha = distance <= coreRadius
                        ? 1f
                        : Mathf.Clamp01(1f - (distance - coreRadius) / Mathf.Max(0.0001f, 1f - coreRadius));
                    alpha = alpha * alpha;
                    alpha = Mathf.Max(alpha, edgeAlpha);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Annulus with a soft inner and outer edge.</summary>
        static Sprite CreateRing(int size, float inner, float thickness)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            float center = size * 0.5f;
            float outer = inner + thickness;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - center + 0.5f) / center;
                    float dy = (y - center + 0.5f) / center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = 0f;
                    if (distance >= inner && distance <= outer)
                    {
                        // Taper toward both edges so the ring never shows a stair-stepped border.
                        float fromInner = Mathf.Clamp01((distance - inner) / Mathf.Max(0.0001f, thickness * 0.45f));
                        float fromOuter = Mathf.Clamp01((outer - distance) / Mathf.Max(0.0001f, thickness * 0.55f));
                        alpha = Mathf.Min(fromInner, fromOuter);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Opaque at the bottom, transparent at the top.</summary>
        static Sprite CreateVerticalFade(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                byte alpha = (byte)(Mathf.Clamp01(1f - t) * 255f);

                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>Bright at the bottom, fading out along the length, with soft sides.</summary>
        static Sprite CreateBeam(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[width * height];
            float halfWidth = (width - 1) * 0.5f;

            for (int y = 0; y < height; y++)
            {
                float alongLength = y / (float)(height - 1);
                float lengthFade = 1f - alongLength;
                lengthFade *= lengthFade;

                for (int x = 0; x < width; x++)
                {
                    float across = Mathf.Abs(x - halfWidth) / Mathf.Max(0.0001f, halfWidth);
                    float sideFade = Mathf.Clamp01(1f - across);
                    sideFade *= sideFade;

                    float alpha = Mathf.Clamp01(lengthFade * sideFade);
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
