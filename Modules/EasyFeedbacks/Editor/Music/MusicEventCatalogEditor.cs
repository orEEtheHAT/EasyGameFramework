using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(MusicEventCatalog))]
public sealed class MusicEventCatalogEditor : UnityEditor.Editor
{
    private const float Spacing = 2f;

    private SerializedProperty preloadOnStart;
    private SerializedProperty events;
    private ReorderableList eventList;

    private void OnEnable()
    {
        preloadOnStart = serializedObject.FindProperty("preloadOnStart");
        events = serializedObject.FindProperty("events");
        eventList = new ReorderableList(serializedObject, events, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Music Events"),
            drawElementCallback = DrawElement,
            elementHeightCallback = GetElementHeight,
            onAddCallback = AddEvent,
            onRemoveCallback = list => ReorderableList.defaultBehaviours.DoRemoveButton(list)
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "Event IDs are used by gameplay code through MusicManager.RequestEvent(). Replace discards lower music; Suspend Previous preserves lower playback. IDs must also be unique across every catalog assigned to the MusicManager.",
            MessageType.Info);

        EditorGUILayout.PropertyField(
            preloadOnStart,
            new GUIContent(
                "Preload On Start",
                "Prepare this catalog's valid playlists when MusicManager starts. Leave disabled to prepare each playlist lazily on its first request."));
        EditorGUILayout.HelpBox(
            "Leave disabled for large or optional catalogs. Enable only when their first request must have the lowest possible startup latency.",
            MessageType.None);
        EditorGUILayout.Space(4f);

        eventList.DoLayoutList();
        DrawValidation();

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        if (GUILayout.Button("Open Music Event Debugger"))
        {
            MusicEventDebuggerWindow.Open((MusicEventCatalog)target);
        }
    }

    private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < 0 || index >= events.arraySize)
        {
            return;
        }

        var element = events.GetArrayElementAtIndex(index);
        var eventId = element.FindPropertyRelative("eventId");
        var playlist = element.FindPropertyRelative("playlist");
        var priority = element.FindPropertyRelative("priority");
        var interruptionMode = element.FindPropertyRelative("interruptionMode");
        var transitionDuration = element.FindPropertyRelative("transitionDuration");
        var line = EditorGUIUtility.singleLineHeight;
        var y = rect.y + Spacing;
        var eventName = string.IsNullOrWhiteSpace(eventId.stringValue)
            ? $"Event {index + 1}"
            : eventId.stringValue;

        element.isExpanded = EditorGUI.Foldout(
            new Rect(rect.x, y, rect.width, line),
            element.isExpanded,
            $"{index + 1}. {eventName}",
            true);

        if (!element.isExpanded)
        {
            return;
        }

        var contentRect = new Rect(rect.x + 12f, y + line + Spacing, rect.width - 12f, line);
        DrawProperty(ref contentRect, eventId, new GUIContent("Event ID"));
        DrawProperty(ref contentRect, playlist, new GUIContent("Playlist"));
        DrawProperty(ref contentRect, priority, new GUIContent("Priority"));
        DrawProperty(
            ref contentRect,
            interruptionMode,
            new GUIContent(
                "Interruption",
                "Suspend Previous freezes lower music at its exact playback position until this event is released."));
        DrawProperty(
            ref contentRect,
            transitionDuration,
            new GUIContent(
                "Transition Duration",
                "Controls fade-in on selection and, for Suspend Previous, fade-out on release. -1 uses the selected playlist default."));
    }

    private float GetElementHeight(int index)
    {
        var line = EditorGUIUtility.singleLineHeight;
        if (index < 0 || index >= events.arraySize || !events.GetArrayElementAtIndex(index).isExpanded)
        {
            return line + Spacing * 2f;
        }

        return 6f * (line + Spacing) + Spacing;
    }

    private void AddEvent(ReorderableList list)
    {
        serializedObject.Update();
        var index = events.arraySize;
        events.arraySize++;
        var element = events.GetArrayElementAtIndex(index);

        element.FindPropertyRelative("eventId").stringValue = $"New Event {index + 1}";
        element.FindPropertyRelative("playlist").objectReferenceValue = null;
        element.FindPropertyRelative("priority").intValue = 0;
        element.FindPropertyRelative("interruptionMode").enumValueIndex =
            (int)MusicInterruptionMode.Replace;
        element.FindPropertyRelative("transitionDuration").floatValue = -1f;
        element.isExpanded = true;

        serializedObject.ApplyModifiedProperties();
        list.index = index;
    }

    private void DrawValidation()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var hasEmptyId = false;
        var hasWhitespaceId = false;
        var hasDuplicateId = false;
        var hasMissingPlaylist = false;

        for (var i = 0; i < events.arraySize; i++)
        {
            var element = events.GetArrayElementAtIndex(i);
            var id = element.FindPropertyRelative("eventId").stringValue;
            if (string.IsNullOrWhiteSpace(id))
            {
                hasEmptyId = true;
            }
            else if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
            {
                hasWhitespaceId = true;
            }
            else if (!ids.Add(id))
            {
                hasDuplicateId = true;
            }

            if (element.FindPropertyRelative("playlist").objectReferenceValue == null)
            {
                hasMissingPlaylist = true;
            }
        }

        if (hasEmptyId)
        {
            EditorGUILayout.HelpBox("Every Music Event needs a non-empty Event ID.", MessageType.Error);
        }

        if (hasDuplicateId)
        {
            EditorGUILayout.HelpBox("Event IDs must be unique within the catalog.", MessageType.Error);
        }

        if (hasWhitespaceId)
        {
            EditorGUILayout.HelpBox(
                "Event IDs cannot contain leading or trailing whitespace.",
                MessageType.Error);
        }

        if (hasMissingPlaylist)
        {
            EditorGUILayout.HelpBox("Music Events without a playlist cannot be requested.", MessageType.Error);
        }
    }

    private static void DrawProperty(ref Rect rect, SerializedProperty property, GUIContent label)
    {
        EditorGUI.PropertyField(rect, property, label);
        rect.y += EditorGUIUtility.singleLineHeight + Spacing;
    }
}
}
