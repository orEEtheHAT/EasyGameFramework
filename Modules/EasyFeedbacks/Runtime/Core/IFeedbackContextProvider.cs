namespace EasyFeedbacks
{

/// <summary>
/// Supplies player-specific defaults for an existing feedback request.
/// Implementations should preserve its source, positioning, strength and any explicitly targeted gamepad.
/// </summary>
public interface IFeedbackContextProvider
{
    FeedbackContext Prepare(FeedbackContext context);
}
}
