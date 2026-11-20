using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DroidHero.Chart;
using DroidHero.Gameplay;

namespace DroidHero.EditorTools
{
    /// <summary>
    /// Headless verification of chart parsing and the hit-detection rules.
    /// Run with: Unity -batchmode -executeMethod DroidHero.EditorTools.ChartSelfTest.RunAll
    /// or via the bundled compiler harness (see tools/run-tests.sh).
    /// </summary>
    public static class ChartSelfTest
    {
        static int _failures;

        public static int RunAll(string sngPath)
        {
            _failures = 0;

            SongChart chart;
            using (var sng = SngReader.Open(sngPath))
            {
                string chartText = sng.ReadText("notes.chart");
                if (chartText != null)
                {
                    chart = ChartFileParser.Parse(chartText, "Expert");
                }
                else
                {
                    byte[] midi = sng.ReadFirstWithExtension(".mid");
                    chart = MidiParser.Parse(midi, "Expert");
                }
            }

            Check(chart.Notes.Count > 0, "chart has notes");
            Check(chart.SongLength > 10f, "chart has a sane length");
            CheckNotesSorted(chart);

            // A perfect playthrough must hit every note.
            var perfect = new GameSession(chart);
            Simulate(perfect, chart, offset: 0f);
            Check(perfect.Score.NotesHit == chart.Notes.Count,
                  "perfect run hits all notes (hit " + perfect.Score.NotesHit + "/" + chart.Notes.Count + ")");
            Check(perfect.Score.NotesMissed == 0,
                  "perfect run misses nothing (missed " + perfect.Score.NotesMissed + ")");
            Check(perfect.Score.MaxCombo == chart.Notes.Count, "perfect run keeps full combo");
            Check(perfect.Score.Score > 0, "perfect run scores points");
            Check(perfect.Score.Stars == 5, "perfect run awards 5 stars (got " + perfect.Score.Stars + ")");

            // Never pressing anything must miss every note.
            var idle = new GameSession(chart);
            Simulate(idle, chart, offset: 0f, pressNothing: true);
            Check(idle.Score.NotesMissed == chart.Notes.Count,
                  "idle run misses all notes (missed " + idle.Score.NotesMissed + "/" + chart.Notes.Count + ")");
            Check(idle.Score.Score == 0, "idle run scores nothing");
            Check(idle.Score.Stars == 1, "idle run awards 1 star");

            // Notes pressed far outside the window must not register.
            var early = new GameSession(chart);
            Simulate(early, chart, offset: 0.5f, pressNothing: true);
            Check(early.Score.NotesHit == 0, "out-of-window presses hit nothing");

            Console.WriteLine(_failures == 0
                ? "SELFTEST: PASS (" + chart.Notes.Count + " notes, " + chart.SongLength.ToString("F1") + "s)"
                : "SELFTEST: FAIL (" + _failures + " checks)");
            return _failures == 0 ? 0 : 1;
        }

        static void CheckNotesSorted(SongChart chart)
        {
            bool sorted = true;
            bool lanesValid = true;
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                if (i > 0 && chart.Notes[i].Time < chart.Notes[i - 1].Time) sorted = false;
                if (chart.Notes[i].Lane < 0 || chart.Notes[i].Lane >= LaneInfo.Count) lanesValid = false;
            }
            Check(sorted, "notes are time-sorted");
            Check(lanesValid, "all note lanes are 0-4");
        }

        /// <summary>
        /// Steps the clock at 60 Hz and presses every note exactly on time. Sustained notes are
        /// held until their tail while the loop keeps processing the notes layered on top of them.
        /// </summary>
        static void Simulate(GameSession session, SongChart chart, float offset, bool pressNothing = false)
        {
            const float dt = 1f / 60f;
            float end = chart.SongLength + 1f;

            // Lane -> time at which the held sustain may be released.
            var releaseAt = new float[LaneInfo.Count];
            for (int i = 0; i < releaseAt.Length; i++) releaseAt[i] = -1f;

            int nextNote = 0;
            bool skipPresses = pressNothing || offset > 0.2f;

            for (float t = 0f; t <= end; t += dt)
            {
                for (int lane = 0; lane < releaseAt.Length; lane++)
                {
                    if (releaseAt[lane] >= 0f && t >= releaseAt[lane])
                    {
                        session.ReleaseFret(lane);
                        releaseAt[lane] = -1f;
                    }
                }

                session.Update(t, dt);
                if (skipPresses) continue;

                while (nextNote < chart.Notes.Count && chart.Notes[nextNote].Time <= t)
                {
                    ChartNote note = chart.Notes[nextNote];
                    session.PressFret(note.Lane, strummed: true);

                    if (note.IsSustain)
                    {
                        releaseAt[note.Lane] = note.EndTime;
                    }
                    else
                    {
                        session.ReleaseFret(note.Lane);
                    }

                    nextNote++;
                }
            }
        }

        static void Check(bool condition, string label)
        {
            if (!condition) _failures++;
            Console.WriteLine((condition ? "  ok   " : "  FAIL ") + label);
        }
    }
}
