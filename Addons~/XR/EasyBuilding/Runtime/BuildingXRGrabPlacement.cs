using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace EasyBuilding.XR
{
    /// <summary>Grab a construction handle, position it, and release to build. The final prefab never becomes the grab preview.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(XRGrabInteractable)), AddComponentMenu("EasyGameFramework/Building/XR/Grab Placement")]
    public sealed class BuildingXRGrabPlacement : MonoBehaviour
    {
        public BuildingPlacer Placer;
        public BuildingDefinition Definition;
        public BuildingSpace Space;
        [Tooltip("Optional placement pivot on the handle.")] public Transform PoseSource;
        public bool ConfirmOnRelease = true;
        private XRGrabInteractable grab;
        private bool held, previousExternal;
        public PlacementResult Result { get; private set; }
        private void OnEnable()
        {
            grab = GetComponent<XRGrabInteractable>(); grab.firstSelectEntered.AddListener(Begin); grab.lastSelectExited.AddListener(End);
            if (Placer != null) { previousExternal = Placer.ExternalTargeting; Placer.ExternalTargeting = true; }
        }
        private void OnDisable()
        {
            if (grab != null) { grab.firstSelectEntered.RemoveListener(Begin); grab.lastSelectExited.RemoveListener(End); }
            if (Placer != null) { Placer.ExternalTargeting = previousExternal; if (held) { Placer.Session?.Cancel(); Placer.HidePreview(); } }
            held = false;
        }
        private void Begin(SelectEnterEventArgs args)
        {
            if (Placer == null || Placer.InputBlocked || Placer.Session == null) return;
            held = Definition != null ? Placer.Session.Select(Definition) : Placer.Session.Selected != null;
        }
        private PlacementRequest Request()
        {
            var source = PoseSource != null ? PoseSource : transform; var selected = Placer.Session.Selected;
            var socket = selected != null && selected.Placement == PlacementMode.Socket ? Placer.System.FindSocket(selected, source.position, Placer.SocketRadius) : null;
            return new PlacementRequest(source.position, source.rotation, socket, socket != null ? socket.Space : Space);
        }
        private void LateUpdate()
        {
            if (!held || Placer == null) return;
            if (Placer.InputBlocked) { Placer.HidePreview(); return; }
            Result = Placer.UpdatePreview(Request());
        }
        private void End(SelectExitEventArgs args)
        {
            if (!held) return; held = false;
            if (Placer == null || Placer.Session == null) return;
            if (!args.isCanceled && ConfirmOnRelease && !Placer.InputBlocked) TryConfirm(); else Placer.Session.Cancel();
            Placer.HidePreview();
        }
        public bool TryConfirm()
        {
            if (Placer == null || Placer.InputBlocked || Placer.Session == null) return false;
            Placer.Session.RepeatPlacement = false;
            bool success = Placer.Session.TryConfirm(Request(), out _, out var result); Result = result; return success;
        }
    }
}
