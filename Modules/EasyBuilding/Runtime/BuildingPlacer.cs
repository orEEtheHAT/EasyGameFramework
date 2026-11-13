using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

namespace EasyBuilding
{
    public enum BuildingControl { Confirm, Cancel, RotateRight, RotateLeft, Demolish }
    public interface IBuildingPlacementInput
    {
        BuildingSession Session { get; }
        PlacementResult PreviewResult { get; }
        string Status { get; }
        bool InputBlocked { get; set; }
        void SetInputBlocked(object owner, bool blocked);
        InputAction InputActionFor(BuildingControl control);
    }
    /// <summary>Ready-to-use Input System controls and a harmless mesh-only preview.</summary>
    [RequireComponent(typeof(BuildingSystem)), DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Placer")]
    public sealed class BuildingPlacer : MonoBehaviour, IBuildingPlacementInput
    {
        public BuildingDefinition StartingBuilding;
        [Tooltip("Defaults to the camera tagged MainCamera.")]
        public Camera ViewCamera;
        [Tooltip("Enable when using the EasyMovement first-person camera.")]
        public bool AimFromScreenCenter;
        [Min(1)] public float Reach = 30f;
        [Min(0.1f)] public float SocketRadius = 2f;
        [Range(1, 180)] public float RotationStep = 90f;
        public bool ShowHelp = true;
        public LayerMask AimLayers = ~0;
        [Tooltip("Optional IBuildingActor or IBuildingSessionProvider. Building Actor supplies one shared session per player/world.")] public MonoBehaviour Actor;
        public bool RepeatPlacement;
        [Tooltip("A custom pose, XR or AR controller supplies UpdatePreview/confirmation. Mouse targeting is disabled.")]
        public bool ExternalTargeting;
        [Tooltip("Optional bindings. The placer owns runtime clones and leaves the source actions untouched.")]
        public InputActionReference ConfirmAction, CancelAction, RotateRightAction, RotateLeftAction, DemolishAction;
        private bool inputBlocked;
        private readonly BuildingInputBlocks inputBlocks = new BuildingInputBlocks();
        public bool InputBlocked { get => inputBlocked || inputBlocks.IsBlocked; set => inputBlocked = value; }
        public void SetInputBlocked(object owner, bool blocked) => inputBlocks.Set(owner, blocked);
        public BuildingSession Session { get { EnsureSession(); return session; } }
        public string ConfigurationError { get; private set; }
        public event Action<BuildingSession> SessionChanged;
        public PlacementResult PreviewResult { get; private set; }
        public BuildingSystem System { get; private set; }
        public string Status => status.text;
        private readonly GUIContent status = new GUIContent("Select a building to begin.");
        private readonly GUIContent help = new GUIContent("R / Q: rotate   |   Left click: build   |   Right click / Esc: cancel   |   X: remove");
        private InputAction confirm, cancel, rotateRight, rotateLeft, remove;
        private BuildingDefinition previewDefinition;
        private GameObject preview;
        private Material previewMaterial;
        private float yaw;
        private bool lastValid;
        private bool hasColor;
        private GUIStyle textStyle;
        private bool ownsSession;
        private bool usesDefaultSession, hasExplicitSession, destroying;
        private BuildingSession session, explicitSession;
        private MonoBehaviour sessionActor;

        /// <summary>Borrow a live session in this world. Null returns to automatic Actor/provider/default resolution.</summary>
        public void SetSession(BuildingSession value)
        {
            if (destroying) throw new ObjectDisposedException(nameof(BuildingPlacer));
            if (System == null) System = GetComponent<BuildingSystem>();
            if (value != null && (value.IsDisposed || value.System != System)) throw new ArgumentException("Borrow an active session from this Building System.", nameof(value));
            if (value == null && !hasExplicitSession) { EnsureSession(); return; }
            hasExplicitSession = value != null; explicitSession = value;
            BindSession(value, false, false); EnsureSession();
        }
        public bool Select(BuildingDefinition definition) => EnsureSession() && session.Select(definition);
        public void CancelSelection() { if (EnsureSession()) session.Cancel(); ClearPreview(); }
        public bool TryConfirm(PlacementRequest request, out PlacedBuilding building, out PlacementResult result)
        {
            building = null;
            if (!EnsureSession()) { result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, ConfigurationError); return false; }
            session.RepeatPlacement = RepeatPlacement;
            bool success = session.TryConfirm(request, out building, out result);
            PreviewResult = result; status.text = result.Reason;
            if (success) ClearPreview();
            return success;
        }
        private bool EnsureSession()
        {
            if (destroying) return false;
            if (System == null) System = GetComponent<BuildingSystem>();
            if ((hasExplicitSession || ReferenceEquals(Actor, null) || Actor != null) && session != null && !session.IsDisposed && session.System == System &&
                (hasExplicitSession || ReferenceEquals(sessionActor, Actor))) return true;
            try
            {
                if (System == null) throw new InvalidOperationException("Assign a Building System before resolving a placement session.");
                BuildingSession next; bool owned = false, useDefault = false;
                if (hasExplicitSession)
                {
                    next = explicitSession;
                    if (next == null || next.IsDisposed) throw new InvalidOperationException("The borrowed session is closed. Call SetSession with a live session, or null to reconnect automatically.");
                }
                else if (ReferenceEquals(Actor, null)) { next = System.DefaultSession; useDefault = true; }
                else if (Actor == null) throw new InvalidOperationException("The assigned Actor was destroyed. Assign a live Actor or explicitly clear the field.");
                else if (Actor is IBuildingSessionProvider provider) next = provider.GetSession(System);
                else if (Actor is IBuildingActor actor) { next = System.CreateSession(actor); owned = true; }
                else throw new InvalidOperationException("Actor must implement IBuildingActor or IBuildingSessionProvider.");
                if (next == null || next.IsDisposed || next.System != System) throw new InvalidOperationException("The session provider must return a live session for this Building System.");
                ConfigurationError = null; sessionActor = Actor; BindSession(next, owned, useDefault); return true;
            }
            catch (Exception exception)
            {
                ConfigurationError = exception.Message; BindSession(null, false, false); status.text = ConfigurationError; return false;
            }
        }
        private void BindSession(BuildingSession next, bool owned, bool useDefault)
        {
            if (ReferenceEquals(session, next)) { ownsSession = owned; usesDefaultSession = useDefault; return; }
            var previous = session; bool disposePrevious = ownsSession;
            if (previous != null) previous.Disposed -= SessionDisposed;
            session = next; ownsSession = owned; usesDefaultSession = useDefault;
            if (next != null) next.Disposed += SessionDisposed;
            ClearPreview(); yaw = 0; PreviewResult = PlacementResult.Fail(PlacementFailure.NoSelection, "Select a building to begin.");
            if (disposePrevious) previous?.Dispose();
            if (SessionChanged != null) foreach (Action<BuildingSession> handler in SessionChanged.GetInvocationList()) try { handler(next); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        private void SessionDisposed(BuildingSession value) { if (ReferenceEquals(session, value)) BindSession(null, false, false); }

        private void Awake()
        {
            System = GetComponent<BuildingSystem>();
            EnsureSession();
            if (ViewCamera == null) ViewCamera = Camera.main;
            confirm = ConfirmAction != null && ConfirmAction.action != null ? ConfirmAction.action.Clone() : new InputAction("Build", InputActionType.Button, "<Mouse>/leftButton");
            if (ConfirmAction == null) confirm.AddBinding("<Gamepad>/buttonSouth");
            cancel = CancelAction != null && CancelAction.action != null ? CancelAction.action.Clone() : new InputAction("Cancel", InputActionType.Button, "<Mouse>/rightButton");
            if (CancelAction == null) { cancel.AddBinding("<Keyboard>/escape"); cancel.AddBinding("<Gamepad>/buttonEast"); }
            rotateRight = RotateRightAction != null && RotateRightAction.action != null ? RotateRightAction.action.Clone() : new InputAction("Rotate Right", InputActionType.Button, "<Keyboard>/r");
            if (RotateRightAction == null) rotateRight.AddBinding("<Gamepad>/rightShoulder");
            rotateLeft = RotateLeftAction != null && RotateLeftAction.action != null ? RotateLeftAction.action.Clone() : new InputAction("Rotate Left", InputActionType.Button, "<Keyboard>/q");
            if (RotateLeftAction == null) rotateLeft.AddBinding("<Gamepad>/leftShoulder");
            remove = DemolishAction != null && DemolishAction.action != null ? DemolishAction.action.Clone() : new InputAction("Demolish", InputActionType.Button, "<Keyboard>/x");
        }
        private void OnEnable() { EnsureSession(); confirm?.Enable(); cancel?.Enable(); rotateRight?.Enable(); rotateLeft?.Enable(); remove?.Enable(); }
        private void Start() { if (StartingBuilding != null && EnsureSession() && session.Selected == null) session.Select(StartingBuilding); }
        private void OnDisable()
        {
            confirm?.Disable(); cancel?.Disable(); rotateRight?.Disable(); rotateLeft?.Disable(); remove?.Disable();
            if (ownsSession || usesDefaultSession) session?.Cancel();
            ClearPreview();
        }
        private void OnDestroy()
        {
            confirm?.Dispose(); cancel?.Dispose(); rotateRight?.Dispose(); rotateLeft?.Dispose(); remove?.Dispose();
            destroying = true; BindSession(null, false, false); SessionChanged = null; ClearPreview();
        }
        private void Update()
        {
            if (!EnsureSession()) return;
            if (InputBlocked) { if (preview != null) preview.SetActive(false); return; }
            if (ExternalTargeting) return;
            if (ViewCamera == null)
            {
                if (preview != null) preview.SetActive(false);
                status.text = "Assign View Camera, or tag a camera MainCamera.";
                return;
            }
            if (cancel.WasPressedThisFrame()) { CancelSelection(); status.text = "Placement cancelled."; }
            if (Session.Selected != previewDefinition)
            {
                ClearPreview(); yaw = 0;
                if (Session.Selected != null) CreatePreview(Session.Selected);
            }
            var screen = AimFromScreenCenter || Mouse.current == null
                ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : Mouse.current.position.ReadValue();
            var ray = ViewCamera.ScreenPointToRay(screen);
            Physics.SyncTransforms();
            bool hitSurface = System.gameObject.scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out var hit, Reach, AimLayers, QueryTriggerInteraction.Ignore);
            if (remove.WasPressedThisFrame() && hitSurface)
            {
                var building = hit.collider.GetComponentInParent<PlacedBuilding>();
                if (building != null && !System.TryDemolish(building, out var demolition, Session.Actor)) status.text = demolition.Reason;
            }
            if (Session.Selected == null) return;
            if (rotateRight.WasPressedThisFrame()) yaw = (yaw + RotationStep) % 360;
            if (rotateLeft.WasPressedThisFrame()) yaw = (yaw - RotationStep) % 360;
            if (!hitSurface)
            {
                if (preview != null) preview.SetActive(false);
                PreviewResult = PlacementResult.Fail(PlacementFailure.MissingSurface, "Point at a surface within Reach.");
                status.text = PreviewResult.Reason;
                return;
            }
            var socket = Session.Selected.Placement == PlacementMode.Socket ? System.FindSocket(Session.Selected, hit.point, SocketRadius) : null;
            var space = socket != null ? socket.Space : hit.collider.GetComponentInParent<BuildingSpace>();
            var rotation = Quaternion.Euler(0, yaw, 0);
            if (Session.Selected.AlignToSurface && socket == null) rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * rotation;
            var request = new PlacementRequest(hit.point, rotation, socket, space, surfaceNormal: hit.normal);
            UpdatePreview(request);
            if (confirm.WasPressedThisFrame())
            {
                if (TryConfirm(request, out _, out var result))
                { ClearPreview(); status.text = "Built. Select a building to place another."; }
                else { PreviewResult = result; status.text = result.Reason; }
            }
        }

        /// <summary>Update the same visual and validation path from a custom targeting controller.</summary>
        public void HidePreview() { if (preview != null) preview.SetActive(false); }
        public PlacementResult UpdatePreview(PlacementRequest request)
        {
            if (!EnsureSession()) return PreviewResult = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, ConfigurationError);
            if (Session.Selected != previewDefinition)
            {
                ClearPreview();
                if (Session.Selected != null) CreatePreview(Session.Selected);
            }
            PreviewResult = Session.Check(request);
            status.text = PreviewResult.Reason;
            if (preview != null)
            {
                preview.SetActive(true);
                bool validRotation = BuildingMath.ValidRotation(PreviewResult.Pose.rotation);
                preview.transform.SetPositionAndRotation(validRotation ? PreviewResult.Pose.position : request.Position, validRotation ? PreviewResult.Pose.rotation : request.Rotation);
                preview.transform.localScale = request.Scale;
                if (!hasColor || lastValid != PreviewResult.IsValid)
                {
                    lastValid = PreviewResult.IsValid; hasColor = true;
                    previewMaterial.color = lastValid ? new Color(0.12f, 0.95f, 0.5f, 0.5f) : new Color(1, 0.15f, 0.12f, 0.5f);
                }
            }
            return PreviewResult;
        }
        public InputAction InputActionFor(BuildingControl control)
        {
            switch (control)
            {
                case BuildingControl.Confirm: return confirm;
                case BuildingControl.Cancel: return cancel;
                case BuildingControl.RotateRight: return rotateRight;
                case BuildingControl.RotateLeft: return rotateLeft;
                case BuildingControl.Demolish: return remove;
                default: return null;
            }
        }

        private void CreatePreview(BuildingDefinition definition)
        {
            previewDefinition = definition;
            preview = new GameObject("Building preview");
            preview.layer = 2;
            previewMaterial = new Material(Resources.Load<Shader>("EasyBuildingPreview"));
            previewMaterial.hideFlags = HideFlags.HideAndDontSave;
            int meshes = definition.Prefab != null ? CopyMeshes(definition.Prefab.transform, preview.transform) : 0;
            if (meshes == 0)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var collider = box.GetComponent<Collider>(); collider.enabled = false; BuildingMath.Destroy(collider);
                box.transform.SetParent(preview.transform, false);
                box.transform.localPosition = definition.BoundsCenter;
                box.transform.localScale = definition.BoundsSize;
                box.GetComponent<Renderer>().sharedMaterial = previewMaterial;
                box.layer = 2;
            }
        }
        private int CopyMeshes(Transform source, Transform target)
        {
            int count = 0;
            if (source.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null && source.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var visual = target.gameObject.AddComponent<MeshRenderer>();
                var materials = new Material[filter.sharedMesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = previewMaterial;
                visual.sharedMaterials = materials;
                visual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                count++;
            }
            for (int i = 0; i < source.childCount; i++)
            {
                var child = source.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                var clone = new GameObject(child.name); clone.layer = 2;
                clone.transform.SetParent(target, false);
                clone.transform.localPosition = child.localPosition;
                clone.transform.localRotation = child.localRotation;
                clone.transform.localScale = child.localScale;
                count += CopyMeshes(child, clone.transform);
            }
            return count;
        }
        private void ClearPreview()
        {
            if (preview != null) { preview.SetActive(false); BuildingMath.Destroy(preview); }
            BuildingMath.Destroy(previewMaterial);
            preview = null; previewMaterial = null; previewDefinition = null; hasColor = false;
        }
        private void OnGUI()
        {
            if (!ShowHelp) return;
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(12, 12, 6, 6) };
            var previous = GUI.matrix;
            float scale = Mathf.Max(0.6f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float width = Screen.width / scale, height = Screen.height / scale;
            GUI.Box(new Rect(16, height - 112, Mathf.Min(1000, width - 32), 48), status, textStyle);
            GUI.Box(new Rect(16, height - 60, Mathf.Min(1000, width - 32), 44), help, textStyle);
            GUI.matrix = previous;
        }
    }
}
