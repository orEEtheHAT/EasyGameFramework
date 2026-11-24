using System.Collections;
using System.Collections.Generic;
using EasyBuilding.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

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
            Assert.That(GameObject.Find("EasyBuilding Palette").GetComponentInChildren<TMP_InputField>().textComponent.font,
                Is.SameAs(TMP_Settings.defaultFontAsset));
            ui.Palette.Filter("Building 199"); yield return null;
            Assert.That(ui.Palette.Visible.Count, Is.EqualTo(1)); Assert.That(ui.Palette.Select(catalog.Buildings[199].Id), Is.True);
            Assert.That(placer.Session.Selected, Is.SameAs(catalog.Buildings[199]));
            ui.SetMenuVisible(false); yield return null;
            Assert.That(placer.Session.Selected, Is.SameAs(catalog.Buildings[199]));
            placer.Session.Cancel(); ui.MessageResolver = new TestMessages(); yield return null;
            bool localized = false; foreach (var text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None)) if (text.text == "Localized NoSelection") localized = true;
            Assert.That(localized, Is.True, "The palette should consume the injected localization adapter.");
        }
        [UnityTest] public IEnumerator TmpFiltersAcceptCyrillicAndNavigateWithKeyboard()
        {
            var prefab = Go("Prefab");
            prefab.SetActive(false);
            var bridge = Definition(prefab, "Мост", "Постройки");
            var ornament = Definition(prefab, "Мостик", "Декор");
            var wall = Definition(prefab, "Стена", "Постройки");
            var catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
            objects.Add(catalog);
            var definitions = new List<BuildingDefinition> { bridge, ornament, wall };
            for (int i = 0; i < 8; i++) definitions.Add(Definition(prefab, "Extra " + i, "Extra category " + i));
            catalog.Buildings = definitions.ToArray();

            Assert.That(TMP_Settings.LoadDefaultSettings(), Is.Not.Null);
            TMP_FontAsset projectFont = TMP_Settings.defaultFontAsset;
            Assert.That(projectFont, Is.Not.Null);
            // The override has its own empty atlas. Every visible glyph must come from the project's fallback chain.
            var overrideFont = Object.Instantiate(projectFont);
            var emptyAtlas = new Texture2D(1, 1, TextureFormat.Alpha8, false);
            overrideFont.atlasTextures = new[] { emptyAtlas };
            overrideFont.material = new Material(projectFont.material) { mainTexture = emptyAtlas };
            objects.Add(overrideFont);
            Assert.That(overrideFont.characterTable, Is.Not.SameAs(projectFont.characterTable));
            overrideFont.characterTable.Clear();
            overrideFont.atlasPopulationMode = AtlasPopulationMode.Static;
            overrideFont.fallbackFontAssetTable = new List<TMP_FontAsset> { projectFont };
            overrideFont.ReadFontAssetDefinition();
            Assert.That(overrideFont.HasCharacter('м'), Is.False, "The primary fixture font must need a Cyrillic fallback.");

            var root = Go("Controls");
            var placer = root.AddComponent<BuildingPlacer>();
            placer.ShowHelp = false;
            placer.ExternalTargeting = true;
            var ui = root.AddComponent<BuildingPaletteUI>();
            ui.Catalog = catalog;
            ui.Controller = placer;
            ui.PreferencesKey = "";
            ui.Font = overrideFont;
            ui.MenuVisible = false;
            yield return null;
            yield return null;

            Assert.That(ui.ConfigurationError, Is.Null);
            var canvas = GameObject.Find("EasyBuilding Palette");
            Assert.That(canvas, Is.Not.Null);
            Assert.That(canvas.GetComponentsInChildren<Text>(true), Is.Empty);
            Assert.That(canvas.GetComponentsInChildren<InputField>(true), Is.Empty);
            Assert.That(canvas.GetComponentsInChildren<Dropdown>(true), Is.Empty);
            var search = canvas.GetComponentInChildren<TMP_InputField>(true);
            var dropdown = canvas.GetComponentInChildren<TMP_Dropdown>(true);
            Assert.That(search, Is.Not.Null);
            Assert.That(dropdown, Is.Not.Null);
            Assert.That(search.textViewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(search.textComponent.rectTransform.parent, Is.SameAs(search.textViewport));
            Assert.That(search.placeholder, Is.TypeOf<TextMeshProUGUI>());
            Assert.That(search.placeholder.rectTransform.parent, Is.SameAs(search.textViewport));
            Assert.That(search.textComponent.font, Is.SameAs(overrideFont));
            Assert.That(dropdown.captionText.font, Is.SameAs(overrideFont));
            Assert.That(dropdown.itemText.font, Is.SameAs(overrideFont));
            var templateScroll = dropdown.template.GetComponent<ScrollRect>();
            Assert.That(templateScroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(templateScroll.content.parent, Is.SameAs(templateScroll.viewport));
            Assert.That(dropdown.itemText.GetComponentInParent<Toggle>(true).transform.parent, Is.SameAs(templateScroll.content));

            yield return Press(Key.B);
            Assert.That(ui.MenuVisible, Is.True);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(search.gameObject));
            Assert.That(search.isFocused, Is.False, "Opening the catalog should select search without entering text mode.");
            yield return Press(Key.Enter);
            Assert.That(search.isFocused, Is.True);
            yield return Press(Key.B);
            yield return Press(Key.Digit1);
            Assert.That(ui.MenuVisible, Is.True, "Editing a filter must not consume the menu key as a shortcut.");
            Assert.That(placer.Session.Selected, Is.Null, "Editing a filter must not select a hotbar slot.");

            const string query = "мост";
            foreach (char c in query) search.ProcessEvent(new Event { type = EventType.KeyDown, character = c });
            // ProcessEvent supplies the text edit directly. Normally OnUpdateSelected also refreshes the label.
            search.ForceLabelUpdate();
            yield return null;
            Assert.That(search.text, Is.EqualTo(query));
            Assert.That(search.placeholder.enabled, Is.False, "The placeholder must hide after text input.");
            CollectionAssert.AreEquivalent(new[] { bridge, ornament }, ui.Palette.Visible);
            AssertRenderedByFallback(search.textComponent, query, overrideFont);

            yield return Press(Key.Enter);
            Assert.That(search.isFocused, Is.False, "Submitting the search should finish editing.");
            yield return Press(Key.DownArrow);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(dropdown.gameObject));
            yield return Press(Key.Enter);
            Assert.That(dropdown.IsExpanded, Is.True);
            var popup = dropdown.transform.Find("Dropdown List").GetComponent<ScrollRect>();
            Assert.That(popup.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(popup.content.rect.height, Is.GreaterThan(popup.viewport.rect.height));
            int bridgeCategory = dropdown.options.FindIndex(option => option.text == bridge.Category);
            Assert.That(bridgeCategory, Is.GreaterThan(6), "Keyboard navigation must cross the edge of the dropdown viewport.");
            for (int i = 0; i < bridgeCategory; i++) yield return Press(Key.DownArrow);
            var focusedOption = EventSystem.current.currentSelectedGameObject.GetComponent<Toggle>();
            Assert.That(focusedOption.transform.parent, Is.SameAs(popup.content));
            Assert.That(popup.content.anchoredPosition.y, Is.GreaterThan(0), "The dropdown must reveal the option reached by keyboard.");
            var focusedBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(popup.viewport, focusedOption.transform);
            Assert.That(focusedBounds.min.y, Is.GreaterThanOrEqualTo(popup.viewport.rect.yMin - 0.5f));
            Assert.That(focusedBounds.max.y, Is.LessThanOrEqualTo(popup.viewport.rect.yMax + 0.5f));
            var optionLabel = focusedOption.GetComponentInChildren<TextMeshProUGUI>();
            Assert.That(optionLabel.text, Is.EqualTo(bridge.Category));
            AssertRenderedByFallback(optionLabel, bridge.Category, overrideFont);
            yield return Press(Key.Enter);
            Assert.That(dropdown.captionText.text, Is.EqualTo(bridge.Category));
            CollectionAssert.AreEqual(new[] { bridge }, ui.Palette.Visible);
            double deadline = Time.realtimeSinceStartupAsDouble + 2;
            while (dropdown.IsExpanded && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(dropdown.IsExpanded, Is.False);
            Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(dropdown.gameObject));
            yield return Press(Key.DownArrow);
            Assert.That(EventSystem.current.currentSelectedGameObject.GetComponentInChildren<TMP_Text>().text, Is.EqualTo(bridge.Name));
            yield return Press(Key.Enter);
            Assert.That(placer.Session.Selected, Is.SameAs(bridge));
        }
        private BuildingDefinition Definition(GameObject prefab, string name, string category)
        {
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            objects.Add(definition);
            definition.Prefab = prefab;
            definition.RequireSurface = false;
            definition.DisplayName = name;
            definition.Category = category;
            return definition;
        }
        private IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
        private static void AssertRenderedByFallback(TMP_Text text, string expected, TMP_FontAsset primary)
        {
            text.ForceMeshUpdate();
            Assert.That(text.textInfo.characterCount, Is.GreaterThanOrEqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                var character = text.textInfo.characterInfo[i];
                Assert.That(character.textElement.unicode, Is.EqualTo((uint)expected[i]), $"Expected the {expected[i]} glyph, not a replacement character.");
                Assert.That(character.fontAsset, Is.Not.SameAs(primary));
            }
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
