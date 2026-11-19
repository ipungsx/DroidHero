using System;
using System.Collections.Generic;
using System.IO;
using BandHero.Chart;
using BandHero.Audio;
using UnityEngine;

namespace BandHero.Core
{
    /// <summary>
    /// Discovers and caches playable songs from the app's chart folder.
    ///
    /// Charts are copied into <c>persistentDataPath/Charts</c> when downloaded from enchor.us,
    /// so the library is just a directory scan. Metadata comes from the package itself, which
    /// avoids a separate index that could drift out of sync with the files.
    /// </summary>
    public class SongLibrary
    {
        public string ChartsDirectory =>
            Path.Combine(Application.persistentDataPath, "Charts");

        public string DownloadsDirectory =>
            Path.Combine(Application.persistentDataPath, "Charts");

        AudioLinkStore _links;

        public SongLibrary()
        {
            Directory.CreateDirectory(ChartsDirectory);
        }

        AudioLinkStore Links =>
            _links ?? (_links = new AudioLinkStore(Path.Combine(Application.persistentDataPath, "audio_links.txt")));

        /// <summary>
        /// Attaches a local audio file to a chart and remembers it across launches.
        ///
        /// Returns false when the file is gone, so the caller can tell the player instead of
        /// silently reverting to the chart's own audio on the next play.
        /// </summary>
        public bool AttachLocalAudio(SongEntry entry, string audioPath)
        {
            if (entry == null || string.IsNullOrEmpty(entry.ChartPath)) return false;
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath)) return false;

            entry.LocalAudioPath = audioPath;
            Links.Set(entry.ChartPath, audioPath);
            return true;
        }

        /// <summary>Detaches the local audio file so the chart plays its bundled track again.</summary>
        public void DetachLocalAudio(SongEntry entry)
        {
            if (entry == null) return;

            entry.LocalAudioPath = null;
            Links.Remove(entry.ChartPath);
        }

        /// <summary>Scans the chart folder for .sng files and reads their metadata.</summary>
        public List<SongEntry> Scan()
        {
            var songs = new List<SongEntry>();
            string[] files;

            try
            {
                files = Directory.GetFiles(ChartsDirectory, "*.sng", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not scan charts: " + ex.Message);
                return songs;
            }

            for (int i = 0; i < files.Length; i++)
            {
                SongEntry entry = ReadEntry(files[i]);
                if (entry != null) songs.Add(entry);
            }

            songs.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return songs;
        }

        /// <summary>Reads metadata for one chart without loading its audio.</summary>
        public SongEntry ReadEntry(string path)
        {
            try
            {
                using (var sng = SngReader.Open(path))
                {
                    var entry = new SongEntry { ChartPath = path };

                    entry.DisplayName = Metadata(sng, "name", Path.GetFileNameWithoutExtension(path));
                    entry.Artist = Metadata(sng, "artist", "Unknown");
                    entry.Album = Metadata(sng, "album", "");
                    entry.Charter = Metadata(sng, "charter", "");

                    if (long.TryParse(Metadata(sng, "song_length", "0"), out long lengthMs) && lengthMs > 0)
                    {
                        entry.LengthSeconds = lengthMs / 1000f;
                    }

                    entry.AlbumArt = sng.ReadFile("album.jpg") ?? sng.ReadFile("album.png");

                    // Only honour a remembered attachment if the file is still on the device;
                    // a stale path would make the song unplayable instead of just reverted.
                    string linked = Links.Get(path);
                    if (!string.IsNullOrEmpty(linked) && File.Exists(linked)) entry.LocalAudioPath = linked;

                    return entry;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Skipping unreadable chart " + path + ": " + ex.Message);
                return null;
            }
        }

        static string Metadata(SngReader sng, string key, string fallback) =>
            sng.Metadata.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;

        /// <summary>Saves a downloaded chart into the library and returns its entry.</summary>
        public SongEntry SaveChart(byte[] sngData, string md5)
        {
            Directory.CreateDirectory(ChartsDirectory);

            string path = Path.Combine(ChartsDirectory, SanitizeName(md5) + ".sng");
            File.WriteAllBytes(path, sngData);

            return ReadEntry(path);
        }

        /// <summary>
        /// Builds the playable chart for a song, applying a local audio file when one is set.
        /// Returns null and reports an error when the chart or audio cannot be used.
        /// </summary>
        public LoadedSong Load(SongEntry entry, string difficulty, out string error)
        {
            error = null;

            SongChart chart;
            byte[] audioBytes = null;
            string audioName = "audio";

            try
            {
                using (var sng = SngReader.Open(entry.ChartPath))
                {
                    string chartText = sng.ReadText("notes.chart");
                    if (chartText != null)
                    {
                        chart = ChartFileParser.Parse(chartText, difficulty);
                    }
                    else
                    {
                        byte[] midi = sng.ReadFirstWithExtension(".mid");
                        if (midi == null)
                        {
                            error = "This chart has no notes.chart or notes.mid.";
                            return null;
                        }
                        chart = MidiParser.Parse(midi, difficulty);
                    }

                    if (!entry.HasLocalAudio)
                    {
                        audioBytes = sng.ReadFirstWithExtension(".ogg") ?? sng.ReadFirstWithExtension(".opus");
                        if (audioBytes == null)
                        {
                            error = "No audio in the chart. Pick a local audio file to play this song.";
                            return null;
                        }
                        audioName = entry.DisplayName;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "Could not read chart: " + ex.Message;
                return null;
            }

            if (chart.Notes.Count == 0)
            {
                error = "This chart has no " + difficulty + " notes.";
                return null;
            }

            AudioClip clip;
            if (entry.HasLocalAudio)
            {
                clip = AudioDecoder.LoadLocalFile(entry.LocalAudioPath, out string audioError);
                if (clip == null)
                {
                    error = audioError;
                    return null;
                }
            }
            else
            {
                clip = AudioDecoder.Decode(audioBytes, audioName);
                if (clip == null)
                {
                    error = "Could not decode the chart audio.";
                    return null;
                }
            }

            // Chart audio and a chosen local track rarely start at the same instant, so the
            // chart offset is applied on top of any manual calibration.
            chart.FinalizeChart();

            return new LoadedSong
            {
                Chart = chart,
                Audio = clip,
                Entry = entry,
                Difficulty = difficulty
            };
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "chart";

            char[] chars = name.ToCharArray();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j]) { chars[i] = '_'; break; }
                }
            }
            return new string(chars);
        }
    }

    public class LoadedSong
    {
        public SongChart Chart;
        public UnityEngine.AudioClip Audio;
        public SongEntry Entry;
        public string Difficulty;
    }
}
