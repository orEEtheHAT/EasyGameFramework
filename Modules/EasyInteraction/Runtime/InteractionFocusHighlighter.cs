using UnityEngine;
using UnityEngine.Rendering;

namespace EasyInteraction
{
    /// <summary>Draws one actor's focused target with viewer-owned style into that viewer's camera only.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(InteractionActor)), DefaultExecutionOrder(100)]
    [AddComponentMenu("EasyGameFramework/Interaction/Focus Highlighter")]
    public sealed class InteractionFocusHighlighter : MonoBehaviour
    {
        [SerializeField, Tooltip("Actor whose selected target is highlighted. Empty uses the Actor on this GameObject.")]
        private InteractionActor actor;
        [SerializeField, Tooltip("Optional viewing camera. Empty uses the Camera directly on Actor.View. There is no Main Camera or all-camera fallback. Disabled cameras can be used by manual URP render requests.")]
        private Camera cameraOverride;
        [SerializeField, ColorUsage(false, true), Tooltip("Shared unlit outline colour for this viewer. Source materials are not changed.")]
        private Color outlineColor = new Color(.1f, .8f, 1, 1);
        [SerializeField, Range(.001f, .1f), Tooltip("Shared extrusion width in world metres, not pixels. Uses only this viewer's resolved camera.")]
        private float width = .015f;
        private InteractionActor subscribedActor;
        private InteractionActor localActor;
        private InteractionTarget observedTarget;
        private FocusOutlineGeometry geometry;
        private Transform cachedView;
        private Camera viewCamera;
        private bool cameraSearched;
        private Material material;
        private static Shader cachedShader;
        private static bool shaderResolved, pipelineChecked, universalPipeline;
        private static RenderPipelineAsset checkedPipeline;
        private static readonly int colorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int widthId = Shader.PropertyToID("_OutlineWidth");

        public InteractionActor Actor
        {
            get => actor ? actor : localActor ? localActor : (localActor = GetComponent<InteractionActor>());
            set
            {
                if (actor == value) return;
                Unsubscribe();
                actor = value;
                if (isActiveAndEnabled) RefreshHighlight();
            }
        }
        public Camera CameraOverride
        {
            get => cameraOverride;
            set
            {
                if (cameraOverride == value) return;
                cameraOverride = value;
                if (isActiveAndEnabled) RefreshHighlight();
            }
        }
        public Color OutlineColor { get => outlineColor; set => outlineColor = value; }
        public float Width { get => width; set => width = float.IsNaN(value) ? .015f : Mathf.Clamp(value, .001f, .1f); }
        public Camera ResolvedCamera => ResolveCamera();
        public bool HasRenderCamera
        {
            get
            {
                var camera = ResolvedCamera;
                return camera && camera.gameObject.activeInHierarchy;
            }
        }
        /// <summary>The Actor's active current target, even if outlining is disabled or suppressed.</summary>
        public InteractionTarget ObservedTarget => observedTarget ? observedTarget : null;
        public InteractionTarget HighlightedTarget => CanHighlight() ? observedTarget : null;
        public bool IsHighlighted => CanHighlight();
        public bool IsSuppressedWhileHeld => observedTarget && observedTarget.IsHeldForHighlight;
        public int RendererCount => geometry != null ? geometry.RendererCount : 0;
        public int UnsupportedRendererCount => geometry != null ? geometry.UnsupportedRendererCount : 0;
        public int SmoothedRendererCount => geometry != null ? geometry.SmoothedRendererCount : 0;
        public int FallbackRendererCount => geometry != null ? geometry.FallbackRendererCount : 0;
        /// <summary>Submeshes submitted to this viewer's camera by its last draw tick.</summary>
        public int RenderedPartCount { get; private set; }
        /// <summary>Checks URP and the bundled shader without creating an effect material.</summary>
        public bool IsShaderAvailable
        {
            get
            {
                var pipeline = GraphicsSettings.currentRenderPipeline;
                if (!pipeline) pipeline = null;
                if (!pipelineChecked || !ReferenceEquals(checkedPipeline, pipeline))
                {
                    pipelineChecked = true;
                    checkedPipeline = pipeline;
                    universalPipeline = false;
                    for (var type = pipeline ? pipeline.GetType() : null; type != null; type = type.BaseType)
                        if (type.FullName == "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset") { universalPipeline = true; break; }
                }
                if (!universalPipeline) return false;
                if (!shaderResolved)
                {
                    shaderResolved = true;
                    cachedShader = Resources.Load<Shader>("EasyInteraction/FocusOutline");
                }
                return cachedShader && cachedShader.isSupported;
            }
        }

