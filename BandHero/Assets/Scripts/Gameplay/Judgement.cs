using System;

namespace BandHero.Gameplay
{
    public enum Judgement
    {
        None,
        Perfect,
        Great,
        Good,
        Miss
    }

    /// <summary>
    /// Hit windows in seconds. Values follow the Guitar Hero / Clone Hero feel: a tight
    /// "perfect" band, a forgiving "good" band, and anything later counting as a miss.
    /// </summary>
    public static class HitWindow
    {
        public const float Perfect = 0.045f;
        public const float Great = 0.075f;
        public const float Good = 0.115f;
        public const float Miss = 0.140f;

        public static Judgement Classify(float deltaSeconds)
        {
            float d = Math.Abs(deltaSeconds);
            if (d <= Perfect) return Judgement.Perfect;
            if (d <= Great) return Judgement.Great;
            if (d <= Good) return Judgement.Good;
            return Judgement.Miss;
        }

        /// <summary>How early a note may be hit before it counts as a miss.</summary>
        public static bool IsInWindow(float deltaSeconds) => deltaSeconds <= Miss && deltaSeconds >= -Miss;
    }

    public static class JudgementScores
    {
        public const int Perfect = 100;
        public const int Great = 80;
        public const int Good = 50;
        public const int Miss = 0;

        public static int Score(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return Perfect;
                case Judgement.Great: return Great;
                case Judgement.Good: return Good;
                default: return Miss;
            }
        }

        public static string Label(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: return "PERFECT";
                case Judgement.Great: return "GREAT";
                case Judgement.Good: return "GOOD";
                case Judgement.Miss: return "MISS";
                default: return "";
            }
        }
    }
}
