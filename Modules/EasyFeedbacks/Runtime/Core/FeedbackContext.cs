using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyFeedbacks
{

/// <summary>
/// Describes where a feedback was requested and how strongly it should be played.
/// </summary>
public readonly struct FeedbackContext
{
    private readonly float intensity;
    private readonly bool hasExplicitIntensity;
    private readonly float audioMultiplier;
    private readonly bool hasExplicitAudioMultiplier;
    private readonly Vector3 visualScale;
    private readonly bool hasExplicitVisualScale;

    public Vector3 WorldPosition { get; }
    public Quaternion Rotation { get; }
    public Transform Source { get; }
    public Transform Follow { get; }
    public FeedbackPositionMode Positioning { get; }
    public Gamepad TargetGamepad { get; }
    /// <summary>True for an explicit device or an explicit choice to suppress gamepad output.</summary>
    public bool HasExplicitGamepadTarget { get; }
    public float Intensity => hasExplicitIntensity ? intensity : 1f;
    public float AudioMultiplier => hasExplicitAudioMultiplier ? audioMultiplier : 1f;
    public Vector3 VisualScale => hasExplicitVisualScale ? visualScale : Vector3.one;

    public FeedbackContext(
        Vector3 worldPosition,
        Quaternion rotation,
        Transform source = null,
        Transform follow = null,
        float intensity = 1f)
        : this(worldPosition, rotation, source, follow, intensity, 1f, Vector3.one)
    {
    }

    public FeedbackContext(
        Vector3 worldPosition,
        Quaternion rotation,
        Transform source,
        Transform follow,
        float intensity,
        Vector3 visualScale)
        : this(worldPosition, rotation, source, follow, intensity, 1f, visualScale)
    {
    }

    public FeedbackContext(
        Vector3 worldPosition,
        Quaternion rotation,
        Transform source,
        Transform follow,
        float intensity,
        float audioMultiplier,
        Vector3 visualScale)
        : this(
            worldPosition,
            rotation,
            source,
            follow,
            intensity,
            audioMultiplier,
            visualScale,
            FeedbackPositionMode.WorldPosition)
    {
    }

    public FeedbackContext(
        Vector3 worldPosition,
        Quaternion rotation,
        Transform source,
        Transform follow,
        float intensity,
        float audioMultiplier,
        Vector3 visualScale,
        FeedbackPositionMode positioning,
        Gamepad targetGamepad = null)
        : this(worldPosition, rotation, source, follow, intensity, audioMultiplier, visualScale,
            positioning, targetGamepad, targetGamepad != null)
    {
    }

    private FeedbackContext(
        Vector3 worldPosition,
        Quaternion rotation,
        Transform source,
        Transform follow,
        float intensity,
        float audioMultiplier,
        Vector3 visualScale,
        FeedbackPositionMode positioning,
        Gamepad targetGamepad,
        bool hasExplicitGamepadTarget)
    {
        WorldPosition = worldPosition;
        Rotation = rotation;
        Source = source;
        Follow = follow;
        Positioning = FeedbackPositioning.Normalize(positioning);
        TargetGamepad = targetGamepad;
        HasExplicitGamepadTarget = hasExplicitGamepadTarget;
        this.intensity = Mathf.Max(0f, intensity);
        hasExplicitIntensity = true;
        this.audioMultiplier = Mathf.Clamp01(audioMultiplier);
        hasExplicitAudioMultiplier = true;
        this.visualScale = ClampScale(visualScale);
        hasExplicitVisualScale = true;
    }

    /// <summary>
    /// Supplies a player's device, or explicitly suppresses vibration when that player has no gamepad.
    /// Preserves any explicit choice, including WithoutGamepad().
    /// </summary>
    public FeedbackContext WithDefaultGamepad(Gamepad gamepad)
    {
        return HasExplicitGamepadTarget ? this : gamepad != null ? WithGamepad(gamepad) : WithoutGamepad();
    }

    /// <summary>Explicitly suppresses gamepad output while preserving every non-gamepad context value.</summary>
    public FeedbackContext WithoutGamepad()
    {
        return new FeedbackContext(
            WorldPosition, Rotation, Source, Follow, Intensity, AudioMultiplier, VisualScale,
            Positioning, null, true);
    }

    /// <summary>
    /// Replaces the target, preserving every non-gamepad value. For backward compatibility, null
    /// restores the legacy Gamepad.current fallback; use WithoutGamepad() to suppress vibration.
    /// </summary>
    public FeedbackContext WithGamepad(Gamepad gamepad)
    {
        return new FeedbackContext(
            WorldPosition,
            Rotation,
            Source,
            Follow,
            Intensity,
            AudioMultiplier,
            VisualScale,
            Positioning,
            gamepad);
    }

    public static FeedbackContext From(Transform source, Transform follow = null, float intensity = 1f)
    {
        return From(source, follow, intensity, 1f, Vector3.one);
    }

    public static FeedbackContext From(
        Transform source,
        Transform follow,
        float intensity,
        Vector3 visualScale)
    {
        return From(source, follow, intensity, 1f, visualScale);
    }

    public static FeedbackContext From(
        Transform source,
        Transform follow,
        float intensity,
        float audioMultiplier,
        Vector3 visualScale)
    {
        return From(
            source,
            follow,
            intensity,
            audioMultiplier,
            visualScale,
            FeedbackPositionMode.WorldPosition);
    }

    public static FeedbackContext From(
        Transform source,
        Transform follow,
        float intensity,
        float audioMultiplier,
        Vector3 visualScale,
        FeedbackPositionMode positioning)
    {
        if (source == null)
        {
            return new FeedbackContext(
                Vector3.zero,
                Quaternion.identity,
                null,
                follow,
                intensity,
                audioMultiplier,
                visualScale,
                positioning);
        }

        return new FeedbackContext(
            source.position,
            source.rotation,
            source,
            follow,
            intensity,
            audioMultiplier,
            visualScale,
            positioning);
    }

    private static Vector3 ClampScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Max(0f, scale.x),
            Mathf.Max(0f, scale.y),
            Mathf.Max(0f, scale.z));
    }
}
}
