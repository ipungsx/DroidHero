using UnityEngine;
using BandHero.Chart;
using BandHero.UI;

namespace BandHero.Gameplay
{
    /// <summary>
    /// The in-game overlay: score, combo, star power meter, judgement popups and pause.
    ///
    /// Drawn with IMGUI on top of the sprite highway, which keeps it independent of the camera
    /// and readable at any screen size. Everything here reads the session, so it can never
    /// disagree with the score the player is awarded.
    /// </summary>
    public class GameplayHud : MonoBehaviour
    {
        public GameSession Session;
        public GameplayController Controller;

        const float JudgementSeconds = 0.55f;

        Judgement _lastJudgement;
        float _judgementTimer;

        bool _paused;

        /// <summary>True while the player has the pause menu open.</summary>
        public bool IsPaused => _paused;

        /// <summary>Set by the HUD when RESUME or QUIT is chosen, read by the controller.</summary>
        public bool QuitRequested { get; private set; }

        void Update()
        {
            if (_judgementTimer > 0f) _judgementTimer -= Time.unscaledDeltaTime;

            // Android's back gesture pauses rather than dropping the run.
            if (Input.GetKeyDown(KeyCode.Escape) && Session != null && !Session.IsFinished) TogglePause();
        }

        public void ShowJudgement(Judgement judgement)
        {
            _lastJudgement = judgement;
            _judgementTimer = JudgementSeconds;
        }

        public void TogglePause()
        {
            if (Controller == null) return;

            _paused = !_paused;
            if (_paused) Controller.Pause();
            else Controller.Resume();
        }

        void OnGUI()
        {
            if (Session == null) return;

            if (_paused) DrawPauseMenu();
            else DrawPlayOverlay();
        }

        void DrawPlayOverlay()
        {
            float w = Screen.width;
            float pad = Scale(24);

            ScoreState score = Session.Score;

            UiSkin.PanelRect(new Rect(pad, pad, Scale(300), Scale(104)), UiSkin.Panel);
            GUI.Label(new Rect(pad + Scale(14), pad + Scale(8), Scale(280), Scale(48)),
                      score.Score.ToString("N0"), UiSkin.Heading);
            GUI.Label(new Rect(pad + Scale(14), pad + Scale(56), Scale(280), Scale(38)),
                      "x" + score.Multiplier + "   " + score.Accuracy.ToString("F1") + "%", UiSkin.Small);

            if (score.Combo >= 10)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.16f, w, Scale(70)),
                          score.Combo + " COMBO", UiSkin.Title);
            }

            DrawStarPowerMeter(score, pad);

            if (_judgementTimer > 0f)
            {
                float alpha = Mathf.Clamp01(_judgementTimer / JudgementSeconds);
                Color previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(0f, Screen.height * 0.30f, w, Scale(60)),
                          JudgementScores.Label(_lastJudgement), UiSkin.Title);
                GUI.color = previous;
            }

            if (score.CanActivateStarPower &&
                GUI.Button(new Rect(w - Scale(190), pad, Scale(166), Scale(72)), "STAR POWER", UiSkin.ButtonAccent))
            {
                Session.ActivateStarPower();
            }

            if (GUI.Button(new Rect(w - Scale(190), pad + Scale(84), Scale(166), Scale(56)), "PAUSE", UiSkin.Button))
            {
                TogglePause();
            }
        }

        void DrawStarPowerMeter(ScoreState score, float pad)
        {
            float width = Scale(260);
            float height = Scale(22);
            var bar = new Rect(pad, pad + Scale(116), width, height);

            UiSkin.PanelRect(bar, UiSkin.Panel);
            UiSkin.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(score.StarPowerFraction), bar.height),
                        score.StarPowerActive ? LaneTheme.StarPowerColor : UiSkin.AccentDim);

            string label = score.StarPowerActive
                ? "STAR POWER ACTIVE"
                : "STAR POWER  " + score.StarPowerBars + "/" + ScoreState.MaxStarPowerBars;
            GUI.Label(new Rect(bar.x, bar.y + bar.height + Scale(2), width, Scale(30)), label, UiSkin.Small);
        }

        void DrawPauseMenu()
        {
            float w = Screen.width;
            float h = Screen.height;

            UiSkin.PanelRect(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.72f));

            GUI.Label(new Rect(0f, h * 0.24f, w, Scale(80)), "PAUSED", UiSkin.Title);

            float buttonWidth = Mathf.Min(Scale(420), w * 0.7f);
            float x = (w - buttonWidth) * 0.5f;

            if (GUI.Button(new Rect(x, h * 0.40f, buttonWidth, Scale(76)), "RESUME", UiSkin.ButtonAccent))
            {
                TogglePause();
            }

            if (GUI.Button(new Rect(x, h * 0.40f + Scale(92), buttonWidth, Scale(76)), "QUIT SONG", UiSkin.Button))
            {
                // The controller tears the run down on its next update, so quitting goes through
                // one path instead of stopping the music from inside a GUI callback.
                QuitRequested = true;
            }
        }

        static float Scale(float value) => value * Mathf.Max(1f, Screen.height / 1080f);
    }
}
