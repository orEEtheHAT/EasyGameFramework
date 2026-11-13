using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasySettings.Tests
{
    public sealed class UrpLifetimeTests
    {
        GameObject cameraObject, volumeObject;
        Camera camera;
        UniversalAdditionalCameraData data;
        Volume volume;
        VolumeProfile sourceProfile;
        UrpSettingsBackend backend;
        RenderPipelineAsset original;
        UniversalRenderPipelineAsset source;
        string sourceJson, volumeJson;
        [SetUp] public void Setup()
        {
            original = QualitySettings.renderPipeline;
            source = (original ? original : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            Assert.NotNull(source, "Run these tests in a URP project."); sourceJson = JsonUtility.ToJson(source);
            cameraObject = new GameObject("Settings test camera"); camera = cameraObject.AddComponent<Camera>(); data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            camera.fieldOfView = 67; data.renderPostProcessing = true;
            volumeObject = new GameObject("Settings test volume"); volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true;
            sourceProfile = ScriptableObject.CreateInstance<VolumeProfile>(); sourceProfile.Add<ColorAdjustments>(); sourceProfile.Add<Bloom>(); volume.sharedProfile = sourceProfile;
            volumeJson = JsonUtility.ToJson(sourceProfile.components[0]);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            backend?.Dispose(); backend = null;
            Assert.AreEqual(original, QualitySettings.renderPipeline); Assert.AreEqual(sourceJson, JsonUtility.ToJson(source));
            Assert.AreEqual(volumeJson, JsonUtility.ToJson(sourceProfile.components[0]));
            Object.Destroy(cameraObject); Object.Destroy(volumeObject);
            foreach (var c in sourceProfile.components) Object.Destroy(c); Object.Destroy(sourceProfile);
            yield return null;
        }
        [UnityTest] public IEnumerator PipelineIsClonedOnFirstGraphicsChangeAndRestored()
        {
            backend = new UrpSettingsBackend(camera, volume, true, true);
            Assert.IsNull(backend.RuntimePipeline); Assert.AreSame(original, QualitySettings.renderPipeline);
            var state = backend.Read(); state.RenderScale = .73f; state.ShadowDistance = 31; backend.Apply(state, false);
            Assert.AreNotSame(source, backend.RuntimePipeline); Assert.AreSame(backend.RuntimePipeline, QualitySettings.renderPipeline);
            var copy = backend.RuntimePipeline; state.RenderScale = .81f; backend.Apply(state, false);
            Assert.AreSame(copy, backend.RuntimePipeline); state.RenderScale = .73f; backend.Apply(state, false);
            Assert.AreEqual(.73f, backend.RuntimePipeline.renderScale); Assert.AreEqual(sourceJson, JsonUtility.ToJson(source));
            backend.Dispose(); Assert.AreSame(original, QualitySettings.renderPipeline); yield return null;
        }
        [UnityTest] public IEnumerator FovAndFramePacingDoNotReplacePipeline()
        {
            int vSync = QualitySettings.vSyncCount, fps = Application.targetFrameRate;
            backend = new UrpSettingsBackend(camera);
            backend.Describe(); var state = backend.Read(); backend.Apply(state, false);
            Assert.IsNull(backend.RuntimePipeline); Assert.AreSame(original, QualitySettings.renderPipeline);
            state.FieldOfView = 80; state.VSyncCount = 0; state.FrameRateLimit = 90; backend.Apply(state, false);
            yield return null;
            Assert.IsNull(backend.RuntimePipeline); Assert.AreSame(original, QualitySettings.renderPipeline);
            Assert.AreEqual(80, camera.fieldOfView); Assert.AreEqual(90, Application.targetFrameRate);
            backend.Dispose(); Assert.AreEqual(67, camera.fieldOfView);
            Assert.AreEqual(vSync, QualitySettings.vSyncCount); Assert.AreEqual(fps, Application.targetFrameRate);
        }
        [Test] public void ExternalPipelineChangeBeforeFirstApplyIsPreserved()
        {
            backend = new UrpSettingsBackend(camera); var state = backend.Read(); state.RenderScale = .73f;
            var external = Object.Instantiate(source);
            try {
                QualitySettings.renderPipeline = external;
                Assert.Throws<InvalidOperationException>(() => backend.Apply(state, false));
                backend.Dispose(); Assert.AreSame(external, QualitySettings.renderPipeline); Assert.IsNull(backend.RuntimePipeline);
            } finally { QualitySettings.renderPipeline = original; Object.Destroy(external); }
        }
        [UnityTest] public IEnumerator CameraAndDeepVolumeCloneRestorePreviousReferences()
        {
            var previousProfile = volume.profile; previousProfile.TryGet<ColorAdjustments>(out var previousExposure); previousExposure.postExposure.value = .4f;
            backend = new UrpSettingsBackend(camera, volume, true, true);
            Assert.AreNotSame(previousProfile, volume.profile); Assert.AreNotSame(previousProfile.components[0], volume.profile.components[0]);
            var state = backend.Read(); state.FieldOfView = 90; state.PostExposure = 1.5f; state.BloomIntensity = 2; backend.Apply(state, false);
            Assert.AreEqual(.4f, previousExposure.postExposure.value); Assert.AreEqual(90, camera.fieldOfView);
            backend.Dispose(); Assert.AreEqual(67, camera.fieldOfView); Assert.AreSame(previousProfile, volume.profile);
            foreach (var c in previousProfile.components) Object.Destroy(c); Object.Destroy(previousProfile); volume.profile = null;
            yield return null;
        }
        [UnityTest] public IEnumerator TargetReplacementRestoresOldCameraAndVolume()
        {
            backend = new UrpSettingsBackend(camera, volume, true, true);
            var state = backend.Read(); state.FieldOfView = 85; backend.Apply(state, false);
            var next = new GameObject("Replacement").AddComponent<Camera>(); next.fieldOfView = 72; next.gameObject.AddComponent<UniversalAdditionalCameraData>();
            backend.BindTargets(next, null); Assert.AreEqual(67, camera.fieldOfView); Assert.IsFalse(volume.HasInstantiatedProfile());
            state = backend.Read(); state.FieldOfView = 80; backend.Apply(state, false); backend.Dispose(); Assert.AreEqual(72, next.fieldOfView);
            Object.Destroy(next.gameObject); yield return null;
        }
        [UnityTest] public IEnumerator RepeatedControllerEnableDisableReleasesOwnership()
        {
            var defaults = ScriptableObject.CreateInstance<SettingsDefaults>();
            var go = new GameObject("Settings controller"); go.SetActive(false); var controller = go.AddComponent<SettingsController>(); controller.Defaults = defaults; controller.TargetCamera = camera;
            for (int i = 0; i < 3; i++) {
                go.SetActive(true); Assert.NotNull(controller.Session);
                var state = controller.Session.Draft; state.RenderScale = .8f; state.FieldOfView = 95; controller.Session.Draft = state;
                Assert.IsTrue(controller.Session.Apply().Success); go.SetActive(false);
                Assert.IsNull(controller.Session); Assert.AreSame(original, QualitySettings.renderPipeline); Assert.AreEqual(67, camera.fieldOfView); yield return null;
            }
            Object.Destroy(go); Object.Destroy(defaults);
        }
        [Test] public void DuplicateOwnerRejectedWithoutChangingExistingOverride()
        {
            backend = new UrpSettingsBackend(camera); var active = QualitySettings.renderPipeline;
            Assert.Throws<InvalidOperationException>(() => new UrpSettingsBackend()); Assert.AreSame(active, QualitySettings.renderPipeline);
        }
        [UnityTest] public IEnumerator DestroyedTargetsCanBeReleased()
        {
            backend = new UrpSettingsBackend(camera, volume, true, true); Object.Destroy(cameraObject); Object.Destroy(volumeObject); yield return null;
            backend.BindTargets(null, null); Assert.IsFalse(backend.Describe().Camera); backend.Dispose();
        }
        [Test] public void EditorDoesNotOfferScreenMutation()
        {
            backend = new UrpSettingsBackend(camera);
            if (Application.isEditor) Assert.IsFalse(backend.Describe().Display);
        }
        [Test] public void DefaultOptInFlagsPreserveCameraPostProcessing()
        {
            backend = new UrpSettingsBackend(camera, volume);
            Assert.IsFalse(backend.Describe().PostProcessing); Assert.IsFalse(backend.Describe().Volume); Assert.IsFalse(volume.HasInstantiatedProfile());
        }
    }
}
