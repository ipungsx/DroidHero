using System.Collections.Generic;
using UnityEngine;
using BandHero.Chart;

namespace BandHero.Gameplay
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

        readonly bool[] _held = new bool[LaneInfo.Count];
        readonly bool[] _keyboardHeld = new bool[LaneInfo.Count];
        readonly Dictionary<int, int> _fingerLane = new Dictionary<int, int>();
        readonly Dictionary<int, bool> _fingerIsStrum = new Dictionary<int, bool>();

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
            bool inStrumStrip = world.y < Layout.HitLineY - Layout.NoteHeight &&
                                world.y > Layout.HitLineY - Layout.NoteHeight - StrumStripHeight;

            if (inStrumStrip)
            {
                _fingerIsStrum[fingerId] = true;
                StrumHeld = true;
                Session.Strum(_held);
                return;
            }

            int lane = Layout.LaneAtX(world.x);
            if (lane < 0) return;

            _fingerIsStrum[fingerId] = false;
            _fingerLane[fingerId] = lane;
            _held[lane] = true;
            Session.PressFret(lane, strummed: false);
        }

        void MoveTouch(int fingerId, Vector2 world)
        {
            if (_fingerIsStrum.TryGetValue(fingerId, out bool isStrum) && isStrum) return;

            if (!_fingerLane.TryGetValue(fingerId, out int previousLane)) return;

            int lane = Layout.LaneAtX(world.x);
            if (lane < 0 || lane == previousLane) return;

            // Leaving the highway vertically (into the strum strip) keeps the fret held.

            // Sliding onto another lane moves the fret, which is how players shift fingers
            // during fast runs instead of lifting off.
            _held[previousLane] = false;
            Session.ReleaseFret(previousLane);

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
                _held[lane] = false;
                Session.ReleaseFret(lane);
            }
        }

        void PruneStaleTouches()
        {
            if (Input.touchCount > 0) return;

            if (_fingerLane.Count > 0 || _fingerIsStrum.Count > 0)
            {
                var lanes = new List<int>(_fingerLane.Values);
                for (int i = 0; i < lanes.Count; i++)
                {
                    _held[lanes[i]] = false;
                    Session.ReleaseFret(lanes[i]);
                }

                _fingerLane.Clear();
                _fingerIsStrum.Clear();
                StrumHeld = false;
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
