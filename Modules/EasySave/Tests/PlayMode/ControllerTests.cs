using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasySave.Tests
{
    public sealed class ControllerTests
    {
        private GameObject root;
        private SaveConfiguration configuration;
        private SaveController controller;
        private ControllerParticipant participant;
        private string directory;
        [SetUp] public void Setup()
        {
            root = new GameObject("EasySave controller test"); root.SetActive(false);
            configuration = ScriptableObject.CreateInstance<SaveConfiguration>(); configuration.DirectoryName = "easysave-tests-" + Guid.NewGuid().ToString("N");
            directory = Path.Combine(Application.persistentDataPath, configuration.DirectoryName);
            participant = root.AddComponent<ControllerParticipant>(); controller = root.AddComponent<SaveController>(); controller.Configure(configuration, participant);
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if (root) UnityEngine.Object.Destroy(root); if (configuration) UnityEngine.Object.Destroy(configuration);
            yield return null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        private static IEnumerator Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted); task.GetAwaiter().GetResult();
        }
        [UnityTest] public IEnumerator DisableCancelsRestoreAndSuppressesLateNotification()
        {
            root.SetActive(true); var saved = controller.QuickSaveAsync(); yield return Await(saved); Assert.IsTrue(saved.Result.Success);
            participant.AwaitRestore = true; int completions = 0; controller.Completed += _ => completions++;
            var loaded = controller.QuickLoadAsync();
            while (!participant.EnteredRestore) yield return null;
            root.SetActive(false); yield return Await(loaded);
            Assert.AreEqual(SaveError.PartialRestore, loaded.Result.Error); Assert.IsNull(controller.Service); Assert.Zero(completions); Assert.Zero(participant.Restores);
        }
        [UnityTest] public IEnumerator DestroyCancelsOwnedSession()
        {
            root.SetActive(true); var session = controller.Service; var saved = controller.QuickSaveAsync(); yield return Await(saved); Assert.IsTrue(saved.Result.Success);
            participant.AwaitRestore = true; var loaded = controller.QuickLoadAsync(); while (!participant.EnteredRestore) yield return null;
            UnityEngine.Object.Destroy(root); yield return null; yield return Await(loaded);
            Assert.AreEqual(SaveError.PartialRestore, loaded.Result.Error); Assert.IsTrue(session.IsDisposed);
        }
        [UnityTest] public IEnumerator ReenableCreatesIndependentSessionAndKeepsDefaults()
        {
            string defaults = JsonUtility.ToJson(configuration); root.SetActive(true); var first = controller.Service;
            var saved = controller.QuickSaveAsync(); yield return Await(saved); Assert.IsTrue(saved.Result.Success);
            root.SetActive(false); root.SetActive(true); Assert.AreNotSame(first, controller.Service); Assert.IsTrue(first.IsDisposed);
            var loaded = controller.QuickLoadAsync(); yield return Await(loaded); Assert.IsTrue(loaded.Result.Success); Assert.AreEqual(1, participant.Restores);
            Assert.AreEqual(defaults, JsonUtility.ToJson(configuration));
        }
        [UnityTest] public IEnumerator AutosaveUsesUnscaledTimeAndProgressOnly()
        {
            configuration.AutoSaveSeconds = .03f; configuration.AutoSaveSlot = "auto"; root.SetActive(true);
            float scale = Time.timeScale;
            try
            {
                Time.timeScale = 0; var deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (controller.LastResult == null && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                Assert.IsNotNull(controller.LastResult); Assert.IsTrue(controller.LastResult.Success, controller.LastResult.ToString());
            }
            finally { Time.timeScale = scale; }
            root.SetActive(false);
            Assert.IsTrue(File.Exists(Path.Combine(directory, "progress", "auto", "save.bin"))); Assert.IsFalse(Directory.Exists(Path.Combine(directory, "preferences")));
        }
        [Test] public void InspectorExplainsDuplicateParticipantKeys()
        { controller.Configure(configuration, participant, participant); StringAssert.Contains("Duplicate", controller.ConfigurationError); }
    }
}
