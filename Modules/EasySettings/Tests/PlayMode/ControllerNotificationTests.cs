using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasySettings.Tests
{
    public sealed class ControllerNotificationTests
    {
        GameObject root, cameraObject;
        SettingsController controller;
        SettingsDefaults defaults;
        RenderPipelineAsset originalPipeline;
        int originalVSync, originalFps;
        [SetUp] public void SetUp()
        {
            originalPipeline = QualitySettings.renderPipeline; originalVSync = QualitySettings.vSyncCount; originalFps = Application.targetFrameRate;
            cameraObject = new GameObject("Notification camera"); var camera = cameraObject.AddComponent<Camera>(); camera.fieldOfView = 67; cameraObject.AddComponent<UniversalAdditionalCameraData>();
            defaults = ScriptableObject.CreateInstance<SettingsDefaults>(); root = new GameObject("Notification settings"); root.SetActive(false);
            controller = root.AddComponent<SettingsController>(); controller.Defaults = defaults; controller.FollowMainCamera = false; controller.TargetCamera = camera; root.SetActive(true);
            Assert.NotNull(controller.Session);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (root) Object.DestroyImmediate(root); if (cameraObject) Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(defaults);
            Assert.AreSame(originalPipeline, QualitySettings.renderPipeline); Assert.AreEqual(originalVSync, QualitySettings.vSyncCount); Assert.AreEqual(originalFps, Application.targetFrameRate);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
        static void Edit(SettingsSession session) { var s = session.Draft; s.RenderScale = .75f; s.FieldOfView = 90; s.VSyncCount = 0; s.FrameRateLimit = 90; session.Draft = s; }
        void Restored() { Assert.AreSame(originalPipeline, QualitySettings.renderPipeline); Assert.AreEqual(originalVSync, QualitySettings.vSyncCount); Assert.AreEqual(originalFps, Application.targetFrameRate); Assert.AreEqual(67, cameraObject.GetComponent<Camera>().fieldOfView); }

        [Test] public void CommittedCanDisableAndImmediatelyReenableGameObject()
        {
            for (int i = 0; i < 3; i++) {
                var old = controller.Session; Edit(old); int late = 0;
                old.Committed += () => { root.SetActive(false); Assert.IsTrue(old.IsDisposed); Assert.IsNull(controller.Session); Restored(); root.SetActive(true); };
                old.Committed += () => late++;
                Assert.IsTrue(old.Apply().Success); Assert.Zero(late); Assert.NotNull(controller.Session); Assert.AreNotSame(old, controller.Session);
                Assert.IsFalse(controller.Session.IsDisposed);
            }
        }
        [Test] public void ChangedCanDisableComponentWithoutLaterCommitted()
        {
            var old = controller.Session; Edit(old); int late = 0;
            old.Changed += () => controller.enabled = false; old.Changed += () => late++; old.Committed += () => late++;
            old.Apply(); Assert.IsTrue(old.IsDisposed); Assert.IsNull(controller.Session); Assert.Zero(late); Restored();
            controller.enabled = true; Assert.NotNull(controller.Session);
        }
        [Test] public void ImmediateDestroyFromChangedStopsDispatchAndReleasesOwner()
        {
            var old = controller.Session; Edit(old); int late = 0;
            old.Changed += () => Object.DestroyImmediate(root); old.Changed += () => late++; old.Committed += () => late++;
            old.Apply(); Assert.IsTrue(old.IsDisposed); Assert.Zero(late); Restored();
            using var next = new UrpSettingsBackend(); Assert.DoesNotThrow(() => next.Read()); Assert.IsNull(next.RuntimePipeline);
        }
        [UnityTest] public IEnumerator DeferredDestroyFromCommittedReleasesOwnerOnDestroy()
        {
            var old = controller.Session; Edit(old); old.Committed += () => Object.Destroy(root); old.Apply();
            yield return null;
            Assert.IsTrue(old.IsDisposed); Restored(); using var next = new UrpSettingsBackend(); Assert.DoesNotThrow(() => next.Read()); Assert.IsNull(next.RuntimePipeline);
        }
        [Test] public void BindTargetsStopsAfterRefreshDisablesController()
        {
            var replacement = new GameObject("Replacement camera").AddComponent<Camera>(); replacement.fieldOfView = 73; replacement.gameObject.AddComponent<UniversalAdditionalCameraData>();
            try {
                var old = controller.Session; old.Changed += () => controller.enabled = false;
                Assert.DoesNotThrow(() => controller.BindTargets(replacement)); Assert.IsTrue(old.IsDisposed); Assert.IsNull(controller.Session);
                Assert.AreEqual(73, replacement.fieldOfView); Restored();
            } finally { Object.DestroyImmediate(replacement.gameObject); }
        }
        [Test] public void BindTargetsDoesNotRestoreOldSnapshotIntoReenabledSession()
        {
            var replacement = new GameObject("Replacement camera").AddComponent<Camera>(); replacement.fieldOfView = 73; replacement.gameObject.AddComponent<UniversalAdditionalCameraData>();
            try {
                var old = controller.Session; Edit(old); old.Apply();
                old.Changed += () => { controller.enabled = false; controller.enabled = true; };
                controller.BindTargets(replacement);
                Assert.IsTrue(old.IsDisposed); Assert.NotNull(controller.Session); Assert.AreNotSame(old, controller.Session);
                Assert.AreEqual(73, controller.Session.Applied.FieldOfView); Assert.AreEqual(73, replacement.fieldOfView);
            } finally { controller.enabled = false; Object.DestroyImmediate(replacement.gameObject); }
        }
        [Test] public void SessionChangedReenableDoesNotReceiveStaleFinalNotification()
        {
            bool once = true; int stale = 0, live = 0;
            controller.SessionChanged += () => { if (controller.Session == null && once) { once = false; controller.enabled = true; } };
            controller.SessionChanged += () => { if (controller.Session == null) stale++; else live++; };
            var old = controller.Session; controller.enabled = false;
            Assert.IsTrue(old.IsDisposed); Assert.NotNull(controller.Session); Assert.AreEqual(1, live); Assert.Zero(stale);
        }
    }
}
