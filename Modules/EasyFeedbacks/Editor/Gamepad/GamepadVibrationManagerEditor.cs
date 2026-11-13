using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(GamepadVibrationManager))]
public sealed class GamepadVibrationManagerEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var manager = (GamepadVibrationManager)target;
        if (manager.transform.parent != null)
        {
            EditorGUILayout.HelpBox(
                "Gamepad Vibration Manager should be a root object because it persists between scenes.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Active Vibrations", manager.ActiveVibrations);
            EditorGUILayout.IntField("Available Vibrations", manager.AvailableVibrations);
            EditorGUILayout.IntField("Total Vibrations", manager.TotalVibrations);
            EditorGUILayout.Toggle("Paused by Feedback Service", Application.isPlaying && FeedbackService.AreAllPaused);
            EditorGUILayout.TextField(
                "Current Gamepad",
                manager.CurrentGamepad != null ? manager.CurrentGamepad.displayName : "None");
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(manager.AreAllVibrationsPaused))
                {
                    if (GUILayout.Button("Pause All"))
                    {
                        manager.PauseAll();
                    }
                }

                using (new EditorGUI.DisabledScope(!manager.AreAllVibrationsPaused))
                {
                    if (GUILayout.Button("Resume All"))
                    {
                        manager.ResumeAll();
                    }
                }
            }

            if (GUILayout.Button("Stop All Immediately"))
            {
                manager.StopAll(true);
            }
        }
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
}
