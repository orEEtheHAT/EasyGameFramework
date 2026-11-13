using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class PrimaryRecordingCommands : InteractionCommandAdapter
    {
        public InteractionAction Pressed, Released;
        public int PressCount;
        public bool Accept;
        public override bool Press(InteractionAction action, bool toggleInsteadOfHold = false) { Pressed = action; PressCount++; return Accept; }
        public override void Release(InteractionAction action) => Released = action;
        public override void Cancel() { }
        public override bool SetValue(InteractionAction action, float value) => false;
        public override bool IsRunning(InteractionAction action) => false;
        public override float GetProgress(InteractionAction action) => 0;
    }

    public sealed class GrabReleaseInputTests
    {
        private readonly List<Object> objects = new List<Object>();
        private InteractionActor actor;
        private InteractionInput reader;
        private InteractionPresenter presenter;
        private GrabAction grab;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputAction interact, look;
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
            var root = Go("Primary input actor", new Vector3(0, 1500, 0)); root.SetActive(false);
            actor = root.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
            reader = root.AddComponent<InteractionInput>(); presenter = root.AddComponent<InteractionPresenter>();
            var asset = ScriptableObject.CreateInstance<InputActionAsset>(); objects.Add(asset);
            interact = asset.AddActionMap("Player").AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            var reference = InputActionReference.Create(interact); objects.Add(reference);
            typeof(InteractionInput).GetField("interactAction", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(reader, reference);
            root.SetActive(true);
            look = new InputAction("Look", InputActionType.Value, "<Mouse>/delta");
            look.performed += c => { actor.View.rotation *= Quaternion.Euler(0, c.ReadValue<Vector2>().x, 0); actor.RefreshTarget(); };
            look.Enable();
            var item = Go("Held item", actor.View.position + Vector3.forward * 2);
            item.AddComponent<BoxCollider>().size = Vector3.one * .6f; item.AddComponent<Rigidbody>(); grab = item.AddComponent<GrabAction>();
            grab.RotationMode = GrabRotation.Stabilized; Physics.SyncTransforms(); actor.RefreshTarget();
        }
        [TearDown] public void TearDown()
        {
            if (actor) Object.DestroyImmediate(actor.gameObject);
            look?.Dispose();
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear();
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            InputSystem.settings.updateMode = oldUpdate; InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditor;
        }
        private void Send(bool down, float delta = 0, bool mouseFirst = true, Key key = Key.E)
        {
            if (mouseFirst) InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(delta, 0) });
            InputSystem.QueueStateEvent(keyboard, down ? new KeyboardState(key) : new KeyboardState());
            if (!mouseFirst) InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(delta, 0) });
            InputSystem.Update();
        }
        private void PickUpAndLookAway()
        {
            Send(true); Send(false); Assert.IsTrue(grab.IsHeld);
            for (int i = 0; i < 6; i++) Send(false, 15);
            Assert.IsNull(actor.CurrentTarget);
        }
        private EventAction Other()
        {
            var go = Go("Other hovered world action", actor.View.position + actor.View.forward * 2);
            go.AddComponent<BoxCollider>(); var action = go.AddComponent<EventAction>(); Physics.SyncTransforms(); actor.RefreshTarget(); return action;
        }

        [TestCase(false, true), TestCase(false, false), TestCase(true, true), TestCase(true, false)]
        public void PrimaryEAndMouseInOneUpdateDropsOwnedGrabWithoutStartingOtherHover(bool otherHover, bool mouseFirst)
        {
            PickUpAndLookAway(); int started = 0, cancelled = 0;
            if (otherHover) { var other = Other(); other.OnExecuteEvent.AddListener(() => started++); Assert.AreSame(other, actor.SelectedAction); }
            grab.OnCancelled.AddListener(() => cancelled++);
            Send(true, 2, mouseFirst); Assert.IsFalse(grab.IsHeld, "Main Interact must release its held body while Look continues.");
            Send(true, 2, mouseFirst); Send(false, 2, mouseFirst);
            Assert.AreEqual(1, cancelled); Assert.AreEqual(0, started); Assert.IsTrue(grab.Body.useGravity);
        }

        [Test] public void DisabledActionBlocksPrimaryAndFreshReboundKeyReleases()
        {
            PickUpAndLookAway(); interact.Disable(); Send(true, 2); Send(false, 2); Assert.IsTrue(grab.IsHeld);
            interact.ApplyBindingOverride(0, "<Keyboard>/k"); interact.Enable();
            Send(true, 2); Send(false, 2); Assert.IsTrue(grab.IsHeld, "Old E binding must not dispatch.");
            Send(true, 2, key: Key.K); Assert.IsFalse(grab.IsHeld); Send(false);
        }

        [Test] public void HoldWhileActiveReleaseUsesCapturedActionAfterLookLosesHover()
        {
            grab.InputGesture = InteractionInputGesture.HoldWhileActive; Send(true); Assert.IsTrue(grab.IsHeld);
            for (int i = 0; i < 6; i++) Send(true, 15);
            Assert.IsTrue(grab.IsHeld); Assert.IsNull(actor.CurrentTarget); Send(false, 2); Assert.IsFalse(grab.IsHeld);
        }

        [Test] public void HoldConfirmationAndAccessibilityToggleRemainIntact()
        {
            grab.InputGesture = InteractionInputGesture.HoldToConfirm; grab.ConfirmationSeconds = .5f;
            Send(true); actor.Tick(.2f); Assert.IsFalse(grab.IsHeld); Send(false); actor.Tick(1); Assert.IsFalse(grab.IsHeld);
            reader.ToggleInsteadOfHold = true; Send(true); Send(false); actor.Tick(.6f); Assert.IsTrue(grab.IsHeld);
            for (int i = 0; i < 6; i++) Send(false, 15);
            Send(true, 2); Assert.IsFalse(grab.IsHeld); Send(false);
        }

        [Test] public void WithoutHeldGrabPrimaryStillStartsHoveredWorldAction()
        {
            grab.gameObject.SetActive(false); int calls = 0; Other().OnExecuteEvent.AddListener(() => calls++);
            Send(true, 2); Send(false, 2); Assert.AreEqual(1, calls);
        }

        [Test] public void PresenterPrimaryReturnsHeldActionAndExplicitMenuActionRemainsAddressable()
        {
            PickUpAndLookAway(); int calls = 0; var other = Other(); other.OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(presenter.Activate(other)); Assert.AreEqual(1, calls); Assert.IsTrue(grab.IsHeld);
            presenter.Refresh(); Assert.AreSame(grab, presenter.Prompt.Action); Assert.AreEqual("Release", presenter.Prompt.Label);
            Assert.IsTrue(presenter.ActivatePrimary(out var chosen)); Assert.AreSame(grab, chosen);
            Assert.IsFalse(grab.IsHeld); Assert.AreEqual(1, calls); presenter.Release(chosen);
        }

        [TestCase(0), TestCase(1), TestCase(2)] public void DefaultPublicCommandsPreferHeldGrabOverOtherHover(int command)
        {
            PickUpAndLookAway(); int calls = 0; Other().OnExecuteEvent.AddListener(() => calls++);
            Assert.IsTrue(command == 0 ? actor.Press() : command == 1 ? actor.Interact() : presenter.Activate());
            Assert.IsFalse(grab.IsHeld); Assert.AreEqual(0, calls);
        }

        [Test] public void PrimaryChoosesGrabWhenInspectOccupiesTheFirstActiveSession()
        {
            var earlier = grab.gameObject.AddComponent<EventAction>(); earlier.Channel = "temporary"; earlier.Execution = InteractionExecutionMode.Timed; earlier.Duration = 100;
            Assert.IsTrue(actor.TryExecuteDirect(earlier)); Send(true); Send(false); Assert.IsTrue(grab.IsHeld);
            actor.Cancel(earlier); var inspect = grab.gameObject.AddComponent<InspectAction>(); Assert.IsTrue(actor.TryExecuteDirect(inspect));
            Assert.AreSame(inspect, actor.ActiveAction, "The test must exercise a non-Grab first session.");
            for (int i = 0; i < 6; i++) Send(false, 15);
            Send(true, 2); Assert.IsFalse(grab.IsHeld); Send(false);
        }

        [Test] public void CommandBridgeReceivesHeldActionOnceAndRejectDoesNotFallThroughToHover()
        {
            PickUpAndLookAway(); int calls = 0; Other().OnExecuteEvent.AddListener(() => calls++);
            var commands = actor.gameObject.AddComponent<PrimaryRecordingCommands>(); actor.CommandAdapter = commands;
            Send(true, 2); Assert.AreSame(grab, commands.Pressed); Assert.AreEqual(1, commands.PressCount); Assert.AreEqual(0, calls); Assert.IsTrue(grab.IsHeld);
            Send(false); commands.Accept = true; Send(true, 2); Send(false, 2);
            Assert.AreSame(grab, commands.Released); Assert.AreEqual(2, commands.PressCount); Assert.AreEqual(0, calls);
        }

        [Test] public void DisabledActorDoesNotReceivePrimaryInput()
        {
            grab.gameObject.SetActive(false); int calls = 0; Other().OnExecuteEvent.AddListener(() => calls++);
            actor.enabled = false; Send(true, 2); Send(false, 2); Assert.AreEqual(0, calls);
            Assert.IsFalse(actor.PressPrimary(out var chosen)); Assert.IsNull(chosen);
        }

        [Test] public void NoLocalTargetPreservesTheCommandBridgesNullPressFallback()
        {
            grab.gameObject.SetActive(false); actor.RefreshTarget();
            var commands = actor.gameObject.AddComponent<PrimaryRecordingCommands>(); commands.Accept = true; actor.CommandAdapter = commands;
            Assert.IsTrue(actor.PressPrimary(out var chosen)); Assert.IsNull(chosen);
            Assert.AreEqual(1, commands.PressCount); Assert.IsNull(commands.Pressed);
        }
    }
}
