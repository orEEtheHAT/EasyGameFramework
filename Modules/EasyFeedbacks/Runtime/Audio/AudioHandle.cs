namespace EasyFeedbacks
{

/// <summary>
/// Safe reference to one pooled AudioSource checkout.
/// </summary>
public readonly struct AudioHandle
{
    private readonly AudioManager manager;
    private readonly int voiceId;
    private readonly uint generation;

    internal AudioHandle(AudioManager manager, int voiceId, uint generation)
    {
        this.manager = manager;
        this.voiceId = voiceId;
        this.generation = generation;
    }

    internal int VoiceId => voiceId;
    internal uint Generation => generation;
    internal AudioManager Owner => manager;

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

    public void SetVolume(float volume)
    {
        if (manager != null)
        {
            manager.SetVolume(this, volume);
        }
    }
}
}
