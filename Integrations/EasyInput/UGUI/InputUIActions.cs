using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace EasyInput.Integrations
{
    [RequireComponent(typeof(InputSystemUIInputModule)), AddComponentMenu("EasyGameFramework/Input/uGUI Actions")]
    public sealed class InputUIActions : MonoBehaviour
    {
        public PlayerInputSettings Player;
        private InputSystemUIInputModule module;
        private bool wasEnabled, connected;
        private PlayerInputSettings subscribed;
        private readonly List<InputActionReference> ownedReferences = new List<InputActionReference>();
        private InputActionReference point, click, rightClick, middleClick, scroll, move, submit, cancel, position, orientation;
        private InputActionAsset asset;
        private InputActionReference Link(string path)
        {
            var reference = InputActionReference.Create(subscribed.Action(path)); ownedReferences.Add(reference); return reference;
        }
        private void OnEnable()
        {
            module = GetComponent<InputSystemUIInputModule>();
            if (!Player) Player = GetComponentInParent<PlayerInputSettings>();
            if (!Player) { Debug.LogError("uGUI Actions: assign Player Input Settings from the local player.", this); enabled = false; return; }
            try { foreach (string path in new[] { "UI/Point", "UI/Click", "UI/Scroll", "UI/Navigate", "UI/Submit", "UI/Cancel" }) Player.Action(path); }
            catch (System.ArgumentException exception) { Debug.LogError("uGUI Actions: " + exception.Message, this); enabled = false; return; }
            wasEnabled = module.enabled;
            module.enabled = false;
            if (!module || !isActiveAndEnabled) { if (module) module.enabled = wasEnabled; return; }
            // Native OnDisable disposes its auto-generated DefaultInputActions and unassigns them.
            // Capture AFTER that transition; authored foreign assets/references remain intact.
            asset = module.actionsAsset; point = module.point; click = module.leftClick; rightClick = module.rightClick;
            middleClick = module.middleClick; scroll = module.scrollWheel; move = module.move; submit = module.submit; cancel = module.cancel;
            position = module.trackedDevicePosition; orientation = module.trackedDeviceOrientation;
            subscribed = Player; connected = true;
            ClearReferences();
            module.actionsAsset = subscribed.Actions;
            module.point = Link("UI/Point"); module.leftClick = Link("UI/Click"); module.scrollWheel = Link("UI/Scroll");
            module.move = Link("UI/Navigate"); module.submit = Link("UI/Submit"); module.cancel = Link("UI/Cancel");
            subscribed.RebindChanged += Refresh; Refresh();
        }
        private void Refresh() { if (module && subscribed) module.enabled = wasEnabled && subscribed.Rebind?.IsActive != true; }
        private void ClearReferences()
        {
            // Changing actionsAsset remaps every non-null reference by map/name, creating new
            // references. Detach first, including optional slots, to avoid stale disposed actions.
            module.point = null; module.leftClick = null; module.rightClick = null; module.middleClick = null;
            module.scrollWheel = null; module.move = null; module.submit = null; module.cancel = null;
            module.trackedDevicePosition = null; module.trackedDeviceOrientation = null;
        }
        private static InputActionReference Live(InputActionReference reference)
        {
            return reference && reference.asset && reference.action?.actionMap?.asset == reference.asset ? reference : null;
        }
        private static InputActionAsset Live(InputActionAsset value)
        {
            if (!value) return null;
            foreach (var map in value.actionMaps) if (map.asset != value) return null;
            return value;
        }
        private void OnDisable()
        {
            if (!connected) return;
            connected = false;
            if (subscribed) subscribed.RebindChanged -= Refresh;
            subscribed = null;
            try
            {
                if (module)
                {
                    module.enabled = false;
                    ClearReferences();
                    module.actionsAsset = Live(asset);
                    module.point = Live(point); module.leftClick = Live(click); module.rightClick = Live(rightClick); module.middleClick = Live(middleClick);
                    module.scrollWheel = Live(scroll); module.move = Live(move); module.submit = Live(submit); module.cancel = Live(cancel);
                    module.trackedDevicePosition = Live(position); module.trackedDeviceOrientation = Live(orientation);
                    // Empty native-default state is recreated by the module's normal OnEnable.
                    module.enabled = wasEnabled;
                }
            }
            finally
            {
                foreach (var reference in ownedReferences) if (reference) { if (Application.isPlaying) Destroy(reference); else DestroyImmediate(reference); }
                ownedReferences.Clear();
                asset = null; point = click = rightClick = middleClick = scroll = move = submit = cancel = position = orientation = null;
            }
        }
    }
}
