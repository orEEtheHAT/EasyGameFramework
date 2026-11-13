using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(VisualFeedbackManager))]
public sealed class VisualFeedbackManagerEditor : UnityEditor.Editor
{
    private SerializedProperty maximumCapacity;

    private void OnEnable()
    {
        maximumCapacity = serializedObject.FindProperty("maximumCapacity");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Pool Settings", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            EditorGUILayout.PropertyField(maximumCapacity);
        }

        var manager = (VisualFeedbackManager)target;
        if (manager.transform.parent != null)
        {
            EditorGUILayout.HelpBox(
                "Place VisualFeedbackManager on a root GameObject. It is moved to the scene root at runtime so it can persist.",
                MessageType.Warning);
        }

        if (manager.transform.localScale != Vector3.one)
        {
            EditorGUILayout.HelpBox(
                "Keep VisualFeedbackManager scale at (1, 1, 1). Its transform scale also affects every pooled visual instance.",
                MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Active Instances", Application.isPlaying ? manager.ActiveInstances : 0);
            EditorGUILayout.IntField("Available Instances", Application.isPlaying ? manager.AvailableInstances : 0);
            EditorGUILayout.IntField("Total Instances", Application.isPlaying ? manager.TotalInstances : 0);
            EditorGUILayout.IntField("Resident Resources", Application.isPlaying ? manager.ResidentResources : 0);
            EditorGUILayout.IntField("Evictions", Application.isPlaying ? manager.EvictionCount : 0);
            EditorGUILayout.IntField("Rejected Plays", Application.isPlaying ? manager.RejectedPlays : 0);
            EditorGUILayout.IntField("Maximum Capacity", manager.MaximumCapacity);
            EditorGUILayout.Toggle("Paused by Feedback Service", Application.isPlaying && FeedbackService.AreAllPaused);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(manager.AreAllVisualsPaused))
            {
                if (GUILayout.Button("Pause All"))
                {
                    manager.PauseAll();
                }
            }

            using (new EditorGUI.DisabledScope(!manager.AreAllVisualsPaused))
            {
                if (GUILayout.Button("Resume All"))
                {
                    manager.ResumeAll();
                }
            }

            if (GUILayout.Button("Stop All"))
            {
                manager.StopAll();
            }
        }
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
}
