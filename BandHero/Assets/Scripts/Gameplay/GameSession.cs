using System;
using BandHero.Chart;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Drives one playthrough: owns the chart clock, the note tracker and the score.
    ///
    /// The clock is supplied by the audio source, so chart time always follows what the
    /// player hears rather than accumulating frame deltas (which drift on mobile).
    /// </summary>
    public class GameSession
    {
        readonly SongChart _chart;
        readonly ScoreState _score = new ScoreState();

        public GameSession(SongChart chart)
        {
            _chart = chart ?? throw new ArgumentNullException(nameof(chart));
            Tracker = new NoteTracker(chart);
            Tracker.NoteJudged += OnNoteJudged;
            _score.Reset(chart.Notes.Count);
        }

        public SongChart Chart => _chart;
        public NoteTracker Tracker { get; }
        public ScoreState Score => _score;

        public float SongTime { get; private set; }
        public bool IsFinished { get; private set; }

        public event Action<ActiveNote, Judgement> NoteJudged;
        public event Action<Judgement> JudgementShown;

        void OnNoteJudged(ActiveNote note, Judgement judgement)
        {
            if (judgement == Judgement.Miss)
            {
                _score.RegisterMiss();
            }
            else
            {
                _score.RegisterHit(judgement, note.Note.StarPower);
            }

            NoteJudged?.Invoke(note, judgement);
            JudgementShown?.Invoke(judgement);
        }

        public void Update(float songTime, float deltaTime)
        {
            if (IsFinished) return;

            SongTime = songTime;
            Tracker.Update(songTime);
            _score.Tick(deltaTime);

            float chartLength = _chart.SongLength;
            if (chartLength > 0f && songTime > chartLength + 2f) IsFinished = true;
        }

        public bool PressFret(int lane, bool strummed) => Tracker.Press(lane, strummed) != null;

        public int Strum(System.Collections.Generic.IList<bool> heldLanes) => Tracker.Strum(heldLanes);

        public void ReleaseFret(int lane) => Tracker.ReleaseLane(lane);

        public bool ActivateStarPower() => _score.ActivateStarPower();

        public void Finish() => IsFinished = true;
    }
}
