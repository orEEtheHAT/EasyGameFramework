namespace EasyFeedbacks
{

/// <summary>
/// Safe reference to one pooled Particle System or VFX Graph checkout.
/// </summary>
public readonly struct VisualHandle
{
    private readonly VisualFeedbackManager manager;
    private readonly int instanceId;
    private readonly uint generation;

    internal VisualHandle(VisualFeedbackManager manager, int instanceId, uint generation)
    {
        this.manager = manager;
        this.instanceId = instanceId;
        this.generation = generation;
    }

    internal VisualFeedbackManager Owner => manager;
    internal int InstanceId => instanceId;
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
