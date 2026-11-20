using System;
using System.Collections.Generic;
using System.IO;
using DroidHero.Audio;
using DroidHero.Chart;
using DroidHero.Gameplay;
using DroidHero.Core;
using DroidHero.Enchor;

namespace DroidHero.Tests
{
    /// <summary>
    /// Headless verification of the engine, runnable without a Unity licence.
    ///
    /// Every test drives real chart data through the real code paths; there are no mocks, so a
    /// pass here means the shipped logic works on the same files the game will load.
    /// </summary>
    public static class HeadlessTests
    {
        static int _failures;
        static int _checks;

        public static int Main(string[] args)
        {
            Console.WriteLine("=== Droid Hero headless tests ===");

            if (args.Length == 0)
            {
                Console.WriteLine("usage: tests <chart.sng> [chart.sng...]");
                return 2;
            }

            foreach (string path in args)
            {
                Console.WriteLine();
                Console.WriteLine("--- " + Path.GetFileName(path) + " ---");

                if (!File.Exists(path))
                {
                    Fail("file exists: " + path);
                    continue;
                }

                TestSngContainer(path);
                TestChartParsing(path);
                TestGameplay(path);
                TestAudioDecoding(path);
                TestDrumHits(path);
                TestRealChartDifficulties(path);
            }

            TestPerspectiveHighway();
            TestDifficultyNaming();
            TestDifficultyScanning();
            TestSettingsRoundTrip();
            TestLeaderboard();
            TestLeaderboardClear();
            TestSongDifficultyStore();
            TestEnchorSearchRequest();
            TestOverdriveMilestone();

            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "ALL TESTS PASSED (" + _checks + " checks)"
                : _failures + " FAILURE(S) of " + _checks + " checks");

            return _failures == 0 ? 0 : 1;
        }

        static void TestSngContainer(string path)
        {
            using (var sng = SngReader.Open(path))
            {
                Check(sng.Metadata.Count > 0, "sng has metadata (" + sng.Metadata.Count + " keys)");
                var fileNames = new List<string>(sng.FileNames);
                Check(fileNames.Count > 0, "sng has files (" + string.Join(", ", fileNames.ToArray()) + ")");

                bool hasChart = sng.ReadText("notes.chart") != null ||
                                sng.ReadFirstWithExtension(".mid") != null;
                Check(hasChart, "sng contains a chart");

                byte[] audio = sng.ReadFirstWithExtension(".ogg") ?? sng.ReadFirstWithExtension(".opus");
                Check(audio != null && audio.Length > 1024, "sng contains audio");

                if (audio != null)
                {
                    AudioFormat format = AudioFormatDetector.Detect(audio);
                    Check(AudioFormatDetector.IsSupported(format),
                          "audio format is playable (" + AudioFormatDetector.Describe(format) + ")");
                }
            }
        }

        static void TestChartParsing(string path)
        {
            SongChart chart = LoadChart(path, "Expert");

            Check(chart.Notes.Count > 0, "expert chart has notes (" + chart.Notes.Count + ")");
            Check(chart.SongLength > 10f, "chart length is sane (" + chart.SongLength.ToString("F1") + "s)");
            Check(chart.Tempos.Count > 0, "chart has a tempo map (" + chart.Tempos.Count + ")");

            CheckSortedByTime(chart);

            foreach (ChartNote note in chart.Notes)
            {
                if (note.Lane < 0 || note.Lane >= LaneInfo.Count)
                {
                    Fail("lane in range (found " + note.Lane + ")");
                    break;
                }
            }

            int sustains = 0, hopos = 0, starPower = 0;
            foreach (ChartNote note in chart.Notes)
            {
                if (note.IsSustain) sustains++;
                if (note.Hopo) hopos++;
                if (note.StarPower) starPower++;
            }

            Console.WriteLine("    notes=" + chart.Notes.Count + " sustains=" + sustains +
                              " hopos=" + hopos + " sp=" + starPower +
                              " phrases=" + chart.StarPower.Count);

            // Each difficulty is a separate block, so all four must parse without exploding.
            foreach (string difficulty in new[] { "Easy", "Medium", "Hard", "Expert" })
            {
                SongChart diffChart = LoadChart(path, difficulty);
                Check(diffChart.Notes.Count >= 0, difficulty + " parses (" + diffChart.Notes.Count + " notes)");
            }
        }

        static void TestGameplay(string path)
        {
            SongChart chart = LoadChart(path, "Expert");

            var perfect = new GameSession(chart);
            Simulate(perfect, chart, pressNotes: true);
            Check(perfect.Score.NotesHit == chart.Notes.Count,
                  "perfect run hits every note (" + perfect.Score.NotesHit + "/" + chart.Notes.Count + ")");
            Check(perfect.Score.NotesMissed == 0,
                  "perfect run misses nothing (" + perfect.Score.NotesMissed + ")");
            Check(perfect.Score.MaxCombo == chart.Notes.Count, "perfect run keeps full combo");
            Check(perfect.Score.Score > 0, "perfect run scores (" + perfect.Score.Score + ")");
            Check(perfect.Score.Stars == 5, "perfect run earns 5 stars (" + perfect.Score.Stars + ")");

            var idle = new GameSession(chart);
            Simulate(idle, chart, pressNotes: false);
            Check(idle.Score.NotesMissed == chart.Notes.Count,
                  "idle run misses every note (" + idle.Score.NotesMissed + "/" + chart.Notes.Count + ")");
            Check(idle.Score.Score == 0, "idle run scores nothing");
            Check(idle.Score.Stars == 1, "idle run earns 1 star (" + idle.Score.Stars + ")");

            TestLeadIn(chart);
            TestFormatDetection();
            TestHighwayGeometry();
            TestAudioLinkStore();
            TestAudioCalibration();

            // Star power: hitting notes with SP phrases must fill a bar the player can activate.
            var starPower = new GameSession(chart);
            Simulate(starPower, chart, pressNotes: true, activateStarPower: true);
            Check(starPower.Score.NotesHit == chart.Notes.Count, "star power run still hits every note");

            // A clean run of a real chart has to cross the first milestone and announce it.
            var milestone = new GameSession(chart);
            int announced = 0;
            milestone.OverdriveStarted += _ => announced++;
            Simulate(milestone, chart, pressNotes: true);

            Check(announced > 0, "a clean run announces overdrive (" + announced + ")");
            Check(milestone.Score.OverdriveCount == announced,
                  "every announcement is counted on the score");
            Check(milestone.Score.OverdriveCount > 0 ||
                  milestone.Score.Score < ScoreState.OverdriveStep,
                  "overdrive is reported whenever the score passes a milestone (" +
                  milestone.Score.Score + ")");
        }

