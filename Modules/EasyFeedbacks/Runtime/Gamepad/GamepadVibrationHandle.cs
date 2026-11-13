namespace EasyFeedbacks
{

/// <summary>
/// Generation-safe reference to one gamepad vibration playback.
/// </summary>
public readonly struct GamepadVibrationHandle
{
    private readonly GamepadVibrationManager manager;
    private readonly int vibrationId;
    private readonly uint generation;

    internal GamepadVibrationHandle(GamepadVibrationManager manager, int vibrationId, uint generation)
    {
        this.manager = manager;
        this.vibrationId = vibrationId;
        this.generation = generation;
    }

    internal GamepadVibrationManager Owner => manager;
    internal int VibrationId => vibrationId;
    internal uint Generation => generation;

    public bool IsValid => manager != null && manager.IsHandleValid(this);
    public bool IsPlaying => manager != null && manager.IsHandlePlaying(this);

    public void Stop()
    {
        if (manager != null)
        {
            manager.Stop(this);
        }
    }

    public void Stop(float fadeOutDuration)
    {
        if (manager != null)
        {
            manager.Stop(this, fadeOutDuration);
        }
    }

    public void Pause()
    {
        if (manager != null)
        {
            manager.Pause(this);
        }
    }

    public void Resume()
    {
        if (manager != null)
        {
            manager.Resume(this);
        }
    }
}
}
