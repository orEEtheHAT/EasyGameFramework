namespace EasyFeedbacks
{

public enum MusicDebugStatus
{
    Selected,
    Suspended,
    Waiting,
    Inactive
}

/// <summary>
/// Read-only snapshot of one playlist candidate registered in MusicManager.
/// </summary>
public readonly struct MusicDebugEntry
{
    public readonly MusicSelectionLayer Layer;
    public readonly MusicDebugStatus Status;
    public readonly MusicPlaylist Playlist;
    public readonly int Priority;
    public readonly MusicInterruptionMode InterruptionMode;
    public readonly string Context;

    public MusicDebugEntry(
        MusicSelectionLayer layer,
        MusicDebugStatus status,
        MusicPlaylist playlist,
        int priority,
        MusicInterruptionMode interruptionMode,
        string context)
    {
        Layer = layer;
        Status = status;
        Playlist = playlist;
        Priority = priority;
        InterruptionMode = interruptionMode;
        Context = context;
    }
}
}