        static void TestAudioDecoding(string path)
        {
            byte[] audio;
            using (var sng = SngReader.Open(path))
            {
                audio = sng.ReadFirstWithExtension(".ogg") ?? sng.ReadFirstWithExtension(".opus");
            }

            if (audio == null)
            {
                Fail("chart audio present for decoding");
                return;
            }

            AudioFormat format = AudioFormatDetector.Detect(audio);

            if (format != AudioFormat.OggOpus)
            {
                Console.WriteLine("    skipping decode: " + AudioFormatDetector.Describe(format) +
                                  " (needs Unity's decoder)");
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            OpusAudioDecoder.DecodedAudio decoded = OpusAudioDecoder.Decode(audio);
            watch.Stop();

            if (decoded == null)
            {
                Fail("opus audio decodes");
                return;
            }

            Check(decoded.Channels == 2, "decoded audio is stereo (" + decoded.Channels + ")");
            Check(decoded.SampleRate == 48000, "decoded at 48 kHz (" + decoded.SampleRate + ")");
            Check(decoded.DurationSeconds > 10f,
                  "decoded duration is sane (" + decoded.DurationSeconds.ToString("F1") + "s)");

            // Silence would mean the Ogg page walker dropped the audio packets.
            float peak = 0f;
            double sumSquares = 0;
            foreach (float sample in decoded.Samples)
            {
                float magnitude = Math.Abs(sample);
                if (magnitude > peak) peak = magnitude;
                sumSquares += sample * (double)sample;
            }

            double rms = Math.Sqrt(sumSquares / decoded.Samples.Length);
            Check(peak > 0.01f, "decoded audio has signal (peak " + peak.ToString("F2") + ")");
            Check(rms > 0.001, "decoded audio is not silent (rms " + rms.ToString("F4") + ")");

            // Decoded audio must cover the chart, or the end of the song plays in silence.
            SongChart chart = LoadChart(path, "Expert");
            Check(decoded.DurationSeconds >= chart.SongLength - 2f,
                  "audio covers the chart (audio " + decoded.DurationSeconds.ToString("F1") +
                  "s vs chart " + chart.SongLength.ToString("F1") + "s)");

            Console.WriteLine("    decode time: " + watch.ElapsedMilliseconds + " ms for " +
                              decoded.DurationSeconds.ToString("F1") + "s of audio");

            TestStreaming(audio, decoded);
        }

        /// <summary>
        /// The game streams audio instead of pre-decoding it. The stream must reproduce the same
        /// PCM as a full decode, and must not grow memory as it advances.
        /// </summary>
        static void TestStreaming(byte[] audio, OpusAudioDecoder.DecodedAudio reference)
        {
            var stream = new OggOpusStream(audio);

            Check(stream.Channels == reference.Channels, "stream reports stereo");
            Check(stream.TotalSamples == reference.Samples.Length,
                  "stream length matches full decode (" + stream.TotalSamples + " vs " +
                  reference.Samples.Length + ")");

            var buffer = new float[4096];
            long totalRead = 0;
            double sumSquares = 0;
            float peak = 0f;
            int read;

            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                totalRead += read;
                for (int i = 0; i < read; i++)
                {
                    float magnitude = Math.Abs(buffer[i]);
                    if (magnitude > peak) peak = magnitude;
                    sumSquares += buffer[i] * (double)buffer[i];
                }
            }

            Check(totalRead == reference.Samples.Length,
                  "stream yields every sample (" + totalRead + "/" + reference.Samples.Length + ")");
            Check(peak > 0.01f, "streamed audio has signal (peak " + peak.ToString("F2") + ")");

            double streamRms = Math.Sqrt(sumSquares / totalRead);
            double referenceRms = 0;
            for (int i = 0; i < reference.Samples.Length; i++)
            {
                referenceRms += reference.Samples[i] * (double)reference.Samples[i];
            }
            referenceRms = Math.Sqrt(referenceRms / reference.Samples.Length);

            // Bit-exact is not required, but the two paths must agree on loudness.
            double drift = Math.Abs(streamRms - referenceRms) / Math.Max(referenceRms, 1e-9);
            Check(drift < 0.02, "streamed audio matches full decode (rms drift " +
                                (drift * 100).ToString("F3") + "%)");

            // Rewinding must reproduce the opening samples, or a retry would start mid-song.
            stream.Reset();
            var first = new float[256];
            int afterReset = stream.Read(first, 0, first.Length);
            Check(afterReset == first.Length, "stream restarts after reset (" + afterReset + " samples)");

            bool sameAsStart = true;
            for (int i = 0; i < first.Length; i++)
            {
                if (Math.Abs(first[i] - reference.Samples[i]) > 1e-4f) { sameAsStart = false; break; }
            }
            Check(sameAsStart, "stream restart reproduces the opening audio");
        }

        static SongChart LoadChart(string path, string difficulty)
        {
            using (var sng = SngReader.Open(path))
            {
                string chartText = sng.ReadText("notes.chart");
                if (chartText != null) return ChartFileParser.Parse(chartText, difficulty);

                byte[] midi = sng.ReadFirstWithExtension(".mid");
                return MidiParser.Parse(midi, difficulty);
            }
        }

