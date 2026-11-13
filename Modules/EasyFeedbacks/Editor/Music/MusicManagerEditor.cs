using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(MusicManager))]
public sealed class MusicManagerEditor : UnityEditor.Editor
{
    private readonly List<MusicEventRegistryEntry> eventRegistryEntries =
        new List<MusicEventRegistryEntry>();
    private readonly HashSet<MusicEventCatalog> seenCatalogs =
        new HashSet<MusicEventCatalog>();

    private SerializedProperty fallbackPlaylist;
    private SerializedProperty eventCatalogs;
    private SerializedProperty volume;
    private ReorderableList eventCatalogList;
    private bool showResolvedEvents;

    private void OnEnable()
    {
        fallbackPlaylist = serializedObject.FindProperty("fallbackPlaylist");
        eventCatalogs = serializedObject.FindProperty("eventCatalogs");
        volume = serializedObject.FindProperty("volume");

        eventCatalogList = new ReorderableList(
            serializedObject,
            eventCatalogs,
            true,
            true,
            true,
            true)
        {
            drawHeaderCallback = DrawCatalogHeader,
            drawElementCallback = DrawCatalog,
            elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            onAddCallback = AddCatalog,
            onRemoveCallback = list => ReorderableList.defaultBehaviours.DoRemoveButton(list)
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Music Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fallbackPlaylist);
        EditorGUILayout.Slider(volume, 0f, 1f);

        EditorGUILayout.Space(8f);
        eventCatalogList.DoLayoutList();
        EditorGUILayout.HelpBox(
            "Every assigned catalog is merged into one case-sensitive Event ID registry. Gameplay code requests events through MusicManager.RequestEvent(). IDs must be unique across all catalogs. Audio is prepared lazily unless its source catalog enables Preload On Start.",
            MessageType.Info);

        var manager = (MusicManager)target;
        if (manager.transform.parent != null)
        {
            EditorGUILayout.HelpBox(
                "Place MusicManager on a root GameObject. It persists between scenes.",
                MessageType.Warning);
        }

        if (!HasAudioManager())
        {
            EditorGUILayout.HelpBox(
                "MusicManager delegates track playback to AudioManager. Add an AudioManager to the bootstrap scene.",
                MessageType.Warning);
        }

        EditorGUILayout.HelpBox(
            "Selection order: Event Requests > Music Zones > Scene Music Manager > Fallback. Suspend Previous events preserve lower playback; release every MusicRequestHandle when its event ends.",
            MessageType.Info);

        serializedObject.ApplyModifiedProperties();

        manager.GetEventRegistryEntries(eventRegistryEntries);
        DrawRegistryValidation(manager);
        DrawResolvedEvents(manager);
        DrawRuntimeState(manager);
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    private void DrawCatalogHeader(Rect rect)
    {
        var currentEvent = Event.current;
        var acceptsDrop = rect.Contains(currentEvent.mousePosition) && HasDraggedCatalogs();
        if (acceptsDrop && currentEvent.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.55f, 0.9f, 0.2f));
        }

        EditorGUI.LabelField(rect, "Music Event Catalogs — drop catalogs here");
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
            if (draggedObjects[i] is not MusicEventCatalog catalog
                || ContainsCatalog(catalog))
            {
                continue;
            }

