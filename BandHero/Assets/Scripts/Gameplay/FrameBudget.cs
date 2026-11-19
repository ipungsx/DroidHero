using UnityEngine;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Keeps the frame rate at the target by shedding optional visuals, never by slowing the game.
    ///
    /// Gameplay is already allocation-free and driven by the audio clock, so the only thing that
    /// can cost a device its 60fps is drawing too much stage. This watches the smoothed frame time
    /// and steps the light count down when the budget is missed, then back up when there is room.
    /// The highway, notes and input are never touched: a rhythm game must not degrade the part
    /// the player is reading.
    /// </summary>
    public class FrameBudget : MonoBehaviour
    {
        public int TargetFrameRate = 60;

        /// <summary>Seconds per frame above which the device is considered to be struggling.</summary>
        public float BudgetSeconds = 1f / 60f;

        /// <summary>Frame time has to stay this bad for <see cref="DownshiftSeconds"/> before trimming.</summary>
        public float DownshiftSeconds = 0.75f;

        /// <summary>Frame time has to stay good this long before restoring detail.</summary>
        public float UpshiftSeconds = 3f;

        public int MaxLights = 14;
        public int MinLights = 4;

        ConcertStage _stage;
        float _smoothedFrame;
        float _badTime;
        float _goodTime;
        int _level = 2; // 0 = minimum detail, 2 = everything

        public int QualityLevel => _level;

        public void Attach(ConcertStage stage) => _stage = stage;

        void Awake()
        {
            Application.targetFrameRate = TargetFrameRate;
            QualitySettings.vSyncCount = 0;
            _smoothedFrame = BudgetSeconds;
        }

        void Update()
        {
            // Unscaled so a paused game does not read as a slow one.
            float frame = Time.unscaledDeltaTime;
            _smoothedFrame = Mathf.Lerp(_smoothedFrame, frame, 0.08f);

            if (_smoothedFrame > BudgetSeconds * 1.22f)
            {
                _badTime += frame;
                _goodTime = 0f;
            }
            else if (_smoothedFrame < BudgetSeconds * 1.02f)
            {
                _goodTime += frame;
                _badTime = 0f;
            }
            else
            {
                _badTime = 0f;
                _goodTime = 0f;
            }

            if (_badTime >= DownshiftSeconds && _level > 0)
            {
                _level--;
                _badTime = 0f;
                ApplyLevel();
            }
            else if (_goodTime >= UpshiftSeconds && _level < 2)
            {
                _level++;
                _goodTime = 0f;
                ApplyLevel();
            }
        }

        void ApplyLevel()
        {
            if (_stage == null) return;

            switch (_level)
            {
                case 0:
                    _stage.ActiveLightCount = MinLights;
                    _stage.BeamShowEnabled = false;
                    break;
                case 1:
                    _stage.ActiveLightCount = (MinLights + MaxLights) / 2;
                    _stage.BeamShowEnabled = false;
                    break;
                default:
                    _stage.ActiveLightCount = MaxLights;
                    _stage.BeamShowEnabled = true;
                    break;
            }
        }

        /// <summary>Smoothed frame time in seconds, for the debug overlay.</summary>
        public float SmoothedFrameSeconds => _smoothedFrame;

        public int SmoothedFps => _smoothedFrame <= 0f ? 0 : Mathf.RoundToInt(1f / _smoothedFrame);
    }
}
