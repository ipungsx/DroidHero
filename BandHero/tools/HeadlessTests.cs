using System;
using System.Collections.Generic;
using System.IO;
using BandHero.Audio;
using BandHero.Chart;
using BandHero.Gameplay;
using BandHero.Core;

namespace BandHero.Tests
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
            Console.WriteLine("=== Band Hero headless tests ===");

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
            }

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

            // Star power: hitting notes with SP phrases must fill a bar the player can activate.
            var starPower = new GameSession(chart);
            Simulate(starPower, chart, pressNotes: true, activateStarPower: true);
            Check(starPower.Score.NotesHit == chart.Notes.Count, "star power run still hits every note");
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