            eventCatalogs.arraySize++;
            eventCatalogs
                .GetArrayElementAtIndex(eventCatalogs.arraySize - 1)
                .objectReferenceValue = catalog;
        }

        GUI.changed = true;
        currentEvent.Use();
    }

    private void DrawCatalog(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (index < 0 || index >= eventCatalogs.arraySize)
        {
            return;
        }

        rect.y += 2f;
        rect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(
            rect,
            eventCatalogs.GetArrayElementAtIndex(index),
            GUIContent.none);
    }

    private void AddCatalog(ReorderableList list)
    {
        var index = eventCatalogs.arraySize;
        eventCatalogs.arraySize++;
        eventCatalogs.GetArrayElementAtIndex(index).objectReferenceValue = null;
        list.index = index;
    }

    private void DrawRegistryValidation(MusicManager manager)
    {
        var missingCatalogCount = 0;
        var duplicateCatalogCount = 0;
        seenCatalogs.Clear();
        var catalogs = manager.EventCatalogs;
        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];
            if (catalog == null)
            {
                missingCatalogCount++;
            }
            else if (!seenCatalogs.Add(catalog))
            {
                duplicateCatalogCount++;
            }
        }

        if (missingCatalogCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"Remove {missingCatalogCount} missing catalog reference(s).",
                MessageType.Error);
        }

        if (duplicateCatalogCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"Remove {duplicateCatalogCount} repeated catalog reference(s). Each catalog is processed once.",
                MessageType.Error);
        }

        var invalidEventCount = 0;
        for (var i = 0; i < eventRegistryEntries.Count; i++)
        {
            if (!eventRegistryEntries[i].IsRegistered)
            {
                invalidEventCount++;
            }
        }

        if (invalidEventCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"The unified registry contains {invalidEventCount} invalid event definition(s). Expand Resolved Music Events for their exact locations.",
                MessageType.Error);
        }
    }

    private void DrawResolvedEvents(MusicManager manager)
    {
        EditorGUILayout.Space(8f);
        showResolvedEvents = EditorGUILayout.Foldout(
            showResolvedEvents,
            $"Resolved Music Events ({manager.RegisteredEventCount} valid, "
            + $"{manager.EventRegistryIssueCount - manager.EventCatalogIssueCount} invalid, "
            + $"{manager.EventCatalogIssueCount} catalog issues)",
            true,
            EditorStyles.foldoutHeader);

        if (!showResolvedEvents)
        {
            return;
        }

        if (eventRegistryEntries.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Assign at least one catalog containing a valid Music Event.",
                MessageType.Info);
            return;
        }

        for (var i = 0; i < eventRegistryEntries.Count; i++)
        {
            DrawRegistryEntry(eventRegistryEntries[i]);
        }
    }

    private static void DrawRegistryEntry(MusicEventRegistryEntry entry)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var eventLabel = string.IsNullOrWhiteSpace(entry.EventId)
                    ? $"Event {entry.DefinitionIndex + 1}"
                    : entry.EventId;
                EditorGUILayout.LabelField(eventLabel, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                var previousColor = GUI.color;
                GUI.color = entry.IsRegistered
                    ? new Color(0.72f, 1f, 0.72f)
                    : new Color(1f, 0.72f, 0.72f);
                GUILayout.Label(entry.IsRegistered ? "REGISTERED" : "INVALID", EditorStyles.miniLabel);
                GUI.color = previousColor;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Source Catalog",
                    entry.Catalog,
                    typeof(MusicEventCatalog),
                    false);
                EditorGUILayout.Toggle(
                    "Preload On Start",
                    entry.Catalog != null && entry.Catalog.PreloadOnStart);

                if (entry.Definition != null)
                {
                    EditorGUILayout.ObjectField(
                        "Playlist",
                        entry.Definition.Playlist,
                        typeof(MusicPlaylist),
                        false);
                    EditorGUILayout.IntField("Priority", entry.Definition.Priority);
                    EditorGUILayout.EnumPopup("Interruption", entry.Definition.InterruptionMode);
                }
            }

            if (!entry.IsRegistered)
            {
                EditorGUILayout.HelpBox(
                    GetRegistryIssueMessage(entry),
                    MessageType.Error);
            }
        }
    }

    private void DrawRuntimeState(MusicManager manager)
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Current Playlist", manager.CurrentPlaylist, typeof(MusicPlaylist), false);
            EditorGUILayout.ObjectField("Current Track", manager.CurrentTrack, typeof(AudioClip), false);
            EditorGUILayout.EnumPopup("Current Layer", manager.CurrentLayer);
            if (manager.CurrentPlaylist != null)
            {
                EditorGUILayout.EnumPopup("Current Interruption", manager.CurrentInterruptionMode);
            }
            else
            {
                EditorGUILayout.LabelField("Current Interruption", "None");
            }

            EditorGUILayout.IntField(
                "Suspended Playbacks",
                Application.isPlaying ? manager.SuspendedPlaybackCount : 0);
            EditorGUILayout.IntField("Registered Catalogs", manager.RegisteredEventCatalogCount);
            EditorGUILayout.IntField("Registered Events", manager.RegisteredEventCount);
            EditorGUILayout.IntField("Registry Issues", manager.EventRegistryIssueCount);
            EditorGUILayout.IntField("Active Requests", Application.isPlaying ? manager.ActiveRequestCount : 0);
            EditorGUILayout.IntField("Active Zones", Application.isPlaying ? manager.ActiveZoneCount : 0);
            EditorGUILayout.IntField("Scene Music Managers", Application.isPlaying ? manager.SceneMusicManagerCount : 0);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying || manager.ActiveRequestCount == 0))
        {
            if (GUILayout.Button("Release All Event Requests"))
            {
                manager.ReleaseAllRequests();
            }
        }
    }

    private bool ContainsCatalog(MusicEventCatalog catalog)
    {
        for (var i = 0; i < eventCatalogs.arraySize; i++)
        {
            if (eventCatalogs.GetArrayElementAtIndex(i).objectReferenceValue == catalog)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDraggedCatalogs()
    {
        var draggedObjects = DragAndDrop.objectReferences;
        for (var i = 0; i < draggedObjects.Length; i++)
        {
            if (draggedObjects[i] is MusicEventCatalog)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetRegistryIssueMessage(MusicEventRegistryEntry entry)
    {
        var message = $"{entry.Catalog.name}, event {entry.DefinitionIndex + 1}: ";
        var separator = string.Empty;

        AppendIssue(ref message, ref separator, entry.Issues, MusicEventRegistryIssues.MissingDefinition, "missing definition");
        AppendIssue(ref message, ref separator, entry.Issues, MusicEventRegistryIssues.MissingEventId, "empty Event ID");
        AppendIssue(ref message, ref separator, entry.Issues, MusicEventRegistryIssues.SurroundingWhitespace, "Event ID has leading or trailing whitespace");
        AppendIssue(ref message, ref separator, entry.Issues, MusicEventRegistryIssues.MissingPlaylist, "missing playlist");
        AppendIssue(ref message, ref separator, entry.Issues, MusicEventRegistryIssues.DuplicateEventId, $"Event ID '{entry.EventId}' is duplicated");

        return message + ".";
    }

    private static void AppendIssue(
        ref string message,
        ref string separator,
        MusicEventRegistryIssues allIssues,
        MusicEventRegistryIssues issue,
        string text)
    {
        if ((allIssues & issue) == 0)
        {
            return;
        }

        message += separator + text;
        separator = ", ";
    }

    private static bool HasAudioManager()
    {
        return Object.FindObjectsByType<AudioManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length > 0;
    }
}
}
