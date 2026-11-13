using System.Collections;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyUI.Integrations.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyUI.ControllerTests
{
    public sealed class ControllerRebindTests
    {
        private GameObject playerObject, uiObject, target;
        private Keyboard keyboard;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        [SetUp] public void Setup()
        {
            background = InputSystem.settings.backgroundBehavior; editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
        }
        [TearDown] public void Cleanup()
        {
            try { if (uiObject) Object.DestroyImmediate(uiObject); if (playerObject) Object.DestroyImmediate(playerObject); if (target) Object.DestroyImmediate(target); if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard); }
            finally { InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput; }
        }
        private void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        [UnityTest] public IEnumerator ExistingAndReplacementInteractionControllerUseBindingChangedInsideEasyUI()
        {
            playerObject = new GameObject("Input player"); playerObject.SetActive(false);
            var player = playerObject.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false;
            var actor = playerObject.AddComponent<InteractionActor>(); actor.View = playerObject.transform;
            var controller = playerObject.AddComponent<EasyInteractionInput>(); controller.Player = player; controller.Actor = actor;
            playerObject.SetActive(true); player.PairDevices(keyboard);
            target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.transform.position = Vector3.forward * 2;
            var action = target.AddComponent<EventAction>(); int interactions = 0; action.OnExecuteEvent.AddListener(() => interactions++);
            Physics.SyncTransforms(); actor.RefreshTarget(); Assert.AreSame(action, actor.SelectedAction);
            Keys(Key.E); Assert.AreEqual(1, interactions); Keys();
            uiObject = new GameObject("UI", typeof(RectTransform), typeof(WindowManager)); var manager = uiObject.GetComponent<WindowManager>();
            var provider = uiObject.AddComponent<EasyInputWindowProvider>(); provider.Player = player; manager.InputProvider = provider;
            var controlsObject = new GameObject("Controls", typeof(RectTransform)); controlsObject.SetActive(false); controlsObject.transform.SetParent(uiObject.transform, false);
            var window = controlsObject.AddComponent<UIWindow>(); window.Policy = new WindowPolicy { BlockGameplay = true, FadeIn = 0, FadeOut = 0 };
            var settings = controlsObject.AddComponent<InputSettingsWindow>(); var handle = manager.Open(window);
            yield return null; yield return null; yield return null;
            settings.BeginRebind("Gameplay/Interact", 0); yield return new WaitForSecondsRealtime(.2f);
            Keys(Key.K); yield return new WaitForSecondsRealtime(.15f); Keys();
            Assert.AreEqual(RebindStatus.Completed, player.Rebind.Status); Assert.AreEqual(1, interactions, "Rebinding must not execute the game action.");
            handle.Close(); yield return null; yield return null; yield return null;
            Keys(Key.E); Assert.AreEqual(1, interactions); Keys();
            Keys(Key.K); Assert.AreEqual(2, interactions); Keys();
            Object.DestroyImmediate(controller); playerObject.AddComponent<EasyInteractionInput>();
            Keys(Key.K); Assert.AreEqual(3, interactions); Keys();
        }
    }
}