        /// <summary>
        /// Attaching a track is a deliberate choice, so it has to survive a restart and it must
        /// not resurrect a path whose file has since been deleted.
        /// </summary>
        static void TestAudioLinkStore()
        {
            string file = Path.Combine(Path.GetTempPath(), "bh_links_" + Guid.NewGuid().ToString("N") + ".txt");
            string chartA = "/charts/a.sng";
            string chartB = "/charts/with space and\ttab.sng";
            string audio = "/music/song one.mp3";

            try
            {
                var store = new AudioLinkStore(file);
                Check(store.Count == 0, "link store starts empty");
                Check(store.Get(chartA) == null, "an unlinked chart has no audio");

                store.Set(chartA, audio);
                Check(store.Get(chartA) == audio, "a linked chart returns its audio");

                // Reload from disk: this is what a relaunch does.
                var reopened = new AudioLinkStore(file);
                Check(reopened.Get(chartA) == audio, "the link survives a restart");

                // Paths with separators and tabs must round-trip, or the file format corrupts.
                store.Set(chartB, "/music/second.wav");
                var reopened2 = new AudioLinkStore(file);
                Check(reopened2.Get(chartB) == "/music/second.wav", "awkward paths survive a restart");
                Check(reopened2.Get(chartA) == audio, "adding a link keeps the earlier ones");

                store.Remove(chartA);
                var reopened3 = new AudioLinkStore(file);
                Check(reopened3.Get(chartA) == null, "removing a link is permanent");
                Check(reopened3.Get(chartB) == "/music/second.wav", "removing one link keeps the other");

                // A corrupt file must degrade to "no links", not crash the library.
                File.WriteAllText(file, "garbage line without a separator\n\n\t\n");
                var corrupt = new AudioLinkStore(file);
                Check(corrupt.Count == 0, "a corrupt link file loads as empty");
            }
            finally
            {
                if (File.Exists(file)) File.Delete(file);
            }
        }

        /// <summary>
        /// A calibration offset shifts every note, so an out-of-range or unparseable value would
        /// silently move the whole chart. Clamping and the zero fallback are what keep it safe.
        /// </summary>
        static void TestAudioCalibration()
        {
            string root = Path.Combine(Path.GetTempPath(), "bh_cal_" + Guid.NewGuid().ToString("N"));

            try
            {
                Check(AudioCalibration.Load(root) == 0, "an unset calibration reads as zero");
                Check(AudioCalibration.ToSeconds(50) == 0.05f, "50 ms converts to 0.05 s");
                Check(AudioCalibration.ToSeconds(-50) == -0.05f, "a negative offset converts too");
                Check(AudioCalibration.Clamp(9999) == AudioCalibration.MaxMilliseconds, "large values clamp to the maximum");
                Check(AudioCalibration.Clamp(-9999) == AudioCalibration.MinMilliseconds, "small values clamp to the minimum");
                Check(AudioCalibration.Clamp(37) == 37, "in-range values pass through");

                AudioCalibration.Save(root, 120);
                Check(AudioCalibration.Load(root) == 120, "a saved calibration is read back");

                AudioCalibration.Save(root, 9999);
                Check(AudioCalibration.Load(root) == AudioCalibration.MaxMilliseconds, "saving clamps before writing");

                File.WriteAllText(Path.Combine(root, "calibration.txt"), "not a number");
                Check(AudioCalibration.Load(root) == 0, "a corrupt calibration falls back to zero");

                Check(AudioCalibration.Describe(0) == "0 ms", "zero describes without a sign");
                Check(AudioCalibration.Describe(35) == "+35 ms", "a positive offset shows a plus");
                Check(AudioCalibration.Describe(-35) == "-35 ms", "a negative offset keeps its sign");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// Geometry decides whether the player can even see and reach every lane, so the layout
        /// has to stay correct across phone shapes.
        /// </summary>
        static void TestHighwayGeometry()
        {
            var layout = new HighwayLayout();

            // Lanes must tile the highway exactly, with no gaps and no overlap.
            float expectedWidth = layout.HighwayRightX - layout.HighwayLeftX;
            Check(Math.Abs(expectedWidth - layout.LaneWidth * LaneInfo.Count) < 0.0001f,
                  "highway width matches the lane count");

            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                int resolved = layout.LaneAtX(layout.LaneCenterX(lane));
                if (resolved != lane)
                {
                    Check(false, "lane " + lane + " centre maps back to lane " + resolved);
                    return;
                }
            }
            Check(true, "every lane centre maps back to its lane");

            Check(layout.LaneAtX(layout.HighwayLeftX - 0.5f) == -1, "left of the highway is no lane");
            Check(layout.LaneAtX(layout.HighwayRightX + 0.5f) == -1, "right of the highway is no lane");

            // A note exactly at the current time sits on the hit line; time mapping must invert.
            Check(Math.Abs(layout.TimeToY(10f, 10f) - layout.HitLineY) < 0.0001f,
                  "a note at song time lands on the hit line");

            float y = layout.TimeToY(12.5f, 10f);
            Check(Math.Abs(layout.YToTime(y, 10f) - 12.5f) < 0.001f, "time and Y convert back and forth");

            // Tall phones are the case that clipped the outer lanes at a fixed camera size.
            float[] aspects = { 0.45f, 0.429f, 0.562f, 0.75f, 1.33f };
            foreach (float aspect in aspects)
            {
                float size = layout.RequiredOrthographicSize(aspect);
                float visibleHalfWidth = size * aspect;
                float needed = Math.Abs(layout.HighwayRightX);
                if (visibleHalfWidth < needed)
                {
                    Check(false, "aspect " + aspect.ToString("F3") + " clips the outer lane");
                    return;
                }
            }
            Check(true, "highway fits the screen on every phone aspect");

            // A fixed 6.4 would have failed the tallest case; the fit must actually widen it.
            Check(layout.RequiredOrthographicSize(0.429f) > 6.4f,
                  "the fit widens the view for tall screens");
        }

        /// <summary>
        /// Local device files are the only audio source the player controls, so every common
        /// container must be identified and the unplayable ones reported as such.
        /// </summary>
        static void TestFormatDetection()
        {
            var wav = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
                                   (byte)'W', (byte)'A', (byte)'V', (byte)'E', 0, 0, 0, 0 };
            Check(AudioFormatDetector.Detect(wav) == AudioFormat.Wav, "detects WAV");
            Check(AudioFormatDetector.IsSupported(AudioFormat.Wav), "WAV is playable");

            // MP3 shows up either with an ID3 tag or straight at a frame sync, and both are common
            // on a phone's music folder.
            var mp3Id3 = new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 0, 0, 0, 0 };
            var mp3Frame = new byte[] { 0xFF, 0xFB, 0x90, 0x64, 0, 0, 0, 0, 0, 0, 0, 0 };
            Check(AudioFormatDetector.Detect(mp3Id3) == AudioFormat.Mp3, "detects MP3 with ID3 tag");
            Check(AudioFormatDetector.Detect(mp3Frame) == AudioFormat.Mp3, "detects bare MP3 frame");
            Check(AudioFormatDetector.IsSupported(AudioFormat.Mp3), "MP3 is playable");

