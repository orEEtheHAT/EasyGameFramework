namespace EasyCore
{
    /// <summary>
    /// Seconds on one explicitly selected timeline. Not UTC, milliseconds, a saved timestamp,
    /// or a scheduler. Keep the same source and Unity callback context for a timed operation.
    /// </summary>
    public interface IClock
    {
        double NowSeconds { get; }
    }
}
