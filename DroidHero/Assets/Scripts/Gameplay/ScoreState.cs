using System;

namespace DroidHero.Gameplay
{
    /// <summary>
    /// Scoring, combo and star power rules, plus the score milestone reward.
    ///
    /// Combo multiplies the base note value (4x cap). Star power doubles the multiplier while
    /// active and drains over time; every 10 consecutive star-power notes earns one bar, up to
    /// three, which the player spends to activate.
    ///
    /// Crossing a score milestone fires Overdrive: a free, timed 2x multiplier that the stage
    /// answers with a light burst. It is the reason to keep pushing after a good streak, and it
    /// arrives on score rather than on a button, so a player who never learns the controls still
    /// gets the reward.
    /// </summary>
    public class ScoreState
    {
        public const int MaxMultiplier = 4;
        public const int NotesPerStarPowerBar = 10;
        public const int MaxStarPowerBars = 3;
        public const float StarPowerDrainSeconds = 8f;

        /// <summary>Score gap between Overdrive triggers. Every milestone fires once.</summary>
        public const int OverdriveStep = 15000;

        public const float OverdriveSeconds = 8f;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int NotesHit { get; private set; }
        public int NotesMissed { get; private set; }
        public int NotesTotal { get; private set; }

        public int StarPowerBars { get; private set; }
        public bool StarPowerActive { get; private set; }

        public bool OverdriveActive { get; private set; }

        /// <summary>How many milestones this run has passed, for the results screen.</summary>
        public int OverdriveCount { get; private set; }

        /// <summary>Seconds of Overdrive left, clamped to the full duration.</summary>
        public float OverdriveRemaining { get; private set; }

        public float OverdriveFraction =>
            OverdriveActive ? Clamp01(OverdriveRemaining / OverdriveSeconds) : 0f;

        /// <summary>Score at which the next milestone fires, so the HUD can show the chase.</summary>
        public int NextOverdriveScore =>
            (OverdriveCount + 1) * OverdriveStep;

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
                int multiplier = StarPowerActive ? baseMultiplier * 2 : baseMultiplier;
                return OverdriveActive ? multiplier * 2 : multiplier;
            }
        }

        /// <summary>Raised with the milestone number (1 for the first) when Overdrive fires.</summary>
        public event Action<int> OverdriveStarted;

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
            OverdriveActive = false;
            OverdriveCount = 0;
            OverdriveRemaining = 0f;
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

            CheckOverdriveMilestone();
        }

        /// <summary>
        /// Fires Overdrive once per milestone crossed. A single hit can cross more than one when
        /// a big sustain pays out, so this loops rather than checking a single threshold.
        /// </summary>
        void CheckOverdriveMilestone()
        {
            while (Score >= NextOverdriveScore)
            {
                OverdriveCount++;
                OverdriveActive = true;

                // Re-triggering refreshes the timer instead of stacking duration, so the
                // multiplier can never run away from the chart.
                OverdriveRemaining = OverdriveSeconds;

                if (OverdriveStarted != null) OverdriveStarted(OverdriveCount);
            }
        }

        /// <summary>Ends Overdrive early, used when a run is torn down.</summary>
        public void ClearOverdrive()
        {
            OverdriveActive = false;
            OverdriveRemaining = 0f;
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
            if (OverdriveActive)
            {
                OverdriveRemaining -= deltaTime;
                if (OverdriveRemaining <= 0f) ClearOverdrive();
            }

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
