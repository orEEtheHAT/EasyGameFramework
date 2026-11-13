using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EasySave
{
    /// <summary>Desktop local-file backend. Every filesystem operation, including durable flush, runs on a
    /// worker. Same-root instances/processes use an exclusive OS handle. No delete-then-move fallback.
    /// This is not a WebGL, cloud, console or hostile-filesystem security backend.</summary>
    public sealed class LocalFileStorage : ISaveStorage
    {
        public string RootPath { get; }
        private static readonly SaveOptions StructuralLimits = new SaveOptions
        { MaximumFileBytes = 256 * 1024 * 1024, MaximumPayloadBytes = 256 * 1024 * 1024, MaximumParticipants = 65536 };

        public LocalFileStorage(string rootPath)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("LocalFileStorage is not supported on WebGL. Use a separately tested browser save integration.");
#else
            if (string.IsNullOrWhiteSpace(rootPath) || !Path.IsPathFullyQualified(rootPath)) throw new ArgumentException("Supply an absolute, application-owned save directory.");
            RootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(RootPath, Path.GetPathRoot(rootPath)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("A drive root is not a save directory.");
#endif
        }

        public Task<byte[]> ReadAsync(SaveAddress address, bool backup, int maximumBytes, CancellationToken cancellation) => Work(() =>
        {
            using var lease = Acquire(cancellation);
            var directory = SlotDirectory(address);
            var file = Path.Combine(directory, backup ? "backup.bin" : "save.bin");
            Guard(file);
            return ReadBounded(file, maximumBytes, cancellation);
        }, cancellation);

        public Task<StorageCommit> WriteAsync(SaveAddress address, byte[] data, CancellationToken cancellation)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            // The caller owns its bytes until this task completes. SaveService passes an immutable private buffer.
            return Work(() =>
            {
                using var lease = Acquire(cancellation);
                SaveFormat.Decode(data, address, StructuralLimits);
                var directory = SlotDirectory(address); Directory.CreateDirectory(directory); Guard(directory);
                var primary = Path.Combine(directory, "save.bin"); var backup = Path.Combine(directory, "backup.bin");
                var pending = Path.Combine(directory, "pending-" + Guid.NewGuid().ToString("N") + ".bin");
                Guard(primary); Guard(backup);
                try
                {
                    using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        for (int offset = 0; offset < data.Length;)
                        {
                            cancellation.ThrowIfCancellationRequested(); int count = Math.Min(65536, data.Length - offset);
                            stream.Write(data, offset, count); offset += count;
                        }
                        stream.Flush(true);
                    }
                    bool exists = File.Exists(primary), preservePrimary = false;
                    if (exists)
                    {
                        try { SaveFormat.Decode(ReadBounded(primary, StructuralLimits.MaximumFileBytes, cancellation), address, StructuralLimits); preservePrimary = true; }
                        catch (SaveStorageException e) when (e.Error == SaveError.CorruptData) { /* keep the last valid backup */ }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    // Commit boundary. From here a cancellation must not report that an acknowledged write was undone.
                    if (exists) File.Replace(pending, primary, preservePrimary ? backup : null);
                    else File.Move(pending, primary);
                    return new StorageCommit(true);
                }
                finally { if (File.Exists(pending)) { try { File.Delete(pending); } catch (IOException) { } catch (UnauthorizedAccessException) { } } }
            }, cancellation);
        }

        public Task<IReadOnlyList<string>> ListAsync(SaveScope scope, CancellationToken cancellation) => Work<IReadOnlyList<string>>(() =>
        {
            SaveNames.CheckScope(scope);
            using var lease = Acquire(cancellation);
            var directory = ScopeDirectory(scope); var slots = new List<string>();
            if (!Directory.Exists(directory)) return slots;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                cancellation.ThrowIfCancellationRequested(); var slot = Path.GetFileName(child);
                try { SaveNames.CheckSlot(slot); } catch (ArgumentException) { continue; }
                Guard(child);
                if (File.Exists(Path.Combine(child, "save.bin")) || File.Exists(Path.Combine(child, "backup.bin"))) slots.Add(slot);
                if (slots.Count > 4096) throw new SaveStorageException(SaveError.StorageError, "Local slot enumeration exceeds 4096 slots.");
            }
            slots.Sort(StringComparer.Ordinal); return slots;
        }, cancellation);

        public Task<StorageCommit> DeleteAsync(SaveAddress address, CancellationToken cancellation) => Work(() =>
        {
            using var lease = Acquire(cancellation);
            var directory = SlotDirectory(address);
            if (!Directory.Exists(directory)) return new StorageCommit(false);
            var removed = Path.Combine(ScopeDirectory(address.Scope), ".deleted-" + address.Slot + "-" + Guid.NewGuid().ToString("N"));
            cancellation.ThrowIfCancellationRequested();
            Directory.Move(directory, removed); // Commit: the complete slot (including backup) disappears together.
            try
            {
                foreach (var file in Directory.EnumerateFiles(removed))
                {
                    var name = Path.GetFileName(file);
                    if (name == "save.bin" || name == "backup.bin" || name.StartsWith("pending-", StringComparison.Ordinal) && name.EndsWith(".bin", StringComparison.Ordinal))
                    { Guard(file); File.Delete(file); }
                }
                Directory.Delete(removed, false);
                return new StorageCommit(true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SaveStorageException)
            { return new StorageCommit(true, "Slot deleted; deferred cleanup remains at " + removed + ": " + e.Message); }
        }, cancellation);

        private FileStream Acquire(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested(); Guard(RootPath); Directory.CreateDirectory(RootPath); Guard(RootPath);
            var path = Path.Combine(RootPath, ".easy-save.lock"); Guard(path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new SaveStorageException(SaveError.Busy, "Another storage operation owns this save directory. Retry after it completes."); }
        }
        private string ScopeDirectory(SaveScope scope)
        {
            SaveNames.CheckScope(scope);
            var path = Path.Combine(RootPath, scope == SaveScope.Progress ? "progress" : "preferences"); Guard(path); return path;
        }
        private string SlotDirectory(SaveAddress address)
        { SaveNames.CheckSlot(address.Slot); var path = Path.Combine(ScopeDirectory(address.Scope), address.Slot); Guard(path); return path; }
        private static void Guard(string path)
        {
            // Reject existing reparse/symlink components, including an existing file at the leaf.
            for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SaveStorageException(SaveError.StorageError, "Save paths may not traverse a symbolic link/reparse point: " + current);
        }
        private static byte[] ReadBounded(string file, int maximumBytes, CancellationToken cancellation)
        {
            if (maximumBytes < 1 || maximumBytes > 256 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (!File.Exists(file)) return null;
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > maximumBytes) throw SaveFormat.Corrupt("File exceeds configured size limit.");
            var data = new byte[(int)stream.Length]; int offset = 0;
            while (offset < data.Length)
            {
                cancellation.ThrowIfCancellationRequested(); int count = stream.Read(data, offset, Math.Min(65536, data.Length - offset));
                if (count == 0) throw SaveFormat.Corrupt("Save was truncated while reading."); offset += count;
            }
            return data;
        }
        private static Task<T> Work<T>(Func<T> work, CancellationToken cancellation)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromException<T>(new SaveStorageException(SaveError.StorageError, "LocalFileStorage is not supported on WebGL. Supply a tested browser backend."));
#else
            return Task.Run(work, cancellation);
#endif
        }
    }
}
