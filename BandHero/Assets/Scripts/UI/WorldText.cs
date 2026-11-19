using UnityEngine;

namespace BandHero.UI
{
    /// <summary>
    /// A text object that lives in the world, so the in-game HUD costs one draw call per label
    /// instead of an IMGUI pass over the whole screen every frame.
    ///
    /// Height is expressed in world units and fitted after the text is set, which keeps the HUD
    /// the same physical size on every screen without measuring pixels.
    /// </summary>
    public class WorldText
    {
        readonly GameObject _root;
        readonly TextMesh _mesh;
        readonly MeshRenderer _renderer;

        float _height;

        public Transform Transform => _root.transform;

        public WorldText(string name, Transform parent, float height, int sortingOrder, Font font = null)
        {
            _root = new GameObject(name);
            _root.transform.SetParent(parent, false);

            _mesh = _root.AddComponent<TextMesh>();
            _renderer = _root.GetComponent<MeshRenderer>();

            Font resolved = font != null ? font : UiSkin.Font;
            _mesh.font = resolved;
            _mesh.fontSize = 96;
            _mesh.characterSize = 0.1f;
            _mesh.anchor = TextAnchor.MiddleCenter;
            _mesh.alignment = TextAlignment.Center;
            _mesh.color = Color.white;

            // The built-in font material is unlit and tinted through TextMesh.color, which is
            // exactly what a HUD needs and costs nothing extra.
            if (resolved != null) _renderer.sharedMaterial = resolved.material;
            _renderer.sortingOrder = sortingOrder;

            _height = height;
        }

        public void SetAnchor(TextAnchor anchor)
        {
            _mesh.anchor = anchor;
        }

        public void SetAlignment(TextAlignment alignment)
        {
            _mesh.alignment = alignment;
        }

        public void SetText(string text)
        {
            if (_mesh.text == text) return;

            _mesh.text = text ?? "";
            Fit();
        }

        /// <summary>Fits the glyph height to the requested world height.</summary>
        void Fit()
        {
            if (_height <= 0f) return;

            // Bounds are only meaningful once the mesh has been rebuilt, which happens on the
            // next render. A fixed glyph-to-character-size ratio is stable and avoids a rebuild.
            float glyphHeight = _mesh.fontSize * _mesh.characterSize * 0.1f;
            if (glyphHeight <= 0.0001f) return;

            _root.transform.localScale = Vector3.one * (_height / glyphHeight);
        }

        public void SetColor(Color color) => _mesh.color = color;

        public Color Color => _mesh.color;

        public void SetPosition(Vector3 position) => _root.transform.position = position;

        public void SetLocalPosition(Vector3 position) => _root.transform.localPosition = position;

        public void SetVisible(bool visible)
        {
            if (_root.activeSelf != visible) _root.SetActive(visible);
        }

        public bool IsVisible => _root.activeSelf;

        public void Destroy()
        {
            if (_root != null) Object.Destroy(_root);
        }
    }
}
