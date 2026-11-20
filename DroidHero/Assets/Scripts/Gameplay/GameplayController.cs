using System;
using UnityEngine;
using DroidHero.Core;
using DroidHero.UI;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// Runs one song: drives the audio clock, feeds the session, and renders the stage.
    ///
    /// Song time is taken from the AudioSource's playback position rather than accumulated frame
    /// deltas. Mobile audio clocks drift from wall time, and chart sync must follow the audio the
    /// player actually hears.
    /// </summary>
    public class GameplayController : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public HighwayRenderer Renderer { get; private set; }
        public ConcertStage Stage { get; private set; }
        public AudioSource Music { get; private set; }

        HighwayLayout _layout;
        TouchInputController _input;
        GameplayHud _hud;
        FrameBudget _budget;
        LoadedSong _song;
        float _leadInRemaining;
        bool _musicStarted;
        bool _finished;

        /// <summary>Raised once when the song ends, with the final session state.</summary>
        public event Action<GameSession> Finished;

        /// <summary>Raised when the player asks to play the same song again from the pause menu.</summary>
        public event Action RestartRequested;

        public float SongTime
        {
            get
            {
                // During the lead-in the clock runs negative, so notes scroll toward the
                // strike line instead of all landing at once when playback begins.
                if (_leadInRemaining > 0f) return -_leadInRemaining;
                if (!_musicStarted) return 0f;
                return Music != null && Music.isPlaying ? Music.time : Session.SongTime;
            }
        }

        public void Begin(LoadedSong song, GameplayTheme theme = null, bool lightShow = true,
                          float leadInSeconds = 2.2f)
        {
            _song = song ?? throw new ArgumentNullException(nameof(song));
            _leadInRemaining = leadInSeconds;
            _musicStarted = false;
            _finished = false;

            LaneTheme.Current = theme ?? GameplayTheme.At(0);

            Session = new GameSession(song.Chart);

            _layout = new HighwayLayout();

            Stage = gameObject.AddComponent<ConcertStage>();
            Stage.LightShowEnabled = lightShow;
            Stage.Build(song.Chart);

            Renderer = gameObject.AddComponent<HighwayRenderer>();
            Renderer.Layout = _layout;
            Renderer.Initialize(Session.Tracker);

            Music = gameObject.AddComponent<AudioSource>();
            Music.clip = song.Audio;
            Music.playOnAwake = false;
            Music.loop = false;
            Music.volume = 1f;
            Music.spatialBlend = 0f;

            _input = gameObject.AddComponent<TouchInputController>();
            _input.Layout = _layout;
            _input.Session = Session;

            _hud = gameObject.AddComponent<GameplayHud>();
            _hud.Session = Session;
            _hud.Controller = this;
            _hud.Build();
            Session.JudgementShown += _hud.ShowJudgement;
            Session.NoteJudged += OnNoteJudged;
            Session.OverdriveStarted += OnOverdriveStarted;

            _budget = gameObject.AddComponent<FrameBudget>();
            _budget.Attach(Stage);
        }

        void OnNoteJudged(ActiveNote note, Judgement judgement)
        {
            if (Renderer == null) return;

            if (judgement != Judgement.Miss)
            {
                Renderer.FlashLane(note.Lane);
            }
        }

        /// <summary>
        /// A milestone answers on the stage as well as in the HUD: the rig flares, the highway
        /// goes gold, and the player feels the reward before reading the banner.
        /// </summary>
        void OnOverdriveStarted(int milestone)
        {
            if (_hud != null) _hud.ShowOverdrive(milestone);
            if (Stage != null) Stage.OverdriveBurst();
        }

        void Update()
        {
            if (Session == null || _finished) return;

            if (_leadInRemaining > 0f)
            {
                // Scaled time so a pause during the lead-in freezes the countdown rather than
                // starting the music while the game is suspended.
                _leadInRemaining -= Time.deltaTime;

                if (_leadInRemaining <= 0f)
                {
                    _leadInRemaining = 0f;
                    _musicStarted = true;
                    Music.Play();
                }
            }

            if (_hud != null && _hud.IsPaused) return;

            if (_hud != null && _hud.QuitRequested)
            {
                // A quit ends the run without results, so the session is reported as null.
                Resume();
                Stop();
                Finished?.Invoke(null);
                return;
            }

            float time = SongTime;
            Session.Update(time, Time.deltaTime);

            if (Stage != null)
            {
                Stage.Tick(time, Time.deltaTime);

                // The strike line breathes with the drums, so the beat is visible as well as heard.
                if (Stage.ConsumeBeat()) Renderer.PulseStrikeLine();
            }

            Renderer.SetStarPowerActive(Session.Score.StarPowerActive);
            Renderer.SetOverdriveActive(Session.Score.OverdriveActive);
            Stage.SetOverdriveActive(Session.Score.OverdriveActive);
            Renderer.Render(time, _input != null ? _input.HeldLanes : null);

            if (_hud != null && _budget != null)
            {
                _hud.DebugLine = DebugOverlay ? "FPS " + _budget.SmoothedFps + "  detail " + _budget.QualityLevel : "";
            }

            bool audioDone = _musicStarted && Music != null && !Music.isPlaying;
            if (Session.IsFinished || audioDone)
            {
                _finished = true;
                Session.Finish();
                Finished?.Invoke(Session);
            }
        }

        /// <summary>Shows the frame counter; off by default so players never see debug text.</summary>
        public bool DebugOverlay { get; set; }

        /// <summary>
        /// Asks the owner to rebuild this run. The controller cannot restart itself: the host
        /// object owns the audio source and the renderer, so tearing it down has to happen from
        /// outside the update that requested it.
        /// </summary>
        public void RequestRestart() => RestartRequested?.Invoke();

        public void Stop()
        {
            if (Music != null && Music.isPlaying) Music.Stop();
            _finished = true;
        }

        public void Pause()
        {
            if (Music != null) Music.Pause();
            Time.timeScale = 0f;
        }

        public void Resume()
        {
            Time.timeScale = 1f;
            if (Music != null) Music.UnPause();
        }

        void OnDestroy()
        {
            Time.timeScale = 1f;
            if (_hud != null && Session != null)
            {
                Session.JudgementShown -= _hud.ShowJudgement;
                Session.NoteJudged -= OnNoteJudged;
            }
        }
    }
}
