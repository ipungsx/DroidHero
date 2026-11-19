using System;
using UnityEngine;
using BandHero.Core;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Runs one song: drives the audio clock, feeds the session, and renders the highway.
    ///
    /// Song time is taken from the AudioSource's playback position rather than accumulated frame
    /// deltas. Mobile audio clocks drift from wall time, and chart sync must follow the audio the
    /// player actually hears.
    /// </summary>
    public class GameplayController : MonoBehaviour
    {
        public GameSession Session { get; private set; }
        public HighwayRenderer Renderer { get; private set; }
        public AudioSource Music { get; private set; }

        HighwayLayout _layout;
        TouchInputController _input;
        GameplayHud _hud;
        LoadedSong _song;
        float _leadInRemaining;
        bool _musicStarted;
        bool _finished;

        /// <summary>Raised once when the song ends, with the final session state.</summary>
        public event Action<GameSession> Finished;

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

        public void Begin(LoadedSong song, float leadInSeconds = 2.5f)
        {
            _song = song ?? throw new ArgumentNullException(nameof(song));
            _leadInRemaining = leadInSeconds;
            _musicStarted = false;
            _finished = false;

            Session = new GameSession(song.Chart);

            _layout = new HighwayLayout();

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
            Session.JudgementShown += _hud.ShowJudgement;
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

            Renderer.SetStarPowerActive(Session.Score.StarPowerActive);
            Renderer.Render(time, _input != null ? _input.HeldLanes : null);

            bool audioDone = _musicStarted && Music != null && !Music.isPlaying;
            if (Session.IsFinished || audioDone)
            {
                _finished = true;
                Session.Finish();
                Finished?.Invoke(Session);
            }
        }

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
            if (_hud != null && Session != null) Session.JudgementShown -= _hud.ShowJudgement;
        }
    }
}
