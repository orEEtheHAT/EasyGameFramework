using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasySave.Tests
{
    public sealed class JsonCaptureTests
    {
        [Serializable] public sealed class Snapshot { public int Value; }
        [Serializable] public sealed class Marker { }
        [Serializable] public struct ValueSnapshot { public int Value; }

        private sealed class RecordingStorage : ISaveStorage
        {
            private readonly LocalFileStorage files;
            internal int Writes;
            internal RecordingStorage(string directory) { files = new LocalFileStorage(directory); }
            public Task<byte[]> ReadAsync(SaveAddress address, bool backup, int maximumBytes, CancellationToken cancellation) => files.ReadAsync(address, backup, maximumBytes, cancellation);
            public Task<StorageCommit> WriteAsync(SaveAddress address, byte[] data, CancellationToken cancellation)
            { Writes++; return files.WriteAsync(address, data, cancellation); }
            public Task<IReadOnlyList<string>> ListAsync(SaveScope scope, CancellationToken cancellation) => files.ListAsync(scope, cancellation);
            public Task<StorageCommit> DeleteAsync(SaveAddress address, CancellationToken cancellation) => files.DeleteAsync(address, cancellation);
        }
        private sealed class EmptyRawParticipant : ISaveParticipant
        {
            public string Key => "raw";
            public SaveScope Scope => SaveScope.Progress;
            public int Version => 1;
            public int RestoreOrder => 0;
            internal int Restores;
            public string Capture() => string.Empty;
            public SaveValidation Validate(string payload) => payload == string.Empty ? SaveValidation.Valid : SaveValidation.Invalid("Expected an empty raw snapshot.");
            public Task RestoreAsync(string payload, CancellationToken cancellation)
            { cancellation.ThrowIfCancellationRequested(); Assert.AreEqual(string.Empty, payload); Restores++; return Task.CompletedTask; }
        }
        private readonly List<SaveService> sessions = new List<SaveService>();
        private string directory;
        private RecordingStorage storage;
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "EasySaveNullCapture-" + Guid.NewGuid().ToString("N"));
            storage = new RecordingStorage(directory);
        }
        [TearDown] public void Cleanup()
        {
            foreach (var session in sessions) session.Dispose(); sessions.Clear();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private SaveService Session()
        { var session = new SaveService(storage); sessions.Add(session); return session; }
        private static IEnumerator Check(Func<Task> body)
        {
            var task = body(); var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Async test timed out."); task.GetAwaiter().GetResult();
        }
        private static void Ok(SaveResult result) => Assert.IsTrue(result.Success, result.ToString());
        private static void Rejected(SaveResult result, string key)
        {
            Assert.AreEqual(SaveError.CaptureFailed, result.Error, result.ToString());
            Assert.IsFalse(result.Success); Assert.IsFalse(result.Committed); Assert.IsFalse(result.WorldMayHaveChanged);
            Assert.AreEqual(key, result.FailedParticipant); Assert.IsEmpty(result.RestoredParticipants);
        }

        [UnityTest] public IEnumerator NullDtoFailsBeforeStorageAndInvokesCaptureOnce() => Check(async () =>
        {
            int captures = 0, validations = 0, restores = 0;
            var session = Session();
            session.Register(new JsonSaveParticipant<Snapshot>("player", SaveScope.Progress, 1,
                () => { captures++; return null; },
                _ => { validations++; return SaveValidation.Valid; }, _ => restores++));
            var result = await session.QuickSaveAsync(); Rejected(result, "player");
            StringAssert.Contains("player", result.Message);
            Assert.AreEqual(1, captures); Assert.Zero(validations); Assert.Zero(restores); Assert.Zero(storage.Writes);
            Assert.IsFalse(Directory.Exists(directory)); Assert.IsFalse(session.IsBusy);
        });

        [UnityTest] public IEnumerator UnserializableCaptureCannotReachStorage() => Check(async () =>
        {
            // JsonUtility rejects this engine object rather than producing a DTO JSON object.
            var texture = new Texture2D(1, 1); int captures = 0, validations = 0;
            try
            {
                var session = Session();
                session.Register(new JsonSaveParticipant<Texture2D>("texture", SaveScope.Progress, 1,
                    () => { captures++; return texture; }, _ => { validations++; return SaveValidation.Valid; }, _ => Assert.Fail("Restore must not run.")));
                Rejected(await session.QuickSaveAsync(), "texture");
                Assert.AreEqual(1, captures); Assert.Zero(validations); Assert.Zero(storage.Writes); Assert.IsFalse(Directory.Exists(directory));
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        });

        [UnityTest] public IEnumerator TwoNullSavesPreservePrimaryBackupAndRecovery() => Check(async () =>
        {
            Snapshot current = new Snapshot { Value = 7 }; int captures = 0, restored = -1;
            var session = Session();
            session.Register(new JsonSaveParticipant<Snapshot>("player", SaveScope.Progress, 1,
                () => { captures++; return current; }, _ => SaveValidation.Valid, state => restored = state.Value));
            Ok(await session.QuickSaveAsync()); current = new Snapshot { Value = 9 }; Ok(await session.QuickSaveAsync());
            string slot = Path.Combine(directory, "progress", "quick"), primary = Path.Combine(slot, "save.bin"), backup = Path.Combine(slot, "backup.bin");
            byte[] primaryBefore = File.ReadAllBytes(primary), backupBefore = File.ReadAllBytes(backup);
            current = null;
            for (int i = 0; i < 2; i++)
            {
                Rejected(await session.QuickSaveAsync(), "player");
                CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
                CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
            }
            Assert.AreEqual(4, captures); Assert.AreEqual(2, storage.Writes);
            Assert.IsEmpty(Directory.GetFiles(slot, "pending-*.bin"));
            var loaded = await session.QuickLoadAsync(); Ok(loaded); Assert.IsFalse(loaded.RecoveredFromBackup); Assert.AreEqual(9, restored);
            // Damage only this test's unique primary to prove the preserved A backup is still usable.
            File.WriteAllBytes(primary, new byte[] { 1, 2 });
            loaded = await session.QuickLoadAsync(); Ok(loaded); Assert.IsTrue(loaded.RecoveredFromBackup); Assert.AreEqual(7, restored);
            Assert.AreEqual(2, storage.Writes);
        });

        [UnityTest] public IEnumerator ValidCaptureIsEvaluatedOnceWithoutRunningLoadValidator() => Check(async () =>
        {
            int captures = 0, validations = 0, restored = -1;
            var writer = Session();
            writer.Register(new JsonSaveParticipant<Snapshot>("player", SaveScope.Progress, 1,
                () => new Snapshot { Value = ++captures },
                _ => { validations++; return SaveValidation.Invalid("This validator is for the later load boundary."); }, _ => Assert.Fail("Writer restore must not run.")));
            Ok(await writer.QuickSaveAsync()); Assert.AreEqual(1, captures); Assert.Zero(validations);
            var reader = Session();
            reader.Register(new JsonSaveParticipant<Snapshot>("player", SaveScope.Progress, 1,
                () => throw new InvalidOperationException("Loading must not recapture."), _ => SaveValidation.Valid, state => restored = state.Value));
            Ok(await reader.QuickLoadAsync()); Assert.AreEqual(1, restored); Assert.AreEqual(1, captures); Assert.Zero(validations);
        });

        [UnityTest] public IEnumerator EmptyObjectAndDefaultStructStillRoundTrip() => Check(async () =>
        {
            int markers = 0, values = 0;
            var session = Session();
            session.Register(new JsonSaveParticipant<Marker>("marker", SaveScope.Progress, 1,
                () => new Marker(), _ => SaveValidation.Valid, state => { Assert.IsNotNull(state); markers++; }));
            session.Register(new JsonSaveParticipant<ValueSnapshot>("value", SaveScope.Progress, 1,
                () => default, _ => SaveValidation.Valid, state => { Assert.Zero(state.Value); values++; }));
            Ok(await session.QuickSaveAsync()); Ok(await session.QuickLoadAsync());
            Assert.AreEqual(1, markers); Assert.AreEqual(1, values);
        });

        [UnityTest] public IEnumerator LastNullPreferenceRejectsWholeDocument() => Check(async () =>
        {
            Snapshot current = new Snapshot { Value = 3 }; int first = 0, last = 0;
            var session = Session();
            session.Register(new JsonSaveParticipant<Snapshot>("a", SaveScope.Preferences, 1,
                () => { first++; return new Snapshot { Value = 1 }; }, _ => SaveValidation.Valid, _ => { }));
            session.Register(new JsonSaveParticipant<Snapshot>("z", SaveScope.Preferences, 1,
                () => { last++; return current; }, _ => SaveValidation.Valid, _ => { }));
            Ok(await session.SavePreferencesAsync());
            string primary = Path.Combine(directory, "preferences", "user", "save.bin"); byte[] before = File.ReadAllBytes(primary);
            current = null; first = last = 0;
            Rejected(await session.SavePreferencesAsync(), "z");
            Assert.AreEqual(1, first); Assert.AreEqual(1, last); Assert.AreEqual(1, storage.Writes);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(primary));
        });

        [UnityTest] public IEnumerator CustomParticipantCanStillStoreEmptyRawPayload() => Check(async () =>
        {
            var participant = new EmptyRawParticipant(); var session = Session(); session.Register(participant);
            Ok(await session.QuickSaveAsync()); Ok(await session.QuickLoadAsync());
            Assert.AreEqual(1, storage.Writes); Assert.AreEqual(1, participant.Restores);
        });
    }
}
