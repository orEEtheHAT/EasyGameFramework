using Unity.Cinemachine;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Reusable Cinemachine impulse shape shared by feedback presets.
/// </summary>
public sealed class CameraShakeProfile : ScriptableObject
{
    [SerializeField, Tooltip("Cinemachine impulse shape, duration, channel and spatial propagation settings.")]
    private CinemachineImpulseDefinition impulseDefinition = CreateDefaultDefinition();

    [SerializeField, Tooltip("Generate a directed positional kick using the impulse shape.")]
    private bool directionalKickEnabled;

    [SerializeField, Tooltip("Camera-oriented Directional Kick vector. Its direction sets the kick axis and its magnitude sets relative kick strength.")]
    private Vector3 kickVector = Vector3.down;

    [SerializeField, Tooltip("Generate a profile-specific multi-axis noise shake.")]
    private bool noiseShakeEnabled = true;

    [SerializeField, Tooltip("Cinemachine noise asset used for positional and rotational shake.")]
    private NoiseSettings noiseProfile;

    [SerializeField, Min(0f), Tooltip("Multiplier for positional channels in the Noise Profile.")]
    private float noisePositionAmplitude = 1f;

    [SerializeField, Min(0f), Tooltip("Multiplier for rotational channels in the Noise Profile.")]
    private float noiseRotationAmplitude = 3f;

    [SerializeField, Min(0f), Tooltip("Playback speed multiplier for the Noise Profile.")]
    private float noiseFrequency = 1f;

    [SerializeField, Tooltip("Use a different Noise Profile phase for every impulse.")]
    private bool randomizeNoise = true;

    public CinemachineImpulseDefinition ImpulseDefinition => impulseDefinition;
    public bool DirectionalKickEnabled => directionalKickEnabled;
    public Vector3 KickVector => kickVector;
    public bool NoiseShakeEnabled => noiseShakeEnabled;

    internal void Generate(
        Vector3 worldPosition,
        Vector3 kickVector,
        float strength,
        bool loopNoise,
        float noiseDuration,
        float noiseFadeIn,
        float noiseFadeOut,
        NoiseShakeSignal noiseSignal,
        out CinemachineImpulseManager.ImpulseEvent kickEvent,
        out CinemachineImpulseManager.ImpulseEvent noiseEvent)
    {
        kickEvent = null;
        noiseEvent = null;
        if (impulseDefinition == null || strength <= Mathf.Epsilon)
        {
            return;
        }

        if (directionalKickEnabled && kickVector.sqrMagnitude > Mathf.Epsilon)
        {
            kickEvent = impulseDefinition.CreateAndReturnEvent(
                worldPosition,
                kickVector * strength);

            if (kickEvent != null
                && impulseDefinition.ImpulseType == CinemachineImpulseDefinition.ImpulseTypes.Uniform)
            {
                kickEvent.PropagationSpeed = float.MaxValue;
            }
        }

        noiseEvent = CreateNoiseShakeEvent(
            worldPosition,
            strength,
            loopNoise,
            noiseDuration,
            noiseFadeIn,
            noiseFadeOut,
            noiseSignal);
    }

    private void OnValidate()
    {
        if (impulseDefinition == null)
        {
            impulseDefinition = CreateDefaultDefinition();
        }

        impulseDefinition.OnValidate();
        noisePositionAmplitude = Mathf.Max(0f, noisePositionAmplitude);
        noiseRotationAmplitude = Mathf.Max(0f, noiseRotationAmplitude);
        noiseFrequency = Mathf.Max(0f, noiseFrequency);
    }

    private CinemachineImpulseManager.ImpulseEvent CreateNoiseShakeEvent(
        Vector3 worldPosition,
        float strength,
        bool loop,
        float requestedDuration,
        float requestedFadeIn,
        float requestedFadeOut,
        NoiseShakeSignal noiseSignal)
    {
        var duration = Mathf.Max(0f, requestedDuration);
        if (!noiseShakeEnabled
            || noiseProfile == null
            || noiseSignal == null
            || (!loop && duration <= Mathf.Epsilon)
            || noiseFrequency <= Mathf.Epsilon
            || strength <= Mathf.Epsilon
            || (noisePositionAmplitude <= Mathf.Epsilon && noiseRotationAmplitude <= Mathf.Epsilon))
        {
            return null;
        }

        var manager = CinemachineImpulseManager.Instance;
        var impulseEvent = manager.NewImpulseEvent();
        var fadeIn = Mathf.Max(0f, requestedFadeIn);
        var fadeOut = Mathf.Max(0f, requestedFadeOut);
        if (!loop)
        {
            var totalFadeDuration = fadeIn + fadeOut;
            if (totalFadeDuration > duration && totalFadeDuration > Mathf.Epsilon)
            {
                var fadeScale = duration / totalFadeDuration;
                fadeIn *= fadeScale;
                fadeOut *= fadeScale;
            }
        }

        impulseEvent.Envelope = new CinemachineImpulseManager.EnvelopeDefinition
        {
            AttackTime = fadeIn,
            SustainTime = loop ? 0f : Mathf.Max(0f, duration - fadeIn - fadeOut),
            DecayTime = fadeOut,
            HoldForever = loop
        };
        noiseSignal.Configure(
            noiseProfile,
            noisePositionAmplitude * strength,
            noiseRotationAmplitude * strength,
            noiseFrequency,
            duration,
            randomizeNoise,
            loop);
        impulseEvent.SignalSource = noiseSignal;
        impulseEvent.Position = worldPosition;
        ConfigureSpatialSettings(impulseEvent);
        manager.AddImpulseEvent(impulseEvent);
        return impulseEvent;
    }

    private void ConfigureSpatialSettings(CinemachineImpulseManager.ImpulseEvent impulseEvent)
    {
        const float bigNumber = 9999999f;

        impulseEvent.Channel = impulseDefinition.ImpulseChannel;
        var isUniform = impulseDefinition.ImpulseType == CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        impulseEvent.Radius = isUniform ? bigNumber : 0f;
        impulseEvent.DirectionMode = CinemachineImpulseManager.ImpulseEvent.DirectionModes.Fixed;
        impulseEvent.DissipationDistance = isUniform ? 0f : impulseDefinition.DissipationDistance;
        impulseEvent.PropagationSpeed = isUniform
            ? float.MaxValue
            : impulseDefinition.ImpulseType == CinemachineImpulseDefinition.ImpulseTypes.Propagating
                ? impulseDefinition.PropagationSpeed
                : bigNumber;
        impulseEvent.CustomDissipation = impulseDefinition.DissipationRate;
    }

    private static CinemachineImpulseDefinition CreateDefaultDefinition()
    {
        return new CinemachineImpulseDefinition
        {
            ImpulseChannel = 1,
            ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump,
            CustomImpulseShape = new AnimationCurve(),
            ImpulseDuration = 0.2f,
            ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform,
            DissipationDistance = 100f,
            DissipationRate = 0.25f,
            PropagationSpeed = 343f
        };
    }
}
}
