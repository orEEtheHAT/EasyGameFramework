using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(SceneMusicManager))]
public sealed class SceneMusicManagerEditor : UnityEditor.Editor
{
    private SerializedProperty playlist;
    private SerializedProperty activationMode;
    private SerializedProperty priority;
    private SerializedProperty transitionDuration;

    private void OnEnable()
    {
        playlist = serializedObject.FindProperty("playlist");
        activationMode = serializedObject.FindProperty("activationMode");
        priority = serializedObject.FindProperty("priority");
        transitionDuration = serializedObject.FindProperty("transitionDuration");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Scene Music Manager", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(playlist);
        EditorGUILayout.PropertyField(activationMode);
        EditorGUILayout.PropertyField(priority);
        EditorGUILayout.PropertyField(transitionDuration);

        if (playlist.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Assign the base Music Playlist for this scene.", MessageType.Warning);
        }

        var mode = (SceneMusicManagerActivationMode)activationMode.enumValueIndex;
        EditorGUILayout.HelpBox(
            mode == SceneMusicManagerActivationMode.ActiveScene
                ? "This playlist applies only while this object's scene is the Unity Active Scene. This is the recommended default for additive scenes."
                : "This playlist competes with other Scene Music Managers for the entire time its scene remains loaded.",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }
}
}
