using Unity.Cinemachine;
using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>
/// Mutable noise source owned by one pooled camera shake voice.
/// Reconfiguration avoids allocating a new signal for every noise shake.
/// </summary>
internal sealed class NoiseShakeSignal : ISignalSource6D
{
    private NoiseSettings noiseProfile;
    private float positionAmplitude;
    private float rotationAmplitude;
    private float frequency;
    private float duration;
    private bool loop;
    private Vector3 timeOffsets;

    public float SignalDuration => loop ? 0f : duration;

    public void Configure(
        NoiseSettings profile,
        float positionAmplitude,
        float rotationAmplitude,
        float frequency,
        float duration,
        bool randomize,
        bool loop)
    {
        noiseProfile = profile;
        this.positionAmplitude = positionAmplitude;
        this.rotationAmplitude = rotationAmplitude;
        this.frequency = frequency;
        this.duration = duration;
        this.loop = loop;
        timeOffsets = randomize
            ? new Vector3(
                Random.Range(-1000f, 1000f),
                Random.Range(-1000f, 1000f),
                Random.Range(-1000f, 1000f))
            : Vector3.zero;
    }

    public void Reset()
    {
        noiseProfile = null;
        positionAmplitude = 0f;
        rotationAmplitude = 0f;
        frequency = 0f;
        duration = 0f;
        loop = false;
        timeOffsets = Vector3.zero;
    }

    public void GetSignal(float timeSinceSignalStart, out Vector3 position, out Quaternion rotation)
    {
        if (noiseProfile == null)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return;
        }

        var time = timeSinceSignalStart * frequency;
        position = NoiseSettings.GetCombinedFilterResults(
            noiseProfile.PositionNoise,
            time,
            timeOffsets) * positionAmplitude;
        var rotationEuler = NoiseSettings.GetCombinedFilterResults(
            noiseProfile.OrientationNoise,
            time,
            timeOffsets) * rotationAmplitude;
        rotation = Quaternion.Euler(rotationEuler);
    }
}
}
