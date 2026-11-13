using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace EasySave.Tests
{
    public sealed class CompositionTests
    {
        [Serializable] public sealed class Value { public int Number; }
        private static IEnumerator Run(Func<Task> body)
        {
            var task = body(); var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult();
        }
        private static ISaveParticipant Item(string key, SaveScope scope, Func<int> read, Action<int> write) =>
            new JsonSaveParticipant<Value>(key, scope, 1, () => new Value { Number = read() }, _ => SaveValidation.Valid, value => write(value.Number));
        private sealed class NoStorage : ISaveStorage
        {
            public Task<byte[]> ReadAsync(SaveAddress a, bool b, int c, System.Threading.CancellationToken t) => throw new AssertionException("Memory restore touched storage.");
            public Task<StorageCommit> WriteAsync(SaveAddress a, byte[] b, System.Threading.CancellationToken t) => throw new AssertionException("Memory capture touched storage.");
            public Task<System.Collections.Generic.IReadOnlyList<string>> ListAsync(SaveScope s, System.Threading.CancellationToken t) => throw new AssertionException("Unexpected storage list.");
            public Task<StorageCommit> DeleteAsync(SaveAddress a, System.Threading.CancellationToken t) => throw new AssertionException("Unexpected storage delete.");
        }
        [UnityTest] public IEnumerator MemorySnapshotHasIndependentScopeAndValues() => Run(async () =>
        {
            int progress = 7, preference = 8;
            using var service = new SaveService(new NoStorage());
            using var bindings = service.RegisterAll(Item("world", SaveScope.Progress, () => progress, v => progress = v), Item("user", SaveScope.Preferences, () => preference, v => preference = v));
            var world = service.CaptureSnapshot(); var preferences = service.CaptureSnapshot(SaveScope.Preferences);
            progress = 20; preference = 30;
            Assert.IsTrue((await service.RestoreSnapshotAsync(world)).Success); Assert.AreEqual(7, progress); Assert.AreEqual(30, preference);
            Assert.IsTrue((await service.RestoreSnapshotAsync(preferences)).Success); Assert.AreEqual(8, preference);
            progress = 50; Assert.IsTrue((await service.RestoreSnapshotAsync(world)).Success); Assert.AreEqual(7, progress);
        });
        [Test] public void DuplicateBatchDoesNotPartiallyRegister()
        {
            using var service = new SaveService(new NoStorage());
            var a = Item("a", SaveScope.Progress, () => 1, _ => { }); var b = Item("b", SaveScope.Progress, () => 2, _ => { });
            service.Register(a); Assert.Throws<ArgumentException>(() => service.RegisterAll(b, a));
            Assert.AreEqual(1, service.CaptureSnapshot().ParticipantCount);
            Assert.DoesNotThrow(() => service.Register(b));
        }
        [Test] public void DisposedHandleCannotUnregisterReplacement()
        {
            using var service = new SaveService(new NoStorage());
            var a = Item("a", SaveScope.Progress, () => 1, _ => { });
            var old = service.RegisterAll(a); service.Unregister(a); service.Register(a); old.Dispose(); old.Dispose();
            Assert.AreEqual(1, service.CaptureSnapshot().ParticipantCount);
        }
        [UnityTest] public IEnumerator MemoryRestoreValidatesEntireSetBeforeMutation() => Run(async () =>
        {
            bool reject = false; int restores = 0;
            using var service = new SaveService(new NoStorage());
            service.Register(Item("a", SaveScope.Progress, () => 1, _ => restores++));
            service.Register(new JsonSaveParticipant<Value>("z", SaveScope.Progress, 1, () => new Value(),
                _ => reject ? SaveValidation.Invalid("Changed content") : SaveValidation.Valid, _ => restores++));
            var snapshot = service.CaptureSnapshot(); reject = true;
            var result = await service.RestoreSnapshotAsync(snapshot);
            Assert.AreEqual(SaveError.ValidationFailed, result.Error); Assert.IsFalse(result.WorldMayHaveChanged); Assert.Zero(restores);
        });
    }
}
