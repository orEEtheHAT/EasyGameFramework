namespace EasyFeedbacks
{

/// <summary>
/// Generation-safe reference to one camera shake owned by CameraShakeManager.
/// </summary>
public readonly struct CameraShakeHandle
{
    private readonly CameraShakeManager manager;
    private readonly int shakeId;
    private readonly uint generation;

    internal CameraShakeHandle(CameraShakeManager manager, int shakeId, uint generation)
    {
        this.manager = manager;
        this.shakeId = shakeId;
        this.generation = generation;
    }

    internal CameraShakeManager Owner => manager;
    internal int ShakeId => shakeId;
    internal uint Generation => generation;

    public bool IsValid => manager != null && manager.IsHandleValid(this);

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
