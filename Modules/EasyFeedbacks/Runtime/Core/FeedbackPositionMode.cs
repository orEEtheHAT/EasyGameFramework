namespace EasyFeedbacks
{

public enum FeedbackPositionMode
{
    WorldPosition,
    FollowTransform
}

/// <summary>Per-entry positioning policy.</summary>
public enum FeedbackPositionOverride
{
    Inherited,
    WorldPosition,
    FollowTransform
}

public static class FeedbackPositioning
{
    public static FeedbackPositionMode Resolve(
        FeedbackPositionOverride positionOverride,
        FeedbackPositionMode inheritedMode)
    {
        switch (positionOverride)
        {
            case FeedbackPositionOverride.WorldPosition:
                return FeedbackPositionMode.WorldPosition;
            case FeedbackPositionOverride.FollowTransform:
                return FeedbackPositionMode.FollowTransform;
            default:
                return Normalize(inheritedMode);
        }
    }

    public static FeedbackPositionMode Normalize(FeedbackPositionMode mode)
    {
        return mode == FeedbackPositionMode.FollowTransform
            ? FeedbackPositionMode.FollowTransform
            : FeedbackPositionMode.WorldPosition;
    }
}
}