            // Unplayable formats must still be named, so the player is told why nothing happened.
            var flac = new byte[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C', 0, 0, 0, 0, 0, 0, 0, 0 };
            Check(AudioFormatDetector.Detect(flac) == AudioFormat.Flac, "detects FLAC");
            Check(!AudioFormatDetector.IsSupported(AudioFormat.Flac), "FLAC is rejected, not silent");
            Check(AudioFormatDetector.Describe(AudioFormat.Flac) == "FLAC", "names FLAC in the message");

            var m4a = new byte[] { 0, 0, 0, 0, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0 };
            Check(AudioFormatDetector.Detect(m4a) == AudioFormat.M4a, "detects M4A");
            Check(!AudioFormatDetector.IsSupported(AudioFormat.M4a), "M4A is rejected, not silent");
        }

        /// <summary>
        /// The game runs a short lead-in with a negative song clock so notes scroll in before the
        /// music starts. Nothing may be judged or auto-missed while the clock is negative.
        /// </summary>
        static void TestLeadIn(SongChart chart)
        {
            var session = new GameSession(chart);
            var visible = new List<ActiveNote>();

            for (float time = -2.5f; time < 0f; time += 1f / 60f)
            {
                session.Update(time, 1f / 60f);
            }

            Check(session.Score.NotesMissed == 0,
                  "lead-in misses nothing (missed " + session.Score.NotesMissed + ")");
            Check(session.Score.NotesHit == 0, "lead-in hits nothing");

            // Notes must already be on screen at the end of the lead-in, or the song starts blind.
            float firstNoteTime = chart.Notes.Count > 0 ? chart.Notes[0].Time : 0f;
            session.Tracker.CollectVisible(-2.5f, firstNoteTime + 0.5f, visible);
            Check(visible.Count > 0, "lead-in shows upcoming notes (" + visible.Count +
                                    " visible, first note at " + firstNoteTime.ToString("F2") + "s)");

            // The clock must be able to move past zero and still judge the first notes.
            session.Update(0f, 1f / 60f);
            Check(session.Score.NotesMissed == 0, "crossing zero misses nothing");
        }

        /// <summary>
        /// Drives a session the way a player would: step the clock, hold frets for notes that are
        /// due, and keep sustains held while they run.
        /// </summary>
        static void Simulate(GameSession session, SongChart chart, bool pressNotes,
                             bool activateStarPower = false)
        {
            const float step = 1f / 240f;
            float end = chart.SongLength + 1f;

            var heldLanes = new bool[LaneInfo.Count];
            var holdingUntil = new float[LaneInfo.Count];
            for (int i = 0; i < holdingUntil.Length; i++) holdingUntil[i] = -1f;

            int nextNote = 0;

            for (float time = 0f; time <= end; time += step)
            {
                session.Update(time, step);

                if (!pressNotes)
                {
                    continue;
                }

                // Release lanes whose sustain has ended.
                for (int lane = 0; lane < LaneInfo.Count; lane++)
                {
                    if (heldLanes[lane] && time > holdingUntil[lane])
                    {
                        heldLanes[lane] = false;
                        session.ReleaseFret(lane);
                    }
                }

                // Press every note whose time has arrived, holding through any sustain.
                while (nextNote < chart.Notes.Count && chart.Notes[nextNote].Time <= time)
                {
                    ChartNote note = chart.Notes[nextNote];
                    nextNote++;

                    if (!heldLanes[note.Lane])
                    {
                        heldLanes[note.Lane] = true;
                        session.PressFret(note.Lane, true);
                    }

                    holdingUntil[note.Lane] = Math.Max(holdingUntil[note.Lane], note.EndTime);
                }

                if (activateStarPower && session.Score.CanActivateStarPower)
                {
                    session.ActivateStarPower();
                }
            }

            session.Finish();
        }

        /// <summary>
        /// Proves the difficulty scanner against a real enchor.us package: every difficulty it
        /// reports must actually load notes, and every one it omits must really be absent.
        /// </summary>
        static void TestRealChartDifficulties(string path)
        {
            List<string> reported;

            using (var sng = SngReader.Open(path))
            {
                string chartText = sng.ReadText("notes.chart");
                reported = chartText != null
                    ? ChartDifficultyScanner.FromChartText(chartText)
                    : ChartDifficultyScanner.FromMidi(sng.ReadFirstWithExtension(".mid"));
            }

            Check(reported.Count > 0, "the chart reports at least one difficulty (" + reported.Count + ")");

            for (int i = 0; i < reported.Count; i++)
            {
                SongChart chart = LoadChart(path, reported[i]);
                Check(chart.Notes.Count > 0, reported[i] + " reports notes when loaded");
            }

            for (int i = 0; i < ChartDifficulty.All.Length; i++)
            {
                string difficulty = ChartDifficulty.All[i];
                if (reported.Contains(difficulty)) continue;

                // An omitted difficulty must be genuinely empty, not just unlisted.
                try
                {
                    SongChart chart = LoadChart(path, difficulty);
                    Check(chart.Notes.Count == 0,
                          difficulty + " is not offered and really has no notes");
                }
                catch (Exception)
                {
                    Pass(difficulty + " is not offered and cannot be loaded");
                }
            }
        }

