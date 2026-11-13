namespace EasyFeedbacks
{

/// <summary>
/// Safe ownership token for one event music request.
/// Releasing one handle never affects requests owned by other events.
/// </summary>
public readonly struct MusicRequestHandle
{
    private readonly MusicManager manager;
    private readonly uint requestId;

    internal MusicRequestHandle(MusicManager manager, uint requestId)
    {
        this.manager = manager;
        this.requestId = requestId;
    }

    public bool IsValid => manager != null && manager.IsRequestActive(requestId);

    public void Release()
    {
        if (manager != null)
        {
            manager.ReleaseRequest(requestId);
        }
    }
}
}
