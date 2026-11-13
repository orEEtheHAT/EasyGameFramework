using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace EasyFeedbacks
{

public enum MusicPlaybackOrder
{
    Shuffle,
    Sequential
}

/// <summary>
/// Reusable collection of music tracks and their shared playback settings.
/// </summary>
[CreateAssetMenu(fileName = "Music Playlist", menuName = "EasyGameFramework/Feedbacks/Music Playlist")]
public sealed class MusicPlaylist : ScriptableObject
{
    [SerializeField, Tooltip("Audio clips played by this playlist.")]
    private List<AudioClip> tracks = new List<AudioClip>();

    [SerializeField, Tooltip("Optional mixer group used to route every track in this playlist.")]
    private AudioMixerGroup mixerGroup;

    [SerializeField, Range(0f, 1f), Tooltip("Volume shared by all tracks in this playlist.")]
    private float volume = 1f;

    [SerializeField, Tooltip("Play tracks in list order or in a shuffled order.")]
    private MusicPlaybackOrder playbackOrder = MusicPlaybackOrder.Shuffle;

    [SerializeField, Tooltip("Start the playlist again after its final track finishes.")]
    private bool loopPlaylist = true;

    [SerializeField, Min(0f), Tooltip("Default transition duration when this playlist becomes selected.")]
    private float transitionDuration = 5f;

    public IReadOnlyList<AudioClip> Tracks => tracks;
    public AudioMixerGroup MixerGroup => mixerGroup;
    public float Volume => Mathf.Clamp01(volume);
    public MusicPlaybackOrder PlaybackOrder => playbackOrder;
    public bool LoopPlaylist => loopPlaylist;
    public float TransitionDuration => Mathf.Max(0f, transitionDuration);

    public void PreloadAudioData()
    {
        for (var i = 0; i < tracks.Count; i++)
        {
            var clip = tracks[i];
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
            {
                clip.LoadAudioData();
            }
        }
    }

    private void OnValidate()
    {
        volume = Mathf.Clamp01(volume);
        transitionDuration = Mathf.Max(0f, transitionDuration);
    }
}
}
