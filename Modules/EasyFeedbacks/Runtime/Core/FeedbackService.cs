using System.Diagnostics;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Module-neutral entry point for playing a feedback preset.
/// </summary>
public static class FeedbackService
{
    private static int pauseDepth;

    /// <summary>
    /// True while one or more paired PauseAll calls are active.
    /// </summary>
    public static bool AreAllPaused => pauseDepth > 0;

    /// <summary>
    /// Pauses all output owned by Easy Feedbacks. Calls may be nested and each
    /// PauseAll call must be paired with a ResumeAll call.
    /// </summary>
    public static void PauseAll()
    {
        if (pauseDepth == int.MaxValue)
        {
            Warn("FeedbackService PauseAll nesting reached its maximum depth.");
            return;
        }

        pauseDepth++;
        if (pauseDepth == 1)
        {
            SetManagersSystemPaused(true);
        }
    }

    /// <summary>
    /// Releases one global pause level and resumes managed output after the
    /// final matching PauseAll call has been released.
    /// </summary>
    public static void ResumeAll()
    {
        if (pauseDepth <= 0)
        {
            pauseDepth = 0;
            Warn("FeedbackService.ResumeAll was called without a matching PauseAll.");
            return;
        }

        pauseDepth--;
        if (pauseDepth == 0)
        {
            SetManagersSystemPaused(false);
        }
    }

    /// <summary>
    /// Stops every output currently owned by Easy Feedbacks. The normal mode
    /// respects each layer's configured fade or stop behaviour; immediate mode
    /// releases all output without waiting and is suitable for scene teardown.
    /// This does not change the global pause nesting depth or block future Play calls.
    /// </summary>
    public static void StopAll(bool immediate = false)
    {
        var audioManager = AudioManager.Instance;
        if (audioManager != null)
        {
            audioManager.StopAll(immediate ? 0f : -1f);
        }
        else
        {
            // Normally AudioManager.StopAll owns this notification. Keep the
            // music controller stopped even when its playback manager is absent.
            var musicManager = MusicManager.Instance;
            if (musicManager != null)
            {
                var fadeOutDuration = !immediate && musicManager.CurrentPlaylist != null
                    ? musicManager.CurrentPlaylist.TransitionDuration
                    : 0f;
                musicManager.StopMusic(fadeOutDuration);
            }
        }

        VisualFeedbackManager.Instance?.StopAll(immediate);
        CameraShakeManager.Instance?.StopAll(immediate);
        GamepadVibrationManager.Instance?.StopAll(immediate);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        pauseDepth = 0;
        SetManagersSystemPaused(false);
    }

    public static void Prewarm(FeedbackPreset preset)
    {
        FeedbackHandleRegistry.Prewarm();

        if (preset == null)
        {
            return;
        }

        var audioEntries = preset.AudioEntries;
        for (var i = 0; i < audioEntries.Count; i++)
        {
            var entry = audioEntries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            var clips = entry.Clips;
            for (var clipIndex = 0; clipIndex < clips.Count; clipIndex++)
            {
                var clip = clips[clipIndex];
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
            }
        }

        var visualManager = VisualFeedbackManager.Instance;
        if (visualManager != null)
        {
            var entries = preset.VisualEntries;
            for (var i = 0; i < entries.Count; i++)
            {
                visualManager.Prewarm(entries[i]);
            }
        }

        var cameraShakeManager = CameraShakeManager.Instance;
        if (cameraShakeManager != null)
        {
            var cameraShakeEntries = preset.CameraShakeEntries;
            var enabledEntryCount = 0;
            for (var i = 0; i < cameraShakeEntries.Count; i++)
            {
                var entry = cameraShakeEntries[i];
                if (entry != null && entry.IsEnabled && entry.Profile != null)
                {
                    enabledEntryCount++;
                }
            }

            cameraShakeManager.Prewarm(enabledEntryCount);
        }

        var gamepadVibrationManager = GamepadVibrationManager.Instance;
        if (gamepadVibrationManager != null)
        {
            var gamepadEntries = preset.GamepadVibrationEntries;
            var enabledEntryCount = 0;
            for (var i = 0; i < gamepadEntries.Count; i++)
            {
                var entry = gamepadEntries[i];
                if (entry != null
                    && entry.IsEnabled
                    && entry.Profile != null
                    && entry.HasOutput
                    && (entry.Loop || entry.Duration > 0f))
                {
                    enabledEntryCount++;
                }
            }

            gamepadVibrationManager.Prewarm(enabledEntryCount);
        }
    }

