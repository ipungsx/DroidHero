using System;
using System.Collections.Generic;
using Concentus.Structs;

namespace DroidHero.Audio
{
    /// <summary>
    /// Decodes Ogg Opus, the codec every enchor.us chart ships.
    ///
    /// Unity has no Opus support on any platform, so charts would otherwise be silent. Only the
    /// Concentus codec is used; the Ogg container is parsed here, which keeps the dependency
    /// surface to a single DLL and avoids the mismatched helper packages on NuGet.
    /// </summary>
    public static class OpusAudioDecoder
    {
        public const int SampleRate = 48000;

        /// <summary>Reason the last decode returned null, for callers that surface it to players.</summary>
        public static string LastError { get; private set; }

        public sealed class DecodedAudio
        {
            public float[] Samples;
            public int Channels;
            public int SampleRate;

            public float DurationSeconds =>
                Channels == 0 ? 0f : Samples.Length / (float)(Channels * SampleRate);
        }

        /// <summary>
        /// Decodes a whole stream into memory. Used by tests; playback uses
        /// <see cref="OggOpusStream"/> so a song does not cost ~100 MB of resident floats.
        /// </summary>
        public static DecodedAudio Decode(byte[] oggData, Action<float> onProgress = null)
        {
            LastError = null;

            if (oggData == null || oggData.Length == 0)
            {
                LastError = "No audio data.";
                return null;
            }

            try
            {
                var stream = new OggOpusStream(oggData);
                var samples = new float[SampleRate * stream.Channels * 30];
                int written = 0;

                var buffer = new float[SampleRate * stream.Channels];
                int read;

                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (written + read > samples.Length)
                    {
                        Array.Resize(ref samples, Math.Max(samples.Length * 2, written + read));
                    }

                    Array.Copy(buffer, 0, samples, written, read);
                    written += read;

                    if (onProgress != null) onProgress(stream.Progress);
                }

                if (written == 0)
                {
                    LastError = "Stream contained no audio.";
                    return null;
                }

                Array.Resize(ref samples, written);

                return new DecodedAudio
                {
                    Samples = samples,
                    Channels = stream.Channels,
                    SampleRate = SampleRate
                };
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// Pull-based PCM reader over an Ogg Opus stream.
    ///
    /// Packets are indexed up front (a few kilobytes) and decoded only when asked for, so memory
    /// stays flat regardless of song length. This is what makes a five minute song affordable on
    /// a phone instead of allocating its whole waveform at once.
    /// </summary>
    public sealed class OggOpusStream
    {
        readonly List<byte[]> _packets;
        OpusDecoder _decoder;
        readonly short[] _frame;

        int _nextPacket;
        short[] _decoded;
        int _decodedOffset;
        int _channels;
        readonly int _preSkip;
        readonly int _totalSamples;
        int _preSkipRemaining;
        int _samplesProduced;

        public int Channels => _channels;

        public int SampleRate => OpusAudioDecoder.SampleRate;

        /// <summary>
        /// Interleaved sample count the clip should expose.
        ///
        /// The Ogg granule position is authoritative: the final packets decode past it with
        /// encoder padding, and the pre-skip is silence the encoder added at the start.
        /// </summary>
        public int TotalSamples => Math.Max(0, _totalSamples - _preSkip * _channels);

        public float Progress
        {
            get
            {
                int total = TotalSamples;
                return total <= 0 ? 0f : Math.Min(1f, _samplesProduced / (float)total);
            }
        }

        public OggOpusStream(byte[] oggData)
        {
            if (oggData == null || oggData.Length == 0) throw new ArgumentException("No audio data.");

            int channels = 2;
            int preSkip = 0;
            int totalSamples = 0;

            _packets = OggPageReader.ReadAudioPackets(oggData, ref channels, ref preSkip, ref totalSamples);

            if (_packets.Count == 0) throw new InvalidOperationException("No Opus audio packets found.");

            _channels = channels;
            _preSkip = preSkip;
            _preSkipRemaining = preSkip;
            // Granule positions count frames, so scale to interleaved samples.
            _totalSamples = totalSamples > 0 ? totalSamples * _channels : EstimateSamples(_packets.Count);

            _decoder = new OpusDecoder(OpusAudioDecoder.SampleRate, _channels);

            // Sized for the largest possible Opus frame (120 ms), not the frame we expect.
            _frame = new short[OpusAudioDecoder.SampleRate / 1000 * 120 * _channels];
        }

        int EstimateSamples(int packetCount)
        {
            // Fall back to 20 ms frames when a stream carries no granule position.
            return packetCount * OpusAudioDecoder.SampleRate / 50;
        }

        /// <summary>Fills the buffer with interleaved samples. Returns 0 once the stream ends.</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            int written = 0;

            while (written < count)
            {
                if (_decoded != null && _decodedOffset < _decoded.Length)
                {
                    int take = Math.Min(_decoded.Length - _decodedOffset, count - written);

                    // Never exceed the declared length; the last packet pads past the granule.
                    int remaining = TotalSamples - _samplesProduced;
                    if (remaining < take) take = remaining;
                    if (take <= 0) break;

                    for (int i = 0; i < take; i++)
                    {
                        buffer[offset + written + i] = _decoded[_decodedOffset + i] / 32768f;
                    }

                    _decodedOffset += take;
                    written += take;
                    _samplesProduced += take;
                    continue;
                }

                if (_nextPacket >= _packets.Count) break;

                if (!DecodeNextPacket()) break;
            }

            return written;
        }

        bool DecodeNextPacket()
        {
            byte[] packet = _packets[_nextPacket++];

            int frames = _decoder.Decode(packet, 0, packet.Length, _frame, 0, _frame.Length / _channels, false);

            if (frames <= 0)
            {
                _decoded = null;
                return true;
            }

            int total = frames * _channels;

            // The pre-skip trims encoder priming samples from the very start of the stream.
            int skip = 0;
            if (_preSkipRemaining > 0)
            {
                skip = Math.Min(_preSkipRemaining * _channels, total);
                _preSkipRemaining -= skip / _channels;
            }

            _decoded = new short[total - skip];
            Array.Copy(_frame, skip, _decoded, 0, total - skip);
            _decodedOffset = 0;

            return true;
        }

        /// <summary>Restarts decoding from the beginning, as a looping clip needs.</summary>
        public void Reset()
        {
            // Rewinding the packets is not enough: the codec carries filter state across frames,
            // so restarting with the old decoder would begin the song with the tail's state.
            _decoder = new OpusDecoder(OpusAudioDecoder.SampleRate, _channels);

            _nextPacket = 0;
            _preSkipRemaining = _preSkip;
            _decoded = null;
            _decodedOffset = 0;
            _samplesProduced = 0;
        }
    }

