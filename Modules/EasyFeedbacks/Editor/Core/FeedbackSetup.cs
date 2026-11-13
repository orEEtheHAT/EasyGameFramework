using EasyFeedbacks;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyGameFramework.Editor
{
/// <summary>Explicit, undoable scene setup; never creates managers during gameplay.</summary>
public static class FeedbackSetup
{
    private const string Menu = "Tools/EasyGameFramework/Feedbacks/Add Playback Managers";

    [MenuItem(Menu)]
    public static void AddPlaybackManagers()
    {
        if (!CanAddPlaybackManagers()) return;

        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Feedback Playback Managers");
        GameObject root = null;
        try
        {
            AddMissing<AudioManager>(ref root);
            AddMissing<VisualFeedbackManager>(ref root);
            AddMissing<CameraShakeManager>(ref root);
            AddMissing<GamepadVibrationManager>(ref root);
            AddMissing<MusicManager>(ref root);
            if (root != null) Selection.activeGameObject = root;
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
        }
    }

    [MenuItem(Menu, true)]
    private static bool CanAddPlaybackManagers()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode
               && PrefabStageUtility.GetCurrentPrefabStage() == null;
    }

    private static void AddMissing<T>(ref GameObject root) where T : MonoBehaviour
    {
        // Include disabled objects: setup must not silently add a second singleton.
        if (Object.FindFirstObjectByType<T>(FindObjectsInactive.Include) != null) return;
        if (root == null)
        {
            root = new GameObject("Easy Feedbacks");
            Undo.RegisterCreatedObjectUndo(root, "Create Feedback Playback Managers");
        }

        Undo.AddComponent<T>(root);
    }

    internal static void DrawRequirements(FeedbackPreset preset)
    {
        if (preset == null) return;
        var audio = false;
        var visuals = false;
        var shake = false;
        var haptics = false;
        for (var i = 0; i < preset.AudioEntries.Count; i++)
            audio |= preset.AudioEntries[i] != null && preset.AudioEntries[i].IsEnabled;
        for (var i = 0; i < preset.VisualEntries.Count; i++)
            visuals |= preset.VisualEntries[i] != null && preset.VisualEntries[i].IsEnabled;
        for (var i = 0; i < preset.CameraShakeEntries.Count; i++)
            shake |= preset.CameraShakeEntries[i] != null && preset.CameraShakeEntries[i].IsEnabled;
        for (var i = 0; i < preset.GamepadVibrationEntries.Count; i++)
            haptics |= preset.GamepadVibrationEntries[i] != null && preset.GamepadVibrationEntries[i].IsEnabled;

        var missing = DrawRequired<AudioManager>(audio);
        missing |= DrawRequired<VisualFeedbackManager>(visuals);
        missing |= DrawRequired<CameraShakeManager>(shake);
        missing |= DrawRequired<GamepadVibrationManager>(haptics);
        if (shake && Object.FindFirstObjectByType<CinemachineImpulseListener>() == null)
        {
            EditorGUILayout.HelpBox("Add a Cinemachine Impulse Listener to each camera that should react to this preset.", MessageType.Warning);
        }

        if (missing && CanAddPlaybackManagers() && GUILayout.Button("Add Missing Playback Managers"))
            AddPlaybackManagers();
    }

    private static bool DrawRequired<T>(bool required) where T : MonoBehaviour
    {
        if (!required) return false;
        var manager = Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (manager == null)
        {
            EditorGUILayout.HelpBox($"This preset needs {typeof(T).Name}. Add playback managers to your bootstrap scene using Tools > EasyGameFramework > Feedbacks.", MessageType.Warning);
            return true;
        }

        if (!manager.isActiveAndEnabled)
            EditorGUILayout.HelpBox($"Enable {typeof(T).Name} and its GameObject before playing this preset.", MessageType.Warning);
        return false;
    }
}
}
