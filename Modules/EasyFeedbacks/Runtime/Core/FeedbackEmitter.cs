using UnityEngine;

namespace EasyFeedbacks
{

public enum FeedbackAutoPlayMode
{
    Manual,
    OnStart,
    OnEnable
}

/// <summary>
/// Scene-friendly trigger for a reusable FeedbackPreset.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Feedback Emitter")]
public sealed class FeedbackEmitter : MonoBehaviour
{
    [SerializeField, Tooltip("Preset played by this emitter.")]
    private FeedbackPreset preset;

    [SerializeField, Tooltip("Optional active component implementing IFeedbackContextProvider, for example the owning player's gamepad provider.")]
    private MonoBehaviour contextProvider;

    [SerializeField, Tooltip("Default positioning used by preset entries set to Inherited.")]
    private FeedbackPositionMode positioning = FeedbackPositionMode.WorldPosition;

    [SerializeField, Tooltip("Optional transform followed by inherited or overridden Follow Transform entries. The emitter transform is used as fallback.")]
    private Transform follow;

    [SerializeField, Min(0f), Tooltip("Common strength multiplier applied to every feedback module.")]
    private float intensity = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("Additional per-emitter multiplier applied only to audio volume.")]
    private float audioMultiplier = 1f;

    [SerializeField, Tooltip("Additional per-emitter scale applied only to visual feedback.")]
    private Vector3 visualScale = Vector3.one;

    [SerializeField, Tooltip("Optional lifecycle event that automatically launches this emitter.")]
    private FeedbackAutoPlayMode autoPlay = FeedbackAutoPlayMode.Manual;

    [SerializeField, Tooltip("Stops the most recent launch when this component or its GameObject is disabled.")]
    private bool stopOnDisable;

    private FeedbackHandle lastHandle;
    private bool hasStarted;

    private void OnEnable()
    {
        if (Application.isPlaying
            && hasStarted
            && autoPlay == FeedbackAutoPlayMode.OnEnable)
        {
            Play();
        }
    }

    private void Start()
    {
        FeedbackService.Prewarm(preset);
        hasStarted = true;

        if (autoPlay == FeedbackAutoPlayMode.OnStart
            || autoPlay == FeedbackAutoPlayMode.OnEnable)
        {
            Play();
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying && stopOnDisable)
        {
            Stop();
        }
    }

    public FeedbackPreset Preset
    {
        get => preset;
        set
        {
            preset = value;
            if (Application.isPlaying)
            {
                FeedbackService.Prewarm(preset);
            }
        }
    }

    public MonoBehaviour ContextProvider
    {
        get => contextProvider;
        set => contextProvider = value;
    }

    public Transform Follow
    {
        get => follow;
        set => follow = value;
    }

    public FeedbackPositionMode Positioning
    {
        get => positioning;
        set => positioning = FeedbackPositioning.Normalize(value);
    }

    public float Intensity
    {
        get => intensity;
        set => intensity = Mathf.Max(0f, value);
    }

    public FeedbackHandle LastHandle => lastHandle;

    public float AudioMultiplier
    {
        get => audioMultiplier;
        set => audioMultiplier = Mathf.Clamp01(value);
    }

    public Vector3 VisualScale
    {
        get => visualScale;
        set => visualScale = ClampScale(value);
    }

    public FeedbackAutoPlayMode AutoPlay
    {
        get => autoPlay;
        set => autoPlay = value;
    }

    public bool StopOnDisable
    {
        get => stopOnDisable;
        set => stopOnDisable = value;
    }

    /// <summary>UnityEvent-compatible feedback trigger.</summary>
    public void Play()
    {
        PlayFeedback();
    }

    /// <summary>Code-friendly trigger that also returns the new handle.</summary>
    public FeedbackHandle PlayFeedback()
    {
        var context = FeedbackContext.From(transform, follow, intensity, audioMultiplier, visualScale, positioning);
        if (contextProvider != null && contextProvider.isActiveAndEnabled
            && contextProvider is IFeedbackContextProvider provider)
        {
            context = provider.Prepare(context);
        }

        lastHandle = FeedbackService.Play(preset, context);
        return lastHandle;
    }

    /// <summary>Stops the most recent feedback launch started by this emitter.</summary>
    public void Stop()
    {
        lastHandle.Stop();
    }

    /// <summary>Pauses the most recent feedback launch started by this emitter.</summary>
    public void Pause()
    {
        lastHandle.Pause();
    }

    /// <summary>Resumes the most recent feedback launch started by this emitter.</summary>
    public void Resume()
    {
        lastHandle.Resume();
    }

    private void OnValidate()
    {
        positioning = FeedbackPositioning.Normalize(positioning);
        intensity = Mathf.Max(0f, intensity);
        audioMultiplier = Mathf.Clamp01(audioMultiplier);
        visualScale = ClampScale(visualScale);
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
