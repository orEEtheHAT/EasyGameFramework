using System;
using UnityEngine;

namespace EasySettings
{
    [Serializable]
    public sealed class GraphicsProfile
    {
        public string Name = "Balanced";
        public SettingsState State = SettingsState.Balanced;
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/Settings/Defaults", fileName = "GraphicsSettings")]
    public sealed class SettingsDefaults : ScriptableObject
    {
        [Tooltip("Reset uses these values. Screen is preserved; display modes always come from the current device.")]
        public SettingsState Defaults = SettingsState.Balanced;
        public SettingsLimits Limits = SettingsLimits.Default;
        [Range(5, 60), Tooltip("Real seconds before an unconfirmed screen change is reverted, even while paused.")]
        public float ConfirmationSeconds = 15;
        [Tooltip("Enable only after retaining and verifying the post-processing variants in a Player build. Camera must have Universal Additional Camera Data.")]
        public bool AllowPostProcessingChanges;
        [Tooltip("Enable only after retaining and verifying ColorAdjustments and Bloom variants in a Player build. Requires a dedicated global Volume of weight 1, visible to the target camera.")]
        public bool AllowVolumeChanges;
        public GraphicsProfile[] Profiles = {
            new GraphicsProfile { Name = "Low", State = new SettingsState { FrameRateLimit = -1, VSyncCount = 1, RenderScale = .75f, MsaaSamples = 1, ShadowDistance = 25, FieldOfView = 60 } },
            new GraphicsProfile(),
            new GraphicsProfile { Name = "High", State = new SettingsState { FrameRateLimit = -1, VSyncCount = 1, RenderScale = 1, MsaaSamples = 4, ShadowDistance = 100, FieldOfView = 60, PostProcessing = true } }
        };
    }
}
