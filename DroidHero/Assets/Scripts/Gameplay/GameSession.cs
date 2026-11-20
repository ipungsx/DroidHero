using System;
using DroidHero.Chart;

namespace DroidHero.Gameplay
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
            _score.OverdriveStarted += milestone => OverdriveStarted?.Invoke(milestone);
        }

        public SongChart Chart => _chart;
        public NoteTracker Tracker { get; }
        public ScoreState Score => _score;

        public float SongTime { get; private set; }
        public bool IsFinished { get; private set; }

        public event Action<ActiveNote, Judgement> NoteJudged;
        public event Action<Judgement> JudgementShown;

        /// <summary>Raised when a score milestone starts Overdrive, with the milestone number.</summary>
        public event Action<int> OverdriveStarted;

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

        /// <summary>
        /// Lands any chord whose lanes are all held, so a chord never needs a third finger on
        /// the strum strip. Returns how many notes it resolved.
        /// </summary>
        public int AutoStrumChords(System.Collections.Generic.IList<bool> heldLanes) =>
            Tracker.AutoStrumChords(heldLanes);

        public void ReleaseFret(int lane) => Tracker.ReleaseLane(lane);

        public bool ActivateStarPower() => _score.ActivateStarPower();

        public void Finish() => IsFinished = true;
    }
}
