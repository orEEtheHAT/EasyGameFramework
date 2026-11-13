using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Cinemachine;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace EasySettings
{
    /// <summary>Lazily owns a runtime URP asset copy and explicitly bound camera/volume overrides.
    /// Only one backend can own the process-wide graphics settings at a time.</summary>
    public sealed class UrpSettingsBackend : ISettingsBackend
    {
        static UrpSettingsBackend owner;
        static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("EasySettings.ApplyURP");
        readonly int thread = Thread.CurrentThread.ManagedThreadId;
        readonly bool allowPost, allowVolume;
        readonly int qualityIndex, originalVSync, originalFps;
        readonly RenderPipelineAsset originalQuality;
        readonly UniversalRenderPipelineAsset source;
        readonly DisplaySettings originalDisplay;
        UniversalRenderPipelineAsset runtime;
        Camera camera;
        UniversalAdditionalCameraData cameraData;
        Volume volume;
        VolumeProfile originalProfile, runtimeProfile;
        ColorAdjustments exposure;
        Bloom bloom;
        float originalFov, lastCameraFov;
        bool originalPost;
        AntialiasingMode originalAA;
        bool disposed, displayTouched, wroteCameraFov;
        ISettingsFieldOfViewProvider fovProvider;
        ISettingsFieldOfViewOverride fovOverride;
        Object fovOwner;
        float lastProviderFov = 60;
        int fovBinding;

        /// <summary>The owned pipeline copy, or null until a pipeline setting is changed.
        /// Reading settings or changing only camera, Volume, frame pacing or display does not create a copy.</summary>
        public UniversalRenderPipelineAsset RuntimePipeline => runtime;
        UniversalRenderPipelineAsset Pipeline => runtime ? runtime : source;
        public UrpSettingsBackend(Camera camera = null, Volume volume = null, bool allowPostProcessingChanges = false, bool allowVolumeChanges = false)
        {
            if (owner != null) throw new InvalidOperationException("Only one enabled Settings Controller may own the global URP settings. Reuse its Session.");
            source = (QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (!source) throw new InvalidOperationException("EasySettings requires an active URP pipeline asset. Assign URP in Graphics and Quality settings.");
            qualityIndex = QualitySettings.GetQualityLevel(); originalQuality = QualitySettings.renderPipeline;
            originalVSync = QualitySettings.vSyncCount; originalFps = Application.targetFrameRate; originalDisplay = ReadDisplay();
            allowPost = allowPostProcessingChanges; allowVolume = allowVolumeChanges;
            owner = this;
            try {
                BindTargets(camera, volume);
            } catch { Dispose(); throw; }
        }

        public void BindTargets(Camera targetCamera, Volume targetVolume)
        {
            Check(); ReleaseTargets();
            camera = targetCamera; volume = targetVolume;
            if (camera) {
                originalFov = camera.fieldOfView;
                camera.TryGetComponent(out cameraData);
                if (cameraData) { originalAA = cameraData.antialiasing; originalPost = cameraData.renderPostProcessing; }
            }
            if (volume && allowVolume) {
                originalProfile = volume.HasInstantiatedProfile() ? volume.profile : null;
                var source = originalProfile ? originalProfile : volume.sharedProfile;
                runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>(); runtimeProfile.name = "EasySettings runtime volume"; runtimeProfile.hideFlags = HideFlags.HideAndDontSave;
                if (source) foreach (var component in source.components) {
                    var copy = Object.Instantiate(component); copy.hideFlags = HideFlags.HideAndDontSave; runtimeProfile.components.Add(copy);
                }
                // No automatic additions: missing overrides must be authored and included in the build.
                runtimeProfile.TryGet(out exposure); runtimeProfile.TryGet(out bloom);
                volume.profile = runtimeProfile;
            }
        }

        /// <summary>Explicitly binds an optional base-FOV owner. Null selects guarded direct Camera FOV.
        /// Does not acquire an override until Apply. Rebinding releases only the previous handle.</summary>
        public void BindFieldOfViewProvider(ISettingsFieldOfViewProvider provider, Object requestOwner)
        {
            Check();
            if (provider != null && !requestOwner) throw new ArgumentNullException(nameof(requestOwner));
            if (ReferenceEquals(fovProvider, provider) && fovOwner == requestOwner) return;
            if (fovProvider == null) ReleaseCameraFov();
            var previous = fovOverride; fovOverride = null;
            fovProvider = provider; fovOwner = requestOwner; fovBinding++;
            previous?.Dispose();
        }
        bool ReadProvider(out float value, out string reason)
        {
            value = lastProviderFov;
            if (fovProvider is Object unityObject && !unityObject) { reason = "The FOV provider was destroyed. Bind a live EasyMovement FOV component."; return false; }
            if (fovProvider is Behaviour behaviour && !behaviour.isActiveAndEnabled) { reason = "Enable the assigned FOV provider."; return false; }
            if (!fovProvider.TryRead(out var current, out reason)) return false;
            if (!EasyCore.Numeric.IsFinite(current) || current < 1 || current > 179) { reason = "The FOV owner must return a finite base FOV between 1 and 179 degrees."; return false; }
            value = lastProviderFov = current; return true;
        }
        bool FovAvailable(out string reason)
        {
            if (fovProvider != null) return ReadProvider(out _, out reason);
            return CanControlFieldOfView(camera, out reason);
        }
        float ReadFieldOfView()
        {
            if (fovProvider == null) return camera ? camera.fieldOfView : 60;
            ReadProvider(out var value, out _); return value;
        }

        public SettingsCapabilities Describe()
        {
            Check();
            bool desktop = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.OSXPlayer;
            bool display = desktop && !Application.isEditor;
            var modes = new List<FullScreenMode> { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow };
            if (Application.platform == RuntimePlatform.WindowsPlayer) modes.Add(FullScreenMode.ExclusiveFullScreen);
            if (Application.platform == RuntimePlatform.OSXPlayer) modes.Add(FullScreenMode.MaximizedWindow);
            var resolutions = new List<DisplaySettings>();
            foreach (var resolution in Screen.resolutions) resolutions.Add(new DisplaySettings { Width = resolution.width, Height = resolution.height,
                Mode = Screen.fullScreenMode, RefreshNumerator = resolution.refreshRateRatio.numerator, RefreshDenominator = resolution.refreshRateRatio.denominator });
            var current = ReadDisplay();
            if (!resolutions.Exists(r => r.Width == current.Width && r.Height == current.Height)) resolutions.Add(current);
            if (!resolutions.Exists(r => r.Width == originalDisplay.Width && r.Height == originalDisplay.Height)) resolutions.Add(originalDisplay);
            var pipeline = Pipeline;
            bool forward = pipeline.rendererDataList.Length > 0, postData = forward;
            foreach (var data in pipeline.rendererDataList) {
                if (!(data is UniversalRendererData renderer)) { forward = false; postData = false; continue; }
                if (renderer.renderingMode != RenderingMode.Forward && renderer.renderingMode != RenderingMode.ForwardPlus) forward = false;
                if (!renderer.postProcessData) postData = false;
            }
            var descriptor = new RenderTextureDescriptor(64, 64, pipeline.supportsHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default, 24) { msaaSamples = 8 };
            int maxMsaa = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            bool canCamera = FovAvailable(out string cameraReason);
            Check(); // Provider reads may synchronously invoke owner callbacks.
            bool post = allowPost && cameraData && camera && camera.isActiveAndEnabled && postData && cameraData.renderType == CameraRenderType.Base && cameraData.antialiasing != AntialiasingMode.TemporalAntiAliasing;
            bool msaa = forward && maxMsaa > 1 && (!camera || (camera.allowMSAA && (!cameraData || cameraData.antialiasing != AntialiasingMode.TemporalAntiAliasing)));
            bool canVolume = allowVolume && post && volume && volume.isActiveAndEnabled && volume.isGlobal && Mathf.Approximately(volume.weight, 1) &&
                (cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer)) != 0 && exposure && bloom && exposure.active && bloom.active;
            return new SettingsCapabilities(display, desktop || Application.isEditor, msaa, pipeline.supportsMainLightShadows || pipeline.supportsAdditionalLightShadows,
                canCamera, post, canVolume, resolutions.ToArray(), modes.ToArray(), maxMsaa,
                "Screen changes are available in desktop Players only. Editor and other platforms keep their existing screen configuration.",
                "MSAA requires Forward/Forward+, camera Allow MSAA, hardware support and no TAA. All renderer entries must be compatible.",
                cameraReason,
                "Enable verified post-processing variants in Defaults, assign a URP base camera and post-process renderer data; TAA cameras are preserved.",
                "Assign a dedicated enabled global Volume (weight 1) on the camera's volume mask, with active Color Adjustments and Bloom; enable verified Volume changes in Defaults.");
        }

        public SettingsState Read()
        {
            Check();
            float fieldOfView = ReadFieldOfView(); Check();
            var pipeline = Pipeline;
            return new SettingsState { Display = ReadDisplay(), VSyncCount = QualitySettings.vSyncCount, FrameRateLimit = Application.targetFrameRate,
                RenderScale = pipeline.renderScale, MsaaSamples = pipeline.msaaSampleCount, ShadowDistance = pipeline.shadowDistance,
                FieldOfView = fieldOfView, CameraAntialiasing = cameraData ? cameraData.antialiasing : AntialiasingMode.None,
                PostProcessing = cameraData && cameraData.renderPostProcessing, PostExposure = exposure ? exposure.postExposure.value : 0,
                BloomIntensity = bloom ? bloom.intensity.value : 0 };
        }
        public static DisplaySettings ReadDisplay()
        {
            var refresh = Screen.currentResolution.refreshRateRatio;
            return new DisplaySettings { Width = Screen.width, Height = Screen.height, Mode = Screen.fullScreenMode,
                RefreshNumerator = refresh.numerator == 0 ? 60u : refresh.numerator, RefreshDenominator = refresh.denominator == 0 ? 1u : refresh.denominator };
        }
        /// <summary>Direct Camera FOV is unavailable while an active Brain owns its lens.
        /// The active-Brain list also covers a Brain on another GameObject using ControlledObject.</summary>
        public static bool CanControlFieldOfView(Camera target, out string reason)
        {
            if (!target || !target.isActiveAndEnabled || target.orthographic || target.usePhysicalProperties) {
                reason = "Assign an enabled perspective camera without Physical Camera; bind replacement cameras explicitly.";
                return false;
            }
            if (HasCinemachineOwner(target)) {
                reason = "Cinemachine controls this camera's lens. Bind an EasyMovement FOV provider to change the player's base FOV; direct output-camera FOV is unavailable.";
                return false;
            }
            reason = ""; return true;
        }
        static bool HasCinemachineOwner(Camera target)
        {
            for (int i = 0; i < CinemachineBrain.ActiveBrainCount; i++) {
                var brain = CinemachineBrain.GetActiveBrain(i);
                if (brain && brain.isActiveAndEnabled && (brain.OutputCamera == target || brain.ControlledObject == target.gameObject)) return true;
            }
            return false;
        }
        public void Apply(SettingsState state, bool applyDisplay)
        {
            Check();
            using var marker = ApplyMarker.Auto();
            var expected = runtime ? runtime : originalQuality;
            if (QualitySettings.GetQualityLevel() != qualityIndex || QualitySettings.renderPipeline != expected ||
                (!runtime && !originalQuality && GraphicsSettings.defaultRenderPipeline != source))
                throw new InvalidOperationException("An external system changed the Unity quality/pipeline override. Disable and re-enable Settings Controller after that change.");
            var pipeline = Pipeline;
            if (pipeline.renderScale != state.RenderScale || pipeline.msaaSampleCount != state.MsaaSamples || pipeline.shadowDistance != state.ShadowDistance) {
                if (!runtime) {
                    runtime = Object.Instantiate(source); runtime.name = source.name + " (EasySettings runtime)"; runtime.hideFlags = HideFlags.HideAndDontSave;
                }
                runtime.renderScale = state.RenderScale; runtime.msaaSampleCount = state.MsaaSamples; runtime.shadowDistance = state.ShadowDistance;
                QualitySettings.renderPipeline = runtime;
            }
            QualitySettings.vSyncCount = state.VSyncCount; Application.targetFrameRate = state.FrameRateLimit;
            // Session validation preserves values for unsupported targets; no source objects are changed.
            ApplyFieldOfView(state.FieldOfView);
            if (disposed) return;
            if (allowPost && cameraData) { cameraData.antialiasing = state.CameraAntialiasing; cameraData.renderPostProcessing = state.PostProcessing; }
            if (allowVolume && exposure && bloom) {
                if (exposure.postExposure.value != state.PostExposure) exposure.postExposure.Override(state.PostExposure);
                if (bloom.intensity.value != state.BloomIntensity) bloom.intensity.Override(state.BloomIntensity);
            }
            if (applyDisplay && !Application.isEditor) {
                var platform = Application.platform;
                if (platform != RuntimePlatform.WindowsPlayer && platform != RuntimePlatform.LinuxPlayer && platform != RuntimePlatform.OSXPlayer)
                    throw new InvalidOperationException("Display changes require a desktop Player.");
                displayTouched = true; SetDisplay(state.Display);
            }
        }
        void ApplyFieldOfView(float value)
        {
            if (fovProvider == null) {
                if (CanControlFieldOfView(camera, out _) && camera.fieldOfView != value) {
                    camera.fieldOfView = value; lastCameraFov = value; wroteCameraFov = true;
                }
                return;
            }
            int binding = fovBinding;
            bool available = ReadProvider(out var current, out var reason);
            if (disposed || binding != fovBinding) return;
            if (!available) {
                if (value != current) throw new InvalidOperationException(reason);
                return;
            }
            var handle = fovOverride;
            bool active = handle != null && handle.IsActive;
            if (disposed || binding != fovBinding) return;
            if (active) {
                bool accepted = handle.TrySet(value);
                if (!disposed && binding == fovBinding && !accepted) throw new InvalidOperationException("The FOV owner rejected the base override. Refresh the target and try again.");
                return;
            }
            fovOverride = null; handle?.Dispose();
            if (disposed || binding != fovBinding) return;
            var created = fovProvider.Acquire(fovOwner, value);
            if (disposed || binding != fovBinding) { created?.Dispose(); return; }
            fovOverride = created;
            if (created == null || !created.IsActive) throw new InvalidOperationException("The FOV owner is unavailable. Enable the owner and settings controller before applying.");
        }
        static void SetDisplay(DisplaySettings display) => Screen.SetResolution(display.Width, display.Height, display.Mode,
            display.Mode == FullScreenMode.ExclusiveFullScreen ? new RefreshRate { numerator = display.RefreshNumerator, denominator = display.RefreshDenominator } : default);
        void ReleaseCameraFov()
        {
            if (wroteCameraFov && camera && camera.fieldOfView == lastCameraFov && !camera.orthographic && !camera.usePhysicalProperties && !HasCinemachineOwner(camera)) camera.fieldOfView = originalFov;
            wroteCameraFov = false;
        }
        void ReleaseTargets()
        {
            ReleaseCameraFov();
            if (cameraData) { cameraData.antialiasing = originalAA; cameraData.renderPostProcessing = originalPost; }
            if (volume && runtimeProfile && volume.HasInstantiatedProfile() && volume.profile == runtimeProfile) volume.profile = originalProfile;
            if (runtimeProfile) { foreach (var component in runtimeProfile.components) Destroy(component); Destroy(runtimeProfile); }
            runtimeProfile = null; originalProfile = null; exposure = null; bloom = null; camera = null; cameraData = null; volume = null;
        }
        static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
        void Check()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) throw new InvalidOperationException("URP settings must be accessed on their creating Unity main thread.");
            if (disposed) throw new ObjectDisposedException(nameof(UrpSettingsBackend));
        }
        public void Dispose()
        {
            if (disposed) return;
            Check();
            disposed = true;
            var ownedFov = fovOverride; fovOverride = null; fovProvider = null; fovOwner = null; fovBinding++;
            try {
                ReleaseTargets();
            } finally {
              try {
                // The override belongs to its quality tier. Restore it even if another system switched tiers.
                int currentQuality = QualitySettings.GetQualityLevel();
                if (runtime && QualitySettings.GetRenderPipelineAssetAt(qualityIndex) == runtime) {
                    if (currentQuality != qualityIndex) QualitySettings.SetQualityLevel(qualityIndex, false);
                    QualitySettings.renderPipeline = originalQuality;
                    if (currentQuality != qualityIndex) QualitySettings.SetQualityLevel(currentQuality, true);
                }
                QualitySettings.vSyncCount = originalVSync; Application.targetFrameRate = originalFps;
                if (displayTouched && !Application.isEditor) SetDisplay(originalDisplay);
                Destroy(runtime); runtime = null;
              } finally {
                // Release native/global ownership before an external FOV handle can notify user code.
                if (ReferenceEquals(owner, this)) owner = null;
                ownedFov?.Dispose();
              }
            }
        }
    }
}
