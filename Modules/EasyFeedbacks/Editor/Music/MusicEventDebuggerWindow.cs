using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

public sealed class MusicEventDebuggerWindow : EditorWindow
{
    [SerializeField]
    private MusicEventCatalog catalogFilter;

    private readonly List<MusicEventRegistryEntry> registryEntries =
        new List<MusicEventRegistryEntry>();
    private readonly Dictionary<string, MusicRequestHandle> debugRequests =
        new Dictionary<string, MusicRequestHandle>(System.StringComparer.Ordinal);
    private readonly HashSet<string> visibleEventIds =
        new HashSet<string>(System.StringComparer.Ordinal);
    private readonly List<string> orphanedEventIds = new List<string>();

    private Vector2 scrollPosition;

    [MenuItem("Tools/EasyGameFramework/Feedbacks/Music Event Debugger")]
    public static void OpenWindow()
    {
        Open(null);
    }

    public static void Open(MusicEventCatalog selectedCatalog)
    {
        var window = GetWindow<MusicEventDebuggerWindow>();
        window.titleContent = new GUIContent("Music Events");
        window.minSize = new Vector2(500f, 300f);

        if (window.catalogFilter != selectedCatalog)
        {
            window.ReleaseDebugRequests();
            window.catalogFilter = selectedCatalog;
        }

        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        ReleaseDebugRequests();
    }

    private void OnInspectorUpdate()
    {
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        var selectedCatalog = (MusicEventCatalog)EditorGUILayout.ObjectField(
            new GUIContent("Catalog Filter", "Leave empty to show every catalog registered by MusicManager."),
            catalogFilter,
            typeof(MusicEventCatalog),
            false);
        if (EditorGUI.EndChangeCheck())
        {
            ReleaseDebugRequests();
            catalogFilter = selectedCatalog;
        }

        var manager = FindMusicManager();
        if (manager == null)
        {
            ReleaseDebugRequests();
            EditorGUILayout.HelpBox(
                Application.isPlaying
                    ? "No active MusicManager was found."
                    : "Open a scene containing a MusicManager to inspect its unified event registry.",
                Application.isPlaying ? MessageType.Error : MessageType.Info);
            return;
        }

        manager.GetEventRegistryEntries(registryEntries);
        PruneDebugRequests();

        if (catalogFilter != null && !manager.IsEventCatalogRegistered(catalogFilter))
        {
            EditorGUILayout.HelpBox(
                $"Catalog '{catalogFilter.name}' is not assigned to this MusicManager.",
                MessageType.Error);
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "The unified registry is available below. Enter Play Mode to request events.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Release All Debug Events"))
            {
                ReleaseDebugRequests();
            }
        }

        EditorGUILayout.Space(4f);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        var visibleCount = 0;
        for (var i = 0; i < registryEntries.Count; i++)
        {
            var entry = registryEntries[i];
            if (catalogFilter != null && entry.Catalog != catalogFilter)
            {
                continue;
            }

            visibleCount++;
            DrawEvent(manager, entry);
        }

        if (visibleCount == 0)
        {
            EditorGUILayout.HelpBox(
                "No Music Events match the current catalog filter.",
                MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawEvent(MusicManager manager, MusicEventRegistryEntry entry)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var eventId = entry.EventId;
            var handle = default(MusicRequestHandle);
            var hasRequest = !string.IsNullOrEmpty(eventId)
                             && debugRequests.TryGetValue(eventId, out handle)
                             && handle.IsValid;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrWhiteSpace(eventId)
                        ? $"Event {entry.DefinitionIndex + 1}"
                        : eventId,
                    EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    entry.IsRegistered
                        ? hasRequest ? "REQUESTED" : "IDLE"
                        : "INVALID",
                    EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(!Application.isPlaying || !entry.IsRegistered))
                {
                    if (GUILayout.Button(hasRequest ? "Restart" : "Request", GUILayout.Width(70f)))
                    {
                        if (hasRequest)
                        {
                            handle.Release();
                            debugRequests.Remove(eventId);
                        }

                        var replacement = manager.RequestEvent(eventId);
                        if (replacement.IsValid)
                        {
                            debugRequests[eventId] = replacement;
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!hasRequest))
                {
                    if (GUILayout.Button("Release", GUILayout.Width(64f)))
                    {
                        handle.Release();
                        debugRequests.Remove(eventId);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    "Source Catalog",
                    entry.Catalog,
                    typeof(MusicEventCatalog),
                    false);

                if (entry.Definition != null)
                {
                    EditorGUILayout.ObjectField(
                        "Playlist",
                        entry.Definition.Playlist,
                        typeof(MusicPlaylist),
                        false);
                    EditorGUILayout.IntField("Priority", entry.Definition.Priority);
                    EditorGUILayout.EnumPopup("Interruption", entry.Definition.InterruptionMode);
                    EditorGUILayout.TextField(
                        "Transition",
                        entry.Definition.TransitionDuration < 0f
                            ? "Playlist Default"
                            : $"{entry.Definition.TransitionDuration:0.###} s");
                }
            }

            if (!entry.IsRegistered)
            {
                EditorGUILayout.HelpBox(
                    ObjectNames.NicifyVariableName(entry.Issues.ToString()),
                    MessageType.Error);
            }
        }
    }

    private void PruneDebugRequests()
    {
        visibleEventIds.Clear();
        for (var i = 0; i < registryEntries.Count; i++)
        {
            var entry = registryEntries[i];
            if (entry.IsRegistered
                && (catalogFilter == null || entry.Catalog == catalogFilter))
            {
                visibleEventIds.Add(entry.EventId);
            }
        }

        orphanedEventIds.Clear();
        foreach (var pair in debugRequests)
        {
            if (!pair.Value.IsValid || !visibleEventIds.Contains(pair.Key))
            {
                orphanedEventIds.Add(pair.Key);
            }
        }

        for (var i = 0; i < orphanedEventIds.Count; i++)
        {
            var eventId = orphanedEventIds[i];
            if (debugRequests.TryGetValue(eventId, out var request))
            {
                request.Release();
                debugRequests.Remove(eventId);
            }
        }
    }

    private void ReleaseDebugRequests()
    {
        foreach (var request in debugRequests.Values)
        {
            request.Release();
        }

        debugRequests.Clear();
    }

    private static MusicManager FindMusicManager()
    {
        if (Application.isPlaying)
        {
            return MusicManager.Instance;
        }

        var managers = Object.FindObjectsByType<MusicManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        return managers.Length > 0 ? managers[0] : null;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            ReleaseDebugRequests();
        }

        Repaint();
    }
}
}
