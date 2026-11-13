using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace EasyFeedbacks
{

[Flags]
public enum MusicEventRegistryIssues
{
    None = 0,
    MissingDefinition = 1 << 0,
    MissingEventId = 1 << 1,
    SurroundingWhitespace = 1 << 2,
    MissingPlaylist = 1 << 3,
    DuplicateEventId = 1 << 4
}

/// <summary>
/// Read-only resolved view of one definition from a MusicManager event registry.
/// </summary>
public readonly struct MusicEventRegistryEntry
{
    public readonly MusicEventCatalog Catalog;
    public readonly int DefinitionIndex;
    public readonly MusicEventDefinition Definition;
    public readonly string EventId;
    public readonly MusicEventRegistryIssues Issues;

    public bool IsRegistered => Issues == MusicEventRegistryIssues.None;

    public MusicEventRegistryEntry(
        MusicEventCatalog catalog,
        int definitionIndex,
        MusicEventDefinition definition,
        string eventId,
        MusicEventRegistryIssues issues)
    {
        Catalog = catalog;
        DefinitionIndex = definitionIndex;
        Definition = definition;
        EventId = eventId;
        Issues = issues;
    }
}

[Serializable]
public sealed class MusicEventDefinition
{
    [SerializeField, Tooltip("Stable identifier used by gameplay code, for example Combat or Boss.")]
    private string eventId = "New Event";

    [SerializeField, Tooltip("Playlist requested while this event remains active.")]
    private MusicPlaylist playlist;

    [SerializeField, Tooltip("Higher-priority Music Events win. Event priority is not compared with zones or scenes.")]
    private int priority;

    [SerializeField, Tooltip("Replace stops lower music. Suspend Previous pauses lower playback at its exact position and resumes it when this event is released.")]
    private MusicInterruptionMode interruptionMode = MusicInterruptionMode.Replace;

    [SerializeField, Min(-1f), Tooltip("Transition duration when this event becomes selected. For Suspend Previous, it also controls the event fade-out when released. -1 uses the playlist default.")]
    private float transitionDuration = -1f;

    public string EventId => eventId;
    public MusicPlaylist Playlist => playlist;
    public int Priority => priority;
    public MusicInterruptionMode InterruptionMode => interruptionMode;
    public float TransitionDuration => Mathf.Max(-1f, transitionDuration);

    internal void Normalize()
    {
        transitionDuration = transitionDuration < -1f ? -1f : transitionDuration;
    }
}

/// <summary>
/// Data-only collection of named music events merged by MusicManager at runtime.
/// </summary>
[CreateAssetMenu(fileName = "Music Event Catalog", menuName = "EasyGameFramework/Feedbacks/Music Event Catalog")]
public sealed class MusicEventCatalog : ScriptableObject
{
    private static int registryRevision;

    [SerializeField, Tooltip("Prepare every valid playlist in this catalog when the MusicManager starts. Enable for latency-sensitive events; leave disabled for large or optional content sets.")]
    private bool preloadOnStart;

    [SerializeField]
    private List<MusicEventDefinition> events = new List<MusicEventDefinition>();

    [NonSerialized]
    private ReadOnlyCollection<MusicEventDefinition> readOnlyEvents;

    public bool PreloadOnStart => preloadOnStart;
    public IReadOnlyList<MusicEventDefinition> Events =>
        readOnlyEvents ??= (events ??= new List<MusicEventDefinition>()).AsReadOnly();

    internal static int RegistryRevision =>
        System.Threading.Volatile.Read(ref registryRevision);

    private void OnValidate()
    {
        if (events == null)
        {
            events = new List<MusicEventDefinition>();
        }

        for (var i = 0; i < events.Count; i++)
        {
            events[i]?.Normalize();
        }

        System.Threading.Interlocked.Exchange(ref readOnlyEvents, null);
        System.Threading.Interlocked.Increment(ref registryRevision);
    }
}
}
