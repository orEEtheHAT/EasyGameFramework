using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(MusicZone))]
public sealed class MusicZoneEditor : UnityEditor.Editor
{
    private SerializedProperty playlist;
    private SerializedProperty priority;
    private SerializedProperty transitionDuration;
    private SerializedProperty playerTag;

    private void OnEnable()
    {
        playlist = serializedObject.FindProperty("playlist");
        priority = serializedObject.FindProperty("priority");
        transitionDuration = serializedObject.FindProperty("transitionDuration");
        playerTag = serializedObject.FindProperty("playerTag");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Zone Music", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(playlist);
        EditorGUILayout.PropertyField(priority);
        EditorGUILayout.PropertyField(transitionDuration);
        playerTag.stringValue = EditorGUILayout.TagField(
            new GUIContent("Player Tag", "Only colliders belonging to an object with this tag activate the zone."),
            playerTag.stringValue);

        if (playlist.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("Assign a Music Playlist.", MessageType.Warning);
        }

        var zone = (MusicZone)target;
        var zoneCollider = zone.GetComponent<Collider>();
        if (zoneCollider != null && !zoneCollider.isTrigger)
        {
            EditorGUILayout.HelpBox("The zone Collider must have Is Trigger enabled.", MessageType.Error);
            if (GUILayout.Button("Enable Is Trigger"))
            {
                Undo.RecordObject(zoneCollider, "Enable Music Zone Trigger");
                zoneCollider.isTrigger = true;
                EditorUtility.SetDirty(zoneCollider);
            }
        }

        var zoneBody = zone.GetComponent<Rigidbody>();
        if (zoneBody != null && (!zoneBody.isKinematic || zoneBody.useGravity))
        {
            EditorGUILayout.HelpBox(
                "The zone Rigidbody should be Kinematic with Use Gravity disabled.",
                MessageType.Warning);
            if (GUILayout.Button("Configure Zone Rigidbody"))
            {
                Undo.RecordObject(zoneBody, "Configure Music Zone Rigidbody");
                zoneBody.isKinematic = true;
                zoneBody.useGravity = false;
                EditorUtility.SetDirty(zoneBody);
            }
        }

        EditorGUILayout.HelpBox(
            "Higher Priority wins overlapping zones. With equal Priority, the most recently entered zone wins.",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }
}
}
