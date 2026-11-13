using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasySettings.Tests
{
    public sealed class CinemachineOwnershipTests
    {
        const OutputChannels Channel = (OutputChannels)(1 << 28);
        readonly List<GameObject> objects = new List<GameObject>();
        readonly List<(CinemachineBrain brain, OutputChannels mask)> previous = new List<(CinemachineBrain, OutputChannels)>();
        Camera camera;
        CinemachineBrain brain;
        CinemachineCamera view;
        RenderTexture texture;
        UrpSettingsBackend backend;
        SettingsSession session;
        GameObject Make(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void Setup()
        {
            for (int i = 0; i < CinemachineBrain.ActiveBrainCount; i++) { var b = CinemachineBrain.GetActiveBrain(i); previous.Add((b, b.ChannelMask)); b.ChannelMask &= ~Channel; }
            camera = Make("Settings output").AddComponent<Camera>(); camera.fieldOfView = 60; camera.cullingMask = 0; camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            texture = new RenderTexture(32, 32, 16); texture.Create(); camera.targetTexture = texture;
            view = Make("Settings virtual").AddComponent<CinemachineCamera>(); view.OutputChannel = Channel;
            var lens = view.Lens; lens.FieldOfView = 60; view.Lens = lens;
        }
        void AddBrain(bool external = false)
        {
            brain = (external ? Make("Separate brain") : camera.gameObject).AddComponent<CinemachineBrain>();
            if (external) brain.ControlledObject = camera.gameObject;
            brain.ChannelMask = Channel; brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.ManualUpdate();
        }
        void StartSession() { backend = new UrpSettingsBackend(camera); session = new SettingsSession(backend, SettingsState.Balanced, SettingsLimits.Default); }
        [UnityTearDown] public IEnumerator TearDown()
        {
            session?.Dispose(); backend?.Dispose(); session = null; backend = null;
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (texture) { texture.Release(); Object.DestroyImmediate(texture); }
            foreach (var item in previous) if (item.brain) item.brain.ChannelMask = item.mask; previous.Clear();
            yield return null; LogAssert.NoUnexpectedReceived();
        }
        [UnityTest] public IEnumerator CinemachineFovIsUnavailableAndApplyDoesNotClaimRequestedValue()
        {
            AddBrain(); StartSession(); Assert.IsFalse(session.Capabilities.Camera); StringAssert.Contains("Cinemachine", session.Capabilities.CameraReason);
            var draft = session.Draft; draft.FieldOfView = 90; draft.RenderScale = .8f; session.Draft = draft;
            var result = session.Apply(); Assert.IsTrue(result.Success); Assert.AreEqual(60, result.State.FieldOfView); Assert.AreEqual(.8f, backend.RuntimePipeline.renderScale);
            yield return null; brain.ManualUpdate();
            Assert.AreEqual(60, camera.fieldOfView); Assert.AreEqual(60, view.Lens.FieldOfView); Assert.AreEqual(60, session.Applied.FieldOfView);
            StringAssert.Contains("Cinemachine", string.Join(" ", result.Messages));
        }
        [Test] public void ControlledObjectOverrideIsDetected()
        {
            AddBrain(true); StartSession(); Assert.AreNotSame(brain.gameObject, camera.gameObject); Assert.AreSame(camera, brain.OutputCamera); Assert.IsFalse(session.Capabilities.Camera);
        }
        [Test] public void BrainEnabledAfterEditingIsDetectedAtApply()
        {
            AddBrain(); brain.enabled = false; StartSession(); Assert.IsTrue(session.Capabilities.Camera);
            var draft = session.Draft; draft.FieldOfView = 90; session.Draft = draft; brain.enabled = true;
            var result = session.Apply(); Assert.AreEqual(60, result.State.FieldOfView); Assert.IsFalse(session.Capabilities.Camera);
        }
        [UnityTest] public IEnumerator DirectBackendApplyCannotWriteOrRestoreAnOwnedLens()
        {
            AddBrain(); StartSession(); var state = backend.Read(); state.FieldOfView = 90; backend.Apply(state, false); Assert.AreEqual(60, camera.fieldOfView);
            var lens = view.Lens; lens.FieldOfView = 72; view.Lens = lens; yield return null; brain.ManualUpdate();
            backend.Dispose(); Assert.AreEqual(72, camera.fieldOfView); Assert.AreEqual(72, view.Lens.FieldOfView);
        }
        [UnityTest] public IEnumerator NewBrainOwnershipPreventsOldDirectOverrideRollback()
        {
            StartSession(); var state = backend.Read(); state.FieldOfView = 90; backend.Apply(state, false); Assert.AreEqual(90, camera.fieldOfView);
            AddBrain(); var lens = view.Lens; lens.FieldOfView = 72; view.Lens = lens; yield return null; brain.ManualUpdate();
            backend.Dispose(); Assert.AreEqual(72, camera.fieldOfView);
        }
        [Test] public void UnrelatedLaterDirectWriterIsNotOverwrittenOnRelease()
        {
            StartSession(); var state = backend.Read(); state.FieldOfView = 90; backend.Apply(state, false); camera.fieldOfView = 75;
            backend.Dispose(); Assert.AreEqual(75, camera.fieldOfView);
        }
        [Test] public void SwitchingVirtualCamerasKeepsDirectFovUnavailable()
        {
            AddBrain(); StartSession(); var next = Make("Other virtual").AddComponent<CinemachineCamera>(); next.OutputChannel = Channel;
            var lens = next.Lens; lens.FieldOfView = 76; next.Lens = lens; view.gameObject.SetActive(false); brain.ResetState(); brain.ManualUpdate();
            session.Refresh(); Assert.IsFalse(session.Capabilities.Camera); Assert.AreEqual(76, camera.fieldOfView);
        }
    }
}