    public static FeedbackHandle Play(FeedbackPreset preset, FeedbackContext context)
    {
        if (preset == null)
        {
            Warn("FeedbackService cannot play a null FeedbackPreset.");
            return default;
        }

        var feedbackHandle = FeedbackHandleRegistry.Rent();

        var audioEntries = preset.AudioEntries;
        AudioManager audioManager = null;
        var audioManagerUnavailable = false;
        for (var i = 0; i < audioEntries.Count; i++)
        {
            var entry = audioEntries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            if (!entry.HasAnyClip)
            {
                Warn($"FeedbackPreset '{preset.name}' has an audio entry without a valid AudioClip at index {i}.");
                continue;
            }

            if (audioManager == null && !audioManagerUnavailable)
            {
                audioManager = AudioManager.Instance;
                if (audioManager == null || !audioManager.isActiveAndEnabled)
                {
                    Warn("FeedbackService needs an active AudioManager to play audio entries.");
                    audioManagerUnavailable = true;
                }
            }

            if (audioManagerUnavailable)
            {
                continue;
            }

            var handle = audioManager.Play(entry, context);
            if (handle.IsValid)
            {
                FeedbackHandleRegistry.Add(feedbackHandle, handle);
            }
        }

        var visualEntries = preset.VisualEntries;
        VisualFeedbackManager visualManager = null;
        var visualManagerUnavailable = false;
        for (var i = 0; i < visualEntries.Count; i++)
        {
            var entry = visualEntries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            if (entry.Resource == null)
            {
                Warn($"FeedbackPreset '{preset.name}' has a visual entry without a resource at index {i}.");
                continue;
            }

            if (visualManager == null && !visualManagerUnavailable)
            {
                visualManager = VisualFeedbackManager.Instance;
                if (visualManager == null || !visualManager.isActiveAndEnabled)
                {
                    Warn("FeedbackService needs an active VisualFeedbackManager to play visual entries.");
                    visualManagerUnavailable = true;
                }
            }

            if (visualManagerUnavailable)
            {
                continue;
            }

            var handle = visualManager.Play(entry, context);
            if (handle.IsValid)
            {
                FeedbackHandleRegistry.Add(feedbackHandle, handle);
            }
        }

        var cameraShakeEntries = preset.CameraShakeEntries;
        CameraShakeManager cameraShakeManager = null;
        var cameraShakeManagerUnavailable = false;
        for (var i = 0; i < cameraShakeEntries.Count; i++)
        {
            var entry = cameraShakeEntries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            if (entry.Profile == null)
            {
                Warn($"FeedbackPreset '{preset.name}' has a camera shake entry without a profile at index {i}.");
                continue;
            }

            if (cameraShakeManager == null && !cameraShakeManagerUnavailable)
            {
                cameraShakeManager = CameraShakeManager.Instance;
                if (cameraShakeManager == null || !cameraShakeManager.isActiveAndEnabled)
                {
                    Warn("FeedbackService needs an active CameraShakeManager to play camera shake entries.");
                    cameraShakeManagerUnavailable = true;
                }
            }

            if (cameraShakeManagerUnavailable)
            {
                continue;
            }

            var handle = cameraShakeManager.Play(entry, context);
            if (handle.IsValid)
            {
                FeedbackHandleRegistry.Add(feedbackHandle, handle);
            }
        }

        var gamepadVibrationEntries = preset.GamepadVibrationEntries;
        GamepadVibrationManager gamepadVibrationManager = null;
        var gamepadVibrationManagerUnavailable = false;
        for (var i = 0; i < gamepadVibrationEntries.Count; i++)
        {
            var entry = gamepadVibrationEntries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            if (entry.Profile == null)
            {
                Warn($"FeedbackPreset '{preset.name}' has a gamepad vibration entry without a profile at index {i}.");
                continue;
            }

            if (!entry.HasOutput)
            {
                continue;
            }

            if (!entry.Loop && entry.Duration <= 0f)
            {
                Warn($"FeedbackPreset '{preset.name}' has a one-shot gamepad vibration entry with zero duration at index {i}.");
                continue;
            }

            if (gamepadVibrationManager == null && !gamepadVibrationManagerUnavailable)
            {
                gamepadVibrationManager = GamepadVibrationManager.Instance;
                if (gamepadVibrationManager == null || !gamepadVibrationManager.isActiveAndEnabled)
                {
                    Warn("FeedbackService needs an active GamepadVibrationManager to play gamepad vibration entries.");
                    gamepadVibrationManagerUnavailable = true;
                }
            }

            if (gamepadVibrationManagerUnavailable)
            {
                continue;
            }

            var handle = gamepadVibrationManager.Play(entry, context);
            if (handle.IsValid)
            {
                FeedbackHandleRegistry.Add(feedbackHandle, handle);
            }
        }

        if (FeedbackHandleRegistry.IsEmpty(feedbackHandle))
        {
            FeedbackHandleRegistry.Discard(feedbackHandle);
            return default;
        }

        return feedbackHandle;
    }

    public static FeedbackHandle Play(FeedbackPreset preset, Transform source, float intensity = 1f)
    {
        return Play(preset, FeedbackContext.From(source, null, intensity));
    }

    private static void SetManagersSystemPaused(bool paused)
    {
        AudioManager.Instance?.SetSystemPaused(paused);
        VisualFeedbackManager.Instance?.SetSystemPaused(paused);
        CameraShakeManager.Instance?.SetSystemPaused(paused);
        GamepadVibrationManager.Instance?.SetSystemPaused(paused);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }
}
}
