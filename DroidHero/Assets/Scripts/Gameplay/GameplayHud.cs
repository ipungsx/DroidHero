using UnityEngine;
using DroidHero.Chart;
using DroidHero.UI;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// The in-game overlay: score, combo, star power, judgement popups and pause.
    ///
    /// Everything is drawn as world-space sprites and text rather than IMGUI, so it scales with
    /// the camera, animates per frame without a GUI rebuild, and adds a fixed handful of draw
    /// calls. All values come from the session, so the overlay can never disagree with the score.
    /// </summary>
    public class GameplayHud : MonoBehaviour
    {
        const float JudgementSeconds = 0.62f;
        const float ComboPopSeconds = 0.16f;
        const float OverdriveBannerSeconds = 1.9f;

        public GameSession Session;
        public GameplayController Controller;

        WorldText _score;
        WorldText _scoreLabel;
        WorldText _combo;
        WorldText _comboLabel;
        WorldText _judgement;
        WorldText _accuracy;
        WorldText _multiplier;
        WorldText _overdriveBanner;
        WorldText _overdriveHint;

        SpriteRenderer _comboGlow;
        SpriteRenderer _judgementGlow;
        SpriteRenderer _overdriveGlow;
        SpriteRenderer _starPowerBar;
        SpriteRenderer _starPowerFill;
        SpriteRenderer _starPowerFrame;
        SpriteRenderer _overdriveBar;
        SpriteRenderer _overdriveFill;

        Camera _camera;

        Judgement _lastJudgement;
        float _judgementTimer;
        float _comboPop;
        float _overdriveBannerTimer;
        float _overdriveBarShown;
        int _overdriveMilestone;
        float _displayedScore;
        float _starPowerShown;

        bool _paused;

        public bool IsPaused => _paused;

        /// <summary>Set by the HUD when QUIT is chosen, read by the controller.</summary>
        public bool QuitRequested { get; private set; }

        /// <summary>Extra line under the score, used by the frame budget to report FPS.</summary>
        public string DebugLine { get; set; } = "";

        public void Build()
        {
            _camera = Camera.main;

            Transform root = transform;

            _score = new WorldText("Score", root, 0.86f, 100, UiSkin.Font);
            _score.SetAnchor(TextAnchor.UpperLeft);
            _score.SetAlignment(TextAlignment.Left);

            _scoreLabel = new WorldText("ScoreLabel", root, 0.26f, 100, UiSkin.Font);
            _scoreLabel.SetAnchor(TextAnchor.UpperLeft);
            _scoreLabel.SetAlignment(TextAlignment.Left);
            _scoreLabel.SetText("SCORE");

            _accuracy = new WorldText("Accuracy", root, 0.30f, 100, UiSkin.Font);
            _accuracy.SetAnchor(TextAnchor.UpperLeft);
            _accuracy.SetAlignment(TextAlignment.Left);

            _multiplier = new WorldText("Multiplier", root, 0.42f, 100, UiSkin.Font);
            _multiplier.SetAnchor(TextAnchor.UpperLeft);
            _multiplier.SetAlignment(TextAlignment.Left);

            _combo = new WorldText("Combo", root, 1.05f, 102, UiSkin.Font);
            _comboLabel = new WorldText("ComboLabel", root, 0.30f, 102, UiSkin.Font);
            _comboLabel.SetText("COMBO");

            _judgement = new WorldText("Judgement", root, 0.62f, 103, UiSkin.Font);

            _overdriveBanner = new WorldText("OverdriveBanner", root, 0.60f, 104, UiSkin.Font);
            _overdriveBanner.SetText("OVERDRIVE");
            _overdriveBanner.SetColor(new Color(1f, 0.86f, 0.30f));
            _overdriveBanner.SetVisible(false);

            _overdriveHint = new WorldText("OverdriveHint", root, 0.26f, 104, UiSkin.Font);
            _overdriveHint.SetColor(new Color(1f, 0.86f, 0.30f));
            _overdriveHint.SetVisible(false);

            _comboGlow = Sprite("ComboGlow", ProceduralSprites.Glow, 101);
            _judgementGlow = Sprite("JudgementGlow", ProceduralSprites.Glow, 102);
            _overdriveGlow = Sprite("OverdriveGlow", ProceduralSprites.Glow, 103);
            _starPowerFrame = Sprite("StarPowerFrame", ProceduralSprites.RoundedNote, 99);
            _starPowerBar = Sprite("StarPowerBar", ProceduralSprites.RoundedNote, 99);
            _starPowerFill = Sprite("StarPowerFill", ProceduralSprites.RoundedNote, 100);
            _overdriveBar = Sprite("OverdriveBar", ProceduralSprites.RoundedNote, 99);
            _overdriveFill = Sprite("OverdriveFill", ProceduralSprites.RoundedNote, 100);

            _combo.SetVisible(false);
            _comboLabel.SetVisible(false);
            _comboGlow.enabled = false;
            _judgement.SetVisible(false);
            _judgementGlow.enabled = false;
            _overdriveGlow.enabled = false;

            Layout();
        }

        SpriteRenderer Sprite(string name, Sprite sprite, int order)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);

            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.color = new Color(1f, 1f, 1f, 0f);

            return renderer;
        }

        /// <summary>Places every element against the current view, so a resize needs no rebuild.</summary>
        void Layout()
        {
            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;

            float left = -halfWidth + 0.34f;
            float top = halfHeight - 0.30f;

            _score.SetPosition(new Vector3(left, top - 0.10f, 0f));
            _scoreLabel.SetPosition(new Vector3(left, top - 0.80f, 0f));
            _accuracy.SetPosition(new Vector3(left, top - 1.12f, 0f));
            _multiplier.SetPosition(new Vector3(left, top - 1.52f, 0f));

            _combo.SetPosition(new Vector3(0f, halfHeight * 0.56f, 0f));
            _comboLabel.SetPosition(new Vector3(0f, halfHeight * 0.56f - 0.86f, 0f));
            _comboGlow.transform.position = new Vector3(0f, halfHeight * 0.56f, 0f);

            _judgement.SetPosition(new Vector3(0f, halfHeight * 0.30f, 0f));
            _judgementGlow.transform.position = new Vector3(0f, halfHeight * 0.30f, 0f);

            float barWidth = Mathf.Min(halfWidth * 0.86f, 4.6f);
            float barY = -halfHeight + 0.46f;

            _starPowerFrame.transform.position = new Vector3(0f, barY, 0f);
            _starPowerFrame.transform.localScale = new Vector3(barWidth + 0.10f, 0.30f, 1f);

            _starPowerBar.transform.position = new Vector3(0f, barY, 0f);
            _starPowerBar.transform.localScale = new Vector3(barWidth, 0.22f, 1f);

            _starPowerFill.transform.position = new Vector3(0f, barY, 0f);
            _starPowerFill.transform.localScale = new Vector3(barWidth, 0.22f, 1f);

            // The Overdrive meter sits above the star power bar, so the two rewards read as a
            // stack rather than competing for the same strip.
            float odY = barY + 0.40f;
            _overdriveBar.transform.position = new Vector3(0f, odY, 0f);
            _overdriveBar.transform.localScale = new Vector3(barWidth, 0.14f, 1f);
            _overdriveFill.transform.position = new Vector3(0f, odY, 0f);
            _overdriveFill.transform.localScale = new Vector3(barWidth, 0.14f, 1f);

            _overdriveBanner.SetPosition(new Vector3(0f, halfHeight * 0.44f, 0f));
            _overdriveHint.SetPosition(new Vector3(0f, halfHeight * 0.44f - 0.40f, 0f));
            _overdriveGlow.transform.position = new Vector3(0f, halfHeight * 0.44f, 0f);
        }

        void Update()
        {
            if (_judgementTimer > 0f) _judgementTimer -= Time.unscaledDeltaTime;
            if (_comboPop > 0f) _comboPop -= Time.unscaledDeltaTime;
            if (_overdriveBannerTimer > 0f) _overdriveBannerTimer -= Time.unscaledDeltaTime;

            // Android's back gesture pauses rather than dropping the run.
            if (Input.GetKeyDown(KeyCode.Escape) && Session != null && !Session.IsFinished) TogglePause();

            if (Session == null) return;

            Layout();
            Animate();
        }

        public void ShowJudgement(Judgement judgement)
        {
            _lastJudgement = judgement;
            _judgementTimer = JudgementSeconds;

            if (judgement != Judgement.Miss) _comboPop = ComboPopSeconds;
        }

        void Animate()
        {
            ScoreState score = Session.Score;

            _displayedScore = Mathf.Lerp(_displayedScore, score.Score, Time.unscaledDeltaTime * 12f);
            if (Mathf.Abs(_displayedScore - score.Score) < 1f) _displayedScore = score.Score;

            _score.SetText(Mathf.RoundToInt(_displayedScore).ToString("N0"));
            _score.SetColor(UiSkin.TextPrimary);

            _accuracy.SetText(score.Accuracy.ToString("F1") + "%  " + new string('*', Mathf.Max(0, score.Stars)));
            _accuracy.SetColor(UiSkin.TextMuted);

            _multiplier.SetText("x" + score.Multiplier);
            _multiplier.SetColor(score.StarPowerActive ? LaneTheme.StarPowerColor : LaneTheme.AccentColor);

            UpdateCombo(score);
            UpdateJudgement();
            UpdateStarPower(score);
            UpdateOverdrive(score);
        }

        /// <summary>
        /// Shows the milestone chase and the reward: a banner when Overdrive starts, then a
        /// draining meter and the countdown of the 2x window.
        /// </summary>
        void UpdateOverdrive(ScoreState score)
        {
            bool active = score.OverdriveActive;

            _overdriveBarShown = Mathf.Lerp(_overdriveBarShown, score.OverdriveFraction,
                                            Time.unscaledDeltaTime * 8f);

            Color fill = active ? new Color(1f, 0.86f, 0.30f) : new Color(1f, 0.62f, 0.28f);

            _overdriveBar.color = new Color(0.16f, 0.14f, 0.12f, 0.55f);
            _overdriveFill.color = new Color(fill.r, fill.g, fill.b, active ? 0.98f : 0.62f);

            float fullWidth = _overdriveBar.transform.localScale.x;
            float fraction = Mathf.Clamp01(_overdriveBarShown);

            _overdriveFill.transform.localScale = new Vector3(
                Mathf.Max(0.02f, fullWidth * fraction), 0.14f, 1f);
            _overdriveFill.transform.position = new Vector3(
                _overdriveBar.transform.position.x - fullWidth * 0.5f + fullWidth * fraction * 0.5f,
                _overdriveBar.transform.position.y, 0f);

            bool banner = _overdriveBannerTimer > 0f;
            _overdriveBanner.SetVisible(banner);

            if (banner)
            {
                float t = Mathf.Clamp01(_overdriveBannerTimer / OverdriveBannerSeconds);
                float pop = 1f + (1f - t) * 0.18f;

                _overdriveBanner.SetText(_overdriveMilestone > 1
                    ? "OVERDRIVE  x" + _overdriveMilestone
                    : "OVERDRIVE");
                _overdriveBanner.SetColor(new Color(1f, 0.86f, 0.30f, Mathf.Clamp01(t * 2f)));
                _overdriveBanner.Transform.localScale = Vector3.one * pop;

                _overdriveGlow.enabled = true;
                _overdriveGlow.color = new Color(1f, 0.82f, 0.30f, 0.30f * t);
                _overdriveGlow.transform.localScale = Vector3.one * (2.6f + (1f - t) * 1.6f);
            }
            else
            {
                _overdriveGlow.enabled = false;
            }

            // Outside Overdrive the hint chases the next milestone, so the meter always means
            // something instead of sitting empty.
            bool showHint = !active && !banner && score.NotesHit > 0;
            _overdriveHint.SetVisible(showHint);

            if (showHint)
            {
                int next = score.NextOverdriveScore;
                int previous = (score.OverdriveCount) * ScoreState.OverdriveStep;
                float progress = Mathf.Clamp01((score.Score - previous) / (float)ScoreState.OverdriveStep);

                _overdriveHint.SetText("OVERDRIVE IN " + Mathf.Max(0, next - score.Score).ToString("N0") +
                                       "   (" + Mathf.RoundToInt(progress * 100f) + "%)");
                _overdriveHint.SetColor(new Color(1f, 0.86f, 0.30f, 0.72f));
            }

            if (active && _overdriveBarShown > 0.01f)
            {
                _overdriveHint.SetVisible(true);
                _overdriveHint.SetText("DOUBLE SCORE  " + Mathf.CeilToInt(score.OverdriveRemaining) + "s");
                _overdriveHint.SetColor(new Color(1f, 0.92f, 0.55f, 0.92f));
            }
        }

        /// <summary>Called when the session reports a milestone, so the banner can pop.</summary>
        public void ShowOverdrive(int milestone)
        {
            _overdriveMilestone = milestone;
            _overdriveBannerTimer = OverdriveBannerSeconds;
        }

        void UpdateCombo(ScoreState score)
        {
            bool show = score.Combo >= 4;
            _combo.SetVisible(show);
            _comboLabel.SetVisible(show);
            _comboGlow.enabled = show;

            if (!show) return;

            float pop = _comboPop > 0f ? _comboPop / ComboPopSeconds : 0f;
            float scale = 1f + pop * 0.22f;

            _combo.SetText(score.Combo.ToString());
            _combo.Transform.localScale = Vector3.one * scale;

            // Combo colour climbs with the streak, which gives the player a target to defend.
            Color color;
            if (score.Combo >= 100) color = new Color(1f, 0.86f, 0.30f);
            else if (score.Combo >= 50) color = new Color(1f, 0.62f, 0.28f);
            else if (score.Combo >= 20) color = new Color(0.72f, 0.55f, 1f);
            else color = UiSkin.TextPrimary;

            _combo.SetColor(color);
            _comboLabel.SetColor(color);

            _comboGlow.color = new Color(color.r, color.g, color.b, 0.10f + pop * 0.16f);
            _comboGlow.transform.localScale = Vector3.one * (2.4f + pop * 1.2f);
        }

        void UpdateJudgement()
        {
            if (_judgementTimer <= 0f)
            {
                _judgement.SetVisible(false);
                _judgementGlow.enabled = false;
                return;
            }

            float t = _judgementTimer / JudgementSeconds;
            float alpha = Mathf.Clamp01(t * 1.8f);
            float rise = (1f - t) * 0.22f;

            Color color = JudgementColor(_lastJudgement);
            color.a = alpha;

            _judgement.SetVisible(true);
            _judgement.SetText(JudgementScores.Label(_lastJudgement));
            _judgement.SetColor(color);
            _judgement.SetPosition(new Vector3(0f, JudgementAnchorY() + rise, 0f));

            _judgementGlow.enabled = true;
            _judgementGlow.color = new Color(color.r, color.g, color.b, alpha * 0.22f);
            _judgementGlow.transform.position = _judgement.Transform.position;
            _judgementGlow.transform.localScale = Vector3.one * (1.8f + (1f - t) * 1.1f);
        }

        float JudgementAnchorY()
        {
            if (_camera == null) return 2f;
            return _camera.orthographicSize * 0.30f;
        }

        /// <summary>Each judgement gets its own colour so the player can read it at a glance.</summary>
        public static Color JudgementColor(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return new Color(0.45f, 0.95f, 1f);
                case Judgement.Great: return new Color(0.55f, 1f, 0.60f);
                case Judgement.Good: return new Color(1f, 0.86f, 0.35f);
                case Judgement.Miss: return new Color(1f, 0.36f, 0.42f);
                default: return Color.white;
            }
        }

        void UpdateStarPower(ScoreState score)
        {
            _starPowerShown = Mathf.Lerp(_starPowerShown, score.StarPowerFraction, Time.unscaledDeltaTime * 8f);

            Color fill = score.StarPowerActive ? LaneTheme.StarPowerColor : LaneTheme.AccentColor;
            float alpha = score.StarPowerActive ? 0.95f : 0.75f;

            _starPowerFrame.color = new Color(0f, 0f, 0f, 0.45f);
            _starPowerBar.color = new Color(0.20f, 0.20f, 0.26f, 0.55f);
            _starPowerFill.color = new Color(fill.r, fill.g, fill.b, alpha);

            float fullWidth = _starPowerBar.transform.localScale.x;
            float fraction = Mathf.Clamp01(_starPowerShown);

            // The fill is anchored left, so it is scaled and shifted by half of what it lost.
            _starPowerFill.transform.localScale = new Vector3(
                Mathf.Max(0.02f, fullWidth * fraction), 0.22f, 1f);
            _starPowerFill.transform.position = new Vector3(
                _starPowerBar.transform.position.x - fullWidth * 0.5f + fullWidth * fraction * 0.5f,
                _starPowerBar.transform.position.y, 0f);
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

            if (!string.IsNullOrEmpty(DebugLine))
            {
                GUI.Label(new Rect(Scale(16), Screen.height - Scale(64), Scale(600), Scale(48)),
                          DebugLine, UiSkin.Small);
            }

            if (_paused) DrawPauseMenu();
        }

        void DrawPauseMenu()
        {
            float w = Screen.width;
            float h = Screen.height;

            UiSkin.PanelRect(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.78f));

            GUI.Label(new Rect(0f, h * 0.22f, w, Scale(80)), "PAUSED", UiSkin.Title);

            float buttonWidth = Mathf.Min(Scale(420), w * 0.7f);
            float x = (w - buttonWidth) * 0.5f;

            if (GUI.Button(new Rect(x, h * 0.38f, buttonWidth, Scale(76)), "RESUME", UiSkin.ButtonAccent))
            {
                TogglePause();
            }

            if (GUI.Button(new Rect(x, h * 0.38f + Scale(92), buttonWidth, Scale(76)), "RESTART", UiSkin.Button))
            {
                if (Controller != null)
                {
                    Resume();
                    Controller.RequestRestart();
                }
            }

            if (GUI.Button(new Rect(x, h * 0.38f + Scale(184), buttonWidth, Scale(76)), "QUIT SONG", UiSkin.Button))
            {
                // The controller tears the run down on its next update, so quitting goes through
                // one path instead of stopping the music from inside a GUI callback.
                QuitRequested = true;
            }
        }

        void Resume()
        {
            if (_paused)
            {
                _paused = false;
                if (Controller != null) Controller.Resume();
            }
        }

        static float Scale(float value) => value * Mathf.Max(1f, Screen.height / 1080f);
    }
}
