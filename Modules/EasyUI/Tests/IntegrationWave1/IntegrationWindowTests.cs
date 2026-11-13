using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyInteraction.Integrations.Building;
using EasyInteraction.Integrations.Combat;
using EasyInteraction.Integrations.Movement;
using EasyMovement.FirstPerson;
using EasyBuilding;
using EasyCombat;
using EasyFeedbacks;
using EasyUI.Integrations;
using EasyUI.Integrations.Building;
using EasyUI.Integrations.Input;
using EasyUI.Integrations.Interaction;
using EasyUI.Integrations.Feedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyUI.IntegrationWave1.Tests
{
    public sealed class MenuTestAction : InteractionAction
    {
        public int Executed;
        public bool Sustained;
        public bool Available = true;
        public override InteractionAvailability GetAvailability(InteractionActor actor) => Available ? InteractionAvailability.Available : InteractionAvailability.Unavailable(InteractionFailure.Cancelled, "Unavailable");
        protected override bool OnExecute(InteractionActor actor) { Executed++; return Sustained; }
    }
    public sealed class MenuInputSource : MonoBehaviour, IInteractionInputSource
    {
        public string BindingLabel { get; set; } = "X";
        public Sprite BindingIcon { get; set; }
        public bool ToggleInsteadOfHold { get; set; }
        public event Action PresentationChanged;
        public void Refresh() => PresentationChanged?.Invoke();
    }
    public sealed class WindowFeedbackContextProbe : MonoBehaviour, IFeedbackContextProvider
    {
        public int Calls;
        public Transform LastSource;
        public Gamepad Target;
        public FeedbackContext Prepare(FeedbackContext context) { Calls++; LastSource = context.Source; return context.WithDefaultGamepad(Target); }
    }
    public sealed class IntegrationWindowTests
    {
        private readonly List<Object> objects = new List<Object>();
        private Keyboard keyboard;
        private Gamepad gamepad;
        private PlayerInputSettings player;
        private GameObject root;
        private WindowManager manager;
        private InputSettings.BackgroundBehavior background;
        private bool runInBackground;
        private FeedbackSettingsState audio;
        private bool restoreLocalization, savedLocalizationReady;
        private LocalizationSettings savedLocalization, unconfiguredLocalization;
        private EasyLocalization.LocalizationConfig savedLocalizationConfig;
        private sealed class NoLocales : ILocalesProvider
        {
            public List<Locale> Locales { get; } = new List<Locale>();
            public Locale GetLocale(LocaleIdentifier id) => null;
            public void AddLocale(Locale locale) => Locales.Add(locale);
            public bool RemoveLocale(Locale locale) => Locales.Remove(locale);
        }
        private void UseUnconfiguredLocalization()
        {
            savedLocalization = LocalizationSettings.GetInstanceDontCreateDefault();
            savedLocalizationReady = savedLocalization && EasyLocalization.Localization.IsReady;
            savedLocalizationConfig = savedLocalizationReady ? EasyLocalization.Localization.Configuration : null;
            restoreLocalization = true;
            EasyLocalization.Localization.Reset();
            unconfiguredLocalization = ScriptableObject.CreateInstance<LocalizationSettings>();
            unconfiguredLocalization.SetAvailableLocales(new NoLocales());
            unconfiguredLocalization.GetStartupLocaleSelectors().Clear();
            LocalizationSettings.Instance = unconfiguredLocalization;
            Assert.IsFalse(EasyLocalization.Localization.IsReady);
        }
        [UnityTearDown] public IEnumerator RestoreLocalizationEnvironment()
        {
            if (!restoreLocalization) yield break;
            try
            {
                // Safe in either NUnit teardown order: views stop while temporary settings are current.
                if (manager) manager.CloseAll();
                foreach (var value in objects) if (value is GameObject go && go) go.SetActive(false);
                EasyLocalization.Localization.Reset(); LocalizationSettings.Instance = savedLocalization;
                if (savedLocalizationReady)
                {
                    var restore = EasyLocalization.Localization.ReinitializeAsync(savedLocalizationConfig);
                    float deadline = Time.realtimeSinceStartup + 15;
                    while (!restore.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                    Assert.IsTrue(restore.IsCompleted, "Original localization restoration timed out.");
                    if (restore.IsFaulted) throw restore.Exception.GetBaseException();
                    Assert.IsFalse(restore.IsCanceled);
                }
                Assert.AreSame(savedLocalization, LocalizationSettings.GetInstanceDontCreateDefault());
                Assert.AreEqual(savedLocalizationReady, EasyLocalization.Localization.IsReady);
            }
            finally
            {
                if (unconfiguredLocalization) Object.DestroyImmediate(unconfiguredLocalization);
                unconfiguredLocalization = null; savedLocalization = null; savedLocalizationConfig = null; restoreLocalization = false;
            }
        }
        #if UNITY_EDITOR
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        #endif
        private GameObject Go(string name, bool active = true, params Type[] types)
        { var go = new GameObject(name, types); go.SetActive(active); objects.Add(go); return go; }
        private T Asset<T>() where T : ScriptableObject { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }
        [SetUp] public void Setup()
        {
            background = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            runInBackground = Application.runInBackground; Application.runInBackground = true;
            #if UNITY_EDITOR
            editorInput = InputSystem.settings.editorInputBehaviorInPlayMode; InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            #endif
            audio = FeedbackSettings.Capture();
            keyboard = InputSystem.AddDevice<Keyboard>(); gamepad = InputSystem.AddDevice<Gamepad>();
            root = Go("Wave player", false); player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false; root.SetActive(true); player.PairDevices(keyboard, gamepad);
            manager = Go("Wave UI", true, typeof(RectTransform), typeof(WindowManager)).GetComponent<WindowManager>();
            var provider = manager.gameObject.AddComponent<EasyInputWindowProvider>(); provider.Player = player; manager.InputProvider = provider;
            manager.EventSystem = Go("Wave EventSystem", true, typeof(EventSystem)).GetComponent<EventSystem>(); manager.Initialize();
        }
        [TearDown] public void Cleanup()
        {
            if (manager) manager.CloseAll();
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (gamepad != null && gamepad.added) InputSystem.RemoveDevice(gamepad);
            FeedbackSettings.Restore(audio); InputSystem.settings.backgroundBehavior = background; Application.runInBackground = runInBackground;
            #if UNITY_EDITOR
            InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
            #endif
        }
        private UIWindow Window(string name)
        {
            var go = Go(name, false, typeof(RectTransform)); var window = go.AddComponent<UIWindow>();
            window.Policy = new WindowPolicy { BlockGameplay = true, Cursor = WindowCursor.Visible, FadeIn = 0, FadeOut = 0 }; return window;
        }
        private IEnumerator Ready() { yield return null; yield return null; yield return null; yield return null; }
        private BuildingPlacer Placer(out BuildingActor actor)
        {
            actor = root.AddComponent<BuildingActor>(); var go = Go("World", false); go.AddComponent<BuildingSystem>();
            var placer = go.AddComponent<BuildingPlacer>(); placer.Actor = actor; placer.ExternalTargeting = true; placer.ShowHelp = false; go.SetActive(true); return placer;
        }
        private BuildingDefinition Building(string name)
        {
            var value = Asset<BuildingDefinition>(); value.name = name; value.DisplayName = name;
            value.Prefab = Go(name + " prefab", false); value.RequireSurface = false; value.CheckObstacles = false; return value;
        }
        private InteractionPresenter Presenter(out MenuTestAction action)
        {
            var actor = root.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false; actor.View = root.transform;
            var presenter = root.AddComponent<InteractionPresenter>(); var input = root.AddComponent<EasyInteractionInput>(); input.Player = player;
            var target = Go("Terminal", false); target.transform.position = root.transform.position + Vector3.forward * 1.5f;
            target.AddComponent<BoxCollider>(); target.AddComponent<InteractionTarget>(); action = target.AddComponent<MenuTestAction>(); action.ActionLabel = "Operate"; target.SetActive(true);
            Physics.SyncTransforms(); actor.RefreshTarget(); presenter.Refresh(); Assert.AreSame(action, actor.SelectedAction); return presenter;
        }
        [UnityTest] public IEnumerator BuildingSelectionUsesReplacedCanonicalSessionAndPreservesOtherWindowLease()
        {
            var placer = Placer(out var actor); var context = root.AddComponent<BuildingInteractionContext>();
            Assert.AreSame(context.GetSession(placer.System), placer.Session);
            var catalog = Asset<BuildingCatalog>(); catalog.Buildings = new[] { Building("Wall"), Building("Floor") };
            var parent = manager.Open(Window("Parent")); var window = Window("Building circle"); window.gameObject.AddComponent<RadialMenu>();
            var menu = window.gameObject.AddComponent<BuildingCatalogWindow>(); menu.Bind(placer, catalog); var child = manager.Open(window, parent); yield return Ready();
            var old = placer.Session; var independent = placer.System.CreateSession(actor); placer.SetSession(independent);
            var radial = window.GetComponent<RadialMenu>(); radial.SelectDirection(Vector2.up, .2f); Assert.IsTrue(radial.ConfirmSelection());
            Assert.AreSame(catalog.Buildings[0], independent.Selected); Assert.IsNull(old.Selected); Assert.IsFalse(child.IsOpen);
            Assert.AreEqual(1, player.ContextRequestCount); Assert.IsFalse(player.Action("Gameplay/Jump").enabled);
            manager.Open(window, parent); yield return Ready(); Assert.IsTrue(manager.Back()); Assert.IsNull(independent.Selected); Assert.AreEqual(2, player.ContextRequestCount);
            yield return Ready(); manager.Back(); Assert.IsFalse(independent.IsDisposed); parent.Close(); yield return Ready(); Assert.AreEqual(0, player.ContextRequestCount);
            independent.Dispose();
        }
        [UnityTest] public IEnumerator InteractionMenuShowsSharedBindingAndBackCancelsOperationBeforeClosing()
        {
            var presenter = Presenter(out var action); action.Sustained = true; action.InputGesture = InteractionInputGesture.HoldWhileActive;
            var input = root.GetComponent<EasyInteractionInput>(); input.ToggleInsteadOfHold = true;
            var window = Window("Action menu"); var menu = window.gameObject.AddComponent<InteractionMenuWindow>(); menu.CloseOnSelection = false; menu.Bind(presenter);
            var h = manager.Open(window); yield return Ready();
            player.Action("Gameplay/Interact").ApplyBindingOverride(0, "<Keyboard>/k"); presenter.Refresh(); menu.Refresh();
            Assert.AreEqual(player.GetPresentation("Gameplay/Interact").Text, menu.Prompt.Binding);
            Assert.IsTrue(window.GetComponentsInChildren<Text>().Any(x => x.text.Contains("Toggle") && x.text.Contains(menu.Prompt.Binding)));
            Assert.IsTrue(menu.ActivateSelected()); Assert.IsFalse(menu.ActivateSelected()); Assert.AreEqual(1, action.Executed); Assert.IsTrue(presenter.Actor.GetTicket(action).IsActive);
            Assert.IsTrue(manager.Back()); Assert.IsFalse(presenter.Actor.GetTicket(action).IsActive); Assert.IsTrue(h.IsOpen);
            yield return Ready(); Assert.IsTrue(manager.Back()); Assert.IsFalse(h.IsOpen); Assert.AreEqual(0, player.ContextRequestCount);
        }
        [UnityTest] public IEnumerator FpsBuildingContextSurvivesNestedCatalogAndHeldUiSubmit()
        {
            var placer = Placer(out _); placer.gameObject.SetActive(false);
            var input = placer.gameObject.AddComponent<EasyBuildingInput>(); input.Player = player; input.Placer = placer; input.UseFirstPersonPreset(); placer.gameObject.SetActive(true);
            var catalog = Asset<BuildingCatalog>(); catalog.Buildings = new[] { Building("Floor") }; placer.Select(catalog.Buildings[0]); Assert.IsTrue(input.Enter());
            Assert.AreEqual(1, player.ContextRequestCount);
            var parent = manager.Open(Window("Terminal")); var window = Window("Catalog child"); var menu = window.gameObject.AddComponent<BuildingCatalogWindow>(); menu.Bind(placer, catalog);
            manager.Open(window, parent); yield return Ready(); Assert.AreEqual(3, player.ContextRequestCount);
            Assert.IsTrue(menu.Select(catalog.Buildings[0])); Assert.AreEqual(2, player.ContextRequestCount); Assert.IsTrue(input.IsBuilding);
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South)); InputSystem.Update(); parent.Close(); yield return null;
            Assert.AreEqual(1, player.ContextRequestCount); Assert.IsTrue(input.IsBuilding); Assert.AreSame(catalog.Buildings[0], placer.Session.Selected);
            InputSystem.QueueStateEvent(gamepad, new GamepadState()); InputSystem.Update(); yield return Ready();
            Assert.IsTrue(player.Action("Gameplay/Move").enabled); Assert.IsFalse(player.Action("Gameplay/Jump").enabled); Assert.IsTrue(player.Action("Building/Confirm").enabled);
            input.Exit(); yield return Ready(); Assert.AreEqual(0, player.ContextRequestCount); Assert.IsFalse(placer.Session.IsDisposed);
        }
        [UnityTest] public IEnumerator SelectionCallbacksCanCloseOrReopenMenusWithoutResubscribingOrClosingNewHandle()
        {
            var placer = Placer(out _); var catalog = Asset<BuildingCatalog>(); catalog.Buildings = new[] { Building("Reentry wall") };
            var window = Window("Reentry catalogue"); var menu = window.gameObject.AddComponent<BuildingCatalogWindow>(); menu.Bind(placer, catalog);
            var current = placer.Session; var closed = manager.Open(window); yield return Ready();
            Action<BuildingDefinition> close = _ => manager.CloseAll(); current.SelectionChanged += close;
            Assert.IsTrue(menu.Select(catalog.Buildings[0])); Assert.IsFalse(closed.IsOpen); Assert.AreEqual(0, player.ContextRequestCount);
            Assert.IsNull(typeof(BuildingCatalogWindow).GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(menu), "A closed view must not reacquire its session subscription after the callback.");
            current.SelectionChanged -= close; current.Cancel();
            var oldHandle = manager.Open(window); yield return Ready(); WindowHandle reopened = null;
            Action<BuildingDefinition> reopen = _ => { manager.CloseAll(); reopened = manager.Open(window); }; current.SelectionChanged += reopen;
            Assert.IsTrue(menu.Select(catalog.Buildings[0])); Assert.IsFalse(oldHandle.IsOpen); Assert.IsTrue(reopened.IsOpen); Assert.AreEqual(1, player.ContextRequestCount);
            current.SelectionChanged -= reopen; manager.CloseAll();
            var presenter = Presenter(out var first); var second = first.gameObject.AddComponent<MenuTestAction>(); second.ActionId = "Reentry second"; first.Target.Refresh(); presenter.Actor.RefreshTarget();
            var actionWindow = Window("Reentry actions"); var actionMenu = actionWindow.gameObject.AddComponent<InteractionMenuWindow>(); actionMenu.Bind(presenter);
            var oldActionHandle = manager.Open(actionWindow); yield return Ready(); WindowHandle newActionHandle = null;
            Action<InteractionPrompt> change = prompt => { if (prompt.Action == second) { manager.CloseAll(); newActionHandle = manager.Open(actionWindow); } };
            presenter.Changed += change; Assert.IsTrue(actionMenu.Select(second)); presenter.Changed -= change;
            Assert.IsFalse(oldActionHandle.IsOpen); Assert.IsTrue(newActionHandle.IsOpen); Assert.AreEqual(1, player.ContextRequestCount);
        }
        [UnityTest] public IEnumerator ClosingFromActionStartedReleasesTheUiOwnedHold()
        {
            var presenter = Presenter(out var action); action.Sustained = true; action.InputGesture = InteractionInputGesture.HoldWhileActive;
            var window = Window("Reentry hold"); var menu = window.gameObject.AddComponent<InteractionMenuWindow>(); menu.Bind(presenter); manager.Open(window); yield return Ready();
            action.OnStarted.AddListener(() => manager.CloseAll()); menu.ActivateSelected();
            Assert.AreEqual(0, manager.Count); Assert.AreEqual(0, player.ContextRequestCount); Assert.IsFalse(presenter.Actor.GetTicket(action).IsActive);
        }
        [UnityTest] public IEnumerator InteractionRadialSelectsOnceEvenWhenWindowClosesImmediately()
        {
            var presenter = Presenter(out var first); var second = first.gameObject.AddComponent<MenuTestAction>(); second.ActionId = "Second"; second.ActionLabel = "Second"; first.Target.Refresh(); presenter.Actor.RefreshTarget();
            var window = Window("Actions radial"); var radial = window.gameObject.AddComponent<RadialMenu>(); var menu = window.gameObject.AddComponent<InteractionMenuWindow>(); menu.Bind(presenter);
            var h = manager.Open(window); yield return Ready(); radial.SelectDirection(Vector2.down, .2f);
            Assert.IsTrue(radial.ConfirmSelection()); Assert.IsFalse(radial.ConfirmSelection());
            Assert.AreSame(second, presenter.Actor.SelectedAction); Assert.IsFalse(h.IsOpen); Assert.AreEqual(0, second.Executed);
        }
        [UnityTest] public IEnumerator CatalogScrollFollowsSelectionAndKeepsUiModuleSingle()
        {
            var placer = Placer(out _); var catalog = Asset<BuildingCatalog>(); catalog.Buildings = Enumerable.Range(0, 15).Select(i => Building("Item " + i)).ToArray();
            var window = Window("Long catalog"); var menu = window.gameObject.AddComponent<BuildingCatalogWindow>(); menu.Bind(placer, catalog); menu.CloseOnSelection = false;
            manager.Open(window); yield return Ready(); var buttons = window.GetComponentsInChildren<Button>(); Assert.AreEqual(15, buttons.Length);
            manager.EventSystem.SetSelectedGameObject(buttons[14].gameObject); yield return null; yield return null;
            Assert.Greater(window.GetComponentInChildren<ScrollRect>().content.anchoredPosition.y, 0);
            Assert.AreEqual(1, manager.EventSystem.GetComponents<BaseInputModule>().Count(x => x.enabled));
            Assert.AreEqual(1, manager.GetComponentsInChildren<EventSystem>(true).Length + (manager.EventSystem.transform.IsChildOf(manager.transform) ? 0 : 1));
        }
        [UnityTest] public IEnumerator BindingGlyphAndAvailabilityUpdateWithoutReplacingRadialItems()
        {
            var presenter = Presenter(out var action); var source = root.AddComponent<MenuInputSource>(); presenter.InputSource = source;
            var texture = new Texture2D(2, 2); objects.Add(texture); var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f); objects.Add(sprite);
            var window = Window("Dynamic action radial"); var radial = window.gameObject.AddComponent<RadialMenu>(); var menu = window.gameObject.AddComponent<InteractionMenuWindow>(); menu.Bind(presenter);
            var h = manager.Open(window); yield return Ready(); radial.SelectDirection(Vector2.up, .2f); var item = window.transform.Find("Item " + action.GetInstanceID());
            source.BindingIcon = sprite; source.BindingLabel = "Current glyph"; source.Refresh(); presenter.Refresh(); menu.Refresh();
            Assert.AreSame(sprite, window.transform.Find("Interaction view/Binding glyph").GetComponent<Image>().sprite);
            Assert.AreSame(item, window.transform.Find("Item " + action.GetInstanceID())); Assert.AreEqual(0, radial.SelectedIndex);
            action.Available = false; menu.Refresh(); Assert.AreEqual(-1, radial.SelectedIndex); Assert.AreSame(item, window.transform.Find("Item " + action.GetInstanceID()));
            var text = window.transform.Find("Interaction view/Binding").GetComponent<Text>(); string before = text.text;
            h.Close(); source.BindingLabel = "After close"; source.Refresh(); presenter.Refresh(); yield return null; Assert.AreEqual(before, text.text);
        }
        [UnityTest] public IEnumerator AudioChildUsesRuntimeStateAndRebindCancelLeavesParentContext()
        {
            var window = Window("Controls"); var controls = window.gameObject.AddComponent<InputSettingsWindow>(); var parent = manager.Open(window); yield return Ready();
            controls.BeginRebind("Gameplay/Jump", 0); Assert.IsTrue(manager.Back()); Assert.IsFalse(player.Rebind.IsActive); Assert.IsTrue(parent.IsOpen); yield return Ready();
            var childWindow = Window("Audio"); var settings = childWindow.gameObject.AddComponent<FeedbackSettingsWindow>(); var child = manager.Open(childWindow, parent); yield return Ready();
            var original = settings.CapturedSettings; settings.SetValue(1, .27f); Assert.AreEqual(.27f, FeedbackSettings.Current.MusicVolume, .001f);
            settings.Restore(); Assert.AreEqual(original.MusicVolume, FeedbackSettings.Current.MusicVolume);
            child.Close(); Assert.AreEqual(1, player.ContextRequestCount); parent.Close(); yield return Ready(); Assert.AreEqual(0, player.ContextRequestCount);
        }
        [UnityTest] public IEnumerator FeedbackContextUsesPlayersGamepadAndDisabledProviderFallsBack()
        {
            InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.West)); InputSystem.Update(); yield return null;
            var provider = root.AddComponent<PlayerFeedbackContext>(); provider.Player = player;
            Assert.AreSame(gamepad, player.ActiveGamepad);
            var window = Window("Feedback"); var feedback = window.gameObject.AddComponent<WindowFeedbacks>(); feedback.ContextProvider = provider;
            Assert.AreSame(player.ActiveGamepad, feedback.PrepareContext().TargetGamepad); Assert.AreSame(window.transform, feedback.PrepareContext().Source);
            provider.enabled = false; Assert.IsNull(feedback.PrepareContext().TargetGamepad);
            InputSystem.QueueStateEvent(gamepad, new GamepadState()); InputSystem.Update();
        }
        [UnityTest] public IEnumerator AudioChildInheritsFeedbackBeforeOpenedReusesAndCleansUp()
        {
            UseUnconfiguredLocalization();
            var localizationBefore = UnityEngine.Localization.Settings.LocalizationSettings.GetInstanceDontCreateDefault();
            bool languageWasReady = EasyLocalization.Localization.IsReady;
            var parentOpen = Asset<FeedbackPreset>(); var explicitOpen = Asset<FeedbackPreset>(); var explicitSelect = Asset<FeedbackPreset>(); var explicitConfirm = Asset<FeedbackPreset>();
            var probe = root.AddComponent<WindowFeedbackContextProbe>(); probe.Target = gamepad;
            var audioTemplate = Window("Audio template"); audioTemplate.gameObject.AddComponent<FeedbackSettingsWindow>();
            var authored = audioTemplate.gameObject.AddComponent<WindowFeedbacks>(); authored.Select = explicitSelect; authored.Confirm = explicitConfirm;
            var audioDefinition = Asset<WindowDefinition>(); audioDefinition.Prefab = audioTemplate; audioDefinition.Policy = audioTemplate.Policy.Copy();
            var parentWindow = Window("Integrated controls"); parentWindow.gameObject.AddComponent<InputSettingsWindow>();
            var combined = parentWindow.gameObject.AddComponent<IntegratedSettingsWindow>(); combined.AudioWindow = audioDefinition;
            var parentFeedback = parentWindow.gameObject.AddComponent<WindowFeedbacks>(); parentFeedback.ContextProvider = probe; parentFeedback.Open = parentOpen;
            var parent = manager.Open(parentWindow); yield return Ready(); int previousCalls = probe.Calls;
            Assert.AreSame(localizationBefore, UnityEngine.Localization.Settings.LocalizationSettings.GetInstanceDontCreateDefault());
            Assert.AreEqual(languageWasReady, EasyLocalization.Localization.IsReady, "Opening controls must not initialize localization.");
            if (!languageWasReady)
            {
                Assert.IsFalse(combined.CanChangeLanguage); Assert.IsNotEmpty(combined.LanguageStatus);
                var languageButton = parentWindow.GetComponentsInChildren<Button>().Single(b => b.name == "Language");
                Assert.IsFalse(languageButton.interactable); Assert.That(languageButton.GetComponentInChildren<Text>().text, Does.Contain("unavailable"));
                combined.NextLanguage(); yield return Ready();
                Assert.AreSame(localizationBefore, UnityEngine.Localization.Settings.LocalizationSettings.GetInstanceDontCreateDefault());
                Assert.IsFalse(EasyLocalization.Localization.IsReady);
                var controls = parentWindow.GetComponent<InputSettingsWindow>();
                float sensitivityBefore = player.MouseSensitivity;
                controls.Capture(); player.MouseSensitivity = 1.73f; controls.Restore();
                Assert.AreEqual(sensitivityBefore, player.MouseSensitivity);
                controls.BeginRebind("Gameplay/Jump", 0); Assert.IsTrue(player.Rebind.IsActive); Assert.IsTrue(controls.HandleBack()); Assert.IsFalse(player.Rebind.IsActive);
                yield return Ready();
            }
            combined.OpenAudio(); var first = manager.ActiveWindow; var firstObject = first.Window; var childFeedback = firstObject.GetComponent<WindowFeedbacks>();
            Assert.AreSame(probe, childFeedback.ContextProvider); Assert.AreSame(parentOpen, childFeedback.Open); Assert.AreSame(explicitSelect, childFeedback.Select); Assert.AreSame(explicitConfirm, childFeedback.Confirm);
            Assert.AreEqual(previousCalls + 1, probe.Calls, "Provider must be ready during child Opened."); Assert.AreSame(firstObject.transform, probe.LastSource);
            Assert.AreSame(gamepad, childFeedback.PrepareContext().TargetGamepad); Assert.AreEqual(2, player.ContextRequestCount);
            first.Close(); yield return Ready(); combined.OpenAudio(); Assert.AreSame(firstObject, manager.ActiveWindow.Window); Assert.AreEqual(2, player.ContextRequestCount);
            parent.Close(); yield return null; Assert.IsTrue(firstObject == null); Assert.AreEqual(0, player.ContextRequestCount);
            authored.Open = explicitOpen; manager.Open(parentWindow); yield return Ready(); combined.OpenAudio();
            var secondObject = manager.ActiveWindow.Window; Assert.AreSame(explicitOpen, secondObject.GetComponent<WindowFeedbacks>().Open);
            Assert.AreEqual(1, manager.EventSystem.GetComponents<BaseInputModule>().Count(x => x.enabled));
            manager.CloseAll(); yield return null; Assert.IsTrue(secondObject == null); Assert.AreEqual(0, player.ContextRequestCount);
        }
        [UnityTest] public IEnumerator RadialPurchaseAndResourceLabelWorkWithoutImportedWorkshopScene()
        {
            var placer = Placer(out _);
            var wood = EasyResources.ResourceDefinition.Create("Wood", "framework-ui-wood"); objects.Add(wood);
            var wallet = root.AddComponent<EasyResources.ResourceContainer>(); wallet.Add(wood, 12);
            var payment = placer.gameObject.AddComponent<EasyBuilding.Integrations.BuildingResources>(); payment.Resources = new[] { wood }; payment.Containers = new[] { wallet }; placer.System.Economy = payment;
            var definition = Building("Paid wall"); definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost(wood.Id, 3) };
            var catalog = Asset<BuildingCatalog>(); catalog.Buildings = new[] { definition };
            var labelObject = Go("Wallet label", false, typeof(RectTransform), typeof(Text));
            var label = labelObject.AddComponent<EasyUI.Integrations.Resources.ResourceBalanceLabel>(); label.Container = wallet; label.Resource = wood; labelObject.SetActive(true);
            using var external = player.AcquireContext(root, new InputContextRequest { BlockedActions = new[] { "Gameplay/Look" } });
            var parent = manager.Open(Window("Workshop")); var window = Window("Build choice"); var radial = window.gameObject.AddComponent<RadialMenu>();
            window.gameObject.AddComponent<BuildingCatalogWindow>().Bind(placer, catalog); manager.Open(window, parent); yield return Ready();
            wallet.Add(wood, 5); yield return null; Assert.That(labelObject.GetComponent<Text>().text, Does.Contain("17"));
            radial.SelectDirection(Vector2.up, .2f); Assert.IsTrue(radial.ConfirmSelection()); Assert.IsFalse(radial.ConfirmSelection());
            Assert.AreEqual(2, player.ContextRequestCount); parent.Close(); yield return Ready(); Assert.AreEqual(1, player.ContextRequestCount);
            Assert.IsTrue(placer.Session.TryConfirm(new PlacementRequest(new Vector3(3, 0, 2), Quaternion.identity), out var placed, out var result), result.Reason);
            objects.Add(placed.gameObject); Assert.AreEqual(14, wallet.GetAvailable(wood));
            Assert.IsFalse(placer.Session.TryConfirm(new PlacementRequest(new Vector3(5, 0, 2), Quaternion.identity), out _, out _));
            yield return null; Assert.That(labelObject.GetComponent<Text>().text, Does.Contain("14"));
        }
        [UnityTest] public IEnumerator GraphicsChildUsesOwnerSessionAndCancelsDraftWithoutClosingControls()
        {
            UseUnconfiguredLocalization();
            var localizationBefore = UnityEngine.Localization.Settings.LocalizationSettings.GetInstanceDontCreateDefault();
            bool languageWasReady = EasyLocalization.Localization.IsReady;
            var defaults = Asset<EasySettings.SettingsDefaults>();
            var camera = Go("Graphics camera", true, typeof(Camera), typeof(UnityEngine.Rendering.Universal.UniversalAdditionalCameraData)).GetComponent<Camera>();
            var source = Go("Graphics owner", false); var controller = source.AddComponent<EasySettings.SettingsController>();
            controller.Defaults = defaults; controller.TargetCamera = camera; source.SetActive(true);
            var template = Window("Graphics template"); template.gameObject.AddComponent<EasySettings.UI.SettingsWindow>(); template.gameObject.AddComponent<WindowFeedbacks>();
            var definition = Asset<WindowDefinition>(); definition.Prefab = template; definition.Policy = template.Policy.Copy();
            var parentWindow = Window("Combined settings"); parentWindow.gameObject.AddComponent<InputSettingsWindow>();
            var combined = parentWindow.gameObject.AddComponent<IntegratedSettingsWindow>(); combined.GraphicsWindow = definition; combined.BindGraphics(controller);
            var parent = manager.Open(parentWindow); yield return Ready(); combined.OpenGraphics(); yield return Ready();
            Assert.AreSame(localizationBefore, UnityEngine.Localization.Settings.LocalizationSettings.GetInstanceDontCreateDefault());
            Assert.AreEqual(languageWasReady, EasyLocalization.Localization.IsReady);
            if (!languageWasReady) Assert.IsFalse(combined.CanChangeLanguage);
            var child = manager.ActiveWindow; var view = child.Window.GetComponent<EasySettings.UI.SettingsWindow>(); var session = controller.Session;
            Assert.AreSame(controller, view.Controller); Assert.AreSame(combined, view.TextProvider); Assert.AreEqual(2, player.ContextRequestCount);
            var draft = session.Draft; draft.RenderScale = .73f; session.Draft = draft;
            child.Close(); yield return Ready(); Assert.IsTrue(parent.IsOpen); Assert.AreEqual(1, player.ContextRequestCount);
            Assert.AreEqual(session.Applied.RenderScale, session.Draft.RenderScale); Assert.IsFalse(session.IsDisposed);
            combined.OpenGraphics(); var instance = manager.ActiveWindow.Window; manager.CloseAll(); yield return null;
            Assert.IsTrue(instance == null); Assert.AreEqual(0, player.ContextRequestCount); Assert.IsFalse(session.IsDisposed);
        }
        #if UNITY_EDITOR
        [UnityTest] public IEnumerator ApproachHeldInputNestedWindowsAndDeathRestoreKeepOwnersIndependent()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab"); Assert.IsNotNull(prefab);
            var characterRoot = Object.Instantiate(prefab); objects.Add(characterRoot); characterRoot.SetActive(false);
            var character = characterRoot.GetComponentInChildren<FirstPersonCharacterController>(true);
            var reader = character.gameObject.AddComponent<EasyMovementInput>(); reader.Player = player;
            var actor = character.gameObject.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
            var motor = character.gameObject.AddComponent<EasyMovementInteractionMotor>(); motor.character = character;
            var health = character.gameObject.AddComponent<Health>(); var guard = character.gameObject.AddComponent<CombatInteractionGuard>(); guard.health = health;
            characterRoot.SetActive(true); yield return Ready();
            var destination = Go("Approach point"); destination.transform.position = character.transform.position + Vector3.right * 3;
            motor.Begin(destination.transform); motor.Tick(.02f); var direction = character.GetMovementDirection();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Enter)); InputSystem.Update(); reader.SendMessage("Update");
            Assert.IsTrue(character.InputBlocked); Assert.AreEqual(direction, character.GetMovementDirection());
            var parent = manager.Open(Window("Terminal")); var child = manager.Open(Window("Nested"), parent); yield return null;
            Assert.IsFalse(manager.InputReady); Assert.AreEqual(2, player.ContextRequestCount); motor.Tick(.02f); direction = character.GetMovementDirection(); reader.SendMessage("Update"); Assert.AreEqual(direction, character.GetMovementDirection());
            var definitions = Asset<CombatDefinitionCatalog>(); var registry = new CombatReferenceRegistry(); registry.RegisterActor("ui-player", health);
            Assert.IsTrue(CombatSaveSystem.TryCapture(definitions, registry, out var snapshot, out var captureError), captureError);
            health.ApplyDamage(new DamageInfo(health.Maximum + 1)); Assert.IsFalse(actor.enabled);
            Assert.IsTrue(CombatSaveSystem.TryRestore(snapshot, definitions, registry, out _, out var restoreError), restoreError); Assert.IsTrue(actor.enabled);
            child.Close(); Assert.AreEqual(1, player.ContextRequestCount); Assert.IsTrue(parent.IsOpen);
            parent.Close(); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); motor.Stop(); yield return Ready();
            Assert.AreEqual(0, player.ContextRequestCount); Assert.IsFalse(character.InputBlocked); Assert.IsTrue(player.Action("Gameplay/Move").enabled);
        }
        #endif
    }
}
