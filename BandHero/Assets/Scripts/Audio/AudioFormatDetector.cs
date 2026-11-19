namespace BandHero.Audio
{
    /// <summary>Container/codec identification for audio bytes.</summary>
    public enum AudioFormat
    {
        Unknown,
        Wav,
        OggVorbis,
        OggOpus,
        Mp3,
        M4a,
        Flac
    }

    /// <summary>
    /// Identifies audio containers from their magic bytes.
    ///
    /// Kept free of UnityEngine so the format rules can be tested headlessly; the actual decoding
    /// lives in <see cref="AudioDecoder"/>.
    /// </summary>
    public static class AudioFormatDetector
    {
        public static AudioFormat Detect(byte[] data)
        {
            if (data == null || data.Length < 12) return AudioFormat.Unknown;

            if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E')
            {
                return AudioFormat.Wav;
            }

            if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S')
            {
                // The codec header sits in the first Ogg page, so a short scan is enough.
                int limit = data.Length - 8;
                if (limit > 4096) limit = 4096;

                for (int i = 0; i <= limit; i++)
                {
                    if (data[i] == 'O' && data[i + 1] == 'p' && data[i + 2] == 'u' && data[i + 3] == 's' &&
                        data[i + 4] == 'H' && data[i + 5] == 'e' && data[i + 6] == 'a' && data[i + 7] == 'd')
                    {
                        return AudioFormat.OggOpus;
                    }
                }

                return AudioFormat.OggVorbis;
            }

            if (data[0] == 'f' && data[1] == 'L' && data[2] == 'a' && data[3] == 'C') return AudioFormat.Flac;
            if (data[0] == 'I' && data[1] == 'D' && data[2] == '3') return AudioFormat.Mp3;

            if (data[4] == 'f' && data[5] == 't' && data[6] == 'y' && data[7] == 'p') return AudioFormat.M4a;

            if (data[0] == 0xFF && (data[1] & 0xE0) == 0xE0) return AudioFormat.Mp3;

            return AudioFormat.Unknown;
        }

        /// <summary>
        /// Formats this build can actually play back.
        ///
        /// FLAC and M4A are detected only so the player gets an accurate "not supported" message
        /// rather than silence; Unity's built-in decoder handles neither.
        /// </summary>
        public static bool IsSupported(AudioFormat format) =>
            format == AudioFormat.Wav || format == AudioFormat.OggVorbis ||
            format == AudioFormat.OggOpus || format == AudioFormat.Mp3;

        public static string Describe(AudioFormat format)
        {
            switch (format)
            {
                case AudioFormat.Wav: return "WAV";
                case AudioFormat.OggVorbis: return "Ogg Vorbis";
                case AudioFormat.OggOpus: return "Ogg Opus";
                case AudioFormat.Mp3: return "MP3";
                case AudioFormat.M4a: return "M4A/AAC";
                case AudioFormat.Flac: return "FLAC";
                default: return "unknown";
            }
        }
    }
}
