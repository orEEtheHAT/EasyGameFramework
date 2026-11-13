#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using EasyInput.Integrations;
using EasyMovement.FirstPerson;

namespace EasyInput.IntegrationTests
{
    public sealed class AdapterTests
    {
        private GameObject root, character, events;
        private PlayerInputSettings player;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad pad;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorBehavior;
        private InputSettings.UpdateMode updateMode;
        private EasyFeedbacks.FeedbackSettingsState feedbackSettings;
        private int rumbleEvents;
        private InputDevice rumbleDevice;
        private unsafe long? ObserveRumble(InputDevice device, InputDeviceCommand* command)
        {
            if (command->type != new UnityEngine.InputSystem.Utilities.FourCC('R', 'M', 'B', 'L')) return null;
            var motors = (float*)command->payloadPtr;
            if (motors[0] > 0 || motors[1] > 0) { rumbleEvents++; rumbleDevice = device; }
            return InputDeviceCommand.GenericSuccess;
        }
        private unsafe void ObserveRumble(bool enabled) { if (enabled) InputSystem.onDeviceCommand += ObserveRumble; else InputSystem.onDeviceCommand -= ObserveRumble; }
        [UnitySetUp] public IEnumerator Setup()
        {
            feedbackSettings = EasyFeedbacks.FeedbackSettings.Capture(); EasyFeedbacks.FeedbackSettings.Apply(EasyFeedbacks.FeedbackSettingsState.Default);
            background = InputSystem.settings.backgroundBehavior; editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            updateMode = InputSystem.settings.updateMode; InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); pad = InputSystem.AddDevice<Gamepad>();
            root = new GameObject("Persistent player settings"); root.SetActive(false); player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false; root.SetActive(true); player.PairDevices(keyboard, mouse, pad);
            yield return null;
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            try
            {
                if (events) Object.Destroy(events); if (character) Object.Destroy(character); if (root) Object.Destroy(root); yield return null;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            }
            finally { InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior; InputSystem.settings.updateMode = updateMode; EasyFeedbacks.FeedbackSettings.Restore(feedbackSettings); }
        }
        private EasyMovementInput SpawnCharacter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab");
            Assert.That(prefab, Is.Not.Null);
            character = Object.Instantiate(prefab); character.SetActive(false);
            var controller = character.GetComponentInChildren<FirstPersonCharacterController>(true); Assert.That(controller, Is.Not.Null);
            var adapter = controller.gameObject.AddComponent<EasyMovementInput>(); adapter.Player = player;
            character.SetActive(true); return adapter;
        }
        [UnityTest] public IEnumerator EasyMovementUsesSharedSettingsOnceAndSurvivesControllerReplacement()
        {
            var adapter = SpawnCharacter(); yield return null;
            var standalone = character.GetComponentInChildren<FirstPersonCharacterControllerInput>(); var controller = character.GetComponentInChildren<FirstPersonCharacterController>();
            Assert.That(standalone.enabled, Is.False); player.MouseSensitivity = 2;
            Assert.That(standalone.sensitivity.x, Is.EqualTo(.16f).Within(.0001));
            float before = controller.GetPendingRotation().eulerAngles.y;
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(20,0) }); InputSystem.Update();
            adapter.SendMessage("Update"); controller.Simulate(1f / 60);
            float after = controller.GetPendingRotation().eulerAngles.y;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(before, after)), Is.EqualTo(3.2f).Within(.1f));
            var saved = player.CaptureSettings(); player.MouseSensitivity = 3;
            Assert.That(player.RestoreSettings(saved, out var error), Is.True, error); Assert.That(standalone.sensitivity.x, Is.EqualTo(.16f).Within(.0001));
            var runtime = player.Actions; Object.Destroy(character); yield return null;
            adapter = SpawnCharacter(); yield return null;
            Assert.That(player.Actions, Is.SameAs(runtime)); Assert.That(character.GetComponentInChildren<FirstPersonCharacterControllerInput>().sensitivity.x, Is.EqualTo(.16f).Within(.0001));
            adapter.enabled = false; Assert.That(character.GetComponentInChildren<FirstPersonCharacterControllerInput>().enabled, Is.True);
        }
        [UnityTest] public IEnumerator MissingUIPlayerPreservesTheExistingModuleConfiguration()
        {
            events = new GameObject("Unconfigured UI"); events.SetActive(false); events.AddComponent<EventSystem>();
            var module = events.AddComponent<InputSystemUIInputModule>(); module.enabled = false; module.actionsAsset = player.Actions;
            var original = InputActionReference.Create(player.Action("UI/Click")); module.leftClick = original;
            try
            {
                var adapter = events.AddComponent<InputUIActions>();
                LogAssert.Expect(LogType.Error,"uGUI Actions: assign Player Input Settings from the local player.");
                events.SetActive(true); yield return null;
                Assert.That(adapter.enabled, Is.False); Assert.That(module.enabled, Is.False);
                Assert.That(module.actionsAsset, Is.SameAs(player.Actions)); Assert.That(module.leftClick, Is.SameAs(original));
            }
            finally { Object.Destroy(original); }
        }
        [UnityTest] public IEnumerator UGUIModuleUsesSharedActionsAndCannotUnblockRebind()
        {
            events = new GameObject("Test UI"); events.SetActive(false); events.AddComponent<EventSystem>(); var module = events.AddComponent<InputSystemUIInputModule>();
            var adapter = events.AddComponent<InputUIActions>(); adapter.Player = player; events.SetActive(true); yield return null;
            using var window = player.AcquireContext(player, InputContextRequest.UI());
            Assert.That(module.actionsAsset, Is.SameAs(player.Actions)); Assert.That(module.submit.action, Is.SameAs(player.Action("UI/Submit")));
            var session = player.BeginRebind(player.Action("Gameplay/Jump"), 0, player); Assert.That(module.enabled, Is.False);
            Assert.That(player.Action("UI/Submit").enabled, Is.False); session.Dispose(); Assert.That(module.enabled, Is.True);
            Assert.That(player.Action("Gameplay/Jump").enabled, Is.False);
        }
        [UnityTest] public IEnumerator UGUIDefaultPointerActionsKeepNavigationAndGameplayButtonSemantics()
        {
            var source = Resources.Load<InputActionAsset>("EasyInputDefaults"); Assert.That(source, Is.Not.Null);
            foreach (var asset in new[] { source, player.Actions })
            {
                foreach (var name in new[] { "UI/Click", "UI/Point", "UI/Scroll" })
                {
                    var action = asset.FindAction(name, true);
                    Assert.That(action.type, Is.EqualTo(InputActionType.PassThrough), name);
                    Assert.That(action.expectedControlType, Is.EqualTo(name == "UI/Click" ? "Button" : "Vector2"), name);
                }
                Assert.That(asset.FindAction("UI/Navigate", true).type, Is.EqualTo(InputActionType.Value));
                foreach (var name in new[] { "UI/Submit", "UI/Cancel", "Gameplay/Fire", "Gameplay/Interact" })
                    Assert.That(asset.FindAction(name, true).type, Is.EqualTo(InputActionType.Button), name);
            }
            string pointerWarning = null;
            Application.LogCallback observe = (message, trace, type) =>
            {
                if (type == LogType.Warning && message.Contains("Pointer-related actions used with the UI input module")) pointerWarning = message;
            };
            Application.logMessageReceived += observe;
            try
            {
                events = new GameObject("Default pointer UI"); events.SetActive(false); events.AddComponent<EventSystem>();
                var module = events.AddComponent<InputSystemUIInputModule>();
                var adapter = events.AddComponent<InputUIActions>(); adapter.Player = player; events.SetActive(true);
                using var window = player.AcquireContext(player, InputContextRequest.UI());
                yield return null;
                Assert.That(module.leftClick.action, Is.SameAs(player.Action("UI/Click")));
                Assert.That(module.point.action, Is.SameAs(player.Action("UI/Point")));
                Assert.That(module.scrollWheel.action, Is.SameAs(player.Action("UI/Scroll")));
                Assert.That(pointerWarning, Is.Null, pointerWarning);
            }
            finally { Application.logMessageReceived -= observe; }
        }
        private static InputActionReference[] UIReferences(InputSystemUIInputModule module) => new[]
        {
            module.point, module.leftClick, module.scrollWheel, module.move, module.submit, module.cancel,
            module.rightClick, module.middleClick, module.trackedDevicePosition, module.trackedDeviceOrientation
        };
        [UnityTest] public IEnumerator UGUIDetachAfterPlayerDisposalRecreatesValidNativeDefaults()
        {
            events = new GameObject("UI outlives player"); events.SetActive(false); events.AddComponent<EventSystem>();
            var module = events.AddComponent<InputSystemUIInputModule>();
            var adapter = events.AddComponent<InputUIActions>(); adapter.enabled = false; adapter.Player = player;
            events.SetActive(true); yield return null;
            Assert.That(module.point.action.actionMap.asset, Is.SameAs(module.actionsAsset));
            adapter.enabled = true; yield return null;
            var owned = UIReferences(module); var runtime = player.Actions;
            Assert.That(module.actionsAsset, Is.SameAs(runtime));
            Object.Destroy(root); root = null; yield return null; yield return null;
            Assert.That(runtime == null, Is.True, "The runtime asset must be destroyed before UI detaches.");
            adapter.enabled = false;
            Assert.That(module.enabled, Is.True);
            Assert.That(module.actionsAsset != null, Is.True);
            Assert.That(module.point.action.actionMap.asset, Is.SameAs(module.actionsAsset));
            yield return null;
            foreach (var reference in owned) Assert.That(reference == null, Is.True, "Adapter-owned references must be released.");
        }
        [UnityTest] public IEnumerator UGUIDetachRestoresAllAuthoredReferencesWithoutDestroyingForeignOwners()
        {
            var authored = ScriptableObject.CreateInstance<InputActionAsset>(); var map = authored.AddActionMap("UI");
            string[] names = { "Point", "Click", "Scroll", "Navigate", "Submit", "Cancel", "RightClick", "MiddleClick", "Position", "Orientation" };
            string[] controls = { "Vector2", "Button", "Vector2", "Vector2", "Button", "Button", "Button", "Button", "Vector3", "Quaternion" };
            var original = new InputActionReference[names.Length];
            try
            {
                for (int i = 0; i < names.Length; i++)
                {
                    var action = map.AddAction(names[i], InputActionType.PassThrough); action.expectedControlType = controls[i];
                    original[i] = InputActionReference.Create(action);
                }
                events = new GameObject("Authored UI"); events.SetActive(false); events.AddComponent<EventSystem>();
                var module = events.AddComponent<InputSystemUIInputModule>(); module.enabled = false; module.actionsAsset = authored;
                module.point = original[0]; module.leftClick = original[1]; module.scrollWheel = original[2]; module.move = original[3];
                module.submit = original[4]; module.cancel = original[5]; module.rightClick = original[6]; module.middleClick = original[7];
                module.trackedDevicePosition = original[8]; module.trackedDeviceOrientation = original[9];
                var adapter = events.AddComponent<InputUIActions>(); adapter.Player = player; events.SetActive(true); yield return null;
                Assert.That(module.actionsAsset, Is.SameAs(player.Actions)); var owned = UIReferences(module);
                using var window = player.AcquireContext(player, InputContextRequest.UI());
                adapter.enabled = false;
                Assert.That(module.enabled, Is.False, "Restore the authored disabled state.");
                Assert.That(module.actionsAsset, Is.SameAs(authored));
                Assert.That(UIReferences(module), Is.EqualTo(original), "Restore the exact authored references, including optional slots.");
                Assert.That(player.Actions != null, Is.True);
                Assert.That(player.Action("Gameplay/Jump").enabled, Is.False, "Another owner's UI context must remain active.");
                yield return null;
                foreach (var reference in owned) Assert.That(reference == null, Is.True);
                foreach (var reference in original) Assert.That(reference != null && reference.action.actionMap.asset == authored, Is.True);
            }
            finally
            {
                // Destroy the consumer before this test's foreign assets, preserving normal production ownership.
                if (events) Object.DestroyImmediate(events); events = null;
                foreach (var reference in original) if (reference) Object.DestroyImmediate(reference);
                Object.DestroyImmediate(authored);
            }
        }
        [UnityTest] public IEnumerator BuildingAdapterPreservesIndependentBlockingOwner()
        {
            character = new GameObject("Building"); character.SetActive(false);
            var placer = character.AddComponent<EasyBuilding.BuildingPlacer>(); placer.ShowHelp = false;
            var adapter = character.AddComponent<EasyBuildingInput>(); adapter.Player = player; character.SetActive(true); yield return null;
            object other = new object(); placer.SetInputBlocked(other, true);
            Assert.That(placer.ExternalTargeting, Is.True); Assert.That(player.Action("Building/Confirm").enabled, Is.True);
            adapter.enabled = false; Assert.That(placer.ExternalTargeting, Is.False); Assert.That(placer.InputBlocked, Is.True);
            placer.SetInputBlocked(other, false);
        }
        [UnityTest] public IEnumerator CombatAdapterUsesDamageEmitterCooldownAndRuntimeBinding()
        {
            character = new GameObject("Combat"); character.SetActive(false);
            var attack = ScriptableObject.CreateInstance<EasyCombat.AttackDefinition>(); var emitter = character.AddComponent<EasyCombat.DamageEmitter>(); emitter.Attack = attack;
            var adapter = character.AddComponent<EasyCombatInput>(); adapter.Player = player; adapter.Emitter = emitter; character.SetActive(true);
            int shots = 0; emitter.Fired += _ => ++shots; yield return null;
            player.TrySetBinding(player.Action("Gameplay/Fire"), 0, "<Keyboard>/k", out _);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.K)); InputSystem.Update(); adapter.SendMessage("Update"); adapter.SendMessage("Update");
            Assert.That(shots, Is.EqualTo(1));
            using var ui = player.AcquireContext(player, InputContextRequest.UI()); adapter.SendMessage("Update"); Assert.That(shots, Is.EqualTo(1));
            Object.Destroy(attack);
        }
        [UnityTest] public IEnumerator InteractionAdapterUsesReboundActionAndReleasesStandaloneReader()
        {
            character = new GameObject("Actor"); character.SetActive(false);
            var actor = character.AddComponent<EasyInteraction.InteractionActor>(); actor.SelectionMode = EasyInteraction.InteractionSelectionMode.Explicit; actor.RequireVisibility = false;
            var original = character.AddComponent<EasyInteraction.InteractionInput>(); var adapter = character.AddComponent<EasyInteractionInput>(); adapter.Player = player;
            var targetObject = new GameObject("Interact target"); targetObject.transform.SetParent(root.transform); targetObject.transform.localPosition = Vector3.forward;
            var target = targetObject.AddComponent<EasyInteraction.InteractionTarget>(); var action = targetObject.AddComponent<EasyInteraction.EventAction>();
            int executions = 0; action.OnExecuteEvent.AddListener(() => ++executions); character.SetActive(true); yield return null; actor.SelectTarget(target);
            Assert.That(original.enabled, Is.False); Assert.That(player.TrySetBinding(player.Action("Gameplay/Interact"), 0, "<Keyboard>/k", out _), Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.K)); InputSystem.Update(); yield return null; Assert.That(executions, Is.EqualTo(1));
            using var window = player.AcquireContext(player, InputContextRequest.UI());
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.K)); InputSystem.Update();
            Assert.That(executions, Is.EqualTo(1)); adapter.enabled = false; Assert.That(original.enabled, Is.True);
        }
        [UnityTest] public IEnumerator KeyboardOnlyFeedbackDoesNotFallBackToAnotherPlayersCurrentGamepad()
        {
            var preset = ScriptableObject.CreateInstance<EasyFeedbacks.FeedbackPreset>(); var profile = ScriptableObject.CreateInstance<EasyFeedbacks.GamepadVibrationProfile>();
            var serialized = new SerializedObject(preset); var entries = serialized.FindProperty("gamepadVibrationEntries"); entries.arraySize = 1;
            var entry = entries.GetArrayElementAtIndex(0); entry.FindPropertyRelative("isEnabled").boolValue = true; entry.FindPropertyRelative("profile").objectReferenceValue = profile;
            entry.FindPropertyRelative("strengthMultiplier").floatValue = 1; entry.FindPropertyRelative("duration").floatValue = 1; entry.FindPropertyRelative("fadeIn").floatValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo();
            var other = InputSystem.AddDevice<Gamepad>(); var previousManager = EasyFeedbacks.GamepadVibrationManager.Instance;
            if (!previousManager) new GameObject("Test vibration manager").AddComponent<EasyFeedbacks.GamepadVibrationManager>();
            var provider = root.AddComponent<PlayerFeedbackContext>(); provider.Player = player;
            character = new GameObject("Player feedback"); character.SetActive(false); var feedback = character.AddComponent<InputFeedback>(); feedback.Player = player; feedback.Preset = preset; feedback.ContextProvider = provider; character.SetActive(true);
            player.SelectGamepad(pad); other.MakeCurrent(); rumbleEvents = 0; rumbleDevice = null; ObserveRumble(true);
            EasyFeedbacks.GamepadVibrationManager.BeginEditorPreviewPlayback();
            try
            {
                Assert.That(provider.Prepare(EasyFeedbacks.FeedbackContext.From(root.transform).WithoutGamepad()).TargetGamepad, Is.Null, "Explicit no-gamepad must survive the player default");
                Assert.That(provider.Prepare(EasyFeedbacks.FeedbackContext.From(root.transform).WithoutGamepad()).HasExplicitGamepadTarget, Is.True);
                feedback.PlayFeedback(); yield return null; yield return null;
                Assert.That(rumbleEvents, Is.GreaterThan(0)); Assert.That(rumbleDevice, Is.SameAs(pad));
                EasyFeedbacks.FeedbackService.StopAll(true); rumbleEvents = 0; rumbleDevice = null;
                var settings = player.Settings; settings.DevicePreference = InputDevicePreference.KeyboardMouse; player.SetSettings(settings);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W)); InputSystem.Update(); other.MakeCurrent();
                Assert.That(player.ActiveGamepad, Is.Null); feedback.PlayFeedback(); yield return null; yield return null;
                Assert.That(rumbleEvents, Is.Zero, "A keyboard-only player must not vibrate Gamepad.current from another player");
                feedback.ContextProvider = null; feedback.PlayFeedback(); yield return null; yield return null;
                Assert.That(rumbleEvents, Is.Zero, "The direct InputFeedback path must use the same player-aware default");
            }
            finally
            {
                ObserveRumble(false); EasyFeedbacks.GamepadVibrationManager.EndEditorPreviewPlayback(); EasyFeedbacks.FeedbackService.StopAll(true);
                if (!previousManager && EasyFeedbacks.GamepadVibrationManager.Instance) Object.Destroy(EasyFeedbacks.GamepadVibrationManager.Instance.gameObject);
                InputSystem.RemoveDevice(other); Object.Destroy(preset); Object.Destroy(profile);
            }
        }
        [UnityTest] public IEnumerator FeedbackAdapterTargetsSelectedGamepadRatherThanGlobalCurrent()
        {
            var preset = ScriptableObject.CreateInstance<EasyFeedbacks.FeedbackPreset>(); var profile = ScriptableObject.CreateInstance<EasyFeedbacks.GamepadVibrationProfile>();
            var serialized = new SerializedObject(preset); var entries = serialized.FindProperty("gamepadVibrationEntries"); entries.arraySize = 1;
            var entry = entries.GetArrayElementAtIndex(0); entry.FindPropertyRelative("isEnabled").boolValue = true; entry.FindPropertyRelative("profile").objectReferenceValue = profile;
            entry.FindPropertyRelative("strengthMultiplier").floatValue = 1; entry.FindPropertyRelative("duration").floatValue = 1; entry.FindPropertyRelative("fadeIn").floatValue = 0; serialized.ApplyModifiedPropertiesWithoutUndo();
            var other = InputSystem.AddDevice<Gamepad>(); var previousManager = EasyFeedbacks.GamepadVibrationManager.Instance;
            if (!previousManager) new GameObject("Test vibration manager").AddComponent<EasyFeedbacks.GamepadVibrationManager>();
            character = new GameObject("Feedback"); character.SetActive(false); var feedback = character.AddComponent<InputFeedback>(); feedback.Player = player; feedback.Preset = preset; character.SetActive(true);
            player.SelectGamepad(pad); other.MakeCurrent(); rumbleEvents = 0; rumbleDevice = null;
            ObserveRumble(true);
            EasyFeedbacks.GamepadVibrationManager.BeginEditorPreviewPlayback();
            try
            {
                feedback.PlayFeedback(); yield return null; yield return null;
                Assert.That(rumbleEvents, Is.GreaterThan(0)); Assert.That(rumbleDevice, Is.SameAs(pad));
            }
            finally
            {
                ObserveRumble(false);
                EasyFeedbacks.GamepadVibrationManager.EndEditorPreviewPlayback();
                EasyFeedbacks.FeedbackService.StopAll(true);
                if (!previousManager && EasyFeedbacks.GamepadVibrationManager.Instance) Object.Destroy(EasyFeedbacks.GamepadVibrationManager.Instance.gameObject);
                InputSystem.RemoveDevice(other); Object.Destroy(preset); Object.Destroy(profile);
            }
        }
    }
}
#endif
