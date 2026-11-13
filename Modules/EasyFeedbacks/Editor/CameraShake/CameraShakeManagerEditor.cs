using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(CameraShakeManager))]
public sealed class CameraShakeManagerEditor : UnityEditor.Editor
{
    private SerializedProperty initialCapacity;
    private SerializedProperty maximumCapacity;

    private void OnEnable()
    {
        initialCapacity = serializedObject.FindProperty("initialCapacity");
        maximumCapacity = serializedObject.FindProperty("maximumCapacity");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Pool Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(initialCapacity);
        EditorGUILayout.PropertyField(maximumCapacity);

        if (initialCapacity.intValue > maximumCapacity.intValue)
        {
            EditorGUILayout.HelpBox("Initial Capacity cannot exceed Maximum Capacity.", MessageType.Error);
        }

        var manager = (CameraShakeManager)target;
        if (manager.transform.parent != null)
        {
            EditorGUILayout.HelpBox(
                "Place CameraShakeManager on a root GameObject. It is moved to the scene root at runtime so it can persist.",
                MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Active Shakes", Application.isPlaying ? manager.ActiveShakes : 0);
            EditorGUILayout.IntField("Available Shakes", Application.isPlaying ? manager.AvailableShakes : 0);
            EditorGUILayout.IntField("Total Shakes", Application.isPlaying ? manager.TotalShakes : 0);
            EditorGUILayout.IntField("Maximum Capacity", manager.MaximumCapacity);
            EditorGUILayout.Toggle("Paused by Feedback Service", Application.isPlaying && FeedbackService.AreAllPaused);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(manager.AreAllShakesPaused))
                {
                    if (GUILayout.Button("Pause All"))
                    {
                        manager.PauseAll();
                    }
                }

                using (new EditorGUI.DisabledScope(!manager.AreAllShakesPaused))
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
