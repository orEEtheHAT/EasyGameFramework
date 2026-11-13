using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasySave.Tests
{
    public sealed class SaveTests
    {
        [Serializable] public sealed class State { public int Value; public string Text; }
        private sealed class MemoryStorage : ISaveStorage
        {
            internal readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
            internal TaskCompletionSource<bool> Gate;
            internal bool WriteStarted;
            internal Action AfterCommit;
            public Task<byte[]> ReadAsync(SaveAddress address, bool backup, int maximumBytes, CancellationToken cancellation)
            { cancellation.ThrowIfCancellationRequested(); Data.TryGetValue(address + (backup ? ".bak" : ""), out var data); return Task.FromResult(data); }
            public async Task<StorageCommit> WriteAsync(SaveAddress address, byte[] data, CancellationToken cancellation)
            {
                WriteStarted = true;
                if (Gate != null) using (cancellation.Register(() => Gate.TrySetCanceled())) await Gate.Task;
                cancellation.ThrowIfCancellationRequested();
                Data[address.ToString()] = (byte[])data.Clone(); AfterCommit?.Invoke(); return new StorageCommit(true);
            }
            public Task<IReadOnlyList<string>> ListAsync(SaveScope scope, CancellationToken cancellation) => Task.FromResult<IReadOnlyList<string>>(Data.Keys.Where(k => k.StartsWith(scope + "/") && !k.EndsWith(".bak")).Select(k => k.Substring(k.IndexOf('/') + 1)).ToArray());
            public Task<StorageCommit> DeleteAsync(SaveAddress address, CancellationToken cancellation) => Task.FromResult(new StorageCommit(Data.Remove(address.ToString())));
        }
        private readonly List<SaveService> sessions = new List<SaveService>();
        private string temporary;
        private MemoryStorage memory;
        private int value, restores, mainThread;
        [SetUp] public void Setup() { memory = new MemoryStorage(); value = 7; restores = 0; mainThread = Thread.CurrentThread.ManagedThreadId; temporary = Path.Combine(Path.GetTempPath(), "EasySaveTests-" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void Cleanup() { foreach (var session in sessions) session.Dispose(); sessions.Clear(); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        private SaveService Session(ISaveStorage backend = null, SaveOptions options = null) { var session = new SaveService(backend ?? memory, options); sessions.Add(session); return session; }
        private JsonSaveParticipant<State> Participant(string key = "player", SaveScope scope = SaveScope.Progress, int version = 1,
            Func<State, SaveValidation> validate = null, Action<State> restore = null, int order = 0) =>
            new JsonSaveParticipant<State>(key, scope, version,
                () => { Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId); return new State { Value = value }; },
                validate ?? (s => { Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId); return s.Value >= 0 ? SaveValidation.Valid : SaveValidation.Invalid("Negative value"); }),
                restore ?? (s => { Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId); value = s.Value; restores++; }), order);
        private static IEnumerator Check(Func<Task> body)
        {
            var task = body(); var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Async test timed out."); task.GetAwaiter().GetResult();
        }
        private static void Ok(SaveResult result) => Assert.IsTrue(result.Success, result.ToString());
        private static async Task Started(MemoryStorage store) { while (!store.WriteStarted) await Task.Yield(); }

        [UnityTest] public IEnumerator RoundTripAndRepeatedRestore() => Check(async () =>
        {
            var session = Session(); session.Register(Participant()); Ok(await session.SaveAsync("slot")); value = 99;
            Ok(await session.LoadAsync("slot")); Assert.AreEqual(7, value);
            Ok(await session.LoadAsync("slot")); Assert.AreEqual(7, value); Assert.AreEqual(2, restores);
        });
        [UnityTest] public IEnumerator ProgressNeverAppliesPreferences() => Check(async () =>
        {
            int preference = 10; var session = Session(); session.Register(Participant());
            session.Register(new JsonSaveParticipant<State>("player", SaveScope.Preferences, 1, () => new State { Value = preference }, _ => SaveValidation.Valid, s => preference = s.Value));
            Ok(await session.SaveAsync("user")); Ok(await session.SavePreferencesAsync()); value = 88; preference = 55;
            Ok(await session.LoadAsync("user")); Assert.AreEqual(7, value); Assert.AreEqual(55, preference);
            Ok(await session.LoadPreferencesAsync()); Assert.AreEqual(10, preference); Assert.AreEqual(7, value);
        });
        [UnityTest] public IEnumerator UnknownOrMissingParticipantsRejectBeforeMutation() => Check(async () =>
        {
            var writer = Session(); writer.Register(Participant()); Ok(await writer.SaveAsync("slot"));
            var reader = Session(); reader.Register(Participant("other"));
            Assert.AreEqual(SaveError.IncompatibleParticipants, (await reader.LoadAsync("slot")).Error); Assert.Zero(restores);
            reader.Register(Participant()); Assert.AreEqual(SaveError.IncompatibleParticipants, (await reader.LoadAsync("slot")).Error); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator NewerPayloadAndMissingMigrationRejectBeforeMutation() => Check(async () =>
        {
            var writer = Session(); writer.Register(Participant(version: 2)); Ok(await writer.SaveAsync("slot"));
            var reader = Session(); reader.Register(Participant()); Assert.AreEqual(SaveError.UnsupportedVersion, (await reader.LoadAsync("slot")).Error);
            var newer = Session(); newer.Register(Participant(version: 3)); Assert.AreEqual(SaveError.UnsupportedVersion, (await newer.LoadAsync("slot")).Error); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator MigrationChainDoesNotRewriteSource() => Check(async () =>
        {
            var writer = Session(); writer.Register(Participant()); Ok(await writer.SaveAsync("slot")); byte[] original = (byte[])memory.Data["Progress/slot"].Clone();
            var reader = Session(); reader.Register(Participant(version: 3));
            reader.RegisterMigration(SaveScope.Progress, "player", 1, json => { var s = JsonUtility.FromJson<State>(json); s.Value += 3; return JsonUtility.ToJson(s); });
            reader.RegisterMigration(SaveScope.Progress, "player", 2, json => { var s = JsonUtility.FromJson<State>(json); s.Value *= 2; return JsonUtility.ToJson(s); });
            Ok(await reader.LoadAsync("slot")); Assert.AreEqual(20, value); CollectionAssert.AreEqual(original, memory.Data["Progress/slot"]);
        });
        [UnityTest] public IEnumerator LastValidatorRejectsBeforeAnyRestore() => Check(async () =>
        {
            var session = Session(); session.Register(Participant("first")); session.Register(Participant("last", validate: _ => SaveValidation.Invalid("Wrong level"), order: 10));
            Ok(await session.SaveAsync("slot")); var result = await session.LoadAsync("slot");
            Assert.AreEqual(SaveError.ValidationFailed, result.Error); Assert.IsFalse(result.WorldMayHaveChanged); Assert.Zero(restores); Assert.AreEqual("last", result.FailedParticipant);
        });
        [UnityTest] public IEnumerator RestoreOrderAndPartialFailureAreReported() => Check(async () =>
        {
            var applied = new List<string>(); var session = Session();
            session.Register(Participant("z", restore: _ => { applied.Add("z"); throw new InvalidOperationException("External callback failed"); }, order: 10));
            session.Register(Participant("b", restore: _ => applied.Add("b")));
            session.Register(Participant("a", restore: _ => applied.Add("a")));
            Ok(await session.SaveAsync("slot")); var result = await session.LoadAsync("slot");
            Assert.AreEqual(SaveError.PartialRestore, result.Error); Assert.IsTrue(result.WorldMayHaveChanged);
            CollectionAssert.AreEqual(new[] { "a", "b" }, result.RestoredParticipants); CollectionAssert.AreEqual(new[] { "a", "b", "z" }, applied); Assert.AreEqual("z", result.FailedParticipant);
        });
        [UnityTest] public IEnumerator ConcurrentRequestsReturnBusyAndCancelBeforeCommit() => Check(async () =>
        {
            memory.Gate = new TaskCompletionSource<bool>(); var session = Session(); session.Register(Participant());
            using var cancellation = new CancellationTokenSource(); var pending = session.SaveAsync("slot", cancellation: cancellation.Token); await Started(memory);
            Assert.AreEqual(SaveError.Busy, (await session.SaveAsync("other")).Error); Assert.AreEqual(SaveError.Busy, (await session.LoadAsync("slot")).Error);
            Assert.AreEqual(SaveError.Busy, (await session.DeleteAsync("slot")).Error); cancellation.Cancel();
            var result = await pending; Assert.AreEqual(SaveError.Cancelled, result.Error); Assert.IsFalse(result.Committed); Assert.IsEmpty(memory.Data); Assert.IsFalse(session.IsBusy);
        });
        [UnityTest] public IEnumerator CancellationAfterCommitReportsSuccess() => Check(async () =>
        {
            using var cancellation = new CancellationTokenSource(); memory.AfterCommit = cancellation.Cancel;
            var session = Session(); session.Register(Participant()); var result = await session.SaveAsync("slot", cancellation: cancellation.Token);
            Ok(result); Assert.IsTrue(result.Committed); Assert.IsTrue(cancellation.IsCancellationRequested); Assert.IsTrue(memory.Data.ContainsKey("Progress/slot"));
        });
        [UnityTest] public IEnumerator PreCancelledRequestDoesNotCapture() => Check(async () =>
        {
            bool captured = false; var session = Session(); session.Register(new JsonSaveParticipant<State>("x", SaveScope.Progress, 1, () => { captured = true; return new State(); }, _ => SaveValidation.Valid, _ => { }));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); Assert.AreEqual(SaveError.Cancelled, (await session.SaveAsync("slot", cancellation: cancellation.Token)).Error); Assert.IsFalse(captured);
        });
        [UnityTest] public IEnumerator DisposeAndUnregisterCancelPendingOperation() => Check(async () =>
        {
            memory.Gate = new TaskCompletionSource<bool>(); var session = Session(); var participant = Participant(); session.Register(participant);
            var task = session.SaveAsync("slot"); await Started(memory); Assert.IsTrue(session.Unregister(participant)); Assert.AreEqual(SaveError.Cancelled, (await task).Error);
            session.Dispose(); Assert.AreEqual(SaveError.Disposed, (await session.LoadAsync("slot")).Error); Assert.IsEmpty(memory.Data);
        });
        [UnityTest] public IEnumerator CancelDuringRestoreStopsLaterParticipants() => Check(async () =>
        {
            var session = Session(); using var cancellation = new CancellationTokenSource();
            session.Register(Participant("a", restore: _ => cancellation.Cancel())); session.Register(Participant("b"));
            Ok(await session.SaveAsync("slot")); var result = await session.LoadAsync("slot", cancellation: cancellation.Token);
            Assert.AreEqual(SaveError.PartialRestore, result.Error); CollectionAssert.AreEqual(new[] { "a" }, result.RestoredParticipants); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator WrongThreadDoesNotInvokeParticipants() => Check(async () =>
        {
            var session = Session(); session.Register(Participant()); Assert.AreEqual(SaveError.WrongThread, (await Task.Run(() => session.SaveAsync("slot"))).Error); Assert.IsEmpty(memory.Data);
        });
        [UnityTest] public IEnumerator AwaitedRestoreReturnsToMainThread() => Check(async () =>
        {
            var session = Session(); session.Register(new JsonSaveParticipant<State>("x", SaveScope.Progress, 1, () => new State(), _ => SaveValidation.Valid,
                async (_, token) => { await Task.Run(() => Thread.SpinWait(1000), token); Assert.AreEqual(mainThread, Thread.CurrentThread.ManagedThreadId); restores++; }));
            Ok(await session.SaveAsync("slot")); Ok(await session.LoadAsync("slot")); Assert.AreEqual(1, restores);
        });
        [UnityTest] public IEnumerator OptionsAndDefaultsAreCopied() => Check(async () =>
        {
            var configuration = ScriptableObject.CreateInstance<SaveConfiguration>();
            try
            {
                string before = JsonUtility.ToJson(configuration); var session = Session(options: configuration.Options); session.Register(Participant());
                Ok(await session.QuickSaveAsync()); Ok(await session.QuickLoadAsync()); Assert.AreEqual(before, JsonUtility.ToJson(configuration));
                configuration.Options.QuickSlot = "changed"; Ok(await session.QuickSaveAsync()); Assert.IsFalse(memory.Data.ContainsKey("Progress/changed"));
            }
            finally { UnityEngine.Object.DestroyImmediate(configuration); }
        });
        [UnityTest] public IEnumerator PayloadLimitAndCaptureFailureDoNotCommit() => Check(async () =>
        {
            var session = Session(options: new SaveOptions { MaximumPayloadBytes = 2 }); session.Register(Participant());
            Assert.AreEqual(SaveError.CaptureFailed, (await session.SaveAsync("slot")).Error); Assert.IsEmpty(memory.Data);
        });
        [UnityTest] public IEnumerator ListMetadataAndDeleteOnlySelectedSlot() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant());
            Ok(await session.SaveAsync("one", displayName: "Chapter one")); Ok(await session.SaveAsync("two")); Ok(await session.SavePreferencesAsync());
            var listed = await session.ListAsync(); Ok(listed); Assert.AreEqual(2, listed.Slots.Count); Assert.AreEqual("Chapter one", listed.Slots.Single(s => s.Address.Slot == "one").DisplayName);
            Ok(await session.DeleteAsync("one")); Assert.AreEqual(SaveError.NotFound, (await session.LoadAsync("one")).Error); Ok(await session.LoadAsync("two")); Ok(await session.LoadPreferencesAsync());
            Assert.AreEqual(1, (await session.ListAsync()).Slots.Count);
        });
        [UnityTest] public IEnumerator CorruptAndTruncatedPrimaryRecoverBackupWithoutRewrite() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant());
            Ok(await session.SaveAsync("slot")); value = 8; Ok(await session.SaveAsync("slot"));
            string primary = Path.Combine(temporary, "progress", "slot", "save.bin"); byte[] data = File.ReadAllBytes(primary); data[data.Length - 1] ^= 0x7f; File.WriteAllBytes(primary, data);
            var result = await session.LoadAsync("slot"); Ok(result); Assert.IsTrue(result.RecoveredFromBackup); Assert.AreEqual(7, value); CollectionAssert.AreEqual(data, File.ReadAllBytes(primary));
            File.WriteAllBytes(primary, new byte[] { 1, 2 }); result = await session.LoadAsync("slot"); Ok(result); Assert.IsTrue(result.RecoveredFromBackup);
        });
        [UnityTest] public IEnumerator SaveAfterCorruptionPreservesLastValidBackup() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant()); Ok(await session.SaveAsync("slot")); value = 8; Ok(await session.SaveAsync("slot"));
            string directory = Path.Combine(temporary, "progress", "slot"); File.WriteAllBytes(Path.Combine(directory, "save.bin"), new byte[] { 1 });
            value = 9; Ok(await session.SaveAsync("slot")); File.WriteAllBytes(Path.Combine(directory, "save.bin"), new byte[] { 2 });
            Ok(await session.LoadAsync("slot")); Assert.AreEqual(7, value);
        });
        [UnityTest] public IEnumerator PendingTempIsNeverPromotedAndMissingPrimaryUsesBackup() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant()); Ok(await session.SaveAsync("slot")); value = 8; Ok(await session.SaveAsync("slot"));
            string directory = Path.Combine(temporary, "progress", "slot"); File.Copy(Path.Combine(directory, "save.bin"), Path.Combine(directory, "pending-interrupted.bin")); File.Delete(Path.Combine(directory, "save.bin"));
            var result = await session.LoadAsync("slot"); Ok(result); Assert.AreEqual(7, value); Assert.IsTrue(result.RecoveredFromBackup);
            Ok(await session.DeleteAsync("slot")); Assert.AreEqual(SaveError.NotFound, (await session.LoadAsync("slot")).Error);
        });
        [UnityTest] public IEnumerator UnsupportedContainerNeverFallsBack() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant()); Ok(await session.SaveAsync("slot")); Ok(await session.SaveAsync("slot"));
            string path = Path.Combine(temporary, "progress", "slot", "save.bin"); var data = File.ReadAllBytes(path); data[8] = 99; File.WriteAllBytes(path, data);
            Assert.AreEqual(SaveError.UnsupportedVersion, (await session.LoadAsync("slot")).Error); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator IncompatiblePrimaryNeverFallsBack() => Check(async () =>
        {
            var writer = Session(new LocalFileStorage(temporary)); var p = Participant(); writer.Register(p); Ok(await writer.SaveAsync("slot")); writer.Unregister(p); writer.Register(Participant("changed")); Ok(await writer.SaveAsync("slot"));
            var reader = Session(new LocalFileStorage(temporary)); reader.Register(Participant()); Assert.AreEqual(SaveError.IncompatibleParticipants, (await reader.LoadAsync("slot")).Error); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator LocalStorageHonorsCrossInstanceLock() => Check(async () =>
        {
            Directory.CreateDirectory(temporary); using var lease = new FileStream(Path.Combine(temporary, ".easy-save.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant()); Assert.AreEqual(SaveError.Busy, (await session.SaveAsync("slot")).Error);
        });
        [UnityTest] public IEnumerator InvalidSlotAndScopeNeverReachStorage() => Check(async () =>
        {
            var session = Session();
            foreach (var name in new[] { "../other", "A", "a/b", "a\\b", "con", "", ".", " slot", "a.", "a:b" }) Assert.AreEqual(SaveError.InvalidRequest, (await session.SaveAsync(name)).Error, name);
            Assert.AreEqual(SaveError.InvalidRequest, (await session.LoadAsync("slot", (SaveScope)9)).Error); Assert.IsEmpty(memory.Data);
        });
        [Test] public void DuplicateKeyRejectedWithinScopeOnly()
        {
            var session = Session(); session.Register(Participant()); Assert.Throws<ArgumentException>(() => session.Register(Participant())); Assert.DoesNotThrow(() => session.Register(Participant(scope: SaveScope.Preferences)));
        }
        [Test] public void FileBackendRequiresFullyQualifiedApplicationDirectory()
        {
            Assert.Throws<ArgumentException>(() => new LocalFileStorage("relative"));
            Assert.Throws<ArgumentException>(() => new LocalFileStorage(Path.GetPathRoot(Path.GetTempPath())));
            if (Path.DirectorySeparatorChar == '\\') Assert.Throws<ArgumentException>(() => new LocalFileStorage("C:relative"));
        }
        [Test] public void FormatRejectsCrossSlotAndCrossScopeDocuments()
        {
            var address = new SaveAddress(SaveScope.Progress, "slot"); var document = new SaveDocument { Address = address, DisplayName = "test", UtcTicks = DateTime.UtcNow.Ticks };
            var data = SaveFormat.Encode(document, new SaveOptions());
            Assert.Throws<SaveStorageException>(() => SaveFormat.Decode(data, new SaveAddress(SaveScope.Progress, "other"), new SaveOptions()));
            Assert.Throws<SaveStorageException>(() => SaveFormat.Decode(data, new SaveAddress(SaveScope.Preferences, "slot"), new SaveOptions()));
        }
        [Test] public void FormatRejectsDuplicateKeysAndInvalidVersions()
        {
            var address = new SaveAddress(SaveScope.Progress, "slot"); var document = new SaveDocument { Address = address, DisplayName = "", UtcTicks = DateTime.UtcNow.Ticks };
            document.Entries.Add(new SaveEntry { Key = "x", Version = 1, Payload = "{}" }); document.Entries.Add(new SaveEntry { Key = "x", Version = 1, Payload = "{}" });
            Assert.Throws<SaveStorageException>(() => SaveFormat.Decode(SaveFormat.Encode(document, new SaveOptions()), address, new SaveOptions()));
            document.Entries.RemoveAt(1); document.Entries[0].Version = 0;
            Assert.Throws<SaveStorageException>(() => SaveFormat.Decode(SaveFormat.Encode(document, new SaveOptions()), address, new SaveOptions()));
        }
        [UnityTest] public IEnumerator CorruptSlotAppearsInListingWithError() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary)); session.Register(Participant()); Ok(await session.SaveAsync("slot")); File.WriteAllText(Path.Combine(temporary, "progress", "slot", "save.bin"), "broken");
            var result = await session.ListAsync(); Ok(result); Assert.AreEqual(SaveError.CorruptData, result.Slots[0].Error); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator CancellationInValidatorDoesNotMutateWorld() => Check(async () =>
        {
            using var cancellation = new CancellationTokenSource(); var session = Session(); session.Register(Participant(validate: _ => { cancellation.Cancel(); return SaveValidation.Valid; }));
            Ok(await session.SaveAsync("slot")); var result = await session.LoadAsync("slot", cancellation: cancellation.Token); Assert.AreEqual(SaveError.Cancelled, result.Error); Assert.IsFalse(result.WorldMayHaveChanged); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator AggregateCaptureBudgetStopsBeforeAllParticipantsAreCaptured() => Check(async () =>
        {
            int captures = 0; var session = Session(options: new SaveOptions { MaximumFileBytes = 1024, MaximumPayloadBytes = 900 });
            for (int i = 0; i < 5; i++) session.Register(new JsonSaveParticipant<State>("part" + i, SaveScope.Progress, 1,
                () => { captures++; return new State { Text = new string('x', 700) }; }, _ => SaveValidation.Valid, _ => { }));
            Assert.AreEqual(SaveError.CaptureFailed, (await session.QuickSaveAsync()).Error); Assert.AreEqual(2, captures); Assert.IsEmpty(memory.Data);
        });
        [UnityTest] public IEnumerator OversizedFileAndBackupDisabledRejectWithoutRestore() => Check(async () =>
        {
            var session = Session(new LocalFileStorage(temporary), new SaveOptions { MaximumFileBytes = 1024, MaximumPayloadBytes = 900, RecoverBackup = false });
            session.Register(Participant()); Ok(await session.QuickSaveAsync()); Ok(await session.QuickSaveAsync());
            File.WriteAllBytes(Path.Combine(temporary, "progress", "quick", "save.bin"), new byte[1025]);
            var result = await session.QuickLoadAsync(); Assert.AreEqual(SaveError.CorruptData, result.Error); Assert.IsFalse(result.RecoveredFromBackup); Assert.Zero(restores);
        });
        [UnityTest] public IEnumerator FailedOverwriteKeepsPreviouslyCommittedSlot() => Check(async () =>
        {
            var session = Session(); session.Register(Participant()); Ok(await session.QuickSaveAsync()); var original = (byte[])memory.Data["Progress/quick"].Clone();
            memory.WriteStarted = false; memory.Gate = new TaskCompletionSource<bool>(); using var cancellation = new CancellationTokenSource();
            value = 50; var pending = session.QuickSaveAsync(cancellation.Token); await Started(memory); cancellation.Cancel(); Assert.AreEqual(SaveError.Cancelled, (await pending).Error);
            CollectionAssert.AreEqual(original, memory.Data["Progress/quick"]);
        });
    }
}