        /// <summary>
        /// The light show is driven by drum onsets. A chart with no drum track must still produce
        /// onsets (falling back to the guitar notes) or the stage would stay dark for the song.
        /// </summary>
        static void TestDrumHits(string path)
        {
            SongChart chart = LoadChart(path, "Expert");

            Check(chart.DrumHits.Count > 0, "drum hits are available for the light show (" + chart.DrumHits.Count + ")");

            bool ascending = true;
            for (int i = 1; i < chart.DrumHits.Count; i++)
            {
                if (chart.DrumHits[i] < chart.DrumHits[i - 1]) ascending = false;
            }
            Check(ascending, "drum hits are in time order");

            bool inRange = true;
            for (int i = 0; i < chart.DrumHits.Count; i++)
            {
                if (chart.DrumHits[i] < -1f || chart.DrumHits[i] > chart.SongLength + 10f) inRange = false;
            }
            Check(inRange, "drum hits fall inside the song");

            // Two onsets on the same frame would fire two light pulses at once.
            bool separated = true;
            for (int i = 1; i < chart.DrumHits.Count; i++)
            {
                if (chart.DrumHits[i] - chart.DrumHits[i - 1] < 0.019f) separated = false;
            }
            Check(separated, "simultaneous drum hits are collapsed");

            // The cached length must agree with a fresh walk of the notes.
            float expected = 0f;
            foreach (ChartNote note in chart.Notes)
            {
                if (note.EndTime > expected) expected = note.EndTime;
            }
            Check(Math.Abs(chart.SongLength - expected) < 0.0001f, "cached song length matches the notes");
        }

        /// <summary>
        /// Notes must travel toward the player: the far end of the highway is narrower, so the
        /// same lane is closer to the centre at the top than at the strike line.
        /// </summary>
        static void TestPerspectiveHighway()
        {
            var layout = new HighwayLayout();

            float atHitLine = Math.Abs(layout.LaneCenterXAt(0, layout.HitLineY));
            float atTop = Math.Abs(layout.LaneCenterXAt(0, layout.TopY));

            Check(atTop < atHitLine, "outer lanes converge toward the far end");
            Check(layout.LaneWidthAt(layout.TopY) < layout.LaneWidthAt(layout.HitLineY),
                  "lanes are narrower at the far end");
            Check(Math.Abs(layout.WidthScaleAt(layout.HitLineY) - 1f) < 0.0001f,
                  "lanes are full width at the strike line");

            // Every lane must still resolve to itself at any height, or taps would land wrong.
            for (int lane = 0; lane < LaneInfo.Count; lane++)
            {
                float y = layout.HitLineY + (lane + 1) * 0.4f;
                int resolved = layout.LaneAtXAt(layout.LaneCenterXAt(lane, y), y);
                if (resolved != lane)
                {
                    Check(false, "lane " + lane + " maps back to lane " + resolved + " at y=" + y);
                    return;
                }
            }
            Check(true, "every lane maps back to itself at any height");

            // A point far outside the tapered highway is no lane, even though it would be one
            // near the strike line.
            float wideX = layout.HighwayRightX - 0.05f;
            Check(layout.LaneAtXAt(wideX, layout.HitLineY) >= 0, "the outer edge is a lane at the strike line");
            Check(layout.LaneAtXAt(wideX, layout.TopY) == -1, "the same point is outside the tapered highway");

            // Notes ahead of the song time sit above the strike line, behind it below.
            float ahead = layout.TimeToY(12f, 10f);
            float behind = layout.TimeToY(9f, 10f);
            Check(ahead > layout.HitLineY, "a future note is above the strike line");
            Check(behind < layout.HitLineY, "a past note is below the strike line");

            // The approach is a perspective projection, so equal time steps must cover
            // shrinking distances as they recede: a note has to crawl in near the horizon and
            // rush at the player. A linear ramp would give equal steps here.
            float nearStep = layout.TimeToY(10.2f, 10f) - layout.TimeToY(10.1f, 10f);
            float farStep = layout.TimeToY(11.5f, 10f) - layout.TimeToY(11.4f, 10f);
            Check(farStep < nearStep, "notes cover less screen per second as they recede");
            Check(farStep < nearStep * 0.75f, "the perspective compression is visible, not a rounding artifact");

            // The far end of the approach window is exactly the horizon, and anything beyond it
            // is clipped. Without that clip a gem would appear in empty sky above the stage
            // instead of arriving from the front.
            float atHorizon = layout.TimeToY(10f + layout.ApproachSeconds, 10f);
            Check(Math.Abs(atHorizon - layout.TopY) < 0.001f,
                  "a note one approach window out lands on the horizon");
            Check(layout.IsVisible(atHorizon), "a note on the horizon is drawn");

            float beyond = layout.TimeToY(10f + layout.ApproachSeconds + 0.5f, 10f);
            Check(!layout.IsVisible(beyond), "a note past the horizon is clipped, not drawn in the sky");
        }

        /// <summary>
        /// The menu speaks enchor's naming (Normal) while chart sections are authored as Medium.
        /// A mismatch here would silently load the wrong difficulty, or none at all.
        /// </summary>
        static void TestDifficultyNaming()
        {
            Check(ChartDifficulty.All.Length == 4, "four difficulties are offered");
            Check(ChartDifficulty.At(0) == "Easy" && ChartDifficulty.At(3) == "Expert",
                  "difficulties run Easy to Expert");
            Check(ChartDifficulty.At(-5) == "Easy" && ChartDifficulty.At(99) == "Expert",
                  "difficulty index is clamped");

            Check(ChartDifficulty.ToSectionName("Normal") == "Medium", "Normal maps to the Medium section");
            Check(ChartDifficulty.ToSectionName("Medium") == "Medium", "Medium is already a section name");
            Check(ChartDifficulty.ToSectionName("expert") == "Expert", "difficulty matching ignores case");
            Check(ChartDifficulty.ToSectionName(null) == "Expert", "a missing difficulty defaults to Expert");

            Check(ChartDifficulty.IndexOf("Normal") == 1, "Normal reads back as the second difficulty");
            Check(ChartDifficulty.IndexOf("Medium") == 1, "Medium reads back as the second difficulty");
            Check(ChartDifficulty.IndexOf("nonsense") == 3, "an unknown difficulty falls back to Expert");
            Check(ChartDifficulty.SectionName(1) == "Medium", "section naming is available by index");
        }