        private void OnEnable() => RefreshHighlight();
        private void LateUpdate()
        {
            RefreshHighlight(false);
            RenderedPartCount = 0;
            if (!CanHighlight()) return;
            if (!material)
                material = new Material(cachedShader) { name = "EasyInteraction Focus Outline (Instance)", hideFlags = HideFlags.HideAndDontSave };
            material.SetColor(colorId, outlineColor);
            material.SetFloat(widthId, width);
            RenderedPartCount = geometry.Draw(material, ResolvedCamera);
        }
        private void OnDisable()
        {
            Unsubscribe();
            ReleaseMaterial();
        }
        private void OnDestroy()
        {
            Unsubscribe();
            ReleaseMaterial();
        }
        private void OnValidate() => Width = width;
        private void OnTargetChanged(InteractionTarget _) => RefreshHighlight(false);

        /// <summary>Refresh focus and resolve a newly added or changed Camera directly on Actor.View.</summary>
        public void RefreshHighlight() => RefreshHighlight(true);

        private void RefreshHighlight(bool refreshCamera)
        {
            if (!isActiveAndEnabled) return;
            if (refreshCamera)
            {
                cameraSearched = false;
                if (!cachedShader) shaderResolved = false;
            }
            var currentActor = Actor;
            if (subscribedActor != currentActor)
            {
                Unsubscribe();
                subscribedActor = currentActor;
                if (subscribedActor) subscribedActor.TargetChanged += OnTargetChanged;
            }

            var target = currentActor && currentActor.isActiveAndEnabled ? currentActor.CurrentTarget : null;
            if (!target || !target.isActiveAndEnabled) target = null;
            if (observedTarget != target || (!target && !ReferenceEquals(observedTarget, null)))
            {
                observedTarget = target;
                geometry = null;
                RenderedPartCount = 0;
            }
            if (geometry != null && geometry.IsDisposed) geometry = null;
            if (target && target.HighlightWhenFocused && !target.IsHeldForHighlight && HasRenderCamera && IsShaderAvailable)
                geometry = target.GetFocusOutlineGeometry();
        }

        private Camera ResolveCamera()
        {
            if (cameraOverride) return cameraOverride;
            var currentActor = Actor;
            var view = currentActor ? currentActor.View : null;
            if (!view) view = null;
            if (!cameraSearched || !ReferenceEquals(cachedView, view))
            {
                cameraSearched = true;
                cachedView = view;
                viewCamera = null;
                if (view) view.TryGetComponent(out viewCamera);
            }
            return viewCamera ? viewCamera : null;
        }

        private bool CanHighlight()
        {
            if (!isActiveAndEnabled || !subscribedActor || !subscribedActor.isActiveAndEnabled || !observedTarget ||
                !observedTarget.isActiveAndEnabled || subscribedActor.CurrentTarget != observedTarget ||
                !observedTarget.HighlightWhenFocused || observedTarget.IsHeldForHighlight || geometry == null || !IsShaderAvailable) return false;
            return geometry.CanRender(ResolvedCamera);
        }

        private void Unsubscribe()
        {
            if (subscribedActor) subscribedActor.TargetChanged -= OnTargetChanged;
            subscribedActor = null;
            observedTarget = null;
            geometry = null;
            cachedView = null;
            viewCamera = null;
            cameraSearched = false;
            RenderedPartCount = 0;
        }

        private void ReleaseMaterial()
        {
            if (!material) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
            material = null;
        }
    }
}
