using System;
using UnityEngine;

namespace DroidHero.Audio
{
    /// <summary>
    /// An <see cref="AudioClip"/> whose PCM is produced on demand by <see cref="OggOpusStream"/>.
    ///
    /// A five minute stereo song is about 115 MB of float samples, which a phone cannot hold
    /// comfortably. Streaming keeps only a small read-ahead window in memory, and Unity pulls it
    /// through the PCM reader callback as playback advances.
    ///
    /// These clips play once. Replaying a song builds a fresh clip, because this Unity version's
    /// streaming overload has no position callback to rewind the decoder.
    /// </summary>
    public static class OpusClipFactory
    {
        /// <summary>
        /// Builds a streaming clip. Returns null and logs the reason when the stream cannot be read.
        /// </summary>
        public static AudioClip Create(byte[] oggData, string clipName)
        {
            OggOpusStream stream;

            try
            {
                stream = new OggOpusStream(oggData);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Could not open Opus stream for " + clipName + ": " + ex.Message);
                return null;
            }

            int channels = stream.Channels;
            int lengthSamples = stream.TotalSamples / channels;

            if (lengthSamples <= 0)
            {
                Debug.LogWarning("Opus stream for " + clipName + " reports no samples.");
                return null;
            }

            // Reused across callbacks so playback does not allocate every audio buffer.
            float[] scratch = null;

            AudioClip clip = AudioClip.Create(clipName, lengthSamples, channels, stream.SampleRate,
                                              true,
                                              data =>
                                              {
                                                  if (scratch == null || scratch.Length != data.Length)
                                                  {
                                                      scratch = new float[data.Length];
                                                  }

                                                  int read = stream.Read(scratch, 0, data.Length);

                                                  for (int i = 0; i < read; i++) data[i] = scratch[i];

                                                  // Pad the tail so a short read is silence, not garbage.
                                                  for (int i = read; i < data.Length; i++) data[i] = 0f;
                                              });

            if (clip == null)
            {
                Debug.LogWarning("Could not create a streaming clip for " + clipName + ".");
                return null;
            }

            return clip;
        }
    }
}
