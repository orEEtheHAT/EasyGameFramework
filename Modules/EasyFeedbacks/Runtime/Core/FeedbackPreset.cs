using System.Collections.Generic;
using UnityEngine;

namespace EasyFeedbacks
{

[CreateAssetMenu(fileName = "Feedback Preset", menuName = "EasyGameFramework/Feedbacks/Feedback Preset")]
public sealed class FeedbackPreset : ScriptableObject
{
    [SerializeField]
    private List<AudioFeedbackEntry> audioEntries = new List<AudioFeedbackEntry>();

    [SerializeField]
    private List<VisualFeedbackEntry> visualEntries = new List<VisualFeedbackEntry>();

    [SerializeField]
    private List<CameraShakeFeedbackEntry> cameraShakeEntries = new List<CameraShakeFeedbackEntry>();

    [SerializeField]
    private List<GamepadVibrationFeedbackEntry> gamepadVibrationEntries = new List<GamepadVibrationFeedbackEntry>();

    public IReadOnlyList<AudioFeedbackEntry> AudioEntries => audioEntries;
    public IReadOnlyList<VisualFeedbackEntry> VisualEntries => visualEntries;
    public IReadOnlyList<CameraShakeFeedbackEntry> CameraShakeEntries => cameraShakeEntries;
    public IReadOnlyList<GamepadVibrationFeedbackEntry> GamepadVibrationEntries => gamepadVibrationEntries;
}
}
