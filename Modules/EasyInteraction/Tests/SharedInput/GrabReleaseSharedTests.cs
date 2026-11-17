using System.Collections.Generic;
using EasyInput;
using EasyInput.Integrations;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.SharedInput
{
    public sealed class GrabReleaseSharedTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private PlayerInputSettings player;
        private InteractionActor actor;
        private GrabAction grab;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputAction look;
        private InputSettings.UpdateMode oldUpdate;
        private InputSettings.BackgroundBehavior oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode oldEditor;
        private GameObject Go(string name, Vector3 position)
        { var go = new GameObject(name); go.transform.position = position; objects.Add(go); return go; }
        [SetUp] public void SetUp()
        {
            oldUpdate = InputSystem.settings.updateMode; oldBackground = InputSystem.settings.backgroundBehavior; oldEditor = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            var root = Go("Shared primary input actor", new Vector3(0, 1550, 0)); root.SetActive(false);
            player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false;
            actor = root.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
            var input = root.AddComponent<EasyInteractionInput>(); input.Player = player;
            root.SetActive(true); player.PairDevices(keyboard, mouse);
            look = player.Action("Gameplay/Look"); look.performed += Turn;
            var item = Go("Shared input crate", actor.View.position + Vector3.forward * 2);
            item.AddComponent<BoxCollider>().size = Vector3.one * .6f; item.AddComponent<Rigidbody>(); grab = item.AddComponent<GrabAction>();
            grab.RotationMode = GrabRotation.Stabilized; Physics.SyncTransforms(); actor.RefreshTarget();
        }
        private void Turn(InputAction.CallbackContext context)
        { actor.View.rotation *= Quaternion.Euler(0, context.ReadValue<Vector2>().x, 0); actor.RefreshTarget(); }
        [TearDown] public void TearDown()
        {
            if (look != null) look.performed -= Turn;
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = oldUpdate; InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
        }
        private void Send(bool down, float delta = 0, Key key = Key.E)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(delta, 0) });
            InputSystem.QueueStateEvent(keyboard, down ? new KeyboardState(key) : new KeyboardState()); InputSystem.Update();
        }
        private void PickUpAndTurn()
        {
            Send(true); Send(false); Assert.IsTrue(grab.IsHeld);
            for (int i = 0; i < 6; i++) Send(false, 15);
            Assert.IsNull(actor.CurrentTarget);
        }
        private void ConfigurePose(GrabPoseMode mode)
        {
            grab.PoseMode = mode;
            grab.LocalPosition = new Vector3(.2f, -.1f, -.15f);
            grab.LocalEulerAngles = new Vector3(15, 70, -20);
        }
        [TestCase(false, GrabPoseMode.PreservePickupPose), TestCase(true, GrabPoseMode.PreservePickupPose)]
        [TestCase(false, GrabPoseMode.AuthoredPose), TestCase(true, GrabPoseMode.AuthoredPose)]
        public void SharedEAndSustainedMouseLookReleaseGrabInsteadOfHoveredAction(bool otherHover, GrabPoseMode mode)
        {
            ConfigurePose(mode);
            PickUpAndTurn(); int calls = 0;
            if (otherHover)
            {
                var other = Go("Hovered action", actor.View.position + actor.View.forward * 2); other.AddComponent<BoxCollider>();
                other.AddComponent<EventAction>().OnExecuteEvent.AddListener(() => calls++); Physics.SyncTransforms(); actor.RefreshTarget();
            }
            Send(true, 2); Assert.IsFalse(grab.IsHeld, "Shared primary input must reach the actor even when SelectedAction is null.");
            Send(true, 2); Send(false, 2); Assert.AreEqual(0, calls);
        }
        [TestCase(GrabPoseMode.PreservePickupPose), TestCase(GrabPoseMode.AuthoredPose)]
        public void ModalContextPreventsDispatchThenFreshInteractCanReleaseExternallyStartedGrab(GrabPoseMode mode)
        {
            ConfigurePose(mode);
            Assert.IsTrue(actor.TryExecuteDirect(grab));
            var window = Go("Modal window owner", Vector3.zero);
            using (player.AcquireContext(window, InputContextRequest.UI()))
            {
                Assert.IsFalse(player.Action("Gameplay/Interact").enabled); Send(true, 5); Send(false, 5); Assert.IsTrue(grab.IsHeld);
            }
            for (int i = 0; i < 6; i++) Send(false, 15);
            Send(true, 2); Assert.IsFalse(grab.IsHeld); Send(false);
        }
        [TestCase(GrabPoseMode.PreservePickupPose), TestCase(GrabPoseMode.AuthoredPose)]
        public void ReboundPrimaryKeyReleasesDuringLookAndOldKeyDoesNothing(GrabPoseMode mode)
        {
            ConfigurePose(mode);
            PickUpAndTurn(); player.Action("Gameplay/Interact").ApplyBindingOverride(0, "<Keyboard>/k");
            Send(true, 2); Send(false, 2); Assert.IsTrue(grab.IsHeld);
            Send(true, 2, Key.K); Assert.IsFalse(grab.IsHeld); Send(false);
        }
    }
}
