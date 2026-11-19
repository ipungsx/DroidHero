using System;
using System.IO;
using BandHero.Chart;

namespace BandHero.Core
{
    /// <summary>
    /// One playable song: a chart plus the audio it should be played against.
    ///
    /// Audio can come from inside the chart package (the usual case) or from a file on the
    /// device, which is how players use their own music without shipping copyrighted audio.
    /// </summary>
    public class SongEntry
    {
        public string DisplayName = "Unknown";
        public string Artist = "Unknown";
        public string Album = "";
        public string Charter = "";
        public float LengthSeconds;

        /// <summary>Path to the .sng (or a folder containing .chart/.mid) on disk.</summary>
        public string ChartPath;

        /// <summary>Optional local audio file that replaces the audio packed in the chart.</summary>
        public string LocalAudioPath;

        public byte[] AlbumArt;

        /// <summary>Decoded album art, cached because the song list is redrawn every frame.</summary>
        public UnityEngine.Texture2D AlbumTexture;

        public bool HasLocalAudio => !string.IsNullOrEmpty(LocalAudioPath);

        public string Subtitle =>
            HasLocalAudio
                ? Artist + "  ·  local audio"
                : Artist;
    }
}
