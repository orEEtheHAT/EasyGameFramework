#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using EasyInput.Integrations;
using EasyInteraction;
using Object = UnityEngine.Object;

namespace EasyInput.IntegrationTests
{
    public sealed class GrabReleaseInputTests
    {
        private GameObject root;
        private PlayerInputSettings player;
        private InteractionActor actor;
        private EasyInteractionInput adapter;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputAction look;
        private GrabAction grab;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorInput;
        private bool runInBackground;
        private int otherExecutions;
        private GameObject New(string name, bool active=true)
        { var go=new GameObject(name);go.SetActive(active);go.transform.SetParent(root.transform);return go; }
        [UnitySetUp] public IEnumerator Setup()
        {
            otherExecutions=0;
            updateMode=InputSystem.settings.updateMode;background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;runInBackground=Application.runInBackground;
            InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Application.runInBackground=true;
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            root=new GameObject("Grab primary regression");root.SetActive(false);
            player=root.AddComponent<PlayerInputSettings>();player.ControlsCursor=false;
            var body=New("Actor",false);body.transform.position=new Vector3(0,150,0);
            actor=body.AddComponent<InteractionActor>();actor.PlayerRoot=body.transform;actor.View=body.transform;actor.SelectionMode=InteractionSelectionMode.Raycast;actor.RequireVisibility=false;actor.AutomaticUpdate=false;
            adapter=body.AddComponent<EasyInteractionInput>();adapter.Player=player;body.SetActive(true);root.SetActive(true);player.PairDevices(keyboard,mouse);
            look=player.Action("Gameplay/Look");look.performed+=ApplyLook;
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.name="Held cube";cube.transform.SetParent(root.transform);cube.transform.position=actor.View.position+actor.View.forward*2;cube.AddComponent<Rigidbody>();grab=cube.AddComponent<GrabAction>();
            Physics.SyncTransforms();InputSystem.Update();yield return null;
        }
        // Minimal look consumer of the SAME runtime action, to exercise mouse and primary ordering
        // within a native input update. Does not change Grab physics, inertia or break distance.
        private void ApplyLook(InputAction.CallbackContext context)
        { var degrees=player.ReadLookDelta(look,1f/60);actor.View.Rotate(Vector3.up,degrees.x,Space.World);Physics.SyncTransforms(); }
        private void Frame(float deltaX=0, params Key[] keys)
        {
            InputSystem.QueueStateEvent(mouse,new MouseState {delta=new Vector2(deltaX,0)});
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));InputSystem.Update();
        }
        private void BeginGrab(Key key=Key.E)
        {
            actor.View.rotation=Quaternion.identity;grab.Body.position=actor.View.position+Vector3.forward*2;Physics.SyncTransforms();
            Frame(0,key);Assert.That(grab.IsHeld,Is.True,"Primary must first pick up through the real adapter");Frame();
        }
        private EventAction HoverOther()
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Other world action";go.transform.SetParent(root.transform);go.transform.position=actor.View.position+actor.View.forward*2;
            var action=go.AddComponent<EventAction>();action.OnExecuteEvent.AddListener(()=>otherExecutions++);Physics.SyncTransforms();actor.RefreshTarget();Assert.That(actor.SelectedAction,Is.SameAs(action));return action;
        }
        private void Released()
        { Assert.That(grab.IsHeld,Is.False);Assert.That(actor.GetTicket(grab).IsActive,Is.False);Assert.That(otherExecutions,Is.Zero,"A release press must not also execute the hovered world action"); }
        [UnityTearDown] public IEnumerator Teardown()
        {
            try
            {
                if(look!=null)look.performed-=ApplyLook;
                if(root)Object.DestroyImmediate(root);
                if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
            }
            finally {InputSystem.settings.updateMode=updateMode;InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;Application.runInBackground=runInBackground;}
            yield return null;
        }
        [UnityTest] public IEnumerator PrimaryAndMouseDeltaInOneUpdateReleaseWithoutHover()
        {
            BeginGrab();Frame(1125,Key.E); // 90 degrees in the standard .08 degrees/pixel profile
            Assert.That(actor.RefreshTarget(),Is.Null);Released();yield return null;
        }
        [UnityTest] public IEnumerator SustainedLookReleasesCubeBehindCameraBeforeAnyWorldAction()
        {
            BeginGrab();for(int i=0;i<5;i++)Frame(450);
            Assert.That(Vector3.Dot(grab.Body.position-actor.View.position,actor.View.forward),Is.LessThan(0));
            HoverOther();Frame(1,Key.E);Released();yield return null;
        }
        [UnityTest] public IEnumerator ReboundPrimaryReleasesWithLookAndDoesNotActivateHover()
        {
            Assert.That(player.TrySetBinding(player.Action("Gameplay/Interact"),0,"<Keyboard>/k",out var error),Is.True,error);
            BeginGrab(Key.K);for(int i=0;i<5;i++)Frame(450);HoverOther();Frame(1,Key.E);Assert.That(grab.IsHeld,Is.True);Frame();Frame(1,Key.K);Released();yield return null;
        }
        [UnityTest] public IEnumerator TogglePrimaryReleasesOffscreenGrabAndHoldGestureStillReleasesOnKeyUp()
        {
            adapter.ToggleInsteadOfHold=true;BeginGrab();Frame(1125,Key.E);Released();Frame();
            var action=HoverOther();action.Execution=InteractionExecutionMode.Timed;action.Duration=10;action.InputGesture=InteractionInputGesture.HoldWhileActive;
            Frame(0,Key.E);Assert.That(actor.GetTicket(action).IsActive,Is.True);Frame();Assert.That(actor.GetTicket(action).IsActive,Is.True);Frame(0,Key.E);Assert.That(actor.GetTicket(action).IsActive,Is.False);Frame();
            adapter.ToggleInsteadOfHold=false;Frame(0,Key.E);Assert.That(actor.GetTicket(action).IsActive,Is.True);Frame();Assert.That(actor.GetTicket(action).IsActive,Is.False);yield return null;
        }
        [UnityTest] public IEnumerator UiBlockOwnsCancellationAndHeldPrimaryCannotLeakAfterClosure()
        {
            BeginGrab();using var window=player.AcquireContext(root,InputContextRequest.UI());Assert.That(grab.IsHeld,Is.False,"Blocking the initiating input cancels its owned grab");
            actor.View.rotation=Quaternion.Euler(0,90,0);var other=HoverOther();Frame(30,Key.E);Assert.That(otherExecutions,Is.Zero);window.Dispose();Frame(30,Key.E);player.SendMessage("Update");Assert.That(otherExecutions,Is.Zero);
            Frame();player.SendMessage("Update");Frame(0,Key.E);Assert.That(otherExecutions,Is.EqualTo(1));yield return null;
        }
        [UnityTest] public IEnumerator RebindAndKeyboardReconnectKeepReleaseGateBeforeFreshPress()
        {
            BeginGrab();using(var rebind=player.BeginRebind(player.Action("Gameplay/Interact"),0,player)) {Frame(30,Key.E);Assert.That(grab.IsHeld,Is.False);}
            Assert.That(player.TrySetBinding(player.Action("Gameplay/Interact"),0,"<Keyboard>/k",out var reason),Is.True,reason);
            Frame();player.SendMessage("Update");BeginGrab(Key.K);
            InputSystem.RemoveDevice(keyboard);InputSystem.AddDevice(keyboard);InputSystem.Update();
            // Reconnect may cancel an owned operation; it must never execute another world action.
            if(!grab.IsHeld){Frame();player.SendMessage("Update");BeginGrab(Key.K);}
            for(int i=0;i<5;i++)Frame(450);HoverOther();Frame(1,Key.K);Released();yield return null;
        }
        [UnityTest] public IEnumerator DisabledAdapterAndActorIgnorePrimaryAndMouseInput()
        {
            BeginGrab();adapter.enabled=false;Assert.That(grab.IsHeld,Is.False);actor.View.rotation=Quaternion.Euler(0,90,0);var other=HoverOther();Frame(10,Key.E);Assert.That(otherExecutions,Is.Zero);
            Frame();adapter.enabled=true;actor.enabled=false;Frame(10,Key.E);Assert.That(otherExecutions,Is.Zero);
            Frame();actor.enabled=true;Frame(0,Key.E);Assert.That(otherExecutions,Is.EqualTo(1));yield return null;
        }
        [UnityTest] public IEnumerator ReconnectDuringUiStillRequiresNeutralBeforePrimary()
        {
            actor.View.rotation=Quaternion.Euler(0,90,0);HoverOther();
            using var window=player.AcquireContext(root,InputContextRequest.UI());Frame(20,Key.E);
            InputSystem.RemoveDevice(keyboard);InputSystem.AddDevice(keyboard);Frame(20,Key.E);
            window.Dispose();Frame(20,Key.E);player.SendMessage("Update");Assert.That(otherExecutions,Is.Zero);
            Frame();player.SendMessage("Update");Frame(0,Key.E);Assert.That(otherExecutions,Is.EqualTo(1));yield return null;
        }
    }
}
#endif
