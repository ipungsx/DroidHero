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

        public static Sprite White
        {
            get
            {
                if (_white == null) _white = CreateSolid(4, 4);
                return _white;
            }
        }

        /// <summary>A quad with softly rounded corners, used for note bodies and buttons.</summary>
        public static Sprite RoundedNote
        {
            get
            {
                if (_roundedNote == null) _roundedNote = CreateRounded(64, 64, 14);
                return _roundedNote;
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
    }
}
