using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;

namespace EasyBuilding.TwoD
{
    [DisallowMultipleComponent, RequireComponent(typeof(BuildingSystem), typeof(BuildingPhysics2D)), AddComponentMenu("EasyGameFramework/Building/2D/Building Placer 2D")]
    public sealed class BuildingPlacer2D : MonoBehaviour, IBuildingPlacementInput
    {
        public Camera ViewCamera;
        public BuildingDefinition StartingBuilding;
        [Tooltip("Optional IBuildingActor or IBuildingSessionProvider. Building Actor shares selection with other controllers and UI.")]
        public MonoBehaviour Actor;
        public bool RepeatPlacement;
        public bool AimFromScreenCenter;
        [Min(1)] public float RotationStep = 90;
        public InputActionReference ConfirmAction, CancelAction, RotateAction;
        public PlacementResult PreviewResult { get; private set; }
        public string Status => ConfigurationError ?? PreviewResult.Reason ?? "Select a building to begin.";
        public string ConfigurationError { get; private set; }
        private bool inputBlocked;
        private readonly BuildingInputBlocks inputBlocks = new BuildingInputBlocks();
        public bool InputBlocked { get => inputBlocked || inputBlocks.IsBlocked; set => inputBlocked = value; }
        public void SetInputBlocked(object owner, bool blocked) => inputBlocks.Set(owner, blocked);
        public BuildingSession Session { get { EnsureSession(); return session; } }
        public event Action<BuildingSession> SessionChanged;
        private BuildingSession session, explicitSession;
        private MonoBehaviour sessionActor;
        private bool ownsSession, hasExplicitSession, destroying, started;
        private BuildingSystem world;
        private BuildingPhysics2D backend;
        private InputAction confirm, cancel, rotate;
        private InputAction ownedConfirm, ownedCancel, ownedRotate;
        private GameObject preview;
        private BuildingDefinition previewDefinition;
        private SpriteRenderer[] sprites;
        private float angle;
        public void SetSession(BuildingSession value)
        {
            if (destroying) throw new ObjectDisposedException(nameof(BuildingPlacer2D));
            if (world == null) world = GetComponent<BuildingSystem>();
            if (value != null && (value.IsDisposed || value.System != world)) throw new ArgumentException("Borrow a live session from this Building System.", nameof(value));
            if (value == null && !hasExplicitSession) { EnsureSession(); return; }
            hasExplicitSession = value != null; explicitSession = value; BindSession(value, false); EnsureSession();
        }
        private bool EnsureSession()
        {
            if (destroying) return false;
            if (world == null) world = GetComponent<BuildingSystem>();
            if ((hasExplicitSession || ReferenceEquals(Actor, null) || Actor != null) && session != null && !session.IsDisposed && session.System == world &&
                (hasExplicitSession || ReferenceEquals(Actor, sessionActor))) return true;
            try
            {
                if (world == null) throw new InvalidOperationException("Assign a Building System before resolving the session.");
                BuildingSession next; bool owned = false;
                if (hasExplicitSession) next = explicitSession;
                else if (ReferenceEquals(Actor, null)) { next = world.CreateSession(); owned = true; }
                else if (Actor == null) throw new InvalidOperationException("The assigned Actor was destroyed. Assign a live actor or clear the field.");
                else if (Actor is IBuildingSessionProvider provider) next = provider.GetSession(world);
                else if (Actor is IBuildingActor actor) { next = world.CreateSession(actor); owned = true; }
                else throw new InvalidOperationException("Actor must implement IBuildingActor or IBuildingSessionProvider.");
                if (next == null || next.IsDisposed || next.System != world) throw new InvalidOperationException("Use a live session from this world. SetSession(null) reconnects the configured Actor.");
                ConfigurationError = null; sessionActor = Actor; BindSession(next, owned); return true;
            }
            catch (Exception exception) { ConfigurationError = exception.Message; BindSession(null, false); return false; }
        }
        private void BindSession(BuildingSession value, bool owned)
        {
            if (ReferenceEquals(session, value)) { ownsSession = owned; return; }
            var previous = session; bool dispose = ownsSession;
            if (previous != null) previous.Disposed -= SessionDisposed;
            session = value; ownsSession = owned; if (value != null) value.Disposed += SessionDisposed;
            DestroyPreview(); angle = 0;
            if (dispose) previous?.Dispose();
            if (SessionChanged != null) foreach (Action<BuildingSession> handler in SessionChanged.GetInvocationList()) try { handler(value); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        private void SessionDisposed(BuildingSession value) { if (ReferenceEquals(value, session)) BindSession(null, false); }
        private void OnEnable()
        {
            world = GetComponent<BuildingSystem>(); backend = GetComponent<BuildingPhysics2D>();
            if (EnsureSession())
            {
                session.RepeatPlacement = RepeatPlacement;
                if (session.Selected == null && (ownsSession || !started) && StartingBuilding != null) session.Select(StartingBuilding);
            }
            started = true;
            confirm = Bind(ConfirmAction, "Confirm", "<Mouse>/leftButton", out ownedConfirm);
            cancel = Bind(CancelAction, "Cancel", "<Keyboard>/escape", out ownedCancel);
            rotate = Bind(RotateAction, "Rotate", "<Keyboard>/r", out ownedRotate);
        }
        private static InputAction Bind(InputActionReference reference, string name, string binding, out InputAction owned)
        {
            owned = reference == null || reference.action == null ? new InputAction(name, InputActionType.Button, binding) : reference.action.Clone();
            owned.Enable(); return owned;
        }
        private void OnDisable()
        {
            if (ownsSession) BindSession(null, false);
            ownedConfirm?.Dispose(); ownedCancel?.Dispose(); ownedRotate?.Dispose();
            ownedConfirm = ownedCancel = ownedRotate = confirm = cancel = rotate = null; DestroyPreview();
        }
        private void OnDestroy() { destroying = true; BindSession(null, false); SessionChanged = null; }
        private void Update()
        {
            if (!EnsureSession() || ViewCamera == null) return;
            if (InputBlocked) { if (preview != null) preview.SetActive(false); return; }
            if (cancel != null && cancel.WasPressedThisFrame()) Session.Cancel();
            if (Session.Selected == null) { DestroyPreview(); return; }
            if (rotate != null && rotate.WasPressedThisFrame()) angle = Mathf.Repeat(angle + RotationStep, 360);
            var screen = AimFromScreenCenter || Pointer.current == null ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : Pointer.current.position.ReadValue();
            var ray = ViewCamera.ScreenPointToRay(screen); var plane = new Plane(Vector3.forward, new Vector3(0, 0, backend.PlaneZ));
            if (!plane.Raycast(ray, out float distance)) return;
            var point = ray.GetPoint(distance); var socket = Session.Selected.Placement == PlacementMode.Socket ? world.FindSocket(Session.Selected, point) : null;
            var request = new PlacementRequest(point, Quaternion.Euler(0, 0, angle), socket);
            PreviewResult = Session.Check(request);
            if (Session.Selected.Prefab != null && Session.Selected.ConfigurationError == null) UpdatePreview(Session.Selected, PreviewResult, request);
            else DestroyPreview();
            if (confirm != null && confirm.WasPressedThisFrame()) { Session.RepeatPlacement = RepeatPlacement; Session.TryConfirm(request, out _, out _); }
        }
        private void UpdatePreview(BuildingDefinition definition, PlacementResult result, PlacementRequest request)
        {
            if (previewDefinition != definition)
            {
                DestroyPreview(); previewDefinition = definition; preview = new GameObject("2D building preview");
                var source = definition.Prefab.GetComponentsInChildren<SpriteRenderer>(true); sprites = new SpriteRenderer[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    var child = new GameObject("Sprite"); child.transform.SetParent(preview.transform, false);
                    child.transform.localPosition = definition.Prefab.transform.InverseTransformPoint(source[i].transform.position);
                    child.transform.localRotation = Quaternion.Inverse(definition.Prefab.transform.rotation) * source[i].transform.rotation;
                    child.transform.localScale = source[i].transform.lossyScale;
                    var sprite = child.AddComponent<SpriteRenderer>(); sprites[i] = sprite;
                    sprite.sprite = source[i].sprite; sprite.sharedMaterial = source[i].sharedMaterial; sprite.flipX = source[i].flipX; sprite.flipY = source[i].flipY;
                    sprite.sortingLayerID = source[i].sortingLayerID; sprite.sortingOrder = source[i].sortingOrder + 1;
                }
            }
            var rotation = result.Pose.rotation;
            bool validRotation = rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w > 0.00001f;
            preview.transform.SetPositionAndRotation(validRotation ? result.Pose.position : request.Position, validRotation ? rotation : request.Rotation);
            preview.SetActive(true);
            var color = result.IsValid ? new Color(0.2f, 1, 0.3f, 0.5f) : new Color(1, 0.2f, 0.2f, 0.5f);
            foreach (var sprite in sprites) sprite.color = color;
        }
        private void DestroyPreview()
        { if (preview != null) { preview.SetActive(false); Destroy(preview); } preview = null; previewDefinition = null; sprites = null; }
        public InputAction InputActionFor(BuildingControl control) => control == BuildingControl.Confirm ? confirm : control == BuildingControl.Cancel ? cancel : control == BuildingControl.RotateRight ? rotate : null;
    }
}
