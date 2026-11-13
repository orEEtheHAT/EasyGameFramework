using System;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Serializable dual-motor vibration settings for one feedback layer.
/// </summary>
[Serializable]
public sealed class GamepadVibrationFeedbackEntry
{
    [SerializeField, Tooltip("Whether this gamepad vibration layer is played as part of the feedback preset.")]
    private bool isEnabled = true;

    [SerializeField, Tooltip("Reusable profile containing the low-frequency and high-frequency motor balance.")]
    private GamepadVibrationProfile profile;

    [SerializeField, Min(0f), Tooltip("Per-layer motor strength multiplied by the common feedback Intensity.")]
    private float strengthMultiplier = 1f;

    [SerializeField, Tooltip("Keeps vibrating until the returned feedback handle is stopped.")]
    private bool loop;

    [SerializeField, Min(0f), Tooltip("Total one-shot duration in seconds. Ignored while Loop is enabled.")]
    private float duration = 0.2f;

    [SerializeField, Min(0f), Tooltip("Seconds used to fade the vibration in from zero.")]
    private float fadeIn;

    [SerializeField, Min(0f), Tooltip("Seconds used at a one-shot's natural end and after Stop for any vibration.")]
    private float fadeOut = 0.05f;

    public bool IsEnabled => isEnabled;
    public GamepadVibrationProfile Profile => profile;
    public float StrengthMultiplier => Mathf.Max(0f, strengthMultiplier);
    public bool Loop => loop;
    public float Duration => Mathf.Max(0f, duration);
    public float FadeIn => Mathf.Max(0f, fadeIn);
    public float FadeOut => Mathf.Max(0f, fadeOut);
    public bool HasOutput => profile != null && profile.HasOutput && StrengthMultiplier > 0f;
}
}
