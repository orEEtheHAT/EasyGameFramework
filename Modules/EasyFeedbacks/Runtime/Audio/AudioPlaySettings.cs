using UnityEngine;
using UnityEngine.Audio;

namespace EasyFeedbacks
{

/// <summary>
/// Compact settings used when playing an AudioClip directly through AudioManager.
/// </summary>
public struct AudioPlaySettings
{
    public AudioMixerGroup MixerGroup;
    public FeedbackAudioCategory Category;
    public float Volume;
    public float Pitch;
    public bool Loop;
    public float FadeInDuration;
    public float FadeOutDuration;
    public float SpatialBlend;
    public float MinDistance;
    public float MaxDistance;
    public AudioRolloffMode RolloffMode;
    public float DopplerLevel;
    public FeedbackPositionMode PositionMode;
    public Vector3 WorldPosition;
    public Quaternion Rotation;
    public Transform FollowTransform;

    public static AudioPlaySettings Default
    {
        get
        {
            return new AudioPlaySettings
            {
                Volume = 1f,
                Pitch = 1f,
                MinDistance = 1f,
                MaxDistance = 500f,
                RolloffMode = AudioRolloffMode.Logarithmic,
                DopplerLevel = 1f,
                PositionMode = FeedbackPositionMode.WorldPosition,
                Rotation = Quaternion.identity
            };
        }
    }
}
}
