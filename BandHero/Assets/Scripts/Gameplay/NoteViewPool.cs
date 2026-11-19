using System.Collections.Generic;
using UnityEngine;
using BandHero.UI;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Recycles note visuals. Charts can hold thousands of notes, so allocating a GameObject per
    /// note would cause GC spikes on Android; instead a fixed set of views is reused.
    ///
    /// Each note is drawn as three stacked sprites: an additive halo, the round body, and a
    /// small specular highlight. The halo is what makes the note read as "glowing" on a phone
    /// screen without any post-processing, which would cost a full-screen pass per frame.
    /// </summary>
    public class NoteViewPool
    {
        public class View
        {
            public GameObject Root;
            public SpriteRenderer Halo;
            public SpriteRenderer Body;
            public SpriteRenderer Core;
            public SpriteRenderer Sustain;
            public SpriteRenderer Ripple;
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

            var sustain = Child(root.transform, "Sustain", ProceduralSprites.VerticalFade, _sortingOrder - 2);
            var halo = Child(root.transform, "Halo", ProceduralSprites.Glow, _sortingOrder - 1);
            var body = Child(root.transform, "Body", ProceduralSprites.Circle, _sortingOrder);
            var core = Child(root.transform, "Core", ProceduralSprites.Glow, _sortingOrder + 1);
            var ripple = Child(root.transform, "Ripple", ProceduralSprites.Ring, _sortingOrder + 2);

            sustain.enabled = false;
            ripple.enabled = false;

            return new View
            {
                Root = root,
                Halo = halo,
                Body = body,
                Core = core,
                Sustain = sustain,
                Ripple = ripple,
                InUse = false
            };
        }

        static SpriteRenderer Child(Transform parent, string name, Sprite sprite, int order)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);

            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, 0f);

            return renderer;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _views.Count; i++)
            {
                _views[i].InUse = false;
                if (_views[i].Root.activeSelf) _views[i].Root.SetActive(false);
            }
        }

        public View Acquire()
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
            view.Ripple.enabled = false;
            view.Sustain.enabled = false;
            view.Core.enabled = true;

            return view;
        }
    }
}
