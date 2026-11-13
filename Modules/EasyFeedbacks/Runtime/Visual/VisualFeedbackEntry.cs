using System;
using UnityEngine;
using UnityEngine.VFX;

namespace EasyFeedbacks
{

public enum VisualFeedbackType
{
    ParticleSystem,
    VFXGraph
}

public enum VisualFeedbackLifetime
{
    OneShot,
    LoopUntilStopped
}

public enum VisualFeedbackStopMode
{
    FinishCurrentParticles,
    ClearImmediately
}

/// <summary>
/// Serializable settings for one Particle System or VFX Graph layer.
/// </summary>
[Serializable]
public sealed class VisualFeedbackEntry
{
    [SerializeField, Tooltip("Whether this visual layer is played as part of the feedback preset.")]
    private bool isEnabled = true;

    [SerializeField, Tooltip("Choose whether this layer uses a Particle System prefab or a VFX prefab.")]
    private VisualFeedbackType type;

    [SerializeField, Tooltip("Particle System prefab instantiated and reused by VisualFeedbackManager.")]
    private ParticleSystem particleSystemPrefab;

    [SerializeField, Tooltip("Prefab whose root contains a VisualEffect component. The complete prefab is instantiated and reused.")]
    private VisualEffect visualEffectPrefab;

    [SerializeField, Tooltip("Pool lifetime policy. One Shot returns automatically. Loop Until Stopped requires an explicit Stop and the resource must be authored to loop.")]
    private VisualFeedbackLifetime lifetime = VisualFeedbackLifetime.OneShot;

    [SerializeField, Tooltip("Keep existing particles alive after Stop, or clear the visual immediately.")]
    private VisualFeedbackStopMode stopMode = VisualFeedbackStopMode.FinishCurrentParticles;

    [SerializeField, Min(0.01f), Tooltip("Complete VFX Graph one-shot lifetime in simulation seconds, including spawn delays, emission, and the longest particle tail. Respects the Visual Effect Play Rate.")]
    private float vfxOneShotDuration = 2f;

    [SerializeField, Min(0), Tooltip("Maximum simulation time that existing VFX Graph particles keep running after Stop when Finish Current Particles is selected. Respects the Visual Effect Play Rate.")]
    private float vfxStopTailDuration = 1f;

    [SerializeField, Min(0), Tooltip("Instances prepared when a Feedback Emitter using this preset starts. One removes the first-play Instantiate spike; increase for effects that can overlap.")]
    private int prewarmCount = 1;

    [SerializeField, Tooltip("Inherit positioning from the Feedback Emitter, or override it for this layer.")]
    private FeedbackPositionOverride positionMode = FeedbackPositionOverride.Inherited;

    [SerializeField, Tooltip("Local position offset relative to the feedback context rotation.")]
    private Vector3 positionOffset;

    [SerializeField, Tooltip("Local Euler rotation applied after the feedback context rotation and before the prefab rotation.")]
    private Vector3 rotationOffset;

    [SerializeField, Tooltip("Per-entry scale multiplied by the prefab scale and FeedbackContext visual scale.")]
    private Vector3 scaleMultiplier = Vector3.one;

    public bool IsEnabled => isEnabled;
    public VisualFeedbackType Type => type;
    public ParticleSystem ParticleSystemPrefab => particleSystemPrefab;
    public VisualEffect VisualEffectPrefab => visualEffectPrefab;
    public VisualFeedbackLifetime Lifetime => lifetime;
    public VisualFeedbackStopMode StopMode => stopMode;
    public float VfxOneShotDuration => Mathf.Max(0.01f, vfxOneShotDuration);
    public float VfxStopTailDuration => Mathf.Max(0f, vfxStopTailDuration);
    public int PrewarmCount => Mathf.Max(0, prewarmCount);
    public FeedbackPositionOverride PositionMode => positionMode;
    public Vector3 PositionOffset => positionOffset;
    public Quaternion RotationOffset => Quaternion.Euler(rotationOffset);
    public Vector3 ScaleMultiplier => scaleMultiplier;

    public UnityEngine.Object Resource => type == VisualFeedbackType.ParticleSystem
        ? particleSystemPrefab
        : visualEffectPrefab;
}
}
