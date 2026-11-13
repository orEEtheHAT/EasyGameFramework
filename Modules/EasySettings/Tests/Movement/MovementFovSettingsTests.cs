using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using EasyMovement.FirstPerson;
using EasySettings.Integrations;
using EasySave.Integrations;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasySettings.Tests
{
    public sealed class MovementFovSettingsTests
    {
        const OutputChannels Channel = (OutputChannels)(1 << 27);
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<(CinemachineBrain brain, OutputChannels mask)> previousBrains = new List<(CinemachineBrain, OutputChannels)>();
        SettingsController settings;
        EasyMovementSettingsFov adapter;
        FirstPersonFov fov;
        FirstPersonCharacterController movement;
        CinemachineCamera walk, crouch, sprint;
        CinemachineBrain brain;
        Camera output;
        RenderTexture texture;
        SettingsDefaults defaults;
        RenderPipelineAsset originalPipeline;
        GameObject Make(string name, bool active = true) { var go = new GameObject(name); go.SetActive(active); objects.Add(go); return go; }
        CinemachineCamera View(string name, float degrees) { var v = Make(name).AddComponent<CinemachineCamera>(); v.OutputChannel = Channel; var lens = v.Lens; lens.FieldOfView = degrees; v.Lens = lens; return v; }
        [UnitySetUp] public IEnumerator SetUp()
        {
            originalPipeline = QualitySettings.renderPipeline;
            for (int i = 0; i < CinemachineBrain.ActiveBrainCount; i++) { var b = CinemachineBrain.GetActiveBrain(i); previousBrains.Add((b, b.ChannelMask)); b.ChannelMask &= ~Channel; }
            output = Make("FOV output").AddComponent<Camera>(); output.cullingMask = 0; output.fieldOfView = 60; output.gameObject.AddComponent<UniversalAdditionalCameraData>();
            texture = new RenderTexture(32, 32, 16); texture.Create(); output.targetTexture = texture;
            brain = output.gameObject.AddComponent<CinemachineBrain>(); brain.ChannelMask = Channel; brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            walk = View("Walk", 60); crouch = View("Crouch", 55); sprint = View("Sprint", 70); crouch.gameObject.SetActive(false); sprint.gameObject.SetActive(false);
            var player = Make("FOV player", false); movement = player.AddComponent<FirstPersonCharacterController>(); movement.enabled = false;
            movement.defaultCamera = walk; movement.crouchedCamera = crouch; movement.sprintCamera = sprint;
            fov = player.AddComponent<FirstPersonFov>(); fov.Controller = movement; player.SetActive(true); Assert.IsTrue(fov.IsAvailable, fov.UnavailableReason);
            brain.ManualUpdate();
            var root = Make("FOV settings", false); defaults = ScriptableObject.CreateInstance<SettingsDefaults>();
            settings = root.AddComponent<SettingsController>(); settings.Defaults = defaults; settings.TargetCamera = output; settings.FollowMainCamera = false;
            adapter = root.AddComponent<EasyMovementSettingsFov>(); adapter.PlayerFov = fov; settings.FieldOfViewProvider = adapter; root.SetActive(true);
            Assert.NotNull(settings.Session); Assert.IsTrue(settings.Session.Capabilities.Camera, settings.Session.Capabilities.CameraReason);
            yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (settings) settings.enabled = false;
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (texture) { texture.Release(); Object.DestroyImmediate(texture); }
            if (defaults) Object.DestroyImmediate(defaults);
            foreach (var item in previousBrains) if (item.brain) item.brain.ChannelMask = item.mask; previousBrains.Clear();
            Assert.AreSame(originalPipeline, QualitySettings.renderPipeline); yield return null; LogAssert.NoUnexpectedReceived();
        }
        void Apply(float value)
        {
            var draft = settings.Session.Draft; draft.FieldOfView = value; settings.Session.Draft = draft;
            var result = settings.Session.Apply(); Assert.IsTrue(result.Success, string.Join(" ", result.Messages));
        }
        [UnityTest] public IEnumerator BaseFovSurvivesBrainAndGaitCameraSwitches()
        {
            Apply(80); yield return null; brain.ManualUpdate(); Assert.AreEqual(80, output.fieldOfView); Assert.AreEqual(90, sprint.Lens.FieldOfView); Assert.AreEqual(75, crouch.Lens.FieldOfView);
            walk.gameObject.SetActive(false); sprint.gameObject.SetActive(true); brain.ResetState(); yield return null; brain.ManualUpdate();
            Assert.AreEqual(90, output.fieldOfView); Assert.AreEqual(80, settings.Session.Capture().State.FieldOfView);
            sprint.gameObject.SetActive(false); crouch.gameObject.SetActive(true); brain.ResetState(); yield return null; brain.ManualUpdate(); Assert.AreEqual(75, output.fieldOfView);
            Assert.AreEqual(60, fov.AuthoredBaseFov);
        }
        [UnityTest] public IEnumerator GameplayOffsetsAreExcludedFromSnapshotAndSurviveSettingsRelease()
        {
            Apply(80); using var zoom = fov.AcquireOffset(Make("Zoom owner"), -15);
            yield return null; brain.ManualUpdate(); Assert.AreEqual(65, output.fieldOfView); Assert.AreEqual(75, sprint.Lens.FieldOfView);
            settings.Session.Refresh(); Assert.AreEqual(80, settings.Session.Observed.FieldOfView); Assert.AreEqual(80, settings.Capture().State.FieldOfView);
            settings.enabled = false; Assert.IsTrue(zoom.IsActive); Assert.AreEqual(60, fov.BaseFieldOfView); Assert.AreEqual(45, walk.Lens.FieldOfView); Assert.AreEqual(55, sprint.Lens.FieldOfView);
            zoom.Dispose(); Assert.AreEqual(60, walk.Lens.FieldOfView); Assert.AreEqual(70, sprint.Lens.FieldOfView);
        }
        [Test] public void OtherBaseOwnerIsPreservedWhenSettingsReleasesItsHandle()
        {
            using var other = fov.AcquireBaseFovOverride(Make("Other base owner"), 65);
            Apply(80); Assert.AreEqual(80, fov.BaseFieldOfView); settings.enabled = false;
            Assert.IsTrue(other.IsActive); Assert.AreEqual(65, fov.BaseFieldOfView); Assert.AreEqual(75, sprint.Lens.FieldOfView);
        }
        [Test] public void ProviderDisableReleasesOnlyItsLeaseAndReacquiresOnApply()
        {
            Apply(80); using var offset = fov.AcquireOffset(Make("Offset owner"), -10);
            adapter.enabled = false; Assert.AreEqual(60, fov.BaseFieldOfView); Assert.IsTrue(offset.IsActive); Assert.AreEqual(50, walk.Lens.FieldOfView);
            settings.Session.Refresh(); Assert.IsFalse(settings.Session.Capabilities.Camera); StringAssert.Contains("Enable", settings.Session.Capabilities.CameraReason);
            adapter.enabled = true; settings.Session.Refresh(); Apply(85); Assert.AreEqual(85, fov.BaseFieldOfView); Assert.AreEqual(75, walk.Lens.FieldOfView);
        }
        [Test] public void ExpiredMovementLeaseCanBeReacquiredAfterPooling()
        {
            Apply(80); fov.enabled = false; settings.Session.Refresh(); Assert.IsFalse(settings.Session.Capabilities.Camera);
            fov.enabled = true; settings.Session.Refresh(); Apply(80); Assert.AreEqual(80, fov.BaseFieldOfView); Assert.AreEqual(90, sprint.Lens.FieldOfView);
        }
        [Test] public void MissingOwnerHasReasonAndDoesNotFallBackToOutputCamera()
        {
            adapter.Bind(null); settings.Session.Refresh(); Assert.IsFalse(settings.Session.Capabilities.Camera);
            StringAssert.Contains("Assign", settings.Session.Capabilities.CameraReason);
            var draft = settings.Session.Draft; draft.FieldOfView = 90; draft.RenderScale = .8f; settings.Session.Draft = draft;
            Assert.IsTrue(settings.Session.Apply().Success); Assert.AreEqual(60, fov.BaseFieldOfView); brain.ManualUpdate(); Assert.AreEqual(60, output.fieldOfView);
            Assert.AreEqual(.8f, settings.Session.Observed.RenderScale);
        }
        [UnityTest] public IEnumerator PublishedSaveAdapterRestoresBaseWithoutSerializingZoom()
        {
            Apply(80); using var zoom = fov.AcquireOffset(Make("Saved zoom"), -15);
            var participant = SettingsSaveAdapter.Create(settings); var payload = participant.Capture();
            Assert.IsTrue(participant.Validate(payload).IsValid); Apply(90);
            var restore = participant.RestoreAsync(payload, CancellationToken.None); while (!restore.IsCompleted) yield return null; restore.GetAwaiter().GetResult();
            Assert.AreEqual(80, fov.BaseFieldOfView); Assert.IsTrue(zoom.IsActive); yield return null; brain.ManualUpdate(); Assert.AreEqual(65, output.fieldOfView);
            Assert.IsFalse(settings.Session.HasPreview);
        }
        [Test] public void CommittedDisableReenableDoesNotLeaveMovementOrUrpOwner()
        {
            var old = settings.Session; var draft = old.Draft; draft.FieldOfView = 80; old.Draft = draft;
            old.Committed += () => { settings.enabled = false; Assert.AreEqual(60, fov.BaseFieldOfView); settings.enabled = true; };
            old.Apply(); Assert.IsTrue(old.IsDisposed); Assert.NotNull(settings.Session); Assert.AreNotSame(old, settings.Session); Assert.AreEqual(60, settings.Capture().State.FieldOfView);
            Apply(85); Assert.AreEqual(85, fov.BaseFieldOfView);
        }
        [Test] public void OwnerCallbackClosingSettingsDuringAcquisitionReleasesReturnedHandle()
        {
            bool once = true; fov.Changed += () => { if (once) { once = false; settings.enabled = false; } };
            var old = settings.Session; var draft = old.Draft; draft.FieldOfView = 80; old.Draft = draft;
            Assert.IsFalse(old.Apply().Success); Assert.IsTrue(old.IsDisposed); Assert.IsNull(settings.Session); Assert.AreEqual(60, fov.BaseFieldOfView);
            settings.enabled = true; Assert.NotNull(settings.Session); Apply(85); Assert.AreEqual(85, fov.BaseFieldOfView);
        }
    }
}
