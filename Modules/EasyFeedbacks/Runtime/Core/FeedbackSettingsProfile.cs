using UnityEngine;

namespace EasyFeedbacks
{

/// <summary>Optional initial configuration. Runtime changes live in FeedbackSettings, not in this asset.</summary>
[CreateAssetMenu(fileName = "Feedback Settings", menuName = "EasyGameFramework/Feedbacks/Feedback Settings")]
public sealed class FeedbackSettingsProfile : ScriptableObject
{
    [SerializeField, Tooltip("Initial user preferences. Apply once from the application's bootstrap before restoring a saved snapshot.")]
    private FeedbackSettingsState values = FeedbackSettingsState.Default;

    public FeedbackSettingsState Values => values.Normalized();

    /// <summary>Copies these initial values to runtime state without changing this asset.</summary>
    public void Apply() => FeedbackSettings.Apply(Values);

    private void OnValidate()
    {
        values = values.Normalized();
    }
}
}
