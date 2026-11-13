using System.Collections;
using System.Collections.Generic;
using EasyBuilding.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace EasyBuilding.Tests.UI
{
    public sealed class PaletteTests
    {
        private readonly List<Object> objects = new List<Object>();
        private readonly HashSet<int> previousEvents = new HashSet<int>();
        private Keyboard keyboard;
        private Keyboard previousKeyboard;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.UpdateMode updateMode;
        private bool runInBackground;
#if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
#endif
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void Setup()
        {
            updateMode = InputSystem.settings.updateMode; InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            runInBackground = Application.runInBackground; Application.runInBackground = true;
            background = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode; InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            previousKeyboard = Keyboard.current; keyboard = InputSystem.AddDevice<Keyboard>(); previousEvents.Clear();
            foreach (var events in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) previousEvents.Add(events.GetInstanceID());
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]); objects.Clear();
            foreach (var events in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!previousEvents.Contains(events.GetInstanceID()) && events.name == "EasyBuilding UI Events") Object.Destroy(events.gameObject);
            InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior = background;
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            InputSystem.settings.updateMode = updateMode; Application.runInBackground = runInBackground;
#if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
#endif
            yield return null;
        }
        [UnityTest] public IEnumerator PaletteCreatesCanvasAndVirtualizesTwoHundredBuildings()
        {
            var root = Go("Controls"); root.AddComponent<BuildingSystem>(); var placer = root.AddComponent<BuildingPlacer>(); placer.ShowHelp = false;
            placer.ViewCamera = Go("Camera").AddComponent<Camera>(); var prefab = Go("Prefab"); prefab.SetActive(false);
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>(); objects.Add(catalog); catalog.Buildings = new BuildingDefinition[200];
            for (int i = 0; i < catalog.Buildings.Length; i++)
            {
                var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab; definition.RequireSurface = false;
                definition.DisplayName = "Building " + i.ToString("D3"); catalog.Buildings[i] = definition;
            }
            var ui = root.AddComponent<BuildingPaletteUI>(); ui.Catalog = catalog; ui.Controller = placer; ui.PreferencesKey = "";
            yield return null; yield return null;
            Assert.That(ui.ConfigurationError, Is.Null); Assert.That(ui.Palette.Visible.Count, Is.EqualTo(200));
            Assert.That(ui.PooledRowCount, Is.GreaterThan(0).And.LessThan(40)); Assert.That(EventSystem.current, Is.Not.Null);
            ui.Palette.Filter("Building 199"); yield return null;
            Assert.That(ui.Palette.Visible.Count, Is.EqualTo(1)); Assert.That(ui.Palette.Select(catalog.Buildings[199].Id), Is.True);
            Assert.That(placer.Session.Selected, Is.SameAs(catalog.Buildings[199]));
            ui.SetMenuVisible(false); yield return null;
            Assert.That(placer.Session.Selected, Is.SameAs(catalog.Buildings[199]));
            placer.Session.Cancel(); ui.MessageResolver = new TestMessages(); yield return null;
            bool localized = false; foreach (var text in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None)) if (text.text == "Localized NoSelection") localized = true;
            Assert.That(localized, Is.True, "The palette should consume the injected localization adapter.");
        }
        private sealed class TestMessages : IBuildingMessageResolver
        { public string Resolve(in PlacementResult result) => "Localized " + result.Failure; }
        [UnityTest] public IEnumerator RebindingPersistsAcrossFreshRuntimeActionIdsAndKeepsOtherInputGates()
        {
            // Drive real Input System updates explicitly; suite load and focus must not decide when the event is processed.
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            using var source = new TestInput(); source.Action.Enable(); object otherGate = new object(); source.SetInputBlocked(otherGate, true);
            using var rebind = new BuildingRebinding(source); int completions = 0; bool succeeded = false;
            rebind.Finished += success => { completions++; succeeded = success; };
            rebind.Begin(BuildingControl.Confirm);
            Assert.That(source.Action.enabled, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.T));
            double deadline = Time.realtimeSinceStartupAsDouble + 5;
            while (rebind.IsRunning && Time.realtimeSinceStartupAsDouble < deadline)
            { InputSystem.Update(); yield return null; }
            Assert.That(completions, Is.EqualTo(1), "Rebinding must finish from the queued key, without forcing completion.");
            Assert.That(succeeded, Is.True);
            Assert.That(rebind.IsRunning, Is.False); Assert.That(source.Action.enabled, Is.True); Assert.That(source.InputBlocked, Is.True);
            Assert.That(source.Action.bindings[0].overridePath, Is.EqualTo("<Keyboard>/t"));
            var saved = JsonUtility.FromJson<BuildingInputPreferences>(JsonUtility.ToJson(BuildingInputPreferences.Capture(source)));
            using var target = new TestInput(); saved.Apply(target); Assert.That(target.Action.bindings[0].overridePath, Is.EqualTo("<Keyboard>/t"));
            source.SetInputBlocked(otherGate, false); Assert.That(source.InputBlocked, Is.False);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        }
        [UnityTest] public IEnumerator PaletteFollowsSessionReplacementAndReleasesOnlyItsOwnInputGate()
        {
            var root = Go("Controls"); var world = root.AddComponent<BuildingSystem>();
            var placer = root.AddComponent<BuildingPlacer>(); placer.ShowHelp = false; placer.ExternalTargeting = true;
            using var first = world.CreateSession(); using var second = world.CreateSession(); placer.SetSession(first);
            var prefab = Go("Prefab"); prefab.SetActive(false);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab; definition.RequireSurface = false;
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>(); objects.Add(catalog); catalog.Buildings = new[] { definition };
            var ui = root.AddComponent<BuildingPaletteUI>(); ui.Catalog = catalog; ui.Controller = placer; ui.PreferencesKey = "";
            yield return null; yield return null;
            Assert.That(ui.Palette.Session, Is.SameAs(first)); ui.Palette.ToggleFavorite(definition.Id); ui.Palette.AssignSlot(2, definition.Id);
            first.Select(definition); placer.SetSession(second); yield return null;
            Assert.That(ui.Palette.Session, Is.SameAs(second)); Assert.That(ui.Palette.IsFavorite(definition.Id), Is.True);
            Assert.That(ui.Palette.GetSlotId(2), Is.EqualTo(definition.Id)); Assert.That(ui.Palette.SelectSlot(2), Is.True);
            Assert.That(first.Selected, Is.SameAs(definition)); Assert.That(second.Selected, Is.SameAs(definition));
            object otherGate = new object(); placer.SetInputBlocked(otherGate, true);
            ui.enabled = false; Assert.That(placer.InputBlocked, Is.True); Assert.That(second.IsDisposed, Is.False); Assert.That(second.Selected, Is.SameAs(definition));
            placer.SetInputBlocked(otherGate, false); Assert.That(placer.InputBlocked, Is.False);
            ui.enabled = true; yield return null;
            Assert.That(ui.Palette, Is.Not.Null, "Re-enabling UI must reconnect without a manual Initialize call.");
            Assert.That(ui.Palette.Session, Is.SameAs(second)); Assert.That(second.Selected, Is.SameAs(definition));
            second.Dispose(); yield return null;
            Assert.That(ui.Palette.Session, Is.Null); Assert.That(ui.Palette.Select(definition.Id), Is.False);
            placer.SetSession(first); yield return null;
            Assert.That(ui.Palette.Session, Is.SameAs(first)); Assert.That(ui.Palette.Select(definition.Id), Is.True);
        }
        private sealed class TestInput : IBuildingPlacementInput, System.IDisposable
        {
            internal readonly InputAction Action = new InputAction("Confirm", InputActionType.Button, "<Keyboard>/r");
            private readonly HashSet<object> blocks = new HashSet<object>(); private bool manuallyBlocked;
            public BuildingSession Session => null;
            public PlacementResult PreviewResult => default;
            public string Status => "";
            public bool InputBlocked { get => manuallyBlocked || blocks.Count != 0; set => manuallyBlocked = value; }
            public void SetInputBlocked(object owner, bool blocked) { if (blocked) blocks.Add(owner); else blocks.Remove(owner); }
            public InputAction InputActionFor(BuildingControl control) => control == BuildingControl.Confirm ? Action : null;
            public void Dispose() => Action.Dispose();
        }
    }
}