        /// <summary>
        /// Preferences must survive a restart, and a corrupt file must not stop the game.
        /// </summary>
        static void TestSettingsRoundTrip()
        {
            string root = Path.Combine(Path.GetTempPath(), "bh_settings_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                GameSettings defaults = GameSettings.Load(root);
                Check(defaults.DifficultyIndex == GameSettings.DefaultDifficultyIndex,
                      "a fresh install starts on Expert");
                Check(defaults.LightShow, "the light show is on by default");

                defaults.ThemeIndex = 2;
                defaults.DifficultyIndex = 1;
                defaults.LightShow = false;
                defaults.Save(root);

                GameSettings reloaded = GameSettings.Load(root);
                Check(reloaded.ThemeIndex == 2, "theme choice survives a restart");
                Check(reloaded.DifficultyIndex == 1, "difficulty choice survives a restart");
                Check(!reloaded.LightShow, "light show choice survives a restart");
                Check(reloaded.Difficulty == "Normal", "the saved difficulty reads back by name");

                File.WriteAllText(Path.Combine(root, "settings.txt"), "garbage\n=broken\ntheme=notanumber\n");
                GameSettings recovered = GameSettings.Load(root);
                Check(recovered.DifficultyIndex == GameSettings.DefaultDifficultyIndex,
                      "a corrupt settings file falls back to defaults");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// The leaderboard is the only progression the game has, so ranking, grouping and
        /// persistence all have to be exact.
        /// </summary>
        static void TestLeaderboard()
        {
            string root = Path.Combine(Path.GetTempPath(), "bh_board_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var board = new Leaderboard(root);

                Check(board.Count == 0, "a new leaderboard is empty");
                Check(!board.Submit(new ScoreRecord { Song = "Song", Difficulty = "Expert", Score = 0 }),
                      "a zero score is not recorded");

                board.Submit(new ScoreRecord { Song = "Song", Difficulty = "Expert", Score = 5000, Accuracy = 70f, MaxCombo = 40 });
                board.Submit(new ScoreRecord { Song = "Song", Difficulty = "Expert", Score = 9000, Accuracy = 95f, MaxCombo = 120 });
                board.Submit(new ScoreRecord { Song = "Song", Difficulty = "Expert", Score = 7000, Accuracy = 80f, MaxCombo = 60 });
                board.Submit(new ScoreRecord { Song = "Song", Difficulty = "Easy", Score = 99999, Accuracy = 99f, MaxCombo = 300 });

                List<ScoreRecord> expert = board.Top("Song", "Expert");
                Check(expert.Count == 3, "records are grouped by difficulty");
                Check(expert[0].Score == 9000, "the best run ranks first");
                Check(expert[2].Score == 5000, "the weakest run ranks last");
                Check(board.BestScore("Song", "Expert") == 9000, "best score is reported per difficulty");
                Check(board.BestScore("Song", "Hard") == 0, "an unplayed difficulty has no score");

                // Grouping must ignore case and surrounding spaces, or a re-downloaded chart
                // with slightly different metadata would start a second board.
                Check(board.Top(" song ", "expert").Count == 3, "grouping ignores case and spaces");

                // Equal scores fall back to accuracy, then combo.
                board.Submit(new ScoreRecord { Song = "Tie", Difficulty = "Expert", Score = 1000, Accuracy = 60f, MaxCombo = 10 });
                board.Submit(new ScoreRecord { Song = "Tie", Difficulty = "Expert", Score = 1000, Accuracy = 90f, MaxCombo = 5 });
                List<ScoreRecord> tie = board.Top("Tie", "Expert");
                Check(tie[0].Accuracy == 90f, "equal scores rank by accuracy");

                // The board must be capped, and the cap must keep the best runs.
                for (int i = 0; i < 20; i++)
                {
                    board.Submit(new ScoreRecord { Song = "Flood", Difficulty = "Expert", Score = 100 + i, Accuracy = i, MaxCombo = i });
                }
                List<ScoreRecord> flood = board.Top("Flood", "Expert");
                Check(flood.Count == Leaderboard.MaxEntriesPerGroup,
                      "a group is capped at " + Leaderboard.MaxEntriesPerGroup + " entries");
                Check(flood[0].Score == 119, "the cap keeps the best run");

                Check(board.BestPerSong().Count == 3, "best-per-song lists each song once");

                // A fresh instance reads what the previous one wrote.
                var reloaded = new Leaderboard(root);
                Check(reloaded.Top("Song", "Expert").Count == 3, "records survive a restart");
                Check(reloaded.BestScore("Song", "Expert") == 9000, "the best score survives a restart");

                ScoreRecord fresh = new ScoreRecord { Song = "Song", Difficulty = "Expert", Score = 12000, Accuracy = 99f, MaxCombo = 200 };
                Check(board.Submit(fresh), "a new best run is ranked");
                Check(board.Top("Song", "Expert")[0].Score == 12000, "a new best run takes first place");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// The difficulty menu is driven by what a chart really contains, so the scanner is
        /// tested against synthetic .chart text and against a real downloaded package.
        /// </summary>
        static void TestDifficultyScanning()
        {
            Console.WriteLine();
            Console.WriteLine("--- difficulty scanning ---");

            const string chartText =
                "[Song]\n{\n  Name = Test\n}\n" +
                "[EasySingle]\n{\n  0 = N 0 0\n}\n" +
                "[ExpertSingle]\n{\n  0 = N 0 0\n  192 = N 1 0\n}\n" +
                "[HardDrums]\n{\n  0 = N 0 0\n}\n";

            List<string> found = ChartDifficultyScanner.FromChartText(chartText);
            Check(found.Count == 2, "only charted difficulties are reported");
            Check(found[0] == "Easy", "difficulties come back in canonical order");
            Check(found[1] == "Expert", "a charted Expert section is reported");
            Check(!found.Contains("Normal"), "a missing section is not offered");
            Check(!found.Contains("Hard"), "a drum-only section does not count as guitar");

            // A section header with no notes must not be offered: it would load zero notes.
            const string emptySection = "[Song]\n{\n  Name = Test\n}\n[MediumSingle]\n{\n}\n";
            Check(ChartDifficultyScanner.FromChartText(emptySection).Count == 0,
                  "an empty section is not offered");

            // Star power lines alone are not notes.
            const string starOnly = "[Song]\n{\n  Name = Test\n}\n[HardSingle]\n{\n  0 = S 2 100\n}\n";
            Check(ChartDifficultyScanner.FromChartText(starOnly).Count == 0,
                  "star power phrases do not count as notes");

            Check(ChartDifficultyScanner.FromChartText(null).Count == 0, "null chart text is handled");
            Check(ChartDifficultyScanner.FromChartText("").Count == 0, "empty chart text is handled");

            var single = new List<string> { "Expert" };
            Check(ChartDifficulty.Resolve("Expert", single) == "Expert",
                  "the requested difficulty is kept when charted");
            Check(ChartDifficulty.Resolve("Easy", single) == "Expert",
                  "a missing difficulty falls back to the hardest charted one");

            Check(ChartDifficulty.Resolve("Hard", new List<string>()) == "Hard",
                  "an unscanned chart keeps the request");
            Check(ChartDifficulty.Resolve("Hard", null) == "Hard",
                  "a null difficulty list keeps the request");

            var multi = new List<string> { "Easy", "Hard" };
            Check(ChartDifficulty.Resolve("Hard", multi) == "Hard",
                  "the requested difficulty wins when several are charted");
            Check(ChartDifficulty.Resolve("hard", multi) == "Hard",
                  "difficulty matching ignores case");
        }

        /// <summary>Clearing is a real feature now, so it has to be proven to persist.</summary>
        static void TestLeaderboardClear()
        {
            Console.WriteLine();
            Console.WriteLine("--- leaderboard clearing ---");

            string root = Path.Combine(Path.GetTempPath(), "bh_clear_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                var board = new Leaderboard(root);
                board.Submit(new ScoreRecord { Song = "A", Difficulty = "Expert", Score = 1000, Accuracy = 90f });
                board.Submit(new ScoreRecord { Song = "A", Difficulty = "Easy", Score = 500, Accuracy = 80f });
                board.Submit(new ScoreRecord { Song = "B", Difficulty = "Expert", Score = 700, Accuracy = 85f });

                Check(board.Count == 3, "three runs are recorded");

                board.Clear("A", "Expert");
                Check(board.Count == 2, "clearing one group leaves the others");
                Check(board.BestScore("A", "Expert") == 0, "the cleared group is empty");
                Check(board.BestScore("A", "Easy") == 500, "another difficulty of the same song is kept");

                var reloaded = new Leaderboard(root);
                Check(reloaded.Count == 2, "a clear survives a restart");

                reloaded.Clear();
                Check(reloaded.Count == 0, "clearing all empties the board");
                Check(new Leaderboard(root).Count == 0, "a full clear survives a restart");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        /// <summary>
        /// Difficulty is remembered per chart, so two songs can sit on different difficulties and
        /// a remembered one that the chart no longer carries must fall back safely.
        /// </summary>
        static void TestSongDifficultyStore()
        {
            Console.WriteLine();
            Console.WriteLine("--- per-song difficulty ---");

            string root = Path.Combine(Path.GetTempPath(), "bh_songdiff_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            try
            {
                string file = Path.Combine(root, "song_difficulties.txt");
                var store = new SongDifficultyStore(file);

                Check(store.Get("/charts/a.sng") == null, "a song has no difficulty until it is played");
                Check(store.Resolve("/charts/a.sng", "Expert", new List<string> { "Easy", "Hard" }) == "Hard",
                      "an unplayed song uses the default resolved against the chart");

                store.Set("/charts/a.sng", "Easy");
                store.Set("/charts/b.sng", "Hard");

                Check(store.Get("/charts/a.sng") == "Easy", "a choice is stored against its own song");
                Check(store.Get("/charts/b.sng") == "Hard", "another song keeps its own choice");
                Check(store.Resolve("/charts/a.sng", "Expert", new List<string> { "Easy", "Hard" }) == "Easy",
                      "the remembered choice wins over the default");

                var reopened = new SongDifficultyStore(file);
                Check(reopened.Get("/charts/a.sng") == "Easy", "a per-song choice survives a restart");
                Check(reopened.Count == 2, "both songs are remembered");

                // A chart re-downloaded with fewer sections must not resolve to a missing one.
                Check(reopened.Resolve("/charts/a.sng", "Expert", new List<string> { "Expert" }) == "Expert",
                      "a remembered difficulty the chart lacks falls back to what it charts");

                reopened.Set("/charts/a.sng", null);
                Check(reopened.Get("/charts/a.sng") == null, "a choice can be forgotten");
                Check(new SongDifficultyStore(file).Get("/charts/b.sng") == "Hard",
                      "forgetting one song leaves the other alone");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        static void CheckSortedByTime(SongChart chart)
        {
            for (int i = 1; i < chart.Notes.Count; i++)
            {
                if (chart.Notes[i].Time < chart.Notes[i - 1].Time)
                {
                    Fail("notes are sorted by time");
                    return;
                }
            }
            Pass("notes are sorted by time");
        }

        /// <summary>
        /// The search body must never carry an empty enum value: the endpoint answers 400 for
        /// "difficulty":"" and for a missing page. These assertions pin the exact payload shape.
        /// </summary>
        static void TestEnchorSearchRequest()
        {
            Console.WriteLine();
            Console.WriteLine("--- enchor search request ---");

            string unfiltered = EnchorSearchRequest.Build("", 1, "guitar", null);

            Check(!unfiltered.Contains("\"difficulty\""),
                  "an unfiltered search omits difficulty (" + unfiltered + ")");
            Check(unfiltered.Contains("\"search\":\"\""), "empty query is sent as an empty string");
            Check(unfiltered.Contains("\"page\":1"), "page 1 is sent as a number");
            Check(unfiltered.Contains("\"instrument\":\"guitar\""), "instrument is lowercased");
            Check(unfiltered.Contains("\"drumType\":\"fourLane\""), "drumType is sent as fourLane");

            string filtered = EnchorSearchRequest.Build("nirvana", 3, "guitar", "Expert");

            Check(filtered.Contains("\"difficulty\":\"expert\""),
                  "a chosen difficulty is lowercased (" + filtered + ")");
            Check(!filtered.Contains("\"difficulty\":\"\""), "an empty difficulty is never sent");
            Check(filtered.Contains("\"page\":3"), "the requested page is sent");

            Check(EnchorSearchRequest.Build("", 0, null, null).Contains("\"page\":1"),
                  "page 0 is clamped to 1, which the endpoint rejects");
            Check(EnchorSearchRequest.Build("", 1, null, null).Contains("\"instrument\":\"guitar\""),
                  "a missing instrument falls back to guitar");
            Check(EnchorSearchRequest.Build("", 1, "Bass", null).Contains("\"instrument\":\"bass\""),
                  "instrument casing is normalised");

            string quoted = EnchorSearchRequest.Build("say \"hi\"", 1, "guitar", null);

            Check(quoted.Contains("\\\"hi\\\""), "quotes in the query are escaped (" + quoted + ")");
            Check(quoted.Contains("\"search\":\"say \\\"hi\\\"\""), "an escaped query stays valid JSON");

            string newline = EnchorSearchRequest.Build("a\nb", 1, "guitar", null);

            Check(newline.Contains("\\n") && !newline.Contains("\n"),
                  "newlines are escaped so the body stays one line");

            Check(EnchorSearchRequest.Build("", 1, "guitar", "hard").Contains("\"difficulty\":\"hard\""),
                  "every valid difficulty passes through unchanged");
        }

        /// <summary>
        /// The score milestone reward. It has to fire on score, fire once per threshold, expire
        /// on its own, and double the multiplier only while it is running.
        /// </summary>
        static void TestOverdriveMilestone()
        {
            Console.WriteLine();
            Console.WriteLine("--- overdrive milestone ---");

            var score = new ScoreState();
            score.Reset(500);

            int events = 0;
            int lastMilestone = 0;
            score.OverdriveStarted += milestone => { events++; lastMilestone = milestone; };

            Check(!score.OverdriveActive, "a fresh run has no overdrive");
            Check(score.NextOverdriveScore == ScoreState.OverdriveStep,
                  "the first milestone sits at one step of score");

            // Perfect hits on a full combo pay 100 * multiplier, so this needs a few hundred notes
            // to reach 15,000. The loop stops the moment the milestone fires.
            int guard = 0;
            while (!score.OverdriveActive && guard++ < 2000)
            {
                score.RegisterHit(Judgement.Perfect, false);
            }

            Check(score.OverdriveActive, "crossing the milestone starts overdrive");
            Check(events == 1, "the milestone is announced exactly once (" + events + ")");
            Check(lastMilestone == 1, "the announcement carries the milestone number");
            Check(score.Score >= ScoreState.OverdriveStep, "the milestone is score driven");
            Check(score.Multiplier == 8, "overdrive doubles a full 4x combo to 8x (" + score.Multiplier + ")");
            Check(score.OverdriveRemaining > 0f, "the reward has time left when it starts");
            Check(score.NextOverdriveScore == ScoreState.OverdriveStep * 2,
                  "the next milestone moves to the following step");

            // A single hit that crosses several thresholds must fire each one, not just the first.
            var burst = new ScoreState();
            burst.Reset(10);
            int burstEvents = 0;
            burst.OverdriveStarted += _ => burstEvents++;
            for (int i = 0; i < 400; i++) burst.RegisterHit(Judgement.Perfect, false, 1f);

            Check(burst.OverdriveCount >= 2, "several milestones can fire in one run (" + burst.OverdriveCount + ")");
            Check(burstEvents == burst.OverdriveCount, "each milestone raises its own event");

            // The reward must end by itself, otherwise a player could bank it forever.
            score.Tick(ScoreState.OverdriveSeconds * 0.5f);
            Check(score.OverdriveActive, "overdrive survives half its duration");

            score.Tick(ScoreState.OverdriveSeconds);
            Check(!score.OverdriveActive, "overdrive expires when the timer runs out");
            Check(score.Multiplier == 4, "the multiplier drops back to 4x (" + score.Multiplier + ")");
            Check(score.OverdriveFraction == 0f, "an expired overdrive reports no progress");

            // Restarting a run has to clear it, or the next song would open at 8x.
            score.Reset(200);
            Check(!score.OverdriveActive, "a reset clears overdrive");
            Check(score.OverdriveCount == 0, "a reset clears the milestone count");
            Check(score.NextOverdriveScore == ScoreState.OverdriveStep,
                  "a reset puts the next milestone back at the first step");

            // A miss must not grant the reward.
            var missed = new ScoreState();
            missed.Reset(100);
            for (int i = 0; i < 300; i++) missed.RegisterMiss();
            Check(!missed.OverdriveActive, "misses never start overdrive");
            Check(missed.Score == 0, "misses score nothing");
        }

        static void Check(bool condition, string description)
        {
            if (condition) Pass(description);
            else Fail(description);
        }

        static void Pass(string description)
        {
            _checks++;
            Console.WriteLine("    PASS  " + description);
        }

        static void Fail(string description)
        {
            _checks++;
            _failures++;
            Console.WriteLine("    FAIL  " + description);
        }
    }
}
