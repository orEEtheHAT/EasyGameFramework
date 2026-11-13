using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(MusicPlaylist))]
public sealed class MusicPlaylistEditor : UnityEditor.Editor
{
    private MusicRequestHandle previewRequest;
    private SerializedProperty tracks;
    private SerializedProperty mixerGroup;
    private SerializedProperty volume;
    private SerializedProperty playbackOrder;
    private SerializedProperty loopPlaylist;
    private SerializedProperty transitionDuration;
    private ReorderableList trackList;

    private void OnEnable()
    {
        tracks = serializedObject.FindProperty("tracks");
        mixerGroup = serializedObject.FindProperty("mixerGroup");
        volume = serializedObject.FindProperty("volume");
        playbackOrder = serializedObject.FindProperty("playbackOrder");
        loopPlaylist = serializedObject.FindProperty("loopPlaylist");
        transitionDuration = serializedObject.FindProperty("transitionDuration");

        trackList = new ReorderableList(serializedObject, tracks, true, true, true, true)
        {
            drawHeaderCallback = DrawTrackHeader,
            drawElementCallback = DrawTrack,
            elementHeight = EditorGUIUtility.singleLineHeight + 4f
        };
    }

    private void OnDisable()
    {
        previewRequest.Release();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(mixerGroup);
        EditorGUILayout.Slider(volume, 0f, 1f);
        EditorGUILayout.PropertyField(playbackOrder);
        EditorGUILayout.PropertyField(loopPlaylist);
        EditorGUILayout.PropertyField(transitionDuration);

        EditorGUILayout.Space(8f);
        trackList.DoLayoutList();

        if (tracks.arraySize == 0)
        {
            EditorGUILayout.HelpBox("Add at least one AudioClip.", MessageType.Warning);
        }
        else if (HasMissingTrack())
        {
            EditorGUILayout.HelpBox("Missing track entries are ignored during playback.", MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || MusicManager.Instance == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Play Test Request"))
            {
                previewRequest.Release();
                previewRequest = MusicManager.Instance.Request(
                    (MusicPlaylist)target,
                    int.MaxValue,
                    -1f,
                    MusicInterruptionMode.SuspendPrevious);
            }

            using (new EditorGUI.DisabledScope(!previewRequest.IsValid))
            {
                if (GUILayout.Button("Release Test Request"))
                {
                    previewRequest.Release();
                    previewRequest = default;
                }
            }
        }
    }

    private void DrawTrack(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < 0 || index >= tracks.arraySize)
        {
            return;
        }

        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(
            rect,
            tracks.GetArrayElementAtIndex(index),
            new GUIContent($"Track {index + 1}"));
    }

    private void DrawTrackHeader(Rect rect)
    {
        var currentEvent = Event.current;
        var acceptsDrop = rect.Contains(currentEvent.mousePosition) && HasDraggedAudioClips();
        if (acceptsDrop && currentEvent.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 0.9f, 0.2f));
        }

        EditorGUI.LabelField(rect, "Tracks — drop AudioClips here");
        if (!acceptsDrop)
        {
            return;
        }

        if (currentEvent.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            currentEvent.Use();
            return;
        }

        if (currentEvent.type != EventType.DragPerform)
        {
            return;
        }

        DragAndDrop.AcceptDrag();
        var draggedObjects = DragAndDrop.objectReferences;
        for (var i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is not AudioClip clip)
            {
                continue;
            }

            var newIndex = tracks.arraySize;
            tracks.InsertArrayElementAtIndex(newIndex);
            tracks.GetArrayElementAtIndex(newIndex).objectReferenceValue = clip;
        }

        GUI.changed = true;
        currentEvent.Use();
    }

    private static bool HasDraggedAudioClips()
    {
        var draggedObjects = DragAndDrop.objectReferences;
        for (var i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is AudioClip)
            {
                return true;
            }
        }

        return false;
    }

    private bool HasMissingTrack()
    {
        for (var i = 0; i < tracks.arraySize; i++)
        {
            if (tracks.GetArrayElementAtIndex(i).objectReferenceValue == null)
            {
                return true;
            }
        }

        return false;
    }
}
}
