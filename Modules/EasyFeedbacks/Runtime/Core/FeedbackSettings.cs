using System;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Runtime user preferences for output owned by EasyFeedbacks. Call on Unity's main thread.
/// No file, PlayerPrefs entry, mixer asset or ScriptableObject is modified.
/// </summary>
public static class FeedbackSettings
{
    private static FeedbackSettingsState current = FeedbackSettingsState.Default;

    public static FeedbackSettingsState Current => current;

    /// <summary>Raised after validated preferences have been applied to current output.</summary>
    public static event Action<FeedbackSettingsState> Changed;

    public static FeedbackSettingsState Capture() => current;

    /// <summary>Restores a copied snapshot, using the same validation as Apply.</summary>
    public static void Restore(FeedbackSettingsState state) => Apply(state);

    /// <summary>
    /// Applies preferences to current and future output. Muting does not pause or release playback.
    /// Loops still require Stop; one-shots finish on their normal timeline.
    /// </summary>
    public static void Apply(FeedbackSettingsState state)
    {
        state = state.Normalized();
        if (current.MasterVolume == state.MasterVolume
            && current.MusicVolume == state.MusicVolume
            && current.SoundEffectsVolume == state.SoundEffectsVolume
            && current.HapticsEnabled == state.HapticsEnabled
            && current.HapticsIntensity == state.HapticsIntensity)
        {
            return;
        }

        current = state;
        RefreshOutput();
        Changed?.Invoke(current);
    }

    internal static float AudioMultiplier(FeedbackAudioCategory category)
    {
        return current.MasterVolume * (category == FeedbackAudioCategory.Music
            ? current.MusicVolume
            : current.SoundEffectsVolume);
    }

    internal static float HapticsMultiplier => current.HapticsEnabled ? current.HapticsIntensity : 0f;

    private static void RefreshOutput()
    {
        AudioManager.Instance?.ApplyUserSettings();
        GamepadVibrationManager.Instance?.ApplyUserSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        current = FeedbackSettingsState.Default;
        Changed = null;
        RefreshOutput();
    }
}
}
