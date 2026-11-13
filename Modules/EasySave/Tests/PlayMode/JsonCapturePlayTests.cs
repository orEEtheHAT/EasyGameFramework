using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace EasySave.Tests
{
    public sealed class JsonCapturePlayTests
    {
        [Serializable] public sealed class Snapshot { public int Value; }
        [UnityTest] public IEnumerator NullJsonCaptureCannotDisplaceGoodSlotInPlayMode()
        {
            string root = Path.Combine(Path.GetTempPath(), "EasySaveNullCapturePlay-" + Guid.NewGuid().ToString("N"));
            Task exercise = Exercise(root); var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!exercise.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(exercise.IsCompleted, "PlayMode capture regression timed out."); exercise.GetAwaiter().GetResult();
        }
        private static async Task Exercise(string root)
        {
            try
            {
                using var saves = new SaveService(new LocalFileStorage(root));
                Snapshot value = new Snapshot { Value = 42 }; int captures = 0, restores = 0;
                saves.Register(new JsonSaveParticipant<Snapshot>("data", SaveScope.Progress, 1,
                    () => { captures++; return value; }, _ => SaveValidation.Valid, state => { Assert.AreEqual(42, state.Value); restores++; }));
                var good = await saves.SaveAsync("slot"); Assert.IsTrue(good.Success, good.ToString());
                string path = Path.Combine(root, "progress", "slot", "save.bin"); byte[] before = File.ReadAllBytes(path);
                value = null; var rejected = await saves.SaveAsync("slot");
                Assert.AreEqual(SaveError.CaptureFailed, rejected.Error); Assert.IsFalse(rejected.Committed); Assert.IsFalse(rejected.WorldMayHaveChanged);
                Assert.AreEqual("data", rejected.FailedParticipant); Assert.AreEqual(2, captures);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
                var loaded = await saves.LoadAsync("slot"); Assert.IsTrue(loaded.Success, loaded.ToString()); Assert.IsFalse(loaded.RecoveredFromBackup);
                Assert.AreEqual(1, restores);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
