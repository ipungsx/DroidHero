using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DroidHero.Chart
{
    /// <summary>
    /// Reader for the .sng container used by enchor.us / Clone Hero.
    ///
    /// Layout: magic "SNGPKG", uint32 version, 16-byte XOR key, then two
    /// length-prefixed blocks (metadata, file listings) followed by the file payloads.
    /// Every payload byte is XORed with a 256-byte mask derived from the key:
    /// mask[i] = key[i % 16] ^ i.
    /// </summary>
    public sealed class SngReader : IDisposable
    {
        const int KeyLength = 16;
        const int MaskLength = 256;

        readonly Stream _stream;
        readonly byte[] _mask = new byte[MaskLength];
        readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        struct Entry
        {
            public long Length;
            public long Position;
        }

        public IReadOnlyDictionary<string, string> Metadata { get; private set; }

        public IEnumerable<string> FileNames => _entries.Keys;

        SngReader(Stream stream)
        {
            _stream = stream;
        }

        public static SngReader Open(string path)
        {
            var reader = new SngReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            try
            {
                reader.ReadHeader();
                return reader;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        public static SngReader Open(byte[] data)
        {
            var reader = new SngReader(new MemoryStream(data, false));
            try
            {
                reader.ReadHeader();
                return reader;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        void ReadHeader()
        {
            var magic = ReadBytes(6);
            if (magic[0] != (byte)'S' || magic[1] != (byte)'N' || magic[2] != (byte)'G' ||
                magic[3] != (byte)'P' || magic[4] != (byte)'K' || magic[5] != (byte)'G')
            {
                throw new InvalidDataException("Not an SNG package (bad magic).");
            }

            ReadUInt32(); // container version, currently always 1

            var keys = ReadBytes(KeyLength);
            for (int i = 0; i < MaskLength; i++)
            {
                _mask[i] = (byte)(keys[i % KeyLength] ^ i);
            }

            long metadataEnd = ReadBlock(out long metadataCount);
            Metadata = ReadMetadata(metadataEnd, metadataCount);

            long listingsEnd = ReadBlock(out long fileCount);
            ReadListings(listingsEnd, fileCount);
        }

        /// <summary>Reads a block header and returns the absolute offset where its payload ends.</summary>
        long ReadBlock(out long count)
        {
            long length = ReadInt64();
            count = (long)ReadUInt64();
            long payloadStart = _stream.Position;
            return payloadStart + length - 8;
        }

        Dictionary<string, string> ReadMetadata(long end, long expectedCount)
        {
            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            while (_stream.Position < end)
            {
                int keyLength = ReadInt32();
                string key = Encoding.UTF8.GetString(ReadBytes(keyLength));
                int valueLength = ReadInt32();
                string value = Encoding.UTF8.GetString(ReadBytes(valueLength));
                meta[key] = value;
            }

            if (meta.Count != expectedCount)
            {
                throw new InvalidDataException(
                    "SNG metadata count mismatch: read " + meta.Count + " of " + expectedCount + ".");
            }

            return meta;
        }

        void ReadListings(long end, long expectedCount)
        {
            while (_stream.Position < end)
            {
                int nameLength = ReadByte();
                string name = Encoding.UTF8.GetString(ReadBytes(nameLength));
                long length = ReadInt64();
                long position = ReadInt64();
                _entries[name] = new Entry { Length = length, Position = position };
            }

            if (_entries.Count != expectedCount)
            {
                throw new InvalidDataException(
                    "SNG file count mismatch: read " + _entries.Count + " of " + expectedCount + ".");
            }
        }

        public bool Contains(string name) => _entries.ContainsKey(name);

        /// <summary>Returns the decrypted contents of a packed file, or null when absent.</summary>
        public byte[] ReadFile(string name)
        {
            if (!_entries.TryGetValue(name, out Entry entry)) return null;

            var data = new byte[entry.Length];
            _stream.Position = entry.Position;

            int read = 0;
            while (read < data.Length)
            {
                int chunk = _stream.Read(data, read, data.Length - read);
                if (chunk <= 0) throw new EndOfStreamException("Unexpected end of SNG payload.");
                read += chunk;
            }

            for (int i = 0; i < data.Length; i++)
            {
                data[i] ^= _mask[i % MaskLength];
            }

            return data;
        }

        /// <summary>Returns the first packed file whose name ends with the given extension.</summary>
        public byte[] ReadFirstWithExtension(string extension)
        {
            foreach (var pair in _entries)
            {
                if (pair.Key.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return ReadFile(pair.Key);
                }
            }
            return null;
        }

        public string ReadText(string name)
        {
            var bytes = ReadFile(name);
            if (bytes == null) return null;

            // Charts are commonly UTF-8 with a BOM.
            int start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        }

        byte[] ReadBytes(int count)
        {
            var buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int chunk = _stream.Read(buffer, read, count - read);
                if (chunk <= 0) throw new EndOfStreamException("Unexpected end of SNG stream.");
                read += chunk;
            }
            return buffer;
        }

        byte ReadByte()
        {
            int value = _stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("Unexpected end of SNG stream.");
            return (byte)value;
        }

        int ReadInt32()
        {
            var b = ReadBytes(4);
            return b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
        }

        uint ReadUInt32()
        {
            var b = ReadBytes(4);
            return (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
        }

        long ReadInt64()
        {
            var b = ReadBytes(8);
            return (long)ReadUInt64(b);
        }

        ulong ReadUInt64()
        {
            return ReadUInt64(ReadBytes(8));
        }

        static ulong ReadUInt64(byte[] b)
        {
            ulong value = 0;
            for (int i = 7; i >= 0; i--)
            {
                value = (value << 8) | b[i];
            }
            return value;
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }
    }
}
