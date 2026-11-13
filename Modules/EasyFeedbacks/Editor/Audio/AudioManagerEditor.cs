using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace EasyFeedbacks.Editor
{

[CustomEditor(typeof(AudioManager))]
public sealed class AudioManagerEditor : UnityEditor.Editor
{
    private readonly List<MusicDebugEntry> musicDebugEntries = new List<MusicDebugEntry>();
    private readonly HashSet<AudioMixerGroup> validGlobalPauseGroups = new HashSet<AudioMixerGroup>();
    private readonly HashSet<AudioMixerGroup> seenGlobalPauseGroups = new HashSet<AudioMixerGroup>();

    private SerializedProperty initialCapacity;
    private SerializedProperty maximumCapacity;
    private SerializedProperty audioMixer;
    private SerializedProperty globalPauseExcludedGroups;
    private bool showMixerChannels = true;
    private bool showMusicRegistry = true;

    private void OnEnable()
    {
        initialCapacity = serializedObject.FindProperty("initialCapacity");
        maximumCapacity = serializedObject.FindProperty("maximumCapacity");
        audioMixer = serializedObject.FindProperty("audioMixer");
        globalPauseExcludedGroups = serializedObject.FindProperty("globalPauseExcludedGroups");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Pool Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(initialCapacity);
        EditorGUILayout.PropertyField(maximumCapacity);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Mixer Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(audioMixer, new GUIContent("Audio Mixer"));
        if (EditorGUI.EndChangeCheck()
            && audioMixer.objectReferenceValue == null
            && globalPauseExcludedGroups != null)
        {
            globalPauseExcludedGroups.arraySize = 0;
        }

        if (initialCapacity.intValue > maximumCapacity.intValue)
        {
            EditorGUILayout.HelpBox("Initial Capacity cannot exceed Maximum Capacity.", MessageType.Error);
        }

        if (maximumCapacity.intValue < 1)
        {
            EditorGUILayout.HelpBox("Maximum Capacity must be at least 1.", MessageType.Error);
        }

        if (((AudioManager)target).transform.parent != null)
        {
            EditorGUILayout.HelpBox(
                "Place AudioManager on a root GameObject. It is moved to the scene root at runtime so it can persist between scenes.",
                MessageType.Warning);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Runtime State", EditorStyles.boldLabel);

        var manager = (AudioManager)target;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Active Voices", Application.isPlaying ? manager.ActiveVoices : 0);
            EditorGUILayout.IntField("Available Voices", Application.isPlaying ? manager.AvailableVoices : 0);
            EditorGUILayout.IntField("Total Voices", Application.isPlaying ? manager.TotalVoices : 0);
            EditorGUILayout.IntField("Maximum Capacity", manager.MaximumCapacity);
            EditorGUILayout.Toggle("Paused by Feedback Service", Application.isPlaying && FeedbackService.AreAllPaused);
        }

        DrawMixerChannelControls(manager);
        DrawMusicControls();
        DrawMusicRegistry();
    }

    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }

    private void DrawMixerChannelControls(AudioManager manager)
    {
        EditorGUILayout.Space(10f);
        showMixerChannels = EditorGUILayout.Foldout(
            showMixerChannels,
            "Mixer Channel Controls",
            true,
            EditorStyles.foldoutHeader);

        if (!showMixerChannels)
        {
            return;
        }

        var mixer = audioMixer.objectReferenceValue as AudioMixer;
        if (mixer == null)
        {
            EditorGUILayout.HelpBox(
                "Assign an Audio Mixer to automatically expose its Master and child groups.",
                MessageType.Info);
            return;
        }

        var groups = mixer.FindMatchingGroups(string.Empty);
        if (groups == null || groups.Length == 0)
        {
            EditorGUILayout.HelpBox("The selected Audio Mixer has no groups.", MessageType.Warning);
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Channel controls are available in Play Mode.", MessageType.Info);
        }

        var masterGroup = FindMasterGroup(mixer, groups);
        SanitizeGlobalPauseExclusions(groups, masterGroup);
        DrawMixerChannel(manager, masterGroup, true);

        for (var i = 0; i < groups.Length; i++)
        {
            if (groups[i] != null && groups[i] != masterGroup)
            {
                DrawMixerChannel(manager, groups[i], false);
            }
        }
    }

    private void DrawMixerChannel(
        AudioManager manager,
        AudioMixerGroup group,
        bool isMaster)
    {
        if (group == null)
        {
            return;
        }

        var activeVoices = Application.isPlaying
            ? isMaster ? manager.ActiveVoices : manager.GetActiveVoiceCount(group)
            : 0;
        var pausedByMaster = Application.isPlaying && !isMaster && manager.AreAllChannelsPaused;
        var paused = Application.isPlaying
            && (isMaster ? manager.AreAllChannelsPaused : manager.IsMixerGroupPaused(group));
        var globalPauseActive = Application.isPlaying && FeedbackService.AreAllPaused;
        var ignoresGlobalPause = !isMaster && ContainsGroup(globalPauseExcludedGroups, group);
        var status = GetChannelStatus(
            isMaster,
            paused,
            pausedByMaster,
            globalPauseActive,
            ignoresGlobalPause);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    isMaster ? $"{group.name} (All Managed Audio)" : group.name,
                    EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    status,
                    EditorStyles.miniLabel,
                    GUILayout.Width(135f));
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Mixer Group", group, typeof(AudioMixerGroup), false);
                EditorGUILayout.IntField("Active Voices", activeVoices);
            }

            var pauseTooltip = isMaster
                ? "Master cannot be excluded. Route pause-menu audio to a dedicated mixer group such as UI."
                : "Keeps audio routed directly to this mixer group active during FeedbackService.PauseAll(). Handle, channel and AudioManager pauses, StopAll, and AudioListener.pause still apply.";
            var pauseLabel = new GUIContent("Ignore Global Pause", pauseTooltip);
            if (isMaster)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle(pauseLabel, false);
                }
            }
            else
            {
                var pauseRect = EditorGUILayout.GetControlRect();
                EditorGUI.BeginProperty(pauseRect, pauseLabel, globalPauseExcludedGroups);
                EditorGUI.BeginChangeCheck();
                var updatedIgnoreState = EditorGUI.Toggle(pauseRect, pauseLabel, ignoresGlobalPause);
                var pauseStateChanged = EditorGUI.EndChangeCheck();
                EditorGUI.EndProperty();

                if (pauseStateChanged)
                {
                    SetGroupMembership(globalPauseExcludedGroups, group, updatedIgnoreState);
                    serializedObject.ApplyModifiedProperties();
                }
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(paused || pausedByMaster))
                {
                    if (GUILayout.Button("Pause"))
                    {
                        if (isMaster)
                        {
                            manager.PauseAll();
                        }
                        else
                        {
                            manager.Pause(group);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(!paused || pausedByMaster))
                {
                    if (GUILayout.Button("Resume"))
                    {
                        if (isMaster)
                        {
                            manager.ResumeAll();
                        }
                        else
                        {
                            manager.Resume(group);
                        }
                    }
                }

                using (new EditorGUI.DisabledScope(activeVoices == 0))
                {
                    if (GUILayout.Button("Stop"))
                    {
                        if (isMaster)
                        {
                            manager.StopAll();
                        }
                        else
                        {
                            manager.Stop(group);
                        }
                    }
                }
            }
        }
    }

    private void SanitizeGlobalPauseExclusions(
        AudioMixerGroup[] groups,
        AudioMixerGroup masterGroup)
    {
        if (globalPauseExcludedGroups == null || !globalPauseExcludedGroups.isArray)
        {
            return;
        }

        validGlobalPauseGroups.Clear();
        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (group != null && group != masterGroup)
            {
                validGlobalPauseGroups.Add(group);
            }
        }

        seenGlobalPauseGroups.Clear();
        var changed = false;
        for (var i = globalPauseExcludedGroups.arraySize - 1; i >= 0; i--)
        {
            var group = globalPauseExcludedGroups
                .GetArrayElementAtIndex(i)
                .objectReferenceValue as AudioMixerGroup;
            if (group == null
                || !validGlobalPauseGroups.Contains(group)
                || !seenGlobalPauseGroups.Add(group))
            {
                DeleteArrayElement(globalPauseExcludedGroups, i);
                changed = true;
            }
        }

        if (changed)
        {
            serializedObject.ApplyModifiedProperties();
        }
    }

    private static bool ContainsGroup(SerializedProperty groups, AudioMixerGroup targetGroup)
    {
        if (groups == null || !groups.isArray || targetGroup == null)
        {
            return false;
        }

        for (var i = 0; i < groups.arraySize; i++)
        {
            if (groups.GetArrayElementAtIndex(i).objectReferenceValue == targetGroup)
            {
                return true;
            }
        }

        return false;
    }

    private static void SetGroupMembership(
        SerializedProperty groups,
        AudioMixerGroup targetGroup,
        bool included)
    {
        if (groups == null || !groups.isArray || targetGroup == null)
        {
            return;
        }

        if (included)
        {
            if (ContainsGroup(groups, targetGroup))
            {
                return;
            }

            groups.arraySize++;
            groups.GetArrayElementAtIndex(groups.arraySize - 1).objectReferenceValue = targetGroup;
            return;
        }

        for (var i = groups.arraySize - 1; i >= 0; i--)
        {
            if (groups.GetArrayElementAtIndex(i).objectReferenceValue == targetGroup)
            {
                DeleteArrayElement(groups, i);
            }
        }
    }

    private static void DeleteArrayElement(SerializedProperty array, int index)
    {
        var previousSize = array.arraySize;
        array.DeleteArrayElementAtIndex(index);
        if (array.arraySize == previousSize)
        {
            array.DeleteArrayElementAtIndex(index);
        }
    }

    private static string GetChannelStatus(
        bool isMaster,
        bool paused,
        bool pausedByMaster,
        bool globalPauseActive,
        bool ignoresGlobalPause)
    {
        if (pausedByMaster)
        {
            return "Paused by Master";
        }

        if (paused)
        {
            return "Paused";
        }

        if (!globalPauseActive)
        {
            return "Running";
        }

        if (isMaster)
        {
            return "Global Pause Active";
        }

        return ignoresGlobalPause ? "Global Pause Ignored" : "Paused by Global";
    }

    private static AudioMixerGroup FindMasterGroup(
        AudioMixer mixer,
        AudioMixerGroup[] groups)
    {
        var mixerObject = new SerializedObject(mixer);
        var masterProperty = mixerObject.FindProperty("m_MasterGroup");
        var masterGroup = masterProperty?.objectReferenceValue as AudioMixerGroup;
        return masterGroup != null ? masterGroup : groups[0];
    }

    private static void DrawMusicControls()
    {
        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("Music Controls", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Music controls are available in Play Mode.", MessageType.Info);
            return;
        }

        var musicManager = MusicManager.Instance;
        if (musicManager == null)
        {
            EditorGUILayout.HelpBox("No active MusicManager was found.", MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("State", GetMusicStateLabel(musicManager));
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(
                       musicManager.IsMusicPaused
                       || musicManager.IsMusicStopped
                       || musicManager.CurrentTrack == null))
            {
                if (GUILayout.Button("Pause"))
                {
                    musicManager.PauseMusic();
                }
            }

            using (new EditorGUI.DisabledScope(
                       !musicManager.IsMusicPaused && !musicManager.IsMusicStopped))
            {
                if (GUILayout.Button("Resume"))
                {
                    musicManager.ResumeMusic();
                }
            }

            using (new EditorGUI.DisabledScope(
                       musicManager.IsMusicStopped && musicManager.CurrentTrack == null))
            {
                if (GUILayout.Button("Stop"))
                {
                    musicManager.StopMusic();
                }
            }

            using (new EditorGUI.DisabledScope(
                       musicManager.IsMusicStopped || musicManager.CurrentTrack == null))
            {
                if (GUILayout.Button("Next Track"))
                {
                    musicManager.NextTrack();
                }
            }
        }
    }

    private void DrawMusicRegistry()
    {
        EditorGUILayout.Space(10f);
        showMusicRegistry = EditorGUILayout.Foldout(
            showMusicRegistry,
            "Music Registry",
            true,
            EditorStyles.foldoutHeader);

        if (!showMusicRegistry)
        {
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter Play Mode to inspect selected, suspended, waiting, and inactive music sources.",
                MessageType.Info);
            return;
        }

        var musicManager = MusicManager.Instance;
        if (musicManager == null)
        {
            EditorGUILayout.HelpBox("No active MusicManager was found.", MessageType.Warning);
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Current Playlist",
                musicManager.CurrentPlaylist,
                typeof(MusicPlaylist),
                false);
            EditorGUILayout.ObjectField(
                "Current Track",
                musicManager.CurrentTrack,
                typeof(AudioClip),
                false);
            EditorGUILayout.EnumPopup("Current Layer", musicManager.CurrentLayer);
            if (musicManager.CurrentPlaylist != null)
            {
                EditorGUILayout.EnumPopup("Current Interruption", musicManager.CurrentInterruptionMode);
            }
            else
            {
                EditorGUILayout.LabelField("Current Interruption", "None");
            }
            EditorGUILayout.IntField("Suspended Playbacks", musicManager.SuspendedPlaybackCount);
            EditorGUILayout.IntField("Music Event Catalogs", musicManager.RegisteredEventCatalogCount);
            EditorGUILayout.IntField("Registered Music Events", musicManager.RegisteredEventCount);
            EditorGUILayout.IntField("Music Event Issues", musicManager.EventRegistryIssueCount);
        }

        musicManager.GetDebugEntries(musicDebugEntries);
        musicDebugEntries.Sort(CompareDebugEntries);

        if (musicDebugEntries.Count == 0)
        {
            EditorGUILayout.HelpBox("No music playlists are currently registered.", MessageType.Info);
            return;
        }

        for (var i = 0; i < musicDebugEntries.Count; i++)
        {
            DrawMusicDebugEntry(musicDebugEntries[i]);
        }
    }

    private static void DrawMusicDebugEntry(MusicDebugEntry entry)
    {
        var previousColor = GUI.color;
        GUI.color = entry.Status == MusicDebugStatus.Selected
            ? new Color(0.72f, 1f, 0.72f)
            : entry.Status == MusicDebugStatus.Suspended
                ? new Color(0.72f, 0.88f, 1f)
            : entry.Status == MusicDebugStatus.Inactive
                ? new Color(0.72f, 0.72f, 0.72f)
                : Color.white;

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(
                $"[{GetStatusLabel(entry.Status)}] {entry.Layer}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Context", entry.Context);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Playlist", entry.Playlist, typeof(MusicPlaylist), false);
            }

            if (entry.Layer != MusicSelectionLayer.Fallback)
            {
                EditorGUILayout.IntField("Priority", entry.Priority);
            }

            if (entry.Layer == MusicSelectionLayer.Event)
            {
                EditorGUILayout.EnumPopup("Interruption", entry.InterruptionMode);
            }
        }

        GUI.color = previousColor;
    }

    private static int CompareDebugEntries(MusicDebugEntry left, MusicDebugEntry right)
    {
        var statusComparison = left.Status.CompareTo(right.Status);
        if (statusComparison != 0)
        {
            return statusComparison;
        }

        var layerComparison = right.Layer.CompareTo(left.Layer);
        if (layerComparison != 0)
        {
            return layerComparison;
        }

        return right.Priority.CompareTo(left.Priority);
    }

    private static string GetStatusLabel(MusicDebugStatus status)
    {
        return status == MusicDebugStatus.Selected
            ? "SELECTED"
            : status == MusicDebugStatus.Suspended
                ? "SUSPENDED"
            : status == MusicDebugStatus.Waiting
                ? "WAITING"
                : "INACTIVE";
    }

    private static string GetMusicStateLabel(MusicManager musicManager)
    {
        if (musicManager.IsMusicStopped)
        {
            return "Stopped";
        }

        if (musicManager.IsMusicPaused)
        {
            return "Paused";
        }

        return musicManager.CurrentTrack != null ? "Playing" : "Idle";
    }
}
}
