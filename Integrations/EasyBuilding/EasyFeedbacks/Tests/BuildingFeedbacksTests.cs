using System.Collections;
using System.Collections.Generic;
using EasyFeedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace EasyBuilding.Integrations.Tests
{
    public sealed class BuildingTestFeedbackProvider : MonoBehaviour, IFeedbackContextProvider
    {
        public Gamepad Gamepad;
        public int Calls;
        public FeedbackContext Last;
        public FeedbackContext Prepare(FeedbackContext context) { Calls++; Last = context; return context.WithDefaultGamepad(Gamepad); }
    }
    public sealed class BuildingFeedbacksTests
    {
        private readonly List<Object> objects = new List<Object>();
        private readonly List<InputDevice> devices = new List<InputDevice>();
        private Gamepad previousGamepad;
        [SetUp] public void Setup() => previousGamepad = Gamepad.current;
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private T Asset<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }
        private Gamepad Pad() { var pad = InputSystem.AddDevice<Gamepad>(); devices.Add(pad); return pad; }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]); objects.Clear();
            foreach (var device in devices) if (device.added) InputSystem.RemoveDevice(device); devices.Clear();
            if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent(); yield return null;
        }
        [UnityTest] public IEnumerator ContextProviderPreservesAuthoredValuesAndExplicitGamepad()
        {
            var adapter = Go("World").AddComponent<BuildingFeedbacks>(); var provider = Go("Player").AddComponent<BuildingTestFeedbackProvider>(); provider.Gamepad = Pad();
            var explicitPad = Pad(); var source = Go("Source").transform; var follow = Go("Follow").transform;
            var context = new FeedbackContext(new Vector3(3, 4, 5), Quaternion.Euler(0, 40, 0), source, follow, 0.4f, 0.6f, new Vector3(2, 3, 4), FeedbackPositionMode.FollowTransform);
            Assert.That(adapter.PrepareContext(context), Is.EqualTo(context)); adapter.ContextProvider = provider;
            var prepared = adapter.PrepareContext(context);
            Assert.That(provider.Last, Is.EqualTo(context)); Assert.That(prepared, Is.EqualTo(context.WithGamepad(provider.Gamepad)));
            var explicitContext = context.WithGamepad(explicitPad);
            Assert.That(adapter.PrepareContext(explicitContext), Is.EqualTo(explicitContext)); Assert.That(provider.Calls, Is.EqualTo(2));
            var noGamepad = context.WithoutGamepad(); Assert.That(adapter.PrepareContext(noGamepad), Is.EqualTo(noGamepad));
            provider.Gamepad = null; explicitPad.MakeCurrent(); var keyboardOnly = adapter.PrepareContext(context);
            Assert.That(keyboardOnly, Is.EqualTo(noGamepad)); Assert.That(keyboardOnly.HasExplicitGamepadTarget, Is.True); Assert.That(keyboardOnly.TargetGamepad, Is.Null);
            Assert.That(provider.Calls, Is.EqualTo(4));
            provider.enabled = false; Assert.That(adapter.PrepareContext(context), Is.EqualTo(context)); Assert.That(provider.Calls, Is.EqualTo(4));
            adapter.ContextProvider = Go("Wrong provider").AddComponent<BuildingActor>(); Assert.That(adapter.ConfigurationError, Is.Not.Empty);
            Assert.That(adapter.PrepareContext(context), Is.EqualTo(context)); yield return null;
        }
        [UnityTest] public IEnumerator PlacementAndRemovalUseProviderOnceAndUnsubscribeWhenDisabled()
        {
            var adapter = Go("World").AddComponent<BuildingFeedbacks>(); var world = adapter.GetComponent<BuildingSystem>();
            var provider = Go("Player").AddComponent<BuildingTestFeedbackProvider>(); adapter.ContextProvider = provider; adapter.Placed = adapter.Removed = Asset<FeedbackPreset>();
            var definition = Asset<BuildingDefinition>(); definition.Prefab = Go("Prefab"); definition.Prefab.SetActive(false); definition.RequireSurface = false;
            var request = new PlacementRequest(new Vector3(74000, 0, 0), Quaternion.Euler(0, 90, 0));
            Assert.That(world.TryPlace(definition, request, out var placed, out var result), Is.True, result.Reason);
            Assert.That(provider.Calls, Is.EqualTo(1)); Assert.That(provider.Last.WorldPosition, Is.EqualTo(request.Position));
            Assert.That(provider.Last.Rotation, Is.EqualTo(placed.transform.rotation));
            Assert.That(Quaternion.Angle(provider.Last.Rotation, request.Rotation), Is.LessThan(0.001f));
            Assert.That(provider.Last.Source, Is.Null); Assert.That(provider.Last.Follow, Is.Null); Assert.That(provider.Last.Intensity, Is.EqualTo(1));
            world.Remove(placed); Assert.That(provider.Calls, Is.EqualTo(2)); adapter.enabled = false;
            Assert.That(world.TryPlace(definition, request, out _, out result), Is.True, result.Reason); Assert.That(provider.Calls, Is.EqualTo(2)); yield return null;
        }
    }
}
