using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Reusable dual-motor vibration character shared by feedback presets.
/// Playback duration, looping and fades remain per feedback invocation.
/// </summary>
public sealed class GamepadVibrationProfile : ScriptableObject
{
    [SerializeField, Range(0f, 1f), Tooltip("Strength of the low-frequency, usually left, gamepad motor.")]
    private float lowFrequencyMotor = 0.6f;

    [SerializeField, Range(0f, 1f), Tooltip("Strength of the high-frequency, usually right, gamepad motor.")]
    private float highFrequencyMotor = 0.6f;

    [SerializeField, Tooltip("Normalized low-frequency motor pattern across a one-shot's Duration. Loop playback repeats this curve.")]
    private AnimationCurve lowFrequencyPattern = CreateConstantPattern();

    [SerializeField, Tooltip("Normalized high-frequency motor pattern across a one-shot's Duration. Loop playback repeats this curve.")]
    private AnimationCurve highFrequencyPattern = CreateConstantPattern();

    [SerializeField, Min(0.02f), Tooltip("Seconds required to repeat the motor patterns while the feedback layer is looping.")]
    private float loopCycleDuration = 0.5f;

    public float LowFrequencyMotor => Mathf.Clamp01(lowFrequencyMotor);
    public float HighFrequencyMotor => Mathf.Clamp01(highFrequencyMotor);
    public float LoopCycleDuration => Mathf.Max(0.02f, loopCycleDuration);
    public bool HasOutput => LowFrequencyMotor > 0f || HighFrequencyMotor > 0f;

    internal float EvaluateLowFrequencyPattern(float normalizedTime)
    {
        return EvaluatePattern(lowFrequencyPattern, normalizedTime);
    }

    internal float EvaluateHighFrequencyPattern(float normalizedTime)
    {
        return EvaluatePattern(highFrequencyPattern, normalizedTime);
    }

    private void OnValidate()
    {
        lowFrequencyMotor = Mathf.Clamp01(lowFrequencyMotor);
        highFrequencyMotor = Mathf.Clamp01(highFrequencyMotor);
        lowFrequencyPattern ??= CreateConstantPattern();
        highFrequencyPattern ??= CreateConstantPattern();
        loopCycleDuration = Mathf.Max(0.02f, loopCycleDuration);
    }

    private static float EvaluatePattern(AnimationCurve pattern, float normalizedTime)
    {
        if (pattern == null || pattern.length == 0)
        {
            return 1f;
        }

        return Mathf.Clamp01(pattern.Evaluate(Mathf.Clamp01(normalizedTime)));
    }

    private static AnimationCurve CreateConstantPattern()
    {
        return AnimationCurve.Linear(0f, 1f, 1f, 1f);
    }
}
}
