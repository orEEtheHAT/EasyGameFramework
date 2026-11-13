using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace EasyFeedbacks
{

public enum MusicSelectionLayer
{
    None,
    Fallback,
    Scene,
    Zone,
    Event
}

public enum MusicInterruptionMode
{
    Replace,
    SuspendPrevious
}

/// <summary>
/// Persistent playlist controller. Event requests override zones, zones override
/// local scene music, and the global fallback is used when no other source applies.
/// Suspend Previous requests form a playback chain whose lower states retain their
/// exact AudioSource position and playlist order while a higher state is active.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Music Manager")]
[DisallowMultipleComponent]
public sealed class MusicManager : MonoBehaviour
{
    private const float FailedPlayRetryDelay = 0.5f;

    [SerializeField, Tooltip("Global last-resort playlist used when no event, zone, or Scene Music Manager applies.")]
    private MusicPlaylist fallbackPlaylist;

    [SerializeField, Tooltip("Catalogs merged into one global Event ID registry. Event IDs must be unique across every assigned catalog.")]
    private List<MusicEventCatalog> eventCatalogs = new List<MusicEventCatalog>();

    [SerializeField, Range(0f, 1f), Tooltip("Additional volume multiplier applied to every music playlist.")]
    private float volume = 1f;

    private readonly List<MusicRequest> activeRequests = new List<MusicRequest>();
    private readonly List<MusicZone> activeZones = new List<MusicZone>();
    private readonly List<SceneMusicManager> sceneMusicManagers = new List<SceneMusicManager>();
    private readonly List<MusicRequest> rankedRequests = new List<MusicRequest>();
    private readonly List<MusicSelection> suspendSelectionBuffer = new List<MusicSelection>();
    private readonly List<MusicSelection> desiredPlaybackChain = new List<MusicSelection>();
    private readonly List<MusicPlaybackState> playbackChain = new List<MusicPlaybackState>();
    private readonly List<MusicPlaybackState> nextPlaybackChain = new List<MusicPlaybackState>();
    private readonly Stack<MusicPlaybackState> playbackStatePool = new Stack<MusicPlaybackState>();
    private readonly List<FadingMusicHandle> fadingHandles = new List<FadingMusicHandle>();
    private readonly Dictionary<string, RegisteredMusicEvent> registeredEvents =
        new Dictionary<string, RegisteredMusicEvent>(System.StringComparer.Ordinal);
    private readonly Dictionary<string, int> eventIdCounts =
        new Dictionary<string, int>(System.StringComparer.Ordinal);
    private readonly HashSet<string> ambiguousEventIds =
        new HashSet<string>(System.StringComparer.Ordinal);
    private readonly HashSet<MusicEventCatalog> registeredEventCatalogs =
        new HashSet<MusicEventCatalog>();
    private readonly HashSet<MusicPlaylist> eventPlaylistPreloadBuffer =
        new HashSet<MusicPlaylist>();
    private readonly List<EventRegistryCandidate> eventRegistryCandidates =
        new List<EventRegistryCandidate>();
    private readonly List<MusicEventRegistryEntry> eventRegistryEntries =
        new List<MusicEventRegistryEntry>();

    private bool musicPaused;
    private bool musicStopped;
    private bool eventRegistryBuilt;
    private uint nextRequestId;
    private ulong nextRequestSequence;
    private int reconcileVersion;
    private bool sceneSelectionDirty;
    private int eventRegistryIssueCount;
    private int eventCatalogIssueCount;
    private int observedEventCatalogRevision;
    private int serializedSettingsDirty;
    private ReadOnlyCollection<MusicEventCatalog> readOnlyEventCatalogs;

    public static MusicManager Instance { get; private set; }

    public MusicPlaylist CurrentPlaylist => CurrentPlaybackState?.Selection.Playlist;
    public AudioClip CurrentTrack => CurrentPlaybackState?.CurrentTrack;
    public MusicPlaylist FallbackPlaylist => fallbackPlaylist;
    public IReadOnlyList<MusicEventCatalog> EventCatalogs
    {
        get
        {
            eventCatalogs ??= new List<MusicEventCatalog>();
            return readOnlyEventCatalogs ??= eventCatalogs.AsReadOnly();
        }
    }
    public MusicSelectionLayer CurrentLayer => CurrentPlaybackState?.Selection.Layer
                                               ?? MusicSelectionLayer.None;
    public MusicInterruptionMode CurrentInterruptionMode =>
        CurrentPlaybackState?.Selection.InterruptionMode ?? MusicInterruptionMode.Replace;
    public int SuspendedPlaybackCount => Mathf.Max(0, playbackChain.Count - 1);
    public int ActiveRequestCount => activeRequests.Count;
    public int ActiveZoneCount => activeZones.Count;
    public int SceneMusicManagerCount => sceneMusicManagers.Count;
    public int RegisteredEventCount
    {
        get
        {
            EnsureEventRegistry();
            return registeredEvents.Count;
        }
    }
    public int EventRegistryIssueCount
    {
        get
        {
            EnsureEventRegistry();
            return eventRegistryIssueCount;
        }
    }
    public int RegisteredEventCatalogCount
    {
        get
        {
            EnsureEventRegistry();
            return registeredEventCatalogs.Count;
        }
    }
    public int EventCatalogIssueCount
    {
        get
        {
            EnsureEventRegistry();
            return eventCatalogIssueCount;
        }
    }
    public float Volume => Mathf.Clamp01(volume);
    public bool IsMusicPaused => musicPaused;
    public bool IsMusicStopped => musicStopped;

    private MusicPlaybackState CurrentPlaybackState => playbackChain.Count > 0
        ? playbackChain[playbackChain.Count - 1]
        : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        eventCatalogs ??= new List<MusicEventCatalog>();
        observedEventCatalogRevision = MusicEventCatalog.RegistryRevision;
        Interlocked.Exchange(ref serializedSettingsDirty, 0);
        RebuildEventRegistry(true);
        PreloadRegisteredEventPlaylists();
        if (transform.parent != null)
        {
            Warn("MusicManager must be a root object to persist between scenes. It was moved to the scene root at runtime.");
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        if (Instance == this)
        {
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }
    }

    private void Start()
    {
        fallbackPlaylist?.PreloadAudioData();
        RegisterExistingSceneMusicManagers();
        RefreshSelection();
    }

    private void Update()
    {
        RefreshEventRegistryIfDirty();
        RemoveFinishedFadingHandles();

        var currentState = CurrentPlaybackState;
        if (currentState != null)
        {
            RemoveFinishedFadingHandles(currentState);
        }

        RemoveDestroyedSources();
        if (sceneSelectionDirty)
        {
            sceneSelectionDirty = false;
            RefreshSelection();
        }

        currentState = CurrentPlaybackState;
        if (musicPaused
            || musicStopped
            || currentState == null
            || currentState.Selection.Playlist == null
            || currentState.PlaylistExhausted
            || currentState.CurrentHandle.IsValid
            || Time.unscaledTime < currentState.NextPlayRetryTime)
        {
            return;
        }

        PlayNextTrack(currentState);
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (Instance == this)
        {
            StopMusic(0f);
        }
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        StopAllPlaybackStates(0f);
        StopFadingHandles(0f);
        Instance = null;
    }

    private void OnValidate()
    {
        if (eventCatalogs == null)
        {
            eventCatalogs = new List<MusicEventCatalog>();
        }

        Interlocked.Exchange(ref readOnlyEventCatalogs, null);
        volume = volume < 0f ? 0f : volume > 1f ? 1f : volume;
        Interlocked.Exchange(ref serializedSettingsDirty, 1);
    }

    /// <summary>
    /// Requests a named event from the unified registry built from every assigned catalog.
    /// </summary>
    public MusicRequestHandle RequestEvent(string eventId)
    {
        if (!isActiveAndEnabled)
        {
            Warn("MusicManager cannot request a Music Event while it is disabled.");
            return default;
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            Warn("MusicManager cannot request an empty Music Event ID.");
            return default;
        }

        if (!string.Equals(eventId, eventId.Trim(), System.StringComparison.Ordinal))
        {
            Warn($"Music Event ID '{eventId}' contains leading or trailing whitespace.");
            return default;
        }

        EnsureEventRegistry();
        if (ambiguousEventIds.Contains(eventId))
        {
            Warn($"Music Event ID '{eventId}' is assigned more than once across the MusicManager catalogs.");
            return default;
        }

        if (!registeredEvents.TryGetValue(eventId, out var registeredEvent))
        {
            Warn($"MusicManager does not contain a registered Music Event with ID '{eventId}'.");
            return default;
        }

        var definition = registeredEvent.Definition;
        return Request(
            definition.Playlist,
            definition.Priority,
            definition.TransitionDuration,
            definition.InterruptionMode,
            $"Music Event '{eventId}' ({registeredEvent.Catalog.name})");
    }

    public bool HasEvent(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return false;
        }

        EnsureEventRegistry();
        return registeredEvents.ContainsKey(eventId);
    }

    public bool IsEventCatalogRegistered(MusicEventCatalog catalog)
    {
        if (catalog == null)
        {
            return false;
        }

        EnsureEventRegistry();
        return registeredEventCatalogs.Contains(catalog);
    }

    /// <summary>
    /// Fills a reusable list with the resolved definitions and validation state
    /// of the unified Music Event registry.
    /// </summary>
    public void GetEventRegistryEntries(List<MusicEventRegistryEntry> results)
    {
        if (results == null)
        {
            return;
        }

        EnsureEventRegistry();

        results.Clear();
        results.AddRange(eventRegistryEntries);
    }

    /// <summary>
    /// Requests a playlist directly without a catalog Event ID. Replace requests use
    /// the normal playlist-selection behaviour. Suspend Previous requests preserve
    /// every lower playback state until the returned handle is released.
    /// </summary>
    public MusicRequestHandle Request(
        MusicPlaylist playlist,
        int priority = 0,
        float transitionDuration = -1f,
        MusicInterruptionMode interruptionMode = MusicInterruptionMode.Replace)
    {
        return Request(
            playlist,
            priority,
            transitionDuration,
            interruptionMode,
            "Programmatic Music Request");
    }

    internal MusicRequestHandle Request(
        MusicPlaylist playlist,
        int priority,
        float transitionDuration,
        MusicInterruptionMode interruptionMode,
        string context)
    {
        if (!isActiveAndEnabled)
        {
            Warn("MusicManager cannot create a request while it is disabled.");
            return default;
        }

        if (playlist == null)
        {
            Warn("MusicManager cannot create a request without a MusicPlaylist.");
            return default;
        }

        playlist.PreloadAudioData();

        var requestId = GetNextRequestId();
        var sequence = GetNextRequestSequence();
        activeRequests.Add(new MusicRequest(
            requestId,
            sequence,
            playlist,
            priority,
            Mathf.Max(-1f, transitionDuration),
            interruptionMode,
            string.IsNullOrWhiteSpace(context) ? "Programmatic Music Request" : context));
        RefreshSelection();
        return new MusicRequestHandle(this, requestId);
    }

    private void RefreshEventRegistryIfDirty()
    {
        var catalogRevision = MusicEventCatalog.RegistryRevision;
        var managerSettingsChanged = Interlocked.Exchange(ref serializedSettingsDirty, 0) != 0;
        if (eventRegistryBuilt
            && !managerSettingsChanged
            && observedEventCatalogRevision == catalogRevision)
        {
            return;
        }

        observedEventCatalogRevision = catalogRevision;
        RebuildEventRegistry(false);
        if (Application.isPlaying)
        {
            PreloadRegisteredEventPlaylists();
            if (managerSettingsChanged)
            {
                ApplyVolumeToAllPlaybackStates();
            }
        }
    }

    private void EnsureEventRegistry()
    {
        RefreshEventRegistryIfDirty();
    }

    private void RebuildEventRegistry(bool logWarnings)
    {
        registeredEvents.Clear();
        eventIdCounts.Clear();
        ambiguousEventIds.Clear();
        registeredEventCatalogs.Clear();
        eventRegistryCandidates.Clear();
        eventRegistryEntries.Clear();
        eventRegistryIssueCount = 0;
        eventCatalogIssueCount = 0;

        eventCatalogs ??= new List<MusicEventCatalog>();
        for (var catalogIndex = 0; catalogIndex < eventCatalogs.Count; catalogIndex++)
        {
            var catalog = eventCatalogs[catalogIndex];
            if (catalog == null)
            {
                eventCatalogIssueCount++;
                if (logWarnings)
                {
                    Warn($"MusicManager has a missing Music Event Catalog at index {catalogIndex}.");
                }

                continue;
            }

            if (!registeredEventCatalogs.Add(catalog))
            {
                eventCatalogIssueCount++;
                if (logWarnings)
                {
                    Warn($"MusicManager contains Music Event Catalog '{catalog.name}' more than once. Repeated references are ignored.");
                }

                continue;
            }

            var definitions = catalog.Events;
            if (definitions == null)
            {
                eventCatalogIssueCount++;
                if (logWarnings)
                {
                    Warn($"Music Event Catalog '{catalog.name}' has no event collection.");
                }

                continue;
            }

            for (var definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
            {
                var definition = definitions[definitionIndex];
                var eventId = definition?.EventId;
                var issues = MusicEventRegistryIssues.None;

                if (definition == null)
                {
                    issues |= MusicEventRegistryIssues.MissingDefinition;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(eventId))
                    {
                        issues |= MusicEventRegistryIssues.MissingEventId;
                    }
                    else if (!string.Equals(
                                 eventId,
                                 eventId.Trim(),
                                 System.StringComparison.Ordinal))
                    {
                        issues |= MusicEventRegistryIssues.SurroundingWhitespace;
                    }

                    if (definition.Playlist == null)
                    {
                        issues |= MusicEventRegistryIssues.MissingPlaylist;
                    }
                }

                var hasComparableId = definition != null
                                      && !string.IsNullOrWhiteSpace(eventId)
                                      && (issues & MusicEventRegistryIssues.SurroundingWhitespace) == 0;
                if (hasComparableId)
                {
                    eventIdCounts.TryGetValue(eventId, out var count);
                    eventIdCounts[eventId] = count + 1;
                }

                eventRegistryCandidates.Add(new EventRegistryCandidate(
                    catalog,
                    definitionIndex,
                    definition,
                    eventId,
                    issues,
                    hasComparableId));
            }
        }

        eventRegistryIssueCount = eventCatalogIssueCount;
        for (var i = 0; i < eventRegistryCandidates.Count; i++)
        {
            var candidate = eventRegistryCandidates[i];
            var issues = candidate.Issues;
            var firstDuplicateEntry = false;
            if (candidate.HasComparableId
                && eventIdCounts.TryGetValue(candidate.EventId, out var count)
                && count > 1)
            {
                issues |= MusicEventRegistryIssues.DuplicateEventId;
                firstDuplicateEntry = ambiguousEventIds.Add(candidate.EventId);
            }

            eventRegistryEntries.Add(new MusicEventRegistryEntry(
                candidate.Catalog,
                candidate.DefinitionIndex,
                candidate.Definition,
                candidate.EventId,
                issues));

            if (issues == MusicEventRegistryIssues.None)
            {
                registeredEvents.Add(
                    candidate.EventId,
                    new RegisteredMusicEvent(candidate.Catalog, candidate.Definition));
                continue;
            }

            eventRegistryIssueCount++;
            if (logWarnings)
            {
                WarnEventRegistryIssues(candidate, issues, firstDuplicateEntry);
            }
        }

        eventRegistryBuilt = true;
    }

    private void PreloadRegisteredEventPlaylists()
    {
        eventPlaylistPreloadBuffer.Clear();
        foreach (var registeredEvent in registeredEvents.Values)
        {
            if (!registeredEvent.Catalog.PreloadOnStart)
            {
                continue;
            }

            var playlist = registeredEvent.Definition.Playlist;
            if (playlist != null && eventPlaylistPreloadBuffer.Add(playlist))
            {
                playlist.PreloadAudioData();
            }
        }
    }

    private static void WarnEventRegistryIssues(
        EventRegistryCandidate candidate,
        MusicEventRegistryIssues issues,
        bool firstDuplicateEntry)
    {
        var location = $"Music Event Catalog '{candidate.Catalog.name}' entry {candidate.DefinitionIndex + 1}";
        if ((issues & MusicEventRegistryIssues.MissingDefinition) != 0)
        {
            Warn($"{location} has a missing definition.");
            return;
        }

        if ((issues & MusicEventRegistryIssues.MissingEventId) != 0)
        {
            Warn($"{location} has an empty Event ID.");
        }

        if ((issues & MusicEventRegistryIssues.SurroundingWhitespace) != 0)
        {
            Warn($"{location} has leading or trailing whitespace in Event ID '{candidate.EventId}'.");
        }

        if ((issues & MusicEventRegistryIssues.MissingPlaylist) != 0)
        {
            Warn($"{location} does not have a MusicPlaylist.");
        }

        if ((issues & MusicEventRegistryIssues.DuplicateEventId) != 0 && firstDuplicateEntry)
        {
            Warn($"Music Event ID '{candidate.EventId}' is assigned more than once across the MusicManager catalogs and was not registered.");
        }
    }

    /// <summary>
    /// Fills a reusable list with every registered music candidate and its current status.
    /// </summary>
    public void GetDebugEntries(List<MusicDebugEntry> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();

        for (var i = 0; i < activeRequests.Count; i++)
        {
            var request = activeRequests[i];
            results.Add(new MusicDebugEntry(
                MusicSelectionLayer.Event,
                GetPlaybackStatus(request),
                request.Playlist,
                request.Priority,
                request.InterruptionMode,
                request.Context));
        }

        var winningZone = GetHighestPriorityZone();
        for (var i = 0; i < activeZones.Count; i++)
        {
            var zone = activeZones[i];
            if (zone == null)
            {
                continue;
            }

            var eligible = zone.isActiveAndEnabled && zone.Playlist != null;
            var status = !eligible
                ? MusicDebugStatus.Inactive
                : zone == winningZone
                    ? GetPlaybackStatus(zone)
                    : MusicDebugStatus.Waiting;
            var sceneName = zone.gameObject.scene.IsValid() ? zone.gameObject.scene.name : "Unknown Scene";

            results.Add(new MusicDebugEntry(
                MusicSelectionLayer.Zone,
                status,
                zone.Playlist,
                zone.Priority,
                MusicInterruptionMode.Replace,
                $"Music Zone '{zone.name}' ({sceneName})"));
        }

        var activeScene = SceneManager.GetActiveScene();
        var winningSceneMusicManager = GetHighestPrioritySceneMusicManager();
        for (var i = 0; i < sceneMusicManagers.Count; i++)
        {
            var source = sceneMusicManagers[i];
            if (source == null)
            {
                continue;
            }

            var eligible = source.isActiveAndEnabled
                           && source.Playlist != null
                           && source.AppliesTo(activeScene);
            var status = !eligible
                ? MusicDebugStatus.Inactive
                : source == winningSceneMusicManager
                    ? GetPlaybackStatus(source)
                    : MusicDebugStatus.Waiting;
            var sceneName = source.gameObject.scene.IsValid() ? source.gameObject.scene.name : "Unknown Scene";

            results.Add(new MusicDebugEntry(
                MusicSelectionLayer.Scene,
                status,
                source.Playlist,
                source.Priority,
                MusicInterruptionMode.Replace,
                $"Scene Music Manager '{source.name}' ({sceneName}, {source.ActivationMode})"));
        }

        if (fallbackPlaylist != null)
        {
            results.Add(new MusicDebugEntry(
                MusicSelectionLayer.Fallback,
                GetPlaybackStatus(fallbackPlaylist),
                fallbackPlaylist,
                int.MinValue,
                MusicInterruptionMode.Replace,
                $"MusicManager Fallback '{name}'"));
        }
    }

    /// <summary>
    /// Releases every event request and returns to zone, scene, or fallback music.
    /// Prefer releasing individual handles in gameplay code.
    /// </summary>
    public void ReleaseAllRequests()
    {
        if (activeRequests.Count == 0)
        {
            return;
        }

        activeRequests.Clear();
        RefreshSelection();
    }

    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        ApplyVolumeToAllPlaybackStates();
    }

    public void SetFallbackPlaylist(MusicPlaylist playlist)
    {
        fallbackPlaylist = playlist;
        fallbackPlaylist?.PreloadAudioData();
        RefreshSelection();
    }

    public void PauseMusic()
    {
        if (musicPaused || musicStopped)
        {
            return;
        }

        musicPaused = true;
        for (var i = 0; i < playbackChain.Count; i++)
        {
            PausePlaybackState(playbackChain[i]);
        }

        PauseFadingHandles();
    }

    public void ResumeMusic()
    {
        if (musicStopped)
        {
            musicStopped = false;
            musicPaused = false;

            var stoppedState = CurrentPlaybackState;
            if (stoppedState != null)
            {
                stoppedState.PlaylistExhausted = false;
                stoppedState.PendingFadeInDuration = ResolveTransitionDuration(
                    stoppedState.Selection.TransitionDuration,
                    null,
                    stoppedState.Selection.Playlist);
                EnsureCurrentPlayback(stoppedState);
            }

            return;
        }

        if (!musicPaused)
        {
            return;
        }

        musicPaused = false;
        var currentState = CurrentPlaybackState;
        if (currentState != null)
        {
            ResumePlaybackState(currentState);
            EnsureCurrentPlayback(currentState);
        }

        ResumeFadingHandles();
    }

    public void StopMusic()
    {
        StopMusic(0f);
    }

    public void StopMusic(float fadeOutDuration)
    {
        if (musicPaused)
        {
            fadeOutDuration = 0f;
        }

        musicPaused = false;
        musicStopped = true;
        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);

        StopFadingHandles(fadeOutDuration);
        StopAllPlaybackStates(fadeOutDuration);
        RefreshSelection();
    }

    /// <summary>
    /// Stops playback sessions routed directly to one mixer group without
    /// invalidating other suspended music sessions in the chain.
    /// </summary>
    internal void StopMixerGroup(AudioMixerGroup mixerGroup, float fadeOutDuration)
    {
        for (var i = 0; i < playbackChain.Count; i++)
        {
            var state = playbackChain[i];
            var playlist = state.Selection.Playlist;
            if (playlist == null || playlist.MixerGroup != mixerGroup)
            {
                continue;
            }

            var resolvedFadeOutDuration = fadeOutDuration >= 0f
                ? fadeOutDuration
                : playlist.TransitionDuration;
            StopPlaybackState(state, resolvedFadeOutDuration);
        }
    }

    public void NextTrack()
    {
        NextTrack(CurrentPlaylist != null ? CurrentPlaylist.TransitionDuration : 0f);
    }

    public void NextTrack(float transitionDuration)
    {
        var currentState = CurrentPlaybackState;
        if (currentState == null || currentState.Selection.Playlist == null)
        {
            return;
        }

        musicStopped = false;
        currentState.PlaylistExhausted = false;
        transitionDuration = Mathf.Max(0f, transitionDuration);
        FadeOutCurrentHandle(currentState, transitionDuration);
        currentState.CurrentTrack = null;
        currentState.PendingFadeInDuration = transitionDuration;
        PlayNextTrack(currentState);
    }

    internal bool IsRequestActive(uint requestId)
    {
        for (var i = 0; i < activeRequests.Count; i++)
        {
            if (activeRequests[i].Id == requestId)
            {
                return true;
            }
        }

        return false;
    }

    internal void ReleaseRequest(uint requestId)
    {
        for (var i = activeRequests.Count - 1; i >= 0; i--)
        {
            if (activeRequests[i].Id != requestId)
            {
                continue;
            }

            activeRequests.RemoveAt(i);
            RefreshSelection();
            return;
        }
    }

    internal void RegisterZone(MusicZone zone)
    {
        if (zone == null)
        {
            return;
        }

        activeZones.Remove(zone);
        activeZones.Add(zone);
        RefreshSelection();
    }

    internal void UnregisterZone(MusicZone zone)
    {
        if (zone != null && activeZones.Remove(zone))
        {
            RefreshSelection();
        }
    }

    internal void RegisterSceneMusicManager(SceneMusicManager source)
    {
        if (source == null)
        {
            return;
        }

        sceneMusicManagers.Remove(source);
        sceneMusicManagers.Add(source);
        sceneSelectionDirty = true;
    }

    internal void UnregisterSceneMusicManager(SceneMusicManager source)
    {
        if (source != null && sceneMusicManagers.Remove(source))
        {
            sceneSelectionDirty = true;
        }
    }

    private void RefreshSelection()
    {
        BuildDesiredPlaybackChain();
        ReconcilePlaybackChain();
    }

    private void BuildDesiredPlaybackChain()
    {
        rankedRequests.Clear();
        rankedRequests.AddRange(activeRequests);
        rankedRequests.Sort(CompareRequests);

        suspendSelectionBuffer.Clear();
        var baseSelection = default(MusicSelection);
        for (var i = 0; i < rankedRequests.Count; i++)
        {
            var request = rankedRequests[i];
            var selection = MusicSelection.FromRequest(request);
            if (request.InterruptionMode == MusicInterruptionMode.Replace)
            {
                baseSelection = selection;
                break;
            }

            suspendSelectionBuffer.Add(selection);
        }

        if (!baseSelection.HasPlaylist)
        {
            baseSelection = SelectWorldMusic();
        }

        desiredPlaybackChain.Clear();
        if (baseSelection.HasPlaylist)
        {
            desiredPlaybackChain.Add(baseSelection);
        }

        for (var i = suspendSelectionBuffer.Count - 1; i >= 0; i--)
        {
            desiredPlaybackChain.Add(suspendSelectionBuffer[i]);
        }
    }

    private void ReconcilePlaybackChain()
    {
        AdvanceReconcileVersion();
        nextPlaybackChain.Clear();

        for (var i = 0; i < desiredPlaybackChain.Count; i++)
        {
            var selection = desiredPlaybackChain[i];
            var state = FindReusablePlaybackState(selection);
            if (state == null)
            {
                state = RentPlaybackState(selection);
            }
            else
            {
                state.Selection = selection;
            }

            state.ReconcileVersion = reconcileVersion;
            nextPlaybackChain.Add(state);
        }

        var oldTop = CurrentPlaybackState;
        var oldTopPlaylist = oldTop?.Selection.Playlist;
        var newTop = nextPlaybackChain.Count > 0
            ? nextPlaybackChain[nextPlaybackChain.Count - 1]
            : null;
        var topChanged = !ReferenceEquals(oldTop, newTop);
        var desiredTopSelection = desiredPlaybackChain.Count > 0
            ? desiredPlaybackChain[desiredPlaybackChain.Count - 1]
            : default;

        for (var i = playbackChain.Count - 1; i >= 0; i--)
        {
            var state = playbackChain[i];
            if (state.ReconcileVersion == reconcileVersion)
            {
                continue;
            }

            var fadeOutDuration = ReferenceEquals(state, oldTop)
                ? ResolveRemovedTopFadeDuration(state.Selection, desiredTopSelection)
                : 0f;
            RetirePlaybackState(state, fadeOutDuration);
            ReturnPlaybackState(state);
        }

        playbackChain.Clear();
        playbackChain.AddRange(nextPlaybackChain);
        nextPlaybackChain.Clear();

        var topIndex = playbackChain.Count - 1;
        for (var i = 0; i < topIndex; i++)
        {
            PausePlaybackState(playbackChain[i]);
        }

        newTop = CurrentPlaybackState;
        if (newTop == null)
        {
            return;
        }

        if (topChanged && !newTop.HasStartedPlayback)
        {
            newTop.PendingFadeInDuration = ResolveTransitionDuration(
                newTop.Selection.TransitionDuration,
                oldTopPlaylist,
                newTop.Selection.Playlist);
        }

        EnsureCurrentPlayback(newTop);
    }

    private MusicPlaybackState FindReusablePlaybackState(MusicSelection selection)
    {
        for (var i = 0; i < playbackChain.Count; i++)
        {
            var state = playbackChain[i];
            if (state.ReconcileVersion != reconcileVersion
                && state.Selection.HasSamePlaybackIdentity(selection))
            {
                return state;
            }
        }

        return null;
    }

    private void EnsureCurrentPlayback(MusicPlaybackState state)
    {
        if (state == null || state.Selection.Playlist == null)
        {
            return;
        }

        if (musicStopped)
        {
            PausePlaybackState(state);
            return;
        }

        if (state.CurrentHandle.IsValid)
        {
            if (musicPaused)
            {
                PausePlaybackState(state);
            }
            else
            {
                ResumePlaybackState(state);
            }

            return;
        }

        if (!state.PlaylistExhausted)
        {
            PlayNextTrack(state);
        }
    }

    private MusicSelection SelectWorldMusic()
    {
        var zone = GetHighestPriorityZone();
        if (zone != null)
        {
            return new MusicSelection(
                zone.Playlist,
                zone.TransitionDuration,
                MusicSelectionLayer.Zone,
                MusicInterruptionMode.Replace,
                zone);
        }

        var sceneMusic = GetHighestPrioritySceneMusicManager();
        if (sceneMusic != null)
        {
            return new MusicSelection(
                sceneMusic.Playlist,
                sceneMusic.TransitionDuration,
                MusicSelectionLayer.Scene,
                MusicInterruptionMode.Replace,
                sceneMusic);
        }

        return fallbackPlaylist != null
            ? new MusicSelection(
                fallbackPlaylist,
                -1f,
                MusicSelectionLayer.Fallback,
                MusicInterruptionMode.Replace,
                fallbackPlaylist)
            : default;
    }

    private MusicZone GetHighestPriorityZone()
    {
        MusicZone bestZone = null;
        for (var i = 0; i < activeZones.Count; i++)
        {
            var zone = activeZones[i];
            if (zone == null || !zone.isActiveAndEnabled || zone.Playlist == null)
            {
                continue;
            }

            if (bestZone == null || zone.Priority >= bestZone.Priority)
            {
                bestZone = zone;
            }
        }

        return bestZone;
    }

    private SceneMusicManager GetHighestPrioritySceneMusicManager()
    {
        SceneMusicManager bestSource = null;
        var activeScene = SceneManager.GetActiveScene();

        for (var i = 0; i < sceneMusicManagers.Count; i++)
        {
            var source = sceneMusicManagers[i];
            if (source == null
                || !source.isActiveAndEnabled
                || source.Playlist == null
                || !source.AppliesTo(activeScene))
            {
                continue;
            }

            if (bestSource == null || source.Priority >= bestSource.Priority)
            {
                bestSource = source;
            }
        }

        return bestSource;
    }

    private void PlayNextTrack(MusicPlaybackState state)
    {
        if (!isActiveAndEnabled || state == null || state.Selection.Playlist == null)
        {
            return;
        }

        var audioManager = AudioManager.Instance;
        if (audioManager == null || !audioManager.isActiveAndEnabled)
        {
            if (!state.WarnedAboutMissingAudioManager)
            {
                Warn("MusicManager needs an active AudioManager to play music.");
                state.WarnedAboutMissingAudioManager = true;
            }

            state.NextPlayRetryTime = Time.unscaledTime + FailedPlayRetryDelay;
            return;
        }

        if (!PrepareNextTrack(state))
        {
            return;
        }

        var playlist = state.Selection.Playlist;
        state.WarnedAboutMissingAudioManager = false;
        var trackIndex = state.PlayOrder[state.PlayOrderCursor];
        var track = playlist.Tracks[trackIndex];
        var settings = AudioPlaySettings.Default;
        settings.MixerGroup = playlist.MixerGroup;
        settings.Category = FeedbackAudioCategory.Music;
        settings.Volume = playlist.Volume * volume;
        settings.Loop = false;
        settings.FadeInDuration = Mathf.Max(0f, state.PendingFadeInDuration);
        settings.FadeOutDuration = playlist.TransitionDuration;
        settings.SpatialBlend = 0f;
        settings.DopplerLevel = 0f;

        var handle = audioManager.Play(track, settings);
        if (!handle.IsValid)
        {
            state.NextPlayRetryTime = Time.unscaledTime + FailedPlayRetryDelay;
            return;
        }

        state.CurrentHandle = handle;
        state.CurrentTrack = track;
        state.LastTrackIndex = trackIndex;
        state.PlayOrderCursor++;
        state.NextPlayRetryTime = 0f;
        state.PendingFadeInDuration = 0f;
        state.HasStartedPlayback = true;

        if (musicPaused || !ReferenceEquals(state, CurrentPlaybackState))
        {
            state.CurrentHandle.Pause();
        }
    }

    private static bool PrepareNextTrack(MusicPlaybackState state)
    {
        if (state.PlayOrderCursor < state.PlayOrder.Count)
        {
            return true;
        }

        var playlist = state.Selection.Playlist;
        if (state.PlayOrder.Count > 0 && !playlist.LoopPlaylist)
        {
            state.PlaylistExhausted = true;
            state.CurrentTrack = null;
            return false;
        }

        BuildPlayOrder(state);
        if (state.PlayOrder.Count > 0)
        {
            return true;
        }

        state.PlaylistExhausted = true;
        state.CurrentTrack = null;
        if (!state.WarnedAboutEmptyPlaylist)
        {
            Warn($"MusicPlaylist '{playlist.name}' does not contain any valid tracks.");
            state.WarnedAboutEmptyPlaylist = true;
        }

        return false;
    }

    private static void BuildPlayOrder(MusicPlaybackState state)
    {
        state.PlayOrder.Clear();
        state.PlayOrderCursor = 0;

        var playlist = state.Selection.Playlist;
        var tracks = playlist.Tracks;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i] != null)
            {
                state.PlayOrder.Add(i);
            }
        }

        if (playlist.PlaybackOrder != MusicPlaybackOrder.Shuffle)
        {
            return;
        }

        for (var i = state.PlayOrder.Count - 1; i > 0; i--)
        {
            var swapIndex = Random.Range(0, i + 1);
            (state.PlayOrder[i], state.PlayOrder[swapIndex]) =
                (state.PlayOrder[swapIndex], state.PlayOrder[i]);
        }

        if (state.PlayOrder.Count > 1 && state.PlayOrder[0] == state.LastTrackIndex)
        {
            var swapIndex = Random.Range(1, state.PlayOrder.Count);
            (state.PlayOrder[0], state.PlayOrder[swapIndex]) =
                (state.PlayOrder[swapIndex], state.PlayOrder[0]);
        }
    }

    private void FadeOutCurrentHandle(MusicPlaybackState state, float duration)
    {
        var handle = state.CurrentHandle;
        state.CurrentHandle = default;

        if (!handle.IsValid)
        {
            return;
        }

        duration = Mathf.Max(0f, duration);
        handle.Stop(duration);
        if (duration <= 0f || !handle.IsValid)
        {
            return;
        }

        if (musicPaused)
        {
            handle.Pause();
        }

        state.FadingHandles.Add(handle);
    }

    private void RetirePlaybackState(MusicPlaybackState state, float duration)
    {
        if (state == null)
        {
            return;
        }

        duration = Mathf.Max(0f, duration);
        var playlist = state.Selection.Playlist;
        RetireHandle(state.CurrentHandle, playlist, duration);
        state.CurrentHandle = default;

        for (var i = 0; i < state.FadingHandles.Count; i++)
        {
            RetireHandle(state.FadingHandles[i], playlist, duration);
        }

        state.FadingHandles.Clear();
        state.CurrentTrack = null;
    }

    private void StopPlaybackState(MusicPlaybackState state, float duration)
    {
        if (state == null)
        {
            return;
        }

        duration = Mathf.Max(0f, duration);
        var playlist = state.Selection.Playlist;
        RetireHandle(state.CurrentHandle, playlist, duration);
        state.CurrentHandle = default;

        for (var i = 0; i < state.FadingHandles.Count; i++)
        {
            RetireHandle(state.FadingHandles[i], playlist, duration);
        }

        state.FadingHandles.Clear();
        state.CurrentTrack = null;
        state.PlaylistExhausted = true;
        state.PendingFadeInDuration = 0f;
    }

    private void RetireHandle(AudioHandle handle, MusicPlaylist playlist, float duration)
    {
        if (!handle.IsValid)
        {
            return;
        }

        handle.Stop(duration);
        if (duration <= 0f || !handle.IsValid)
        {
            return;
        }

        if (musicPaused)
        {
            handle.Pause();
        }

        fadingHandles.Add(new FadingMusicHandle(handle, playlist));
    }

    private static void PausePlaybackState(MusicPlaybackState state)
    {
        if (state == null)
        {
            return;
        }

        state.CurrentHandle.Pause();
        for (var i = 0; i < state.FadingHandles.Count; i++)
        {
            state.FadingHandles[i].Pause();
        }
    }

    private static void ResumePlaybackState(MusicPlaybackState state)
    {
        if (state == null)
        {
            return;
        }

        state.CurrentHandle.Resume();
        for (var i = 0; i < state.FadingHandles.Count; i++)
        {
            state.FadingHandles[i].Resume();
        }
    }

    private void StopAllPlaybackStates(float topFadeOutDuration)
    {
        var top = CurrentPlaybackState;
        for (var i = playbackChain.Count - 1; i >= 0; i--)
        {
            var state = playbackChain[i];
            RetirePlaybackState(
                state,
                ReferenceEquals(state, top) ? topFadeOutDuration : 0f);
            ReturnPlaybackState(state);
        }

        playbackChain.Clear();
        nextPlaybackChain.Clear();
    }

    private void StopFadingHandles(float duration)
    {
        duration = Mathf.Max(0f, duration);
        for (var i = 0; i < fadingHandles.Count; i++)
        {
            var handle = fadingHandles[i].Handle;
            if (!handle.IsValid)
            {
                continue;
            }

            handle.Stop(duration);
            if (musicPaused && duration > 0f && handle.IsValid)
            {
                handle.Pause();
            }
        }

        if (duration <= 0f)
        {
            fadingHandles.Clear();
        }
        else
        {
            RemoveFinishedFadingHandles();
        }
    }

    private void PauseFadingHandles()
    {
        for (var i = 0; i < fadingHandles.Count; i++)
        {
            fadingHandles[i].Handle.Pause();
        }
    }

    private void ResumeFadingHandles()
    {
        for (var i = 0; i < fadingHandles.Count; i++)
        {
            fadingHandles[i].Handle.Resume();
        }
    }

    private void RemoveFinishedFadingHandles()
    {
        for (var i = fadingHandles.Count - 1; i >= 0; i--)
        {
            if (!fadingHandles[i].Handle.IsValid)
            {
                fadingHandles.RemoveAt(i);
            }
        }
    }

    private static void RemoveFinishedFadingHandles(MusicPlaybackState state)
    {
        for (var i = state.FadingHandles.Count - 1; i >= 0; i--)
        {
            if (!state.FadingHandles[i].IsValid)
            {
                state.FadingHandles.RemoveAt(i);
            }
        }
    }

    private void ApplyVolumeToAllPlaybackStates()
    {
        for (var i = 0; i < playbackChain.Count; i++)
        {
            ApplyVolume(playbackChain[i]);
        }

        for (var i = 0; i < fadingHandles.Count; i++)
        {
            var fading = fadingHandles[i];
            if (fading.Handle.IsValid && fading.Playlist != null)
            {
                fading.Handle.SetVolume(fading.Playlist.Volume * volume);
            }
        }
    }

    private void ApplyVolume(MusicPlaybackState state)
    {
        var playlist = state.Selection.Playlist;
        if (playlist == null)
        {
            return;
        }

        var resolvedVolume = playlist.Volume * volume;
        state.CurrentHandle.SetVolume(resolvedVolume);
        for (var i = 0; i < state.FadingHandles.Count; i++)
        {
            state.FadingHandles[i].SetVolume(resolvedVolume);
        }
    }

    private MusicDebugStatus GetPlaybackStatus(object owner)
    {
        for (var i = playbackChain.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(playbackChain[i].Selection.Owner, owner))
            {
                continue;
            }

            return i == playbackChain.Count - 1
                ? MusicDebugStatus.Selected
                : MusicDebugStatus.Suspended;
        }

        return MusicDebugStatus.Waiting;
    }

    private MusicPlaybackState RentPlaybackState(MusicSelection selection)
    {
        var state = playbackStatePool.Count > 0
            ? playbackStatePool.Pop()
            : new MusicPlaybackState();
        state.Reset(selection);
        return state;
    }

    private void ReturnPlaybackState(MusicPlaybackState state)
    {
        state.Reset(default);
        playbackStatePool.Push(state);
    }

    private void AdvanceReconcileVersion()
    {
        unchecked
        {
            reconcileVersion++;
        }

        if (reconcileVersion != 0)
        {
            return;
        }

        reconcileVersion = 1;
        for (var i = 0; i < playbackChain.Count; i++)
        {
            playbackChain[i].ReconcileVersion = 0;
        }
    }

    private void RegisterExistingSceneMusicManagers()
    {
        var sources = Object.FindObjectsByType<SceneMusicManager>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (var i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            source.Playlist?.PreloadAudioData();
            if (!sceneMusicManagers.Contains(source))
            {
                sceneMusicManagers.Add(source);
            }
        }
    }

    private void RemoveDestroyedSources()
    {
        var removedAny = RemoveDestroyed(activeZones);
        removedAny |= RemoveDestroyed(sceneMusicManagers);
        if (removedAny)
        {
            RefreshSelection();
        }
    }

    private static bool RemoveDestroyed<T>(List<T> sources) where T : Object
    {
        var removedAny = false;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] != null)
            {
                continue;
            }

            sources.RemoveAt(i);
            removedAny = true;
        }

        return removedAny;
    }

    private uint GetNextRequestId()
    {
        do
        {
            unchecked
            {
                nextRequestId++;
            }
        }
        while (nextRequestId == 0 || IsRequestActive(nextRequestId));

        return nextRequestId;
    }

    private ulong GetNextRequestSequence()
    {
        unchecked
        {
            nextRequestSequence++;
        }

        if (nextRequestSequence == 0)
        {
            nextRequestSequence = 1;
        }

        return nextRequestSequence;
    }

    private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
    {
        sceneSelectionDirty = true;
    }

    private static int CompareRequests(MusicRequest left, MusicRequest right)
    {
        var priorityComparison = right.Priority.CompareTo(left.Priority);
        return priorityComparison != 0
            ? priorityComparison
            : right.Sequence.CompareTo(left.Sequence);
    }

    private static float ResolveRemovedTopFadeDuration(
        MusicSelection removedSelection,
        MusicSelection nextSelection)
    {
        if (removedSelection.InterruptionMode == MusicInterruptionMode.SuspendPrevious)
        {
            return removedSelection.TransitionDuration >= 0f
                ? removedSelection.TransitionDuration
                : removedSelection.Playlist != null
                    ? removedSelection.Playlist.TransitionDuration
                    : 0f;
        }

        var requestedDuration = nextSelection.HasPlaylist
            ? nextSelection.TransitionDuration
            : -1f;
        return ResolveTransitionDuration(
            requestedDuration,
            removedSelection.Playlist,
            nextSelection.Playlist);
    }

    private static float ResolveTransitionDuration(
        float requestedDuration,
        MusicPlaylist previousPlaylist,
        MusicPlaylist nextPlaylist)
    {
        if (requestedDuration >= 0f)
        {
            return requestedDuration;
        }

        if (nextPlaylist != null)
        {
            return nextPlaylist.TransitionDuration;
        }

        return previousPlaylist != null ? previousPlaylist.TransitionDuration : 0f;
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    private readonly struct RegisteredMusicEvent
    {
        public readonly MusicEventCatalog Catalog;
        public readonly MusicEventDefinition Definition;

        public RegisteredMusicEvent(
            MusicEventCatalog catalog,
            MusicEventDefinition definition)
        {
            Catalog = catalog;
            Definition = definition;
        }
    }

    private readonly struct EventRegistryCandidate
    {
        public readonly MusicEventCatalog Catalog;
        public readonly int DefinitionIndex;
        public readonly MusicEventDefinition Definition;
        public readonly string EventId;
        public readonly MusicEventRegistryIssues Issues;
        public readonly bool HasComparableId;

        public EventRegistryCandidate(
            MusicEventCatalog catalog,
            int definitionIndex,
            MusicEventDefinition definition,
            string eventId,
            MusicEventRegistryIssues issues,
            bool hasComparableId)
        {
            Catalog = catalog;
            DefinitionIndex = definitionIndex;
            Definition = definition;
            EventId = eventId;
            Issues = issues;
            HasComparableId = hasComparableId;
        }
    }

    private sealed class MusicRequest
    {
        public readonly uint Id;
        public readonly ulong Sequence;
        public readonly MusicPlaylist Playlist;
        public readonly int Priority;
        public readonly float TransitionDuration;
        public readonly MusicInterruptionMode InterruptionMode;
        public readonly string Context;

        public MusicRequest(
            uint id,
            ulong sequence,
            MusicPlaylist playlist,
            int priority,
            float transitionDuration,
            MusicInterruptionMode interruptionMode,
            string context)
        {
            Id = id;
            Sequence = sequence;
            Playlist = playlist;
            Priority = priority;
            TransitionDuration = transitionDuration;
            InterruptionMode = interruptionMode;
            Context = context;
        }
    }

    private readonly struct MusicSelection
    {
        public readonly MusicPlaylist Playlist;
        public readonly float TransitionDuration;
        public readonly MusicSelectionLayer Layer;
        public readonly MusicInterruptionMode InterruptionMode;
        public readonly object Owner;

        public bool HasPlaylist => Playlist != null;

        public MusicSelection(
            MusicPlaylist playlist,
            float transitionDuration,
            MusicSelectionLayer layer,
            MusicInterruptionMode interruptionMode,
            object owner)
        {
            Playlist = playlist;
            TransitionDuration = transitionDuration;
            Layer = layer;
            InterruptionMode = interruptionMode;
            Owner = owner;
        }

        public bool HasSamePlaybackIdentity(MusicSelection other)
        {
            if (InterruptionMode == MusicInterruptionMode.SuspendPrevious
                || other.InterruptionMode == MusicInterruptionMode.SuspendPrevious)
            {
                return InterruptionMode == other.InterruptionMode
                       && ReferenceEquals(Owner, other.Owner);
            }

            return Playlist == other.Playlist;
        }

        public static MusicSelection FromRequest(MusicRequest request)
        {
            return new MusicSelection(
                request.Playlist,
                request.TransitionDuration,
                MusicSelectionLayer.Event,
                request.InterruptionMode,
                request);
        }
    }

    private sealed class MusicPlaybackState
    {
        public readonly List<int> PlayOrder = new List<int>();
        public readonly List<AudioHandle> FadingHandles = new List<AudioHandle>();

        public MusicSelection Selection;
        public AudioClip CurrentTrack;
        public AudioHandle CurrentHandle;
        public bool PlaylistExhausted;
        public bool WarnedAboutEmptyPlaylist;
        public bool WarnedAboutMissingAudioManager;
        public bool HasStartedPlayback;
        public int PlayOrderCursor;
        public int LastTrackIndex;
        public float NextPlayRetryTime;
        public float PendingFadeInDuration;
        public int ReconcileVersion;

        public void Reset(MusicSelection selection)
        {
            Selection = selection;
            CurrentTrack = null;
            CurrentHandle = default;
            PlaylistExhausted = false;
            WarnedAboutEmptyPlaylist = false;
            WarnedAboutMissingAudioManager = false;
            HasStartedPlayback = false;
            PlayOrder.Clear();
            FadingHandles.Clear();
            PlayOrderCursor = 0;
            LastTrackIndex = -1;
            NextPlayRetryTime = 0f;
            PendingFadeInDuration = 0f;
            ReconcileVersion = 0;
        }
    }

    private readonly struct FadingMusicHandle
    {
        public readonly AudioHandle Handle;
        public readonly MusicPlaylist Playlist;

        public FadingMusicHandle(AudioHandle handle, MusicPlaylist playlist)
        {
            Handle = handle;
            Playlist = playlist;
        }
    }
}
}