    /// <summary>Walks Ogg pages and collects the audio packets that follow the Opus headers.</summary>
    static class OggPageReader
    {
        public static List<byte[]> ReadAudioPackets(byte[] data, ref int channels, ref int preSkip,
                                                    ref int totalSamples)
        {
            var packets = new List<byte[]>(4096);
            var pending = new List<byte>(8192);

            int pos = 0;
            int packetIndex = 0;

            while (pos + 27 <= data.Length)
            {
                if (data[pos] != 'O' || data[pos + 1] != 'g' || data[pos + 2] != 'g' || data[pos + 3] != 'S')
                {
                    break;
                }

                // Granule position on a page is the running sample count at its last completed packet.
                long granule = BitConverter.ToInt64(data, pos + 6);
                if (granule > 0 && granule < int.MaxValue) totalSamples = (int)granule;

                int segmentCount = data[pos + 26];
                int headerEnd = pos + 27 + segmentCount;
                if (headerEnd > data.Length) break;

                int bodySize = 0;
                for (int i = 0; i < segmentCount; i++) bodySize += data[pos + 27 + i];

                int cursor = headerEnd;
                int end = headerEnd + bodySize;
                if (end > data.Length) break;

                for (int i = 0; i < segmentCount; i++)
                {
                    int length = data[pos + 27 + i];

                    for (int b = 0; b < length; b++) pending.Add(data[cursor + b]);
                    cursor += length;

                    // A segment shorter than 255 bytes terminates the packet.
                    if (length < 255)
                    {
                        byte[] packet = pending.ToArray();
                        pending.Clear();

                        if (packetIndex == 0)
                        {
                            ParseOpusHead(packet, ref channels, ref preSkip);
                        }
                        else if (packetIndex > 1)
                        {
                            // Packet 0 is OpusHead and packet 1 is OpusTags.
                            packets.Add(packet);
                        }

                        packetIndex++;
                    }
                }

                pos = end;
            }

            return packets;
        }

        static void ParseOpusHead(byte[] header, ref int channels, ref int preSkip)
        {
            // "OpusHead", version, channel count, then a little-endian pre-skip.
            if (header.Length < 12) return;
            if (header[0] != 'O' || header[1] != 'p' || header[2] != 'u' || header[3] != 's' ||
                header[4] != 'H' || header[5] != 'e' || header[6] != 'a' || header[7] != 'd')
            {
                return;
            }

            int channelCount = header[9];
            if (channelCount > 0) channels = channelCount;

            preSkip = header[10] | (header[11] << 8);
        }
    }
}
