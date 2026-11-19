using System;

namespace BandHero.Gameplay
{
    /// <summary>
    /// Scoring, combo and star power rules.
    ///
    /// Combo multiplies the base note value (4x cap). Star power doubles the multiplier while
    /// active and drains over time; every 10 consecutive star-power notes earns one bar, up to
    /// three, which the player spends to activate.
    /// </summary>
    public class ScoreState
    {
        public const int MaxMultiplier = 4;
        public const int NotesPerStarPowerBar = 10;
        public const int MaxStarPowerBars = 3;
        public const float StarPowerDrainSeconds = 8f;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int NotesHit { get; private set; }
        public int NotesMissed { get; private set; }
        public int NotesTotal { get; private set; }

        public int StarPowerBars { get; private set; }
        public bool StarPowerActive { get; private set; }

        float _starPowerRemaining;
        int _starPowerNotes;

        public float StarPowerFraction =>
            StarPowerActive
                ? Clamp01(_starPowerRemaining / StarPowerDrainSeconds)
                : StarPowerBars / (float)MaxStarPowerBars;

        public bool CanActivateStarPower => !StarPowerActive && StarPowerBars > 0;

        public int Multiplier
        {
            get
            {
                int baseMultiplier = Math.Max(1, Math.Min(1 + Combo / 10, MaxMultiplier));
                return StarPowerActive ? baseMultiplier * 2 : baseMultiplier;
            }
        }

        public void Reset(int totalNotes)
        {
            Score = 0;
            Combo = 0;
            MaxCombo = 0;
            NotesHit = 0;
            NotesMissed = 0;
            NotesTotal = totalNotes;
            StarPowerBars = 0;
            StarPowerActive = false;
            _starPowerRemaining = 0f;
            _starPowerNotes = 0;
        }

        public void RegisterHit(Judgement judgement, bool isStarPowerNote, float sustainFraction = 0f)
        {
            if (judgement == Judgement.Miss)
            {
                RegisterMiss();
                return;
            }

            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            NotesHit++;

            int baseValue = JudgementScores.Score(judgement);
            Score += (int)Math.Round(baseValue * Multiplier * (1.0 + Clamp01(sustainFraction)));

            if (isStarPowerNote && !StarPowerActive)
            {
                _starPowerNotes++;
                if (_starPowerNotes >= NotesPerStarPowerBar && StarPowerBars < MaxStarPowerBars)
                {
                    _starPowerNotes = 0;
                    StarPowerBars++;
                }
            }
        }

        public void RegisterMiss()
        {
            Combo = 0;
            NotesMissed++;
            if (StarPowerActive) DeactivateStarPower();
        }

        public bool ActivateStarPower()
        {
            if (!CanActivateStarPower) return false;

            StarPowerActive = true;
            StarPowerBars--;
            _starPowerRemaining = StarPowerDrainSeconds;
            return true;
        }

        public void DeactivateStarPower()
        {
            StarPowerActive = false;
            _starPowerRemaining = 0f;
        }

        public void Tick(float deltaTime)
        {
            if (!StarPowerActive) return;

            _starPowerRemaining -= deltaTime;
            if (_starPowerRemaining <= 0f) DeactivateStarPower();
        }

        /// <summary>Percentage of notes hit, 0-100.</summary>
        public float Accuracy
        {
            get
            {
                int judged = NotesHit + NotesMissed;
                return judged == 0 ? 100f : NotesHit * 100f / judged;
            }
        }

        static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);

        public int Stars
        {
            get
            {
                float accuracy = Accuracy;
                if (accuracy >= 95f) return 5;
                if (accuracy >= 90f) return 4;
                if (accuracy >= 80f) return 3;
                if (accuracy >= 65f) return 2;
                return 1;
            }
        }
    }
}
