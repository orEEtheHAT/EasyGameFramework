using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace EasySettings
{
    public sealed class SettingsCapabilities
    {
        public readonly bool Display, VSync, Msaa, Shadows, Camera, PostProcessing, Volume;
        public readonly int MaxMsaa;
        public readonly IReadOnlyList<DisplaySettings> Resolutions;
        public readonly IReadOnlyList<FullScreenMode> Modes;
        public readonly string DisplayReason, MsaaReason, CameraReason, PostProcessingReason, VolumeReason;
        public SettingsCapabilities(bool display, bool vsync, bool msaa, bool shadows, bool camera, bool postProcessing, bool volume,
            DisplaySettings[] resolutions, FullScreenMode[] modes, int maxMsaa = 8,
            string displayReason = "", string msaaReason = "", string cameraReason = "", string postProcessingReason = "", string volumeReason = "")
        {
            Display = display; VSync = vsync; Msaa = msaa; Shadows = shadows; Camera = camera; PostProcessing = postProcessing; Volume = volume;
            MaxMsaa = maxMsaa; Resolutions = Array.AsReadOnly((DisplaySettings[])(resolutions ?? Array.Empty<DisplaySettings>()).Clone());
            Modes = Array.AsReadOnly((FullScreenMode[])(modes ?? Array.Empty<FullScreenMode>()).Clone());
            DisplayReason = displayReason; MsaaReason = msaaReason; CameraReason = cameraReason; PostProcessingReason = postProcessingReason; VolumeReason = volumeReason;
        }
    }

    public sealed class SettingsResult
    {
        public bool Success { get; }
        public SettingsState State { get; }
        public IReadOnlyList<string> Messages { get; }
        internal SettingsResult(bool success, SettingsState state, params string[] messages)
        { Success = success; State = state; Messages = Array.AsReadOnly(messages); }
        public static SettingsResult Failure(string reason) => new SettingsResult(false, default, reason);
    }

    public static class SettingsValidation
    {
        internal static float FiniteClamp(float value, float min, float max, float fallback) =>
            !EasyCore.Numeric.IsFinite(value) ? Mathf.Clamp(fallback, min, max) : Mathf.Clamp(value, min, max);

        /// <summary>Normalizes a copy, without writing assets or calling a backend.</summary>
        public static SettingsResult Validate(SettingsState state, SettingsState current, SettingsLimits limits, SettingsCapabilities caps)
        {
            if (caps == null) throw new ArgumentNullException(nameof(caps));
            var messages = new List<string>();
            var before = state;
            limits = limits.Sanitize();
            state.RenderScale = FiniteClamp(state.RenderScale, limits.MinRenderScale, limits.MaxRenderScale, current.RenderScale);
            state.ShadowDistance = FiniteClamp(state.ShadowDistance, 0, limits.MaxShadowDistance, current.ShadowDistance);
            state.FieldOfView = FiniteClamp(state.FieldOfView, limits.MinFieldOfView, limits.MaxFieldOfView, current.FieldOfView);
            state.PostExposure = FiniteClamp(state.PostExposure, -5, 5, current.PostExposure);
            state.BloomIntensity = FiniteClamp(state.BloomIntensity, 0, 10, current.BloomIntensity);
            state.VSyncCount = Mathf.Clamp(state.VSyncCount, 0, 4);
            state.FrameRateLimit = state.FrameRateLimit < 1 ? -1 : Mathf.Clamp(state.FrameRateLimit, 1, limits.MaxFrameRate);
            if (!caps.VSync && state.VSyncCount != current.VSyncCount) { state.VSyncCount = current.VSyncCount; messages.Add("VSync is controlled by this platform."); }
            if (caps.VSync && state.VSyncCount > 0 && state.FrameRateLimit != -1) { state.FrameRateLimit = -1; messages.Add("VSync takes priority; FPS limit changed to Unlimited."); }
            state.MsaaSamples = state.MsaaSamples >= 8 ? 8 : state.MsaaSamples >= 4 ? 4 : state.MsaaSamples >= 2 ? 2 : 1;
            if (!caps.Msaa) { if (state.MsaaSamples != current.MsaaSamples) messages.Add(caps.MsaaReason); state.MsaaSamples = current.MsaaSamples; }
            else while (state.MsaaSamples > caps.MaxMsaa && state.MsaaSamples > 1) state.MsaaSamples /= 2;
            if (!caps.Shadows) { if (state.ShadowDistance != current.ShadowDistance) messages.Add("Realtime shadows are disabled in the source URP asset."); state.ShadowDistance = current.ShadowDistance; }
            if (!caps.Camera) { if (state.FieldOfView != current.FieldOfView) messages.Add(caps.CameraReason); state.FieldOfView = current.FieldOfView; }
            if (!caps.PostProcessing) {
                if (state.PostProcessing != current.PostProcessing || state.CameraAntialiasing != current.CameraAntialiasing) messages.Add(caps.PostProcessingReason);
                state.PostProcessing = current.PostProcessing; state.CameraAntialiasing = current.CameraAntialiasing;
            } else {
                if (state.CameraAntialiasing != AntialiasingMode.None && state.CameraAntialiasing != AntialiasingMode.FastApproximateAntialiasing &&
                    state.CameraAntialiasing != AntialiasingMode.SubpixelMorphologicalAntiAliasing) {
                    state.CameraAntialiasing = AntialiasingMode.None; messages.Add("Only None, FXAA and SMAA are selectable. TAA needs a separately configured camera pipeline.");
                }
                if (!state.PostProcessing) state.CameraAntialiasing = AntialiasingMode.None;
            }
            if (!caps.Volume) {
                if (state.PostExposure != current.PostExposure || state.BloomIntensity != current.BloomIntensity) messages.Add(caps.VolumeReason);
                state.PostExposure = current.PostExposure; state.BloomIntensity = current.BloomIntensity;
            }
            if (!state.Display.Equals(current.Display)) {
                if (!caps.Display) { state.Display = current.Display; messages.Add(caps.DisplayReason); }
                else {
                    bool mode = false, resolution = false;
                    foreach (var m in caps.Modes) if (m == state.Display.Mode) mode = true;
                    if (!mode) return SettingsResult.Failure("This window mode is unavailable on the current platform.");
                    if (state.Display.Width <= 0 || state.Display.Height <= 0 || state.Display.RefreshDenominator == 0 || state.Display.RefreshNumerator == 0)
                        return SettingsResult.Failure("Choose a valid resolution and refresh rate from Capabilities.Resolutions.");
                    foreach (var r in caps.Resolutions)
                        if (r.Width == state.Display.Width && r.Height == state.Display.Height &&
                            (state.Display.Mode != FullScreenMode.ExclusiveFullScreen || Math.Abs(r.RefreshHz - state.Display.RefreshHz) < .001)) resolution = true;
                    if (state.Display.Width == current.Display.Width && state.Display.Height == current.Display.Height && state.Display.Mode != FullScreenMode.ExclusiveFullScreen) resolution = true;
                    if (!resolution) return SettingsResult.Failure("The selected resolution/refresh rate is no longer advertised by this display.");
                    if (state.Display.Mode != FullScreenMode.ExclusiveFullScreen) {
                        state.Display.RefreshNumerator = current.Display.RefreshNumerator; state.Display.RefreshDenominator = current.Display.RefreshDenominator;
                        messages.Add("Windowed and borderless modes use the desktop refresh rate; the monitor refresh rate will not be changed.");
                    }
                }
            }
            if (!state.Equals(before) && messages.Count == 0) messages.Add("Values were clamped to the configured supported range.");
            return new SettingsResult(true, state, messages.ToArray());
        }
    }
}
