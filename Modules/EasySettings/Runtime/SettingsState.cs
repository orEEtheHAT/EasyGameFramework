using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace EasySettings
{
    [Serializable]
    public struct DisplaySettings : IEquatable<DisplaySettings>
    {
        public int Width, Height;
        public FullScreenMode Mode;
        public uint RefreshNumerator, RefreshDenominator;
        public double RefreshHz => RefreshDenominator == 0 ? 0 : (double)RefreshNumerator / RefreshDenominator;
        public bool Equals(DisplaySettings other) => Width == other.Width && Height == other.Height && Mode == other.Mode &&
            (RefreshDenominator == 0 || other.RefreshDenominator == 0
                ? RefreshNumerator == other.RefreshNumerator && RefreshDenominator == other.RefreshDenominator
                : (ulong)RefreshNumerator * other.RefreshDenominator == (ulong)other.RefreshNumerator * RefreshDenominator);
        public override bool Equals(object obj) => obj is DisplaySettings other && Equals(other);
        // Equivalent refresh ratios must have the same hash.
        public override int GetHashCode() => HashCode.Combine(Width, Height, Mode, RefreshHz);
        public override string ToString() => $"{Width} x {Height} @ {RefreshHz:0.##} Hz ({Mode})";
    }

    /// <summary>A value copy. Editing this DTO never changes Unity or a source asset.</summary>
    [Serializable]
    public struct SettingsState : IEquatable<SettingsState>
    {
        public DisplaySettings Display;
        public int VSyncCount;
        public int FrameRateLimit;
        public float RenderScale;
        public int MsaaSamples;
        public float ShadowDistance;
        public float FieldOfView;
        public AntialiasingMode CameraAntialiasing;
        public bool PostProcessing;
        public float PostExposure;
        public float BloomIntensity;

        public static SettingsState Balanced => new SettingsState {
            FrameRateLimit = -1, VSyncCount = 1, RenderScale = 1, MsaaSamples = 1,
            ShadowDistance = 50, FieldOfView = 60, PostProcessing = true
        };
        public bool Equals(SettingsState s) => Display.Equals(s.Display) && VSyncCount == s.VSyncCount && FrameRateLimit == s.FrameRateLimit &&
            RenderScale.Equals(s.RenderScale) && MsaaSamples == s.MsaaSamples && ShadowDistance.Equals(s.ShadowDistance) &&
            FieldOfView.Equals(s.FieldOfView) && CameraAntialiasing == s.CameraAntialiasing && PostProcessing == s.PostProcessing &&
            PostExposure.Equals(s.PostExposure) && BloomIntensity.Equals(s.BloomIntensity);
        public override bool Equals(object obj) => obj is SettingsState s && Equals(s);
        public override int GetHashCode() => HashCode.Combine(Display, RenderScale, MsaaSamples, ShadowDistance, FieldOfView);
    }

    [Serializable]
    public sealed class SettingsSnapshot
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public SettingsState State;
    }

    [Serializable]
    public struct SettingsLimits
    {
        [Range(.1f, 2)] public float MinRenderScale, MaxRenderScale;
        [Min(0)] public float MaxShadowDistance;
        [Range(1, 179)] public float MinFieldOfView, MaxFieldOfView;
        [Min(1)] public int MaxFrameRate;
        public static SettingsLimits Default => new SettingsLimits {
            MinRenderScale = .5f, MaxRenderScale = 2, MaxShadowDistance = 200, MinFieldOfView = 40, MaxFieldOfView = 100, MaxFrameRate = 360
        };
        internal SettingsLimits Sanitize()
        {
            var s = this;
            s.MinRenderScale = SettingsValidation.FiniteClamp(s.MinRenderScale, .1f, 2, .5f);
            s.MaxRenderScale = SettingsValidation.FiniteClamp(s.MaxRenderScale, s.MinRenderScale, 2, 2);
            s.MaxShadowDistance = SettingsValidation.FiniteClamp(s.MaxShadowDistance, 0, 10000, 200);
            s.MinFieldOfView = SettingsValidation.FiniteClamp(s.MinFieldOfView, 1, 179, 40);
            s.MaxFieldOfView = SettingsValidation.FiniteClamp(s.MaxFieldOfView, s.MinFieldOfView, 179, 100);
            s.MaxFrameRate = Mathf.Clamp(s.MaxFrameRate, 1, 1000);
            return s;
        }
    }
}
