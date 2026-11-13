using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace EasyFeedbacks
{

/// <summary>
/// Serializable settings for one audio layer in a feedback preset.
/// </summary>
[Serializable]
public sealed class AudioFeedbackEntry
{
    [SerializeField, Tooltip("Whether this audio layer is played as part of the feedback preset.")]
    private bool isEnabled = true;

    [SerializeField, Tooltip("One clip is selected randomly each time this layer plays.")]
    private List<AudioClip> clips = new List<AudioClip>();

    [SerializeField, Tooltip("Optional mixer group used to route this layer.")]
    private AudioMixerGroup mixerGroup;

    [SerializeField, Range(0f, 1f), Tooltip("Minimum randomized volume. Final volume also uses feedback intensity and the emitter audio multiplier.")]
    private float minVolume = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("Maximum randomized volume. Final volume also uses feedback intensity and the emitter audio multiplier.")]
    private float maxVolume = 1f;

    [SerializeField, Range(0.1f, 3f), Tooltip("Minimum randomized AudioSource pitch.")]
    private float minPitch = 1f;

    [SerializeField, Range(0.1f, 3f), Tooltip("Maximum randomized AudioSource pitch.")]
    private float maxPitch = 1f;

    [SerializeField, Tooltip("Keep the selected AudioClip playing until explicitly stopped.")]
    private bool loop;

    [SerializeField, Min(0f), Tooltip("Seconds used to fade in. Zero starts at full volume.")]
    private float fadeInDuration;

    [SerializeField, Min(0f), Tooltip("Seconds used to fade out after an explicit Stop command. Natural one-shot completion is not faded. Zero stops immediately.")]
    private float fadeOutDuration;

    [SerializeField, Range(0f, 1f), Tooltip("0 is fully 2D, 1 is fully 3D.")]
    private float spatialBlend;

    [SerializeField, Min(0f), Tooltip("Distance at which 3D attenuation starts.")]
    private float minDistance = 1f;

    [SerializeField, Min(0f), Tooltip("Distance at which 3D attenuation reaches its limit.")]
    private float maxDistance = 500f;

    [SerializeField, Tooltip("Attenuation curve used by the AudioSource.")]
    private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

    [SerializeField, Min(0f), Tooltip("Strength of the AudioSource doppler effect.")]
    private float dopplerLevel = 1f;

    [SerializeField, Tooltip("Inherit positioning from the Feedback Emitter, or override it for this layer.")]
    private FeedbackPositionOverride positionMode = FeedbackPositionOverride.Inherited;

    public bool IsEnabled => isEnabled;
    public IReadOnlyList<AudioClip> Clips => clips;
    public AudioMixerGroup MixerGroup => mixerGroup;
    public float MinVolume => Mathf.Clamp01(Mathf.Min(minVolume, maxVolume));
    public float MaxVolume => Mathf.Clamp01(Mathf.Max(minVolume, maxVolume));
    public float MinPitch => Mathf.Clamp(Mathf.Min(minPitch, maxPitch), 0.1f, 3f);
    public float MaxPitch => Mathf.Clamp(Mathf.Max(minPitch, maxPitch), 0.1f, 3f);
    public bool Loop => loop;
    public float FadeInDuration => fadeInDuration;
    public float FadeOutDuration => fadeOutDuration;
    public float SpatialBlend => spatialBlend;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;
    public AudioRolloffMode RolloffMode => rolloffMode;
    public float DopplerLevel => dopplerLevel;
    public FeedbackPositionOverride PositionMode => positionMode;

    public bool HasAnyClip
    {
        get
        {
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public AudioClip GetRandomClip()
    {
        var validClipCount = 0;
        for (var i = 0; i < clips.Count; i++)
        {
            if (clips[i] != null)
            {
                validClipCount++;
            }
        }

        if (validClipCount == 0)
        {
            return null;
        }

        var selectedValidIndex = UnityEngine.Random.Range(0, validClipCount);
        for (var i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            if (clip == null)
            {
                continue;
            }

            if (selectedValidIndex == 0)
            {
                return clip;
            }

            selectedValidIndex--;
        }

        return null;
    }

    public float GetRandomVolume()
    {
        return RandomRange(MinVolume, MaxVolume);
    }

    public float GetRandomPitch()
    {
        return RandomRange(MinPitch, MaxPitch);
    }

    private static float RandomRange(float min, float max)
    {
        return max > min ? UnityEngine.Random.Range(min, max) : min;
    }
}
}
