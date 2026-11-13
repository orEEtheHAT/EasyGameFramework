using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace EasyBuilding.PlayModeTests
{
    public sealed class BuildingLifecycleTests
    {
        private GameObject controls, prefab, ground, camera;
        private BuildingDefinition definition;
        private BuildingSystem system;
        private BuildingPlacer placer;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.BackgroundBehavior previousBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;
#endif
        private readonly Vector3 origin = new Vector3(20000, 0, 0);

        [UnitySetUp] public IEnumerator SetUp()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            prefab = new GameObject("Source"); prefab.SetActive(false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(prefab.transform, false); cube.transform.localPosition = Vector3.up * 0.5f; cube.layer = 31;
            prefab.AddComponent<LifecycleProbe>(); LifecycleProbe.Awakes = 0;
            definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = prefab; definition.ObstacleLayers = 1 << 31; definition.SurfaceLayers = 1 << 31;
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.layer = 31; ground.transform.position = origin + Vector3.down * 0.5f; ground.transform.localScale = new Vector3(20, 1, 20);
            camera = new GameObject("Test camera"); var view = camera.AddComponent<Camera>(); camera.transform.position = origin + Vector3.up * 10; camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            controls = new GameObject("Controls"); system = controls.AddComponent<BuildingSystem>(); placer = controls.AddComponent<BuildingPlacer>();
            placer.ViewCamera = view; placer.AimFromScreenCenter = true; placer.ShowHelp = false; placer.StartingBuilding = definition;
            yield return null; yield return null;
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            if (system != null)
                foreach (var record in system.CaptureState().Buildings) { system.TryGet(record.InstanceId, out var building); system.Remove(building); }
            Object.Destroy(controls); Object.Destroy(prefab); Object.Destroy(ground); Object.Destroy(camera); Object.Destroy(definition);
            if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.backgroundBehavior = previousBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
#endif
            yield return null;
        }
        [UnityTest] public IEnumerator PreviewDoesNotRunPrefabScriptsAndConfirmActivatesOnce()
        {
            Assert.That(placer.PreviewResult.IsValid, Is.True, placer.Status);
            Assert.That(LifecycleProbe.Awakes, Is.Zero);
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 });
            yield return null; yield return null;
            Assert.That(system.Count, Is.EqualTo(1)); Assert.That(LifecycleProbe.Awakes, Is.EqualTo(1));
            Assert.That(system.Selected, Is.Null);
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1 }); yield return null;
            Assert.That(system.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator InputSystemRotatesAndCancelsWithoutCreatingObject()
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R)); yield return null; yield return null;
            Assert.That(Quaternion.Angle(placer.PreviewResult.Pose.rotation, Quaternion.Euler(0, 90, 0)), Is.LessThan(0.1));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); yield return null; yield return null;
            Assert.That(system.Selected, Is.Null); Assert.That(system.Count, Is.Zero); Assert.That(LifecycleProbe.Awakes, Is.Zero);
        }
        [UnityTest] public IEnumerator RemovalDisablesCollidersBeforeDeferredDestroy()
        {
            var request = new PlacementRequest(origin, Quaternion.identity);
            Assert.That(system.TryConfirm(request, out var first, out var result), Is.True, result.Reason);
            Assert.That(system.Remove(first), Is.True); system.Select(definition);
            Assert.That(system.TryConfirm(request, out _, out result), Is.True, result.Reason);
            yield return null; Assert.That(system.Count, Is.EqualTo(1));
        }
        [UnityTest] public IEnumerator ConstructionNeverInstantiatesFinishedGameplayUntilCompletion()
        {
            definition.WorkRequired = 2;
            Assert.That(system.TryConfirm(new PlacementRequest(origin, Quaternion.identity), out var site, out var result), Is.True, result.Reason);
            yield return null; Assert.That(LifecycleProbe.Awakes, Is.Zero); Assert.That(site.GetComponent<LifecycleProbe>(), Is.Null);
            string id = site.InstanceId;
            Assert.That(system.TryWork(site, 1, out _, out result), Is.True, result.Reason); Assert.That(LifecycleProbe.Awakes, Is.Zero);
            Assert.That(system.TryWork(site, 1, out var complete, out result), Is.True, result.Reason);
            yield return null; Assert.That(LifecycleProbe.Awakes, Is.EqualTo(1)); Assert.That(complete.InstanceId, Is.EqualTo(id));
        }
    }
}
