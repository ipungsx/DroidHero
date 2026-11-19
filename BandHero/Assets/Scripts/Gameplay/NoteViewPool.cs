using System.Collections.Generic;
using UnityEngine;
using BandHero.UI;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Recycles note visuals. Charts can hold thousands of notes, so allocating a GameObject per
    /// note would cause GC spikes on Android; instead a fixed set of views is reused.
    /// </summary>
    public class NoteViewPool
    {
        class View
        {
            public GameObject Root;
            public SpriteRenderer Body;
            public SpriteRenderer Glow;
            public SpriteRenderer Sustain;
            public bool InUse;
        }

        readonly Transform _parent;
        readonly List<View> _views = new List<View>();
        readonly int _sortingOrder;

        public NoteViewPool(Transform parent, int initialCapacity, int sortingOrder)
        {
            _parent = parent;
            _sortingOrder = sortingOrder;

            for (int i = 0; i < initialCapacity; i++) _views.Add(CreateView());
        }

        View CreateView()
        {
            var root = new GameObject("NoteView");
            root.transform.SetParent(_parent, false);

            var sustain = new GameObject("Sustain");
            sustain.transform.SetParent(root.transform, false);
            var sustainRenderer = sustain.AddComponent<SpriteRenderer>();
            sustainRenderer.sprite = ProceduralSprites.White;
            sustainRenderer.sortingOrder = _sortingOrder - 1;
            sustainRenderer.enabled = false;

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var bodyRenderer = body.AddComponent<SpriteRenderer>();
            bodyRenderer.sprite = ProceduralSprites.RoundedNote;
            bodyRenderer.sortingOrder = _sortingOrder;

            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);
            var glowRenderer = glow.AddComponent<SpriteRenderer>();
            glowRenderer.sprite = ProceduralSprites.RoundedNote;
            glowRenderer.sortingOrder = _sortingOrder - 1;
            glowRenderer.color = new Color(1f, 1f, 1f, 0.35f);
            glowRenderer.enabled = false;

            return new View
            {
                Root = root,
                Body = bodyRenderer,
                Glow = glowRenderer,
                Sustain = sustainRenderer,
                InUse = false
            };
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].InUse = false;
                _views[i].Root.SetActive(false);
            }
        }

        public void Acquire(out SpriteRenderer body, out SpriteRenderer glow, out SpriteRenderer sustain)
        {
            View view = null;

            for (int i = 0; i < _views.Count; i++)
            {
                if (_views[i].InUse) continue;
                view = _views[i];
                break;
            }

            if (view == null)
            {
                view = CreateView();
                _views.Add(view);
            }

            view.InUse = true;
            view.Root.SetActive(true);
            view.Glow.enabled = false;
            view.Sustain.enabled = false;

            body = view.Body;
            glow = view.Glow;
            sustain = view.Sustain;
        }
    }
}
