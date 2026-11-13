using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EasyMovement.FirstPerson;
using ECM2;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyMovement.MotorTests
{
    public sealed class FovTestCharacter : FirstPersonCharacterController
    { public void Initialize() { base.Reset(); enableAutoSimulation = false; } }

    public sealed class FovOwnershipTests
    {
        GameObject root, ownerA, ownerB, output;
        FovTestCharacter character;
        FirstPersonFov fov;
        CinemachineCamera walk, sprint, crouch;
        readonly List<GameObject> extras = new List<GameObject>();

        [UnitySetUp] public IEnumerator SetUp()
        {
            root = new GameObject("Fov test character"); root.SetActive(false);
            character = root.AddComponent<FovTestCharacter>(); character.Initialize();
            root.GetComponent<CharacterMovement>()._advanced.Reset();
            var pivot = new GameObject("pivot"); pivot.transform.SetParent(root.transform); character.LookPivot = pivot.transform;
            walk = View("walk", 60); sprint = View("sprint", 70); crouch = View("crouch", 55);
            character.defaultCamera = walk; character.sprintCamera = sprint; character.crouchedCamera = crouch;
            fov = root.AddComponent<FirstPersonFov>();
            ownerA = new GameObject("settings owner"); ownerB = new GameObject("gameplay owner");
            root.SetActive(true); yield return null; fov.RefreshCameras();
            Assert.IsTrue(fov.IsAvailable, fov.UnavailableReason);
        }
        CinemachineCamera View(string label, float degrees)
        {
            var go = new GameObject(label); go.transform.SetParent(root.transform); var view = go.AddComponent<CinemachineCamera>();
            var lens = view.Lens; lens.ModeOverride = LensSettings.OverrideModes.Perspective; lens.FieldOfView = degrees; view.Lens = lens;
            view.OutputChannel = OutputChannels.Channel15;
            return view;
        }
        static void AssertFov(CinemachineCamera camera, float expected) => Assert.That(camera.Lens.FieldOfView, Is.EqualTo(expected).Within(.001f));
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (root) Object.Destroy(root); if (ownerA) Object.Destroy(ownerA); if (ownerB) Object.Destroy(ownerB); if (output) Object.Destroy(output);
            foreach (var e in extras) if(e)Object.Destroy(e); extras.Clear(); yield return null;
        }
        [Test] public void BaseGaitDeltasAndOwnedOffsetsComposeAndRestore()
        {
            var settings = fov.AcquireBaseFovOverride(ownerA, 80); AssertFov(walk, 80); AssertFov(sprint, 90); AssertFov(crouch, 75);
            var zoom = fov.AcquireOffset(ownerB, -15); AssertFov(walk, 65); AssertFov(sprint, 75); Assert.AreEqual(80, fov.BaseFieldOfView);
            Assert.IsTrue(settings.TrySet(90)); AssertFov(walk, 75); AssertFov(sprint, 85);
            settings.Dispose(); settings.Dispose(); AssertFov(walk, 45); AssertFov(sprint, 55);
            zoom.Dispose(); AssertFov(walk, 60); AssertFov(sprint, 70); AssertFov(crouch, 55);
        }
        [Test] public void LatestBaseWinsAndExpiryRevealsRemainingOwners()
        {
            var first = fov.AcquireBaseFovOverride(ownerA, 80); var second = fov.AcquireBaseFovOverride(ownerB, 95);
            first.TrySet(85); AssertFov(walk, 95); ownerB.SetActive(false); fov.RefreshCameras(); AssertFov(walk, 85); Assert.IsFalse(second.IsActive);
            Object.DestroyImmediate(ownerA); fov.RefreshCameras(); AssertFov(walk, 60); Assert.IsFalse(first.IsActive);
        }
        [Test] public void ClampInvalidValuesDisablePoolingAndCameraReplacementAreSafe()
        {
            var a = fov.AcquireBaseFovOverride(ownerA, 177); AssertFov(walk, 177); AssertFov(sprint, 179);
            Assert.IsFalse(a.TrySet(float.NaN)); AssertFov(walk, 177);
            var oldSprint = sprint; sprint = View("replacement sprint", 75); character.sprintCamera = sprint; fov.RefreshCameras();
            AssertFov(oldSprint, 70); AssertFov(sprint, 179); a.TrySet(80); AssertFov(sprint, 95);
            fov.enabled = false; Assert.IsFalse(a.IsActive); AssertFov(walk, 60); AssertFov(sprint, 75);
            fov.enabled = true; Assert.IsTrue(fov.IsAvailable); Assert.AreEqual(60, fov.BaseFieldOfView); Assert.IsFalse(a.TrySet(100));
            var lens = sprint.Lens; lens.ModeOverride = LensSettings.OverrideModes.Orthographic; sprint.Lens = lens;
            fov.RefreshCameras(); Assert.IsFalse(fov.IsAvailable); StringAssert.Contains("perspective", fov.UnavailableReason);
        }
        [Test] public void CleanupDoesNotOverwriteANewerDirectLensWriter()
        {
            var a = fov.AcquireBaseFovOverride(ownerA, 80); var lens = walk.Lens; lens.FieldOfView = 73; walk.Lens = lens;
            fov.RefreshCameras(); AssertFov(walk, 73); // stable refresh does not fight a direct writer
            a.Dispose(); AssertFov(walk, 73); AssertFov(sprint, 70);
        }
        [Test] public void CustomViewsJoinTheSameCompositionAndWarmRefreshDoesNotAllocate()
        {
            var custom = View("custom gait", 65);
            var field = typeof(FirstPersonCharacterController).GetField("customStates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var elementType = field.FieldType.GetElementType();
            var state = Activator.CreateInstance(elementType);
            elementType.GetField("stateId").SetValue(state, "test"); elementType.GetField("view").SetValue(state, custom);
            var array = Array.CreateInstance(elementType, 1); array.SetValue(state, 0); field.SetValue(character, array);
            var basis = fov.AcquireBaseFovOverride(ownerA, 80); var offset = fov.AcquireOffset(ownerB, -10);
            AssertFov(custom, 75);
            Action refresh = fov.RefreshCameras; Action control = () => GC.KeepAlive(new byte[512]);
            long Measure(Action action, int count)
            {
                using var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "GC.Alloc", 1,
                    Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                for (int i = 0; i < count; ++i) action();
                recorder.Stop(); Assert.IsTrue(recorder.Valid);
                return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
            bool prior = UnityEngine.Profiling.Profiler.enabled; UnityEngine.Profiling.Profiler.enabled = true;
            long positive, events;
            try { Measure(refresh, 20); Measure(control, 1); positive = Measure(control, 1); events = Measure(refresh, 1000); }
            finally { UnityEngine.Profiling.Profiler.enabled = prior; }
            Assert.AreEqual(1, positive); Assert.AreEqual(0, events); AssertFov(custom, 75);
        }
        [Test] public void SubscriberExceptionsCannotEscapeAcquisitionOrCleanup()
        {
            fov.Changed += () => throw new InvalidOperationException("expected FOV subscriber error");
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: expected FOV subscriber error"));
            FirstPersonFov.FovOverride a = null; Assert.DoesNotThrow(() => a = fov.AcquireBaseFovOverride(ownerA, 80));
            Assert.IsTrue(a.IsActive);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: expected FOV subscriber error"));
            Assert.DoesNotThrow(() => a.Dispose()); AssertFov(walk, 60);
            // Teardown disables an available component and notifies once as well.
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: expected FOV subscriber error"));
            fov.enabled = false;
        }
        [Test] public void ReentrantDisableAndReleaseStopStaleNotificationDispatch()
        {
            int stale = 0;
            Action disable = () => fov.enabled = false;
            Action later = () => ++stale;
            fov.Changed += disable; fov.Changed += later;
            var expired = fov.AcquireBaseFovOverride(ownerA, 80); Assert.IsFalse(expired.IsActive); Assert.AreEqual(0, stale); AssertFov(walk, 60);
            fov.Changed -= disable; fov.Changed -= later; fov.enabled = true;
            var live = fov.AcquireBaseFovOverride(ownerA, 80);
            Action release = () => live.Dispose(); fov.Changed += release; fov.Changed += later;
            live.TrySet(85); Assert.IsFalse(live.IsActive); Assert.AreEqual(0, stale); AssertFov(walk, 60);
            fov.Changed -= release; fov.Changed -= later;
        }
        [UnityTest] public IEnumerator RealBrainUsesComposedGaitLensAndRestoresWithoutOutputCameraWrites()
        {
            output = new GameObject("isolated output camera"); var camera = output.AddComponent<Camera>(); camera.enabled = false;
            var brain = output.AddComponent<CinemachineBrain>(); brain.ChannelMask = OutputChannels.Channel15;
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
            var settings = fov.AcquireBaseFovOverride(ownerA, 80); var zoom = fov.AcquireOffset(ownerB, -5);
            brain.ManualUpdate(); yield return null; brain.ManualUpdate(); Assert.That(camera.fieldOfView, Is.EqualTo(75).Within(.01f));
            walk.gameObject.SetActive(false); sprint.gameObject.SetActive(true); brain.ManualUpdate(); yield return null; brain.ManualUpdate();
            Assert.That(camera.fieldOfView, Is.EqualTo(85).Within(.01f));
            settings.Dispose(); AssertFov(sprint, 65); yield return null; brain.ManualUpdate(); Assert.That(camera.fieldOfView, Is.EqualTo(65).Within(.01f));
            zoom.Dispose(); AssertFov(sprint, 70); yield return null; brain.ManualUpdate(); Assert.That(camera.fieldOfView, Is.EqualTo(70).Within(.01f));
        }
    }
}
