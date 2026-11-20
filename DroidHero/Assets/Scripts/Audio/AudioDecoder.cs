using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace DroidHero.Audio
{
    /// <summary>
    /// Turns chart audio and local device files into playable clips.
    ///
    /// Two codecs matter in practice: charts ship Ogg Opus (which Unity cannot decode, so it goes
    /// through <see cref="OpusAudioDecoder"/>), and local files are usually WAV or Ogg Vorbis, which
    /// Unity decodes natively. Anything else is reported clearly instead of playing silence.
    /// </summary>
    public static class AudioDecoder
    {
        public static AudioFormat Detect(byte[] data) => AudioFormatDetector.Detect(data);

        public static bool IsSupported(AudioFormat format) => AudioFormatDetector.IsSupported(format);

        public static string DescribeFormat(AudioFormat format) => AudioFormatDetector.Describe(format);

        /// <summary>Decodes audio bytes into a clip. Returns null when the format cannot be played.</summary>
        public static AudioClip Decode(byte[] data, string clipName, Action<float> onProgress = null)
        {
            if (data == null || data.Length == 0) return null;

            AudioFormat format = Detect(data);

            if (format == AudioFormat.OggOpus)
            {
                // Streamed rather than pre-decoded: a full song is ~115 MB of float samples,
                // which is too much resident memory for a phone.
                return OpusClipFactory.Create(data, clipName);
            }

            if (format == AudioFormat.Wav || format == AudioFormat.OggVorbis || format == AudioFormat.Mp3)
            {
                return DecodeWithUnity(data, clipName, format);
            }

            Debug.LogWarning("Cannot decode " + DescribeFormat(format) +
                             "; use WAV, Ogg Vorbis, Ogg Opus or MP3.");
            return null;
        }

        static AudioClip DecodeWithUnity(byte[] data, string clipName, AudioFormat format)
        {
            string extension = format == AudioFormat.Wav ? ".wav"
                             : format == AudioFormat.Mp3 ? ".mp3"
                             : ".ogg";
            string directory = Path.Combine(Application.persistentDataPath, "audio_cache");
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, SanitizeFileName(clipName) + extension);
            File.WriteAllBytes(file, data);

            // Unity's audio decoder needs a real file path, so the bytes are staged on disk.
            using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + file, AudioType.UNKNOWN))
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone) { }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("Audio decode failed for " + clipName + ": " + request.error);
                    return null;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip != null) clip.name = clipName;
                return clip;
            }
        }

        /// <summary>Loads an audio file from device storage, as picked by the player.</summary>
        public static AudioClip LoadLocalFile(string sourcePath, out string error)
        {
            error = null;

            if (!File.Exists(sourcePath))
            {
                error = "File not found: " + sourcePath;
                return null;
            }

            byte[] data = File.ReadAllBytes(sourcePath);
            AudioFormat format = Detect(data);

            if (!IsSupported(format))
            {
                error = DescribeFormat(format) + " is not supported. Use WAV, Ogg Vorbis, Ogg Opus or MP3.";
                return null;
            }

            var clip = Decode(data, Path.GetFileNameWithoutExtension(sourcePath));
            if (clip == null) error = "Could not decode this audio file.";
            return clip;
        }

        static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "clip";

            var chars = name.ToCharArray();
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
}
