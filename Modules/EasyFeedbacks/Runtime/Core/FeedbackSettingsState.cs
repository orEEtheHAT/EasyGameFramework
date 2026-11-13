using System;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// A serializable copy of the user's feedback preferences. Values are linear multipliers in 0..1.
/// Persist this value in the application's save/settings system when desired.
/// </summary>
[Serializable]
public struct FeedbackSettingsState
{
    [Range(0f, 1f)] public float MasterVolume;
    [Range(0f, 1f)] public float MusicVolume;
    [Range(0f, 1f)] public float SoundEffectsVolume;
    public bool HapticsEnabled;
    [Range(0f, 1f)] public float HapticsIntensity;

    public static FeedbackSettingsState Default => new FeedbackSettingsState
    {
        MasterVolume = 1f,
        MusicVolume = 1f,
        SoundEffectsVolume = 1f,
        HapticsEnabled = true,
        HapticsIntensity = 1f
    };

    /// <summary>Clamps finite values; non-finite values use the default multiplier of one.</summary>
    public FeedbackSettingsState Normalized()
    {
        return new FeedbackSettingsState
        {
            MasterVolume = Normalize(MasterVolume),
            MusicVolume = Normalize(MusicVolume),
            SoundEffectsVolume = Normalize(SoundEffectsVolume),
            HapticsEnabled = HapticsEnabled,
            HapticsIntensity = Normalize(HapticsIntensity)
        };
    }

    private static float Normalize(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Clamp01(value);
    }
}
}
