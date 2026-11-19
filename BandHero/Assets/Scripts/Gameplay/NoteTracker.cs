using System;
using System.Collections.Generic;
using BandHero.Chart;

namespace BandHero.Gameplay
{
    public enum NoteState
    {
        Pending,
        Hit,
        Missed
    }

    /// <summary>
    /// Live state for one authored note while the song plays.
    /// </summary>
    public class ActiveNote
    {
        public ChartNote Note;
        public NoteState State;
        public bool SustainActive;
        public float SustainProgress;
        public bool SustainBroken;
        public Judgement Judgement;

        public bool IsSustain => Note.IsSustain;
        public float Time => Note.Time;
        public float EndTime => Note.EndTime;
        public int Lane => Note.Lane;
    }

    /// <summary>
    /// Owns per-note runtime state and resolves input against chart timing.
    ///
    /// Input is evaluated against <see cref="SongTime"/> only, never against frame delta, so
    /// hit accuracy stays stable regardless of framerate. Notes are consumed in chart order
    /// via a moving cursor, which keeps per-frame work proportional to visible notes.
    /// </summary>
    public class NoteTracker
    {
        readonly SongChart _chart;
        readonly List<ActiveNote> _notes;

        int _cursor;
        int _pendingCount;

        public float SongTime { get; set; }
        public IReadOnlyList<ActiveNote> Notes => _notes;
        public int Cursor => _cursor;
        public int PendingCount => _pendingCount;
        public int TotalNotes => _notes.Count;

        /// <summary>Raised when a note is judged, with the judgement and the note.</summary>
        public event Action<ActiveNote, Judgement> NoteJudged;

        public NoteTracker(SongChart chart)
        {
            _chart = chart ?? throw new ArgumentNullException(nameof(chart));

            _notes = new List<ActiveNote>(chart.Notes.Count);
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                _notes.Add(new ActiveNote { Note = chart.Notes[i], State = NoteState.Pending });
            }

            _pendingCount = _notes.Count;
        }

        /// <summary>Index of the first note that is still pending or still holding a sustain.</summary>
        int FirstUnresolved
        {
            get
            {
                int i = _cursor;
                while (i < _notes.Count)
                {
                    ActiveNote n = _notes[i];
                    if (n.State == NoteState.Pending) return i;
                    if (n.State == NoteState.Hit && n.SustainActive && !n.SustainBroken) return i;
                    i++;
                }
                return _notes.Count;
            }
        }

        /// <summary>
        /// Advances the song clock, auto-misses notes that fell past the window, and keeps
        /// sustain progress in sync.
        /// </summary>
        public void Update(float songTime)
        {
            SongTime = songTime;

            _cursor = FirstUnresolved;

            for (int i = _cursor; i < _notes.Count; i++)
            {
                ActiveNote note = _notes[i];

                if (note.Time > songTime + HitWindow.Miss) break;

                if (note.State == NoteState.Pending)
                {
                    if (songTime - note.Time > HitWindow.Miss)
                    {
                        Miss(note);
                    }
                    continue;
                }

                if (note.State == NoteState.Hit && note.SustainActive && !note.SustainBroken)
                {
                    if (note.IsSustain && songTime >= note.EndTime)
                    {
                        note.SustainActive = false;
                        note.SustainProgress = 1f;
                    }
                    else if (note.IsSustain)
                    {
                        float span = note.EndTime - note.Time;
                        note.SustainProgress = span <= 0f ? 1f : (songTime - note.Time) / span;
                    }
                }
            }
        }

        /// <summary>
        /// Resolves a fret press. Returns the judged note, or null when the press matched nothing
        /// inside the hit window (a stray strum, which costs nothing in this ruleset).
        /// </summary>
        public ActiveNote Press(int lane, bool strummed)
        {
            ActiveNote best = null;
            float bestDelta = float.MaxValue;

            int start = FirstUnresolved;
            for (int i = start; i < _notes.Count; i++)
            {
                ActiveNote note = _notes[i];

                if (note.Time > SongTime + HitWindow.Miss) break;
                if (note.State != NoteState.Pending) continue;
                if (note.Lane != lane) continue;

                float delta = SongTime - note.Time;
                if (!HitWindow.IsInWindow(delta)) continue;

                // HOPO notes resolve on the fret press alone; everything else needs a strum.
                bool requiresStrum = !note.Note.Hopo && !note.Note.Tap;
                if (requiresStrum && !strummed) continue;

                float abs = Math.Abs(delta);
                if (abs < bestDelta)
                {
                    bestDelta = abs;
                    best = note;
                }
            }

            if (best == null) return null;

            Judgement judgement = HitWindow.Classify(SongTime - best.Time);
            best.State = NoteState.Hit;
            best.Judgement = judgement;
            best.SustainActive = best.IsSustain;
            best.SustainProgress = 0f;
            _pendingCount--;

            NoteJudged?.Invoke(best, judgement);
            return best;
        }

        /// <summary>
        /// Resolves a strum bar press for every lane currently held, which is how chords are hit.
        /// </summary>
        public int Strum(IList<bool> heldLanes)
        {
            int hits = 0;
            for (int lane = 0; lane < heldLanes.Count; lane++)
            {
                if (!heldLanes[lane]) continue;
                if (Press(lane, true) != null) hits++;
            }
            return hits;
        }

        /// <summary>Called when the player releases a fret during a sustain, breaking it.</summary>
        public void ReleaseLane(int lane)
        {
            for (int i = _cursor; i < _notes.Count; i++)
            {
                ActiveNote note = _notes[i];
                if (note.Time > SongTime + HitWindow.Miss) break;
                if (note.Lane != lane) continue;
                if (note.State == NoteState.Hit && note.SustainActive && !note.SustainBroken && note.IsSustain)
                {
                    if (SongTime < note.EndTime)
                    {
                        note.SustainBroken = true;
                        note.SustainActive = false;
                    }
                }
            }
        }

        void Miss(ActiveNote note)
        {
            note.State = NoteState.Missed;
            note.Judgement = Judgement.Miss;
            note.SustainActive = false;
            _pendingCount--;
            NoteJudged?.Invoke(note, Judgement.Miss);
        }

        /// <summary>Notes whose time falls inside the visible window, for rendering.</summary>
        public void CollectVisible(float fromTime, float toTime, List<ActiveNote> buffer)
        {
            buffer.Clear();

            int start = FirstUnresolved;
            for (int i = start; i < _notes.Count; i++)
            {
                ActiveNote note = _notes[i];
                if (note.Time > toTime) break;
                if (note.EndTime < fromTime) continue;
                if (note.State == NoteState.Missed) continue;
                if (note.State == NoteState.Hit && !note.SustainActive) continue;
                buffer.Add(note);
            }
        }

        public void Reset()
        {
            for (int i = 0; i < _notes.Count; i++)
            {
                ActiveNote note = _notes[i];
                note.State = NoteState.Pending;
                note.SustainActive = false;
                note.SustainBroken = false;
                note.SustainProgress = 0f;
                note.Judgement = Judgement.None;
            }
            _cursor = 0;
            _pendingCount = _notes.Count;
            SongTime = 0f;
        }
    }
}
