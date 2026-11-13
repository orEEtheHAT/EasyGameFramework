using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace EasyBuilding.AR
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/AR/Plane Placement")]
    public sealed class BuildingARPlanePlacement : MonoBehaviour
    {
        public BuildingPlacer Placer;
        public ARRaycastManager Raycasts;
        [Tooltip("Persistent space under the application's XR Origin or ARAnchor.")] public BuildingSpace Space;
        public bool ConfirmOnTouch = true;
        public bool AimFromScreenCenter;
        public float Yaw;
        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private bool previousExternal;
        public bool HasTrackedPlane { get; private set; }
        public PlacementResult Result { get; private set; }
        private void OnEnable() { if (Placer != null) { previousExternal = Placer.ExternalTargeting; Placer.ExternalTargeting = true; } }
        private void OnDisable() { HasTrackedPlane = false; if (Placer != null) { Placer.ExternalTargeting = previousExternal; Placer.HidePreview(); } }
        private Vector2 ScreenPoint()
        {
            if (!AimFromScreenCenter && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return Touchscreen.current.primaryTouch.position.ReadValue();
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }
        private bool TryRequest(Vector2 screenPoint, out PlacementRequest request)
        {
            request = default;
            HasTrackedPlane = Placer != null && Placer.Session != null && !Placer.InputBlocked && Raycasts != null && Raycasts.enabled &&
                ARSession.state == ARSessionState.SessionTracking && Raycasts.Raycast(screenPoint, hits, TrackableType.PlaneWithinPolygon) && hits.Count != 0;
            if (!HasTrackedPlane) return false;
            var pose = hits[0].pose;
            request = new PlacementRequest(pose.position, pose.rotation * Quaternion.Euler(0, Yaw, 0), space: Space, surfaceNormal: pose.rotation * Vector3.up); return true;
        }
        private void Update()
        {
            var point = ScreenPoint();
            if (!TryRequest(point, out var request)) { Placer?.HidePreview(); Result = PlacementResult.Fail(PlacementFailure.MissingSurface, "Find a tracked AR plane."); return; }
            Result = Placer.UpdatePreview(request);
            if (ConfirmOnTouch && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) TryConfirm(point);
        }
        public bool TryConfirm(Vector2 screenPoint)
        {
            if (!TryRequest(screenPoint, out var request)) { Result = PlacementResult.Fail(PlacementFailure.MissingSurface, "Find a tracked AR plane."); return false; }
            Placer.Session.RepeatPlacement = Placer.RepeatPlacement;
            bool success = Placer.Session.TryConfirm(request, out _, out var result); Result = result; if (success) Placer.HidePreview(); return success;
        }
    }
}
