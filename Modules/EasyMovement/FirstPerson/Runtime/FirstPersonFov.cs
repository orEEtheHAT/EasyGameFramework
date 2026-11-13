using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace EasyMovement.FirstPerson
{
    /// <summary>Composes a user's base vertical FOV and owned additive gameplay offsets across gait-camera instances.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Movement/First Person FOV")]
    public sealed class FirstPersonFov : MonoBehaviour
    {
        [SerializeField, Tooltip("Resolves the FPS controller on this object when empty. Configure its default/crouch/sprint/custom cameras first.")]
        private FirstPersonCharacterController controller;
        private readonly List<CinemachineCamera> views = new List<CinemachineCamera>(8);
        private readonly List<ViewState> captured = new List<ViewState>(8);
        private readonly List<FovOverride> overrides = new List<FovOverride>(4);
        private bool available, notifying;
        private uint revision;
        private float authoredBase = 60f, effectiveBase = 60f;
        private CinemachineCamera baseline;
        private string unavailableReason = "Enable First Person FOV in Play Mode.";

        private struct ViewState
        {
            internal CinemachineCamera Camera;
            internal float Authored, LastWrite;
            internal bool Written;
        }

        public FirstPersonCharacterController Controller
        {
            get => controller;
            set
            {
                if (controller == value) return;
                RestoreViews(); captured.Clear(); baseline = null;
                controller = value;
                RefreshCameras();
            }
        }
        public bool IsAvailable { get { RefreshCameras(); return available; } }
        public string UnavailableReason { get { RefreshCameras(); return unavailableReason; } }
        public float AuthoredBaseFov { get { RefreshCameras(); return authoredBase; } }
        public float BaseFieldOfView { get { RefreshCameras(); return effectiveBase; } }
        public event Action Changed;

        public sealed class FovOverride : IDisposable
        {
            internal static readonly FovOverride Empty = new FovOverride(null, null, 0, false);
            internal FirstPersonFov Host;
            internal UnityEngine.Object Owner;
            internal readonly bool Offset;
            private float value;
            internal FovOverride(FirstPersonFov host, UnityEngine.Object owner, float degrees, bool offset)
            { Host = host; Owner = owner; value = degrees; Offset = offset; }
            public bool IsActive
            {
                get
                {
                    if (Host) Host.RefreshCameras();
                    return Host && Host.isActiveAndEnabled && Active(Owner);
                }
            }
            public float Value => value;
            public bool TrySet(float degrees)
            {
                if (!Finite(degrees) || !IsActive) return false;
                degrees = Clamp(degrees, Offset);
                if (value == degrees) return true;
                value = degrees; Host.Apply(); return true;
            }
            public void Dispose()
            {
                var host = Host;
                Host = null; Owner = null;
                if (host && host.overrides.Remove(this)) host.Apply();
            }
        }

        /// <summary>The last acquired live base wins; updating a handle does not reorder it. Inactive owners/providers return an expired handle.</summary>
        public FovOverride AcquireBaseFovOverride(UnityEngine.Object owner, float degrees) => Acquire(owner, degrees, false);
        /// <summary>Additive degrees, composed with the base and other live offsets. Use this for temporary zoom/kicks instead of writing Lens directly.</summary>
        public FovOverride AcquireOffset(UnityEngine.Object owner, float degrees) => Acquire(owner, degrees, true);
        private FovOverride Acquire(UnityEngine.Object owner, float degrees, bool offset)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (!Finite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
            RefreshCameras();
            if (!available || !Active(owner)) return FovOverride.Empty;
            var handle = new FovOverride(this, owner, Clamp(degrees, offset), offset);
            overrides.Add(handle); Apply(); return handle;
        }

        private void OnEnable() => RefreshCameras();
        private void LateUpdate() => RefreshCameras();
        private void OnDisable()
        {
            bool changed = available || overrides.Count != 0;
            Expire(); RestoreViews(); captured.Clear(); views.Clear(); baseline = null;
            available = false; unavailableReason = "Enable First Person FOV in Play Mode.";
            effectiveBase = authoredBase;
            if (changed) Notify();
        }

        /// <summary>Refreshes camera membership and expired owners. Stable frames only inspect references; they do not rewrite lenses.</summary>
        public void RefreshCameras()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || !gameObject.scene.IsValid()) return;
            if (!controller) controller = GetComponent<FirstPersonCharacterController>();
            string error = ValidateViews();
            if (error != null)
            {
                bool changed = available || overrides.Count != 0;
                Expire(); RestoreViews(); captured.Clear(); baseline = null;
                available = false; unavailableReason = error; effectiveBase = authoredBase;
                if (changed) Notify();
                return;
            }
            bool dirty = !available || baseline != controller.defaultCamera;
            available = true; unavailableReason = string.Empty;
            for (int i = captured.Count - 1; i >= 0; --i)
            {
                if (captured[i].Camera && views.Contains(captured[i].Camera)) continue;
                Restore(captured[i]); captured.RemoveAt(i); dirty = true;
            }
            for (int i = 0; i < views.Count; ++i)
            {
                bool found = false;
                for (int j = 0; j < captured.Count; ++j) if (captured[j].Camera == views[i]) { found = true; break; }
                if (found) continue;
                captured.Add(new ViewState { Camera = views[i], Authored = views[i].Lens.FieldOfView }); dirty = true;
            }
            baseline = controller.defaultCamera;
            for (int i = 0; i < captured.Count; ++i)
                if (captured[i].Camera == baseline) authoredBase = captured[i].Authored;
            for (int i = overrides.Count - 1; i >= 0; --i)
            {
                if (Active(overrides[i].Owner)) continue;
                overrides[i].Host = null; overrides[i].Owner = null; overrides.RemoveAt(i); dirty = true;
            }
            if (dirty) Apply();
        }

        private string ValidateViews()
        {
            if (!controller || !controller.defaultCamera) return "Assign an FPS controller with a default Cinemachine camera.";
            controller.GetViewCameras(views);
            for (int i = 0; i < views.Count; ++i)
            {
                var view = views[i];
                if (!view.gameObject.scene.IsValid()) return "Assign scene camera instances, not prefab assets.";
                if (view.Lens.Orthographic || view.Lens.IsPhysicalCamera) return "Gait FOV requires perspective, non-physical Cinemachine lenses.";
                float value = view.Lens.FieldOfView;
                if (!Finite(value) || value < 1 || value > 179) return "Each gait camera needs a finite vertical FOV between 1 and 179 degrees.";
            }
            return null;
        }

        private void Apply()
        {
            effectiveBase = authoredBase;
            double offset = 0;
            for (int i = 0; i < overrides.Count; ++i)
            {
                var handle = overrides[i];
                if (handle.Offset) offset += handle.Value;
                else effectiveBase = handle.Value;
            }
            if (overrides.Count == 0) RestoreViews();
            else if (available)
            {
                for (int i = 0; i < captured.Count; ++i)
                {
                    var state = captured[i];
                    if (!state.Camera) continue;
                    float fov = (float)Math.Max(1d, Math.Min(179d, effectiveBase + state.Authored - authoredBase + offset));
                    var lens = state.Camera.Lens;
                    if (lens.FieldOfView != fov) { lens.FieldOfView = fov; state.Camera.Lens = lens; }
                    state.LastWrite = fov; state.Written = true; captured[i] = state;
                }
            }
            Notify();
        }
        private void Expire()
        {
            for (int i = 0; i < overrides.Count; ++i) { overrides[i].Host = null; overrides[i].Owner = null; }
            overrides.Clear();
        }
        private void RestoreViews()
        {
            for (int i = 0; i < captured.Count; ++i)
            {
                var state = captured[i]; Restore(state); state.Written = false; captured[i] = state;
            }
        }
        private static void Restore(ViewState state)
        {
            if (!state.Written || !state.Camera || state.Camera.Lens.FieldOfView != state.LastWrite) return;
            var lens = state.Camera.Lens; lens.FieldOfView = state.Authored; state.Camera.Lens = lens;
        }
        private void Notify()
        {
            ++revision;
            if (notifying) return;
            var callbacks = Changed;
            if (callbacks == null) return;
            uint expected = revision;
            notifying = true;
            try
            {
                // Changes are infrequent; snapshot allocation is outside the steady-state path.
                foreach (Action callback in callbacks.GetInvocationList())
                {
                    try { callback(); } catch (Exception error) { Debug.LogException(error, this); }
                    // A callback may disable this component, release a handle or change its value.
                    if (!this || revision != expected) break;
                }
            }
            finally { notifying = false; }
        }
        private static float Clamp(float value, bool offset) => offset ? Mathf.Clamp(value, -178, 178) : Mathf.Clamp(value, 1, 179);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Active(UnityEngine.Object owner)
        {
            if (!owner) return false;
            if (owner is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (owner is Component component) return component.gameObject.activeInHierarchy;
            if (owner is GameObject go) return go.activeInHierarchy;
            return true;
        }
    }
}
