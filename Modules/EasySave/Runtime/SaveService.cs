using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasySave
{
    /// <summary>An explicitly owned session; no singleton or world discovery. Construct/use on Unity's main
    /// thread. One operation per session; overlapping requests return Busy rather than queue stale saves.
    /// Freeze gameplay in the application's composition while capturing/restoring a consistent world.</summary>
    public sealed class SaveService : IDisposable
    {
        private sealed class Participant
        {
            internal ISaveParticipant Value;
            internal string Key;
            internal SaveScope Scope;
            internal int Version, Order;
        }
        private readonly ISaveStorage storage;
        private readonly SaveOptions options;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly List<Participant> participants = new List<Participant>();
        private readonly Dictionary<(SaveScope, string, int), Func<string, string>> migrations = new Dictionary<(SaveScope, string, int), Func<string, string>>();
        private CancellationTokenSource operation;
        private bool disposed;
        public bool IsBusy { get; private set; }
        public bool IsDisposed => disposed;

        public SaveService(ISaveStorage storage, SaveOptions options = null)
        {
            if (SynchronizationContext.Current == null) throw new InvalidOperationException("Create SaveService on Unity's main thread with its SynchronizationContext.");
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.options = (options ?? new SaveOptions()).Copy();
        }
        public void Register(ISaveParticipant participant)
        { RegisterAll(participant); }

        /// <summary>Registers a complete set or nothing. Dispose the returned handle to unregister only this set.</summary>
        public IDisposable RegisterAll(params ISaveParticipant[] additions)
        {
            CheckOwner(); if (disposed) throw new ObjectDisposedException(nameof(SaveService));
            if (IsBusy) throw new InvalidOperationException("Register participants between save operations.");
            if (additions == null) throw new ArgumentNullException(nameof(additions));
            var prepared = new List<Participant>(additions.Length);
            var keys = new HashSet<(SaveScope, string)>();
            int progress = 0, preferences = 0;
            foreach (var registered in participants)
            {
                keys.Add((registered.Scope, registered.Key));
                if (registered.Scope == SaveScope.Progress) progress++; else preferences++;
            }
            foreach (var participant in additions)
            {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            var entry = new Participant { Value = participant, Key = participant.Key, Scope = participant.Scope, Version = participant.Version, Order = participant.RestoreOrder };
            SaveNames.CheckKey(entry.Key); SaveNames.CheckScope(entry.Scope);
            if (entry.Version < 1) throw new ArgumentException("Payload version must be positive.");
            if (!keys.Add((entry.Scope, entry.Key))) throw new ArgumentException("Duplicate participant: " + entry.Scope + "/" + entry.Key);
            if (entry.Scope == SaveScope.Progress) progress++; else preferences++;
            if (progress > options.MaximumParticipants || preferences > options.MaximumParticipants) throw new ArgumentException("Participant registration budget exceeded.");
            prepared.Add(entry);
            }
            participants.AddRange(prepared);
            return new Registration(this, prepared);
        }
        private sealed class Registration : IDisposable
        {
            private SaveService service;
            private readonly List<Participant> entries;
            internal Registration(SaveService service, List<Participant> entries) { this.service = service; this.entries = entries; }
            public void Dispose()
            {
                if (service == null) return;
                service.CheckOwner(); var owner = service; service = null;
                bool removed = false;
                foreach (var entry in entries) removed |= owner.participants.Remove(entry);
                if (removed) owner.operation?.Cancel();
            }
        }
        /// <summary>Unregistering during an operation requests cancellation before any further callbacks.</summary>
        public bool Unregister(ISaveParticipant participant)
        {
            CheckOwner(); int index = participants.FindIndex(p => ReferenceEquals(p.Value, participant));
            if (index < 0) return false;
            participants.RemoveAt(index); operation?.Cancel(); return true;
        }
        /// <summary>Registers a pure migration from N to N+1. Does not modify the source file.</summary>
        public void RegisterMigration(SaveScope scope, string key, int fromVersion, Func<string, string> migrate)
        {
            CheckOwner(); if (disposed) throw new ObjectDisposedException(nameof(SaveService));
            if (IsBusy) throw new InvalidOperationException("Register migrations between save operations.");
            SaveNames.CheckScope(scope); SaveNames.CheckKey(key);
            if (fromVersion < 1 || fromVersion == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(fromVersion));
            migrations.Add((scope, key, fromVersion), migrate ?? throw new ArgumentNullException(nameof(migrate)));
        }
        public Task<SaveResult> QuickSaveAsync(CancellationToken cancellation = default) => SaveAsync(options.QuickSlot, cancellation: cancellation);
        public Task<SaveResult> QuickLoadAsync(CancellationToken cancellation = default) => LoadAsync(options.QuickSlot, cancellation: cancellation);
        public Task<SaveResult> SavePreferencesAsync(CancellationToken cancellation = default) => SaveAsync(options.PreferencesSlot, SaveScope.Preferences, cancellation: cancellation);
        public Task<SaveResult> LoadPreferencesAsync(CancellationToken cancellation = default) => LoadAsync(options.PreferencesSlot, SaveScope.Preferences, cancellation);

        public Task<SaveResult> SaveAsync(string slot, SaveScope scope = SaveScope.Progress, string displayName = null, CancellationToken cancellation = default) => Run(async (token, result) =>
        {
            result.Error = SaveError.InvalidRequest;
            var address = new SaveAddress(scope, slot);
            if (displayName != null && Encoding.UTF8.GetByteCount(displayName) > 512) throw new ArgumentException("Display name exceeds 512 UTF-8 bytes.");
            var document = CaptureDocument(address, displayName, token, result);
            var timer = Stopwatch.StartNew();
            token.ThrowIfCancellationRequested();
            var data = await Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                try { return SaveFormat.Encode(document, options); }
                finally { result.Timings.SerializationMilliseconds = clock.Elapsed.TotalMilliseconds; }
            }, token);
            CheckOwner(); result.Timings.Bytes = data.Length; result.Error = SaveError.StorageError;
            timer.Restart();
            try
            {
                var commit = await storage.WriteAsync(address, data, token);
                result.Committed = commit.Committed; result.Message = commit.Warning;
                if (!commit.Committed) throw new SaveStorageException(SaveError.StorageError, "Backend did not acknowledge the save commit.");
            }
            finally { result.Timings.StorageMilliseconds = timer.Elapsed.TotalMilliseconds; }
        }, cancellation);

        public Task<SaveResult> LoadAsync(string slot, SaveScope scope = SaveScope.Progress, CancellationToken cancellation = default) => Run(async (token, result) =>
        {
            result.Error = SaveError.InvalidRequest; var address = new SaveAddress(scope, slot);
            result.Error = SaveError.StorageError; var document = await ReadDocument(address, token, result);
            await RestoreDocument(document, token, result);
        }, cancellation);

        public Task<SaveResult> ListAsync(SaveScope scope = SaveScope.Progress, CancellationToken cancellation = default) => Run(async (token, result) =>
        {
            result.Error = SaveError.InvalidRequest; SaveNames.CheckScope(scope);
            result.Error = SaveError.StorageError;
            var names = await storage.ListAsync(scope, token); CheckOwner();
            if (names == null || names.Count > 4096) throw new SaveStorageException(SaveError.StorageError, "Backend returned an invalid slot list.");
            var slots = new List<SaveSlotInfo>(); var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                token.ThrowIfCancellationRequested(); var address = new SaveAddress(scope, name);
                if (!unique.Add(name)) throw new SaveStorageException(SaveError.StorageError, "Backend returned duplicate slots.");
                var info = new SaveSlotInfo { Address = address }; var readResult = new SaveResult();
                try
                {
                    var document = await ReadDocument(address, token, readResult);
                    info.DisplayName = document.DisplayName; info.SavedUtc = new DateTime(document.UtcTicks, DateTimeKind.Utc);
                    info.ParticipantCount = document.Entries.Count; info.RecoveredFromBackup = readResult.RecoveredFromBackup;
                }
                catch (SaveStorageException e) when (e.Error != SaveError.Busy) { info.Error = e.Error; info.Message = e.Message; }
                slots.Add(info);
            }
            slots.Sort((a, b) => { int order = b.SavedUtc.CompareTo(a.SavedUtc); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Address.Slot, b.Address.Slot); });
            result.Slots = slots.AsReadOnly();
        }, cancellation);

        public Task<SaveResult> DeleteAsync(string slot, SaveScope scope = SaveScope.Progress, CancellationToken cancellation = default) => Run(async (token, result) =>
        {
            result.Error = SaveError.InvalidRequest; var address = new SaveAddress(scope, slot);
            result.Error = SaveError.StorageError; var commit = await storage.DeleteAsync(address, token);
            result.Committed = commit.Committed; result.Message = commit.Warning;
        }, cancellation);

        public SaveSnapshot CaptureSnapshot(SaveScope scope = SaveScope.Progress)
        {
            CheckOwner();
            if (disposed) throw new ObjectDisposedException(nameof(SaveService));
            if (IsBusy) throw new InvalidOperationException("A save operation is already running.");
            var address = new SaveAddress(scope, "memory");
            IsBusy = true; operation = new CancellationTokenSource();
            try { return new SaveSnapshot(CaptureDocument(address, "Memory snapshot", operation.Token, new SaveResult())); }
            finally { operation.Dispose(); operation = null; IsBusy = false; }
        }
        public Task<SaveResult> RestoreSnapshotAsync(SaveSnapshot snapshot, CancellationToken cancellation = default) => Run(async (token, result) =>
        {
            result.Error = SaveError.InvalidRequest;
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            await RestoreDocument(snapshot.Document, token, result);
        }, cancellation);
        private SaveDocument CaptureDocument(SaveAddress address, string displayName, CancellationToken token, SaveResult result)
        {
            var selected = Select(address.Scope);
            var document = new SaveDocument { Address = address, DisplayName = displayName ?? address.Slot, UtcTicks = DateTime.UtcNow.Ticks };
            result.Error = SaveError.CaptureFailed;
            var timer = Stopwatch.StartNew();
            long capturedBytes = 0;
            try
            {
                foreach (var participant in selected)
                {
                    token.ThrowIfCancellationRequested(); CheckOwner();
                    result.FailedParticipant = participant.Key;
                    string payload = participant.Value.Capture();
                    capturedBytes += CheckPayload(payload);
                    if (capturedBytes > options.MaximumFileBytes) throw new ArgumentException("Combined participant payloads exceed the file budget.");
                    document.Entries.Add(new SaveEntry { Key = participant.Key, Version = participant.Version, Payload = payload });
                }
            }
            finally { result.Timings.CaptureMilliseconds = timer.Elapsed.TotalMilliseconds; }
            result.Timings.Participants = selected.Count; result.FailedParticipant = null;
            token.ThrowIfCancellationRequested();
            return document;
        }

        private async Task RestoreDocument(SaveDocument document, CancellationToken token, SaveResult result)
        {
            var scope = document.Address.Scope;
            CheckOwner(); token.ThrowIfCancellationRequested();
            result.Error = SaveError.ValidationFailed;
            var selected = Select(scope); var plan = new List<(Participant participant, string payload)>();
            var timer = Stopwatch.StartNew();
            long preparedBytes = 0;
            try
            {
                if (document.Entries.Count != selected.Count) throw new SaveStorageException(SaveError.IncompatibleParticipants, "The saved participant set differs from the registered set.");
                var saved = new Dictionary<string, SaveEntry>(StringComparer.Ordinal);
                foreach (var entry in document.Entries) saved.Add(entry.Key, entry);
                foreach (var participant in selected)
                {
                    token.ThrowIfCancellationRequested(); result.FailedParticipant = participant.Key;
                    if (!saved.TryGetValue(participant.Key, out var entry)) throw new SaveStorageException(SaveError.IncompatibleParticipants, "Missing participant: " + participant.Key);
                    string payload = entry.Payload; int version = entry.Version, steps = 0;
                    if (version > participant.Version) throw new SaveStorageException(SaveError.UnsupportedVersion, "Newer payload version for " + participant.Key);
                    while (version < participant.Version)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++steps > 256 || !migrations.TryGetValue((scope, participant.Key, version), out var migrate))
                            throw new SaveStorageException(SaveError.UnsupportedVersion, "Missing/bounded migration for " + participant.Key + " v" + version);
                        payload = migrate(payload); CheckPayload(payload); version++;
                    }
                    preparedBytes += CheckPayload(payload);
                    if (preparedBytes > options.MaximumFileBytes) throw new ArgumentException("Combined migrated payloads exceed the file budget.");
                    var check = participant.Value.Validate(payload);
                    if (!check.IsValid) throw new SaveStorageException(SaveError.ValidationFailed, check.Message);
                    plan.Add((participant, payload));
                }
            }
            finally { result.Timings.ValidationMilliseconds = timer.Elapsed.TotalMilliseconds; }
            result.Timings.Participants = plan.Count; result.FailedParticipant = null;
            token.ThrowIfCancellationRequested();
            var restored = new List<string>(); result.RestoredParticipants = restored.AsReadOnly();
            timer.Restart();
            try
            {
                foreach (var step in plan)
                {
                    token.ThrowIfCancellationRequested(); CheckOwner();
                    result.FailedParticipant = step.participant.Key;
                    result.WorldMayHaveChanged = true;
                    await step.participant.Value.RestoreAsync(step.payload, token);
                    CheckOwner(); restored.Add(step.participant.Key); result.FailedParticipant = null;
                }
            }
            finally { result.Timings.RestoreMilliseconds = timer.Elapsed.TotalMilliseconds; }
        }

        private async Task<SaveDocument> ReadDocument(SaveAddress address, CancellationToken token, SaveResult result)
        {
            try { return await ReadCopy(address, false, token, result); }
            catch (SaveStorageException primary) when (options.RecoverBackup && (primary.Error == SaveError.CorruptData || primary.Error == SaveError.NotFound))
            {
                try
                {
                    var document = await ReadCopy(address, true, token, result);
                    result.RecoveredFromBackup = true; result.Message = "Read backup; primary was not rewritten. " + primary.Message; return document;
                }
                catch (SaveStorageException backup) when (backup.Error == SaveError.NotFound) { throw primary; }
            }
        }
        private async Task<SaveDocument> ReadCopy(SaveAddress address, bool backup, CancellationToken token, SaveResult result)
        {
            var timer = Stopwatch.StartNew(); byte[] data;
            try { data = await storage.ReadAsync(address, backup, options.MaximumFileBytes, token); }
            finally { result.Timings.StorageMilliseconds += timer.Elapsed.TotalMilliseconds; }
            CheckOwner(); token.ThrowIfCancellationRequested();
            if (data == null) throw new SaveStorageException(SaveError.NotFound, "No save at " + address + (backup ? " (backup)." : "."));
            result.Timings.Bytes = data.Length;
            return await Task.Run(() =>
            {
                var clock = Stopwatch.StartNew();
                try { return SaveFormat.Decode(data, address, options); }
                finally { result.Timings.SerializationMilliseconds += clock.Elapsed.TotalMilliseconds; }
            }, token);
        }
        private List<Participant> Select(SaveScope scope)
        {
            var selected = participants.FindAll(p => p.Scope == scope);
            if (selected.Count > options.MaximumParticipants) throw new ArgumentException("Participant budget exceeded.");
            selected.Sort((a, b) => { int order = a.Order.CompareTo(b.Order); return order != 0 ? order : StringComparer.Ordinal.Compare(a.Key, b.Key); }); return selected;
        }
        private int CheckPayload(string payload)
        {
            if (payload == null) throw new ArgumentException("Missing participant payload.");
            int bytes = Encoding.UTF8.GetByteCount(payload);
            if (bytes > options.MaximumPayloadBytes) throw new ArgumentException("Oversized participant payload.");
            return bytes;
        }
        private async Task<SaveResult> Run(Func<CancellationToken, SaveResult, Task> action, CancellationToken cancellation)
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) return SaveResult.Fail(SaveError.WrongThread, "Call SaveService on its owning Unity main thread.");
            if (disposed) return SaveResult.Fail(SaveError.Disposed, "The save session has been disposed.");
            if (IsBusy) return SaveResult.Fail(SaveError.Busy, "A save operation is already running.");
            var result = new SaveResult(); IsBusy = true;
            operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            try
            {
                operation.Token.ThrowIfCancellationRequested();
                await action(operation.Token, result);
                CheckOwner(); result.Error = SaveError.None;
            }
            catch (Exception exception)
            {
                result.Message = exception.Message;
                if (result.WorldMayHaveChanged) result.Error = SaveError.PartialRestore;
                else if (exception is OperationCanceledException) result.Error = SaveError.Cancelled;
                else if (exception is SaveStorageException known) result.Error = known.Error;
                else if (result.Error == SaveError.None) result.Error = SaveError.StorageError;
            }
            finally { operation.Dispose(); operation = null; IsBusy = false; }
            return result;
        }
        private void CheckOwner()
        { if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new SaveStorageException(SaveError.WrongThread, "A continuation left the service's owning thread."); }
        public void CancelCurrent() { CheckOwner(); operation?.Cancel(); }
        public void Dispose()
        {
            CheckOwner(); if (disposed) return; disposed = true; operation?.Cancel(); participants.Clear(); migrations.Clear();
        }
    }
}
