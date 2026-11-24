using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EasySave
{
    internal sealed class SaveDocument
    {
        internal SaveAddress Address;
        internal string DisplayName;
        internal long UtcTicks;
        internal readonly List<SaveEntry> Entries = new List<SaveEntry>();
    }
    internal sealed class SaveEntry { internal string Key, Payload; internal int Version; }

    // No type names, reflection, object instantiation or Unity API. Little-endian integers, bounded UTF-8 strings.
    internal static class SaveFormat
    {
        internal const int HeaderBytes = 48;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("EASYSAVE");
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static byte[] Encode(SaveDocument document, SaveOptions options)
        {
            using var body = new MemoryStream();
            using (var writer = new BinaryWriter(body, Utf8, true))
            {
                writer.Write((int)document.Address.Scope);
                Write(writer, document.Address.Slot, 64);
                Write(writer, document.DisplayName ?? "", 512);
                writer.Write(document.UtcTicks);
                if (document.Entries.Count > options.MaximumParticipants) throw Corrupt("Participant budget exceeded.");
                writer.Write(document.Entries.Count);
                foreach (var entry in document.Entries)
                {
                    Write(writer, entry.Key, 128); writer.Write(entry.Version);
                    Write(writer, entry.Payload, options.MaximumPayloadBytes);
                    if (body.Length + HeaderBytes > options.MaximumFileBytes) throw Corrupt("File budget exceeded.");
                }
            }
            var payload = body.ToArray();
            using var hash = SHA256.Create();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Utf8, true))
            { writer.Write(Magic); writer.Write(1); writer.Write(payload.Length); writer.Write(hash.ComputeHash(payload)); writer.Write(payload); }
            if (stream.Length > options.MaximumFileBytes) throw Corrupt("File budget exceeded.");
            return stream.ToArray();
        }
        internal static SaveDocument Decode(byte[] data, SaveAddress expected, SaveOptions options) => DecodeCore(data, expected, options);
        internal static SaveDocument Inspect(byte[] data, SaveOptions options) => DecodeCore(data, null, options);
        private static SaveDocument DecodeCore(byte[] data, SaveAddress? expected, SaveOptions options)
        {
            try
            {
                if (data == null || data.Length < HeaderBytes || data.Length > options.MaximumFileBytes) throw Corrupt("Truncated or oversized save.");
                using var stream = new MemoryStream(data, false);
                using var reader = new BinaryReader(stream, Utf8, true);
                for (int i = 0; i < Magic.Length; i++) if (reader.ReadByte() != Magic[i]) throw Corrupt("Invalid save header.");
                if (reader.ReadInt32() != 1) throw new SaveStorageException(SaveError.UnsupportedVersion, "Unsupported save container version; use the matching game version or an explicit importer.");
                if (reader.ReadInt32() != data.Length - HeaderBytes) throw Corrupt("Invalid save length.");
                var expectedHash = reader.ReadBytes(32);
                using (var hash = SHA256.Create())
                {
                    var actual = hash.ComputeHash(data, HeaderBytes, data.Length - HeaderBytes);
                    int difference = 0; for (int i = 0; i < 32; i++) difference |= expectedHash[i] ^ actual[i];
                    if (difference != 0) throw Corrupt("Save checksum mismatch.");
                }
                var scope = (SaveScope)reader.ReadInt32();
                var slot = Read(reader, 64);
                var address = new SaveAddress(scope, slot);
                if (expected.HasValue && (scope != expected.Value.Scope || slot != expected.Value.Slot)) throw Corrupt("Save belongs to a different scope or slot.");
                var document = new SaveDocument { Address = address, DisplayName = Read(reader, 512), UtcTicks = reader.ReadInt64() };
                if (document.UtcTicks < DateTime.MinValue.Ticks || document.UtcTicks > DateTime.MaxValue.Ticks) throw Corrupt("Invalid UTC timestamp.");
                int count = reader.ReadInt32();
                if (count < 0 || count > options.MaximumParticipants || count > (stream.Length - stream.Position) / 12) throw Corrupt("Invalid participant count.");
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    var key = Read(reader, 128); SaveNames.CheckKey(key);
                    int version = reader.ReadInt32();
                    if (!keys.Add(key) || version < 1) throw Corrupt("Duplicate participant key or invalid payload version.");
                    document.Entries.Add(new SaveEntry { Key = key, Version = version, Payload = Read(reader, options.MaximumPayloadBytes) });
                }
                if (stream.Position != stream.Length) throw Corrupt("Unexpected trailing data.");
                return document;
            }
            catch (SaveStorageException) { throw; }
            catch (Exception e) when (e is EndOfStreamException || e is ArgumentException || e is OverflowException)
            { throw Corrupt("Malformed save: " + e.Message); }
        }
        private static void Write(BinaryWriter writer, string value, int maximum)
        {
            if (value == null || Utf8.GetByteCount(value) > maximum) throw Corrupt("Missing or oversized string payload.");
            var bytes = Utf8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string Read(BinaryReader reader, int maximum)
        {
            int size = reader.ReadInt32();
            if (size < 0 || size > maximum || size > reader.BaseStream.Length - reader.BaseStream.Position) throw Corrupt("Invalid string length.");
            return Utf8.GetString(reader.ReadBytes(size));
        }
        internal static SaveStorageException Corrupt(string message) => new SaveStorageException(SaveError.CorruptData, message);
    }
}
