using System.Collections.Generic;
using UnityEngine;
using DroidHero.Chart;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// Turns touch (or mouse) input into fret presses and strums.
    ///
    /// Android has no strum bar, so the highway is split into two zones: touching a lane below
    /// the hit line presses that fret, and a separate strum strip along the bottom registers the
    /// strum. Multi-touch is tracked per finger so chords work.
    /// </summary>
    public class TouchInputController : MonoBehaviour
    {
        public HighwayLayout Layout = new HighwayLayout();
        public GameSession Session;

        /// <summary>Height of the strum strip measured up from the bottom of the screen.</summary>
        public float StrumStripHeight = 1.5f;

        /// <summary>
        /// Marker stored in <see cref="_fingerLane"/> for a finger that is down but not over a
        /// lane. It is never a real lane, so every consumer has to test for it.
        /// </summary>
        const int OutsideLane = -2;

        readonly bool[] _held = new bool[LaneInfo.Count];
        readonly bool[] _keyboardHeld = new bool[LaneInfo.Count];
        readonly Dictionary<int, int> _fingerLane = new Dictionary<int, int>();
        readonly Dictionary<int, bool> _fingerIsStrum = new Dictionary<int, bool>();

        /// <summary>Scratch buffers, reused every frame so input never allocates.</summary>
        readonly List<int> _activeFingers = new List<int>(10);
        readonly List<int> _staleFingers = new List<int>(10);

        Camera _camera;

        public IList<bool> HeldLanes => _held;

        /// <summary>True when the player is holding the strum strip, for the renderer's feedback.</summary>
        public bool StrumHeld { get; private set; }

        void Awake()
        {
            _camera = Camera.main;
        }

        void Update()
        {
            if (Session == null) return;

            if (Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer ||
                Application.platform == RuntimePlatform.LinuxPlayer)
            {
                PollKeyboard();
            }

            PollTouches();
            RefreshHeldState();
            Session.AutoStrumChords(_held);
        }

        void PollKeyboard()
        {
            KeyCode[] codes = { KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.F, KeyCode.G };

            for (int i = 0; i < codes.Length; i++)
            {
                if (Input.GetKeyDown(codes[i]))
                {
                    _keyboardHeld[i] = true;
                    Session.PressFret(i, strummed: false);
                }
                else if (Input.GetKeyUp(codes[i]))
                {
                    _keyboardHeld[i] = false;
                    Session.ReleaseFret(i);
                }
            }

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                Session.Strum(_held);
            }

            if (Input.GetKeyDown(KeyCode.LeftShift))
            {
                Session.ActivateStarPower();
            }
        }

        void PollTouches()
        {
            int count = Input.touchCount;
            if (count == 0)
            {
                HandleMouse();
                PruneStaleTouches();
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Touch touch = Input.GetTouch(i);
                Vector2 world = ScreenToWorld(touch.position);

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        BeginTouch(touch.fingerId, world);
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        MoveTouch(touch.fingerId, world);
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        EndTouch(touch.fingerId);
                        break;
                }
            }

            PruneStaleTouches();
        }

        void HandleMouse()
        {
            Vector2 world = ScreenToWorld(Input.mousePosition);

            if (Input.GetMouseButtonDown(0))
            {
                BeginTouch(-1, world);
            }
            else if (Input.GetMouseButton(0))
            {
                MoveTouch(-1, world);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                EndTouch(-1);
            }
        }

        void BeginTouch(int fingerId, Vector2 world)
        {
            // The strum strip sits below the highway, so a thumb resting there can strum
            // while the other fingers hold frets on the highway itself.
            if (Layout.IsInStrumStrip(world.y, StrumStripHeight))
            {
                _fingerIsStrum[fingerId] = true;
                StrumHeld = true;
                Session.Strum(_held);
                return;
            }

            // A touch that starts beside the highway still has to be tracked: the player may
            // drag it onto a lane, and dropping it here meant that swipe never pressed a fret.
            _fingerIsStrum[fingerId] = false;
            _fingerLane[fingerId] = OutsideLane;

            int lane = Layout.LaneAtXAt(world.x, world.y);
            if (lane < 0) return;

            _fingerLane[fingerId] = lane;
            _held[lane] = true;
            Session.PressFret(lane, strummed: false);
        }

        /// <summary>
        /// Refreshes a finger that is already down.
        ///
        /// This runs for both Moved and Stationary because Android stops sending Moved once a
        /// finger settles, and a chord needs its lane to stay correct without further motion.
        /// A finger that slides off the side of the highway releases its fret: without that the
        /// lane stayed held forever, so the next note in it auto-landed and the fret looked stuck.
        /// </summary>
        void MoveTouch(int fingerId, Vector2 world)
        {
            if (_fingerIsStrum.TryGetValue(fingerId, out bool isStrum) && isStrum) return;

            if (!_fingerLane.TryGetValue(fingerId, out int previousLane)) return;

            int lane = Layout.LaneAtXAt(world.x, world.y);

            if (lane < 0)
            {
                if (previousLane == OutsideLane) return;

                _fingerLane[fingerId] = OutsideLane;
                _held[previousLane] = false;
                Session.ReleaseFret(previousLane);
                return;
            }

            if (lane == previousLane) return;

            // Leaving the highway vertically (into the strum strip) keeps the fret held.

            // Sliding onto another lane moves the fret, which is how players shift fingers
            // during fast runs instead of lifting off.
            if (previousLane != OutsideLane)
            {
                _held[previousLane] = false;
                Session.ReleaseFret(previousLane);
            }

            _fingerLane[fingerId] = lane;
            _held[lane] = true;
            Session.PressFret(lane, strummed: false);
        }

        void EndTouch(int fingerId)
        {
            if (_fingerIsStrum.TryGetValue(fingerId, out bool isStrum))
            {
                if (isStrum) StrumHeld = false;
                _fingerIsStrum.Remove(fingerId);
            }

            if (_fingerLane.TryGetValue(fingerId, out int lane))
            {
                _fingerLane.Remove(fingerId);

                // OutsideLane is a marker, not a real lane: releasing it would clear the wrong
                // entry and could drop a fret another finger is still holding.
                if (lane != OutsideLane)
                {
                    _held[lane] = false;
                    Session.ReleaseFret(lane);
                }
            }
        }

        /// <summary>
        /// Releases any tracked finger that is no longer down.
        ///
        /// A touch can end without an <see cref="TouchPhase.Ended"/>: the OS drops it when the
        /// app loses focus, a gesture is stolen, or the touch budget overruns. Leaning on that
        /// event alone left the lane held, which both stuck the fret lit and let the next note in
        /// that lane auto-land. Comparing against the live touch list catches every such case,
        /// including one finger vanishing while others stay down.
        /// </summary>
        void PruneStaleTouches()
        {
            _activeFingers.Clear();
            for (int i = 0; i < Input.touchCount; i++)
            {
                _activeFingers.Add(Input.GetTouch(i).fingerId);
            }

            if (_fingerLane.Count == 0 && _fingerIsStrum.Count == 0) return;

            _staleFingers.Clear();
            foreach (var pair in _fingerLane)
            {
                // The mouse is tracked as finger -1 and released by its own button state, so it
                // must not be pruned against the touch list, which never contains it.
                if (pair.Key < 0) continue;
                if (!_activeFingers.Contains(pair.Key)) _staleFingers.Add(pair.Key);
            }
            foreach (var pair in _fingerIsStrum)
            {
                if (pair.Key < 0) continue;
                if (!_activeFingers.Contains(pair.Key)) _staleFingers.Add(pair.Key);
            }

            for (int i = 0; i < _staleFingers.Count; i++)
            {
                EndTouch(_staleFingers[i]);
            }
        }

        /// <summary>
        /// Rebuilds the held-lane view from every input source.
        ///
        /// Both sources have to be folded in here: rebuilding from fingers alone would clear a
        /// lane held by the keyboard on the very next frame, and the strum would then be sent
        /// with no frets down.
        /// </summary>
        void RefreshHeldState()
        {
            for (int lane = 0; lane < _held.Length; lane++)
            {
                bool held = _keyboardHeld[lane];

                if (!held)
                {
                    foreach (var pair in _fingerLane)
                    {
                        if (pair.Value == lane) { held = true; break; }
                    }
                }

                _held[lane] = held;
            }
        }

        Vector2 ScreenToWorld(Vector2 screen)
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return Vector2.zero;

            Vector3 world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            return new Vector2(world.x, world.y);
        }
    }
}
