using System;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Serializable invocation settings for one reusable camera shake profile.
/// </summary>
[Serializable]
public sealed class CameraShakeFeedbackEntry
{
    [SerializeField, Tooltip("Whether this camera shake layer is played as part of the feedback preset.")]
    private bool isEnabled = true;

    [SerializeField, Tooltip("Reusable Cinemachine impulse profile.")]
    private CameraShakeProfile profile;

    [SerializeField, Min(0f), Tooltip("Per-layer impulse amplitude multiplied by the common feedback Intensity.")]
    private float strengthMultiplier = 1f;

    [SerializeField, Tooltip("Keep the Noise Shake layer active until this feedback handle is stopped. Directional Kick is emitted once when playback starts.")]
    private bool loop;

    [SerializeField, Min(0f), Tooltip("Total duration of a one-shot Noise Shake. Ignored while Loop is enabled.")]
    private float noiseDuration = 0.35f;

    [SerializeField, Min(0f), Tooltip("Seconds used to fade the Noise Shake in from zero for this preset invocation.")]
    private float noiseFadeIn;

    [SerializeField, Min(0f), Tooltip("Seconds used to fade the Noise Shake out. A loop uses this duration after Stop.")]
    private float noiseFadeOut = 0.25f;

    public bool IsEnabled => isEnabled;
    public CameraShakeProfile Profile => profile;
    public float StrengthMultiplier => Mathf.Max(0f, strengthMultiplier);
    public bool Loop => loop;
    public float NoiseDuration => Mathf.Max(0f, noiseDuration);
    public float NoiseFadeIn => Mathf.Max(0f, noiseFadeIn);
    public float NoiseFadeOut => Mathf.Max(0f, noiseFadeOut);
}
}
