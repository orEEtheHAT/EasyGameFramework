using System.Diagnostics;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Lightweight direct API for camera shake playback through CameraShakeManager.
/// </summary>
public static class CameraShakeService
{
    public static CameraShakeHandle Play(
        CameraShakeProfile profile,
        Vector3 worldPosition,
        Vector3 cameraSpaceKickVector,
        float strength = 1f,
        bool loop = false,
        float noiseDuration = 0.35f,
        float noiseFadeIn = 0f,
        float noiseFadeOut = 0f)
    {
        if (profile == null || strength <= 0f)
        {
            return default;
        }

        var manager = CameraShakeManager.Instance;
        if (manager == null)
        {
            Warn("CameraShakeService needs an active CameraShakeManager.");
            return default;
        }

        return manager.Play(
            profile,
            worldPosition,
            cameraSpaceKickVector,
            strength,
            loop,
            noiseDuration,
            noiseFadeIn,
            noiseFadeOut);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }
}
}
