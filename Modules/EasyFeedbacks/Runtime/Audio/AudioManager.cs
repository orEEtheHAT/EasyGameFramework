using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Audio;

namespace EasyFeedbacks
{

/// <summary>
/// Persistent owner of pooled AudioSources and the direct AudioClip playback API.
/// </summary>
[AddComponentMenu("Easy Feedbacks/Audio Manager")]
[DisallowMultipleComponent]
public sealed class AudioManager : MonoBehaviour
{
    private const int FocusResumeGraceFrames = 3;

    private static int editorPreviewPlaybackDepth;

    [System.Flags]
    private enum PauseReason
    {
        None = 0,
        Handle = 1 << 0,
        Master = 1 << 1,
        MixerGroup = 1 << 2,
        System = 1 << 3
    }

    [SerializeField, Min(0), Tooltip("AudioSources created when this manager wakes up.")]
    private int initialCapacity = 16;

    [SerializeField, Min(1), Tooltip("Hard limit for simultaneous pooled AudioSources.")]
    private int maximumCapacity = 64;

    [SerializeField, Tooltip("AudioMixer whose groups are exposed as runtime channel controls in the AudioManager inspector.")]
    private AudioMixer audioMixer;

    [SerializeField, HideInInspector]
    private List<AudioMixerGroup> globalPauseExcludedGroups = new List<AudioMixerGroup>();

    private readonly List<Voice> allVoices = new List<Voice>();
    private readonly List<Voice> activeVoices = new List<Voice>();
    private readonly Stack<Voice> availableVoices = new Stack<Voice>();
    private readonly HashSet<AudioMixerGroup> pausedMixerGroups = new HashSet<AudioMixerGroup>();
    private readonly HashSet<AudioMixerGroup> globalPauseExcludedGroupSet = new HashSet<AudioMixerGroup>();

    private bool applicationHasFocus = true;
    private bool applicationPaused;
    private bool allChannelsPaused;
    private bool systemPaused;
    private bool globalPauseExclusionCacheDirty = true;
    private int focusResumeGraceFrames;

    public static AudioManager Instance { get; private set; }

    public int ActiveVoices => activeVoices.Count;
    public int AvailableVoices => availableVoices.Count;
    public int TotalVoices => allVoices.Count;
    public int MaximumCapacity => maximumCapacity;
    public AudioMixer Mixer => audioMixer;
    public bool AreAllChannelsPaused => allChannelsPaused;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (transform.parent != null)
        {
            Warn("AudioManager must be a root object to persist between scenes. It was moved to the scene root at runtime.");
            transform.SetParent(null, true);
        }

        DontDestroyOnLoad(gameObject);

        NormalizeCapacity();
        RebuildGlobalPauseExclusionCache();
        for (var i = 0; i < initialCapacity; i++)
        {
            availableVoices.Push(CreateVoice());
        }

        SetSystemPaused(FeedbackService.AreAllPaused);
    }

    private void Update()
    {
        RefreshGlobalPauseExclusionsIfNeeded();

        var deltaTime = Time.unscaledDeltaTime;
        var suppressCompletionChecks = applicationPaused
                                       || AudioListener.pause
                                       || focusResumeGraceFrames > 0;

        if (focusResumeGraceFrames > 0)
        {
            focusResumeGraceFrames--;
        }

        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];

            if (voice.Follow != null)
            {
                voice.Source.transform.SetPositionAndRotation(voice.Follow.position, voice.Follow.rotation);
            }
            else if (voice.WasFollowing)
            {
                // A destroyed Unity object compares equal to null. Clearing this flag leaves
                // the AudioSource at its last known position without dereferencing it again.
                voice.WasFollowing = false;
                voice.Follow = null;
            }

            if (voice.IsPaused)
            {
                continue;
            }

            if (voice.Source.isPlaying)
            {
                voice.HasStarted = true;
            }

            if (UpdateFade(voice, deltaTime))
            {
                continue;
            }

            if (!suppressCompletionChecks
                && (applicationHasFocus || voice.AllowCompletionWhileUnfocused)
                && !voice.Loop
                && voice.HasStarted
                && !voice.Source.isPlaying)
            {
                ReleaseVoice(voice);
            }
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        applicationHasFocus = hasFocus;
        if (hasFocus)
        {
            focusResumeGraceFrames = FocusResumeGraceFrames;
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        applicationPaused = pauseStatus;
        if (!pauseStatus)
        {
            focusResumeGraceFrames = FocusResumeGraceFrames;
        }
    }

    private void OnDisable()
    {
        if (Instance == this)
        {
            StopManagedMusic(null, 0f, true);
            ReleaseAllVoicesImmediate();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        // Removing just the component leaves its GameObject alive. The pool is
        // owned by this manager, not by the remaining components on that object.
        for (var i = 0; i < allVoices.Count; i++)
        {
            var voice = allVoices[i];
            if (voice.GameObject != null)
            {
                voice.GameObject.SetActive(false);
                Destroy(voice.GameObject);
            }
        }

        activeVoices.Clear();
        availableVoices.Clear();
        allVoices.Clear();
    }

    private void OnValidate()
    {
        initialCapacity = Mathf.Max(0, initialCapacity);
        maximumCapacity = Mathf.Max(1, maximumCapacity);
        globalPauseExclusionCacheDirty = true;
    }

    public AudioHandle Play(AudioClip clip)
    {
        return Play(clip, AudioPlaySettings.Default);
    }

    public AudioHandle Play(AudioClip clip, Vector3 worldPosition)
    {
        var settings = AudioPlaySettings.Default;
        settings.WorldPosition = worldPosition;
        return Play(clip, settings);
    }

    public AudioHandle Play(AudioClip clip, Transform followTransform)
    {
        var settings = AudioPlaySettings.Default;
        settings.PositionMode = FeedbackPositionMode.FollowTransform;
        settings.FollowTransform = followTransform;

        if (followTransform != null)
        {
            settings.WorldPosition = followTransform.position;
            settings.Rotation = followTransform.rotation;
        }

        return Play(clip, settings);
    }

    public AudioHandle Play(AudioFeedbackEntry entry, FeedbackContext context)
    {
        if (entry == null || !entry.IsEnabled)
        {
            return default;
        }

        var clip = entry.GetRandomClip();
        if (clip == null)
        {
            return default;
        }

        var positionMode = FeedbackPositioning.Resolve(entry.PositionMode, context.Positioning);
        var followTransform = positionMode == FeedbackPositionMode.FollowTransform
            ? (context.Follow != null ? context.Follow : context.Source)
            : null;

        var settings = new AudioPlaySettings
        {
            MixerGroup = entry.MixerGroup,
            Volume = entry.GetRandomVolume() * context.Intensity * context.AudioMultiplier,
            Pitch = entry.GetRandomPitch(),
            Loop = entry.Loop,
            FadeInDuration = entry.FadeInDuration,
            FadeOutDuration = entry.FadeOutDuration,
            SpatialBlend = entry.SpatialBlend,
            MinDistance = entry.MinDistance,
            MaxDistance = entry.MaxDistance,
            RolloffMode = entry.RolloffMode,
            DopplerLevel = entry.DopplerLevel,
            PositionMode = positionMode,
            WorldPosition = context.WorldPosition,
            Rotation = context.Rotation,
            FollowTransform = followTransform
        };

        return Play(clip, settings);
    }

    public AudioHandle Play(AudioClip clip, AudioPlaySettings settings)
    {
        RefreshGlobalPauseExclusionsIfNeeded();

        if (!isActiveAndEnabled)
        {
            Warn("AudioManager cannot play while it is disabled.");
            return default;
        }

        if (clip == null)
        {
            Warn("AudioManager cannot play a null AudioClip.");
            return default;
        }

        var voice = AcquireVoice();
        if (voice == null)
        {
            Warn($"AudioManager reached Maximum Capacity ({maximumCapacity}). '{clip.name}' was not played.");
            return default;
        }

        ConfigureVoice(voice, clip, settings);
        voice.AllowCompletionWhileUnfocused = editorPreviewPlaybackDepth > 0;
        voice.Source.Play();
        voice.HasStarted = voice.Source.isPlaying;

        if (allChannelsPaused)
        {
            PauseVoice(voice, PauseReason.Master);
        }

        if (IsMixerGroupPaused(settings.MixerGroup))
        {
            PauseVoice(voice, PauseReason.MixerGroup);
        }

        if (systemPaused && !IsExcludedFromGlobalPause(settings.MixerGroup))
        {
            PauseVoice(voice, PauseReason.System);
        }

        return new AudioHandle(this, voice.Id, voice.Generation);
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void BeginEditorPreviewPlayback()
    {
        editorPreviewPlaybackDepth++;
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static void EndEditorPreviewPlayback()
    {
        editorPreviewPlaybackDepth = Mathf.Max(0, editorPreviewPlaybackDepth - 1);
    }

    public void Stop(AudioHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            RequestStop(voice, voice.ConfiguredFadeOutDuration);
        }
    }

    public void Stop(AudioHandle handle, float fadeOutDuration)
    {
        if (TryGetVoice(handle, out var voice))
        {
            RequestStop(voice, Mathf.Max(0f, fadeOutDuration));
        }
    }

    public void Pause(AudioHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            PauseVoice(voice, PauseReason.Handle);
        }
    }

    public void Resume(AudioHandle handle)
    {
        if (TryGetVoice(handle, out var voice))
        {
            ResumeVoice(voice, PauseReason.Handle);
        }
    }

    public void SetVolume(AudioHandle handle, float volume)
    {
        if (!TryGetVoice(handle, out var voice))
        {
            return;
        }

        voice.BaseVolume = Mathf.Clamp01(volume);
        ApplyVolume(voice);
    }

    internal void ApplyUserSettings()
    {
        for (var i = 0; i < activeVoices.Count; i++)
        {
            ApplyVolume(activeVoices[i]);
        }
    }

    public void StopAll(float fadeOutDuration = -1f)
    {
        StopManagedMusic(null, fadeOutDuration, true);

        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            RequestStop(voice, fadeOutDuration < 0f ? voice.ConfiguredFadeOutDuration : fadeOutDuration);
        }
    }

    public void PauseAll()
    {
        allChannelsPaused = true;
        for (var i = 0; i < activeVoices.Count; i++)
        {
            PauseVoice(activeVoices[i], PauseReason.Master);
        }
    }

    public void ResumeAll()
    {
        allChannelsPaused = false;
        for (var i = 0; i < activeVoices.Count; i++)
        {
            ResumeVoice(activeVoices[i], PauseReason.Master);
        }
    }

    internal void SetSystemPaused(bool paused)
    {
        RebuildGlobalPauseExclusionCacheIfNeeded();
        systemPaused = paused;
        ApplySystemPausePolicy();
    }

    private void ApplySystemPausePolicy()
    {
        for (var i = 0; i < activeVoices.Count; i++)
        {
            var voice = activeVoices[i];
            if (systemPaused && !IsExcludedFromGlobalPause(voice.Source.outputAudioMixerGroup))
            {
                PauseVoice(voice, PauseReason.System);
            }
            else
            {
                ResumeVoice(voice, PauseReason.System);
            }
        }
    }

    private bool IsExcludedFromGlobalPause(AudioMixerGroup mixerGroup)
    {
        return audioMixer != null
               && mixerGroup != null
               && globalPauseExcludedGroupSet.Contains(mixerGroup);
    }

    private void RebuildGlobalPauseExclusionCache()
    {
        globalPauseExclusionCacheDirty = false;
        globalPauseExcludedGroupSet.Clear();
        if (globalPauseExcludedGroups == null)
        {
            globalPauseExcludedGroups = new List<AudioMixerGroup>();
            return;
        }

        for (var i = 0; i < globalPauseExcludedGroups.Count; i++)
        {
            var group = globalPauseExcludedGroups[i];
            if (group != null)
            {
                globalPauseExcludedGroupSet.Add(group);
            }
        }
    }

    private void RebuildGlobalPauseExclusionCacheIfNeeded()
    {
        if (globalPauseExclusionCacheDirty)
        {
            RebuildGlobalPauseExclusionCache();
        }
    }

    private void RefreshGlobalPauseExclusionsIfNeeded()
    {
        if (!globalPauseExclusionCacheDirty)
        {
            return;
        }

        RebuildGlobalPauseExclusionCache();
        ApplySystemPausePolicy();
    }

    public void Stop(AudioMixerGroup mixerGroup, float fadeOutDuration = -1f)
    {
        StopManagedMusic(mixerGroup, fadeOutDuration, false);

        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            var voice = activeVoices[i];
            if (voice.Source.outputAudioMixerGroup == mixerGroup)
            {
                RequestStop(voice, fadeOutDuration < 0f ? voice.ConfiguredFadeOutDuration : fadeOutDuration);
            }
        }
    }

    public void Pause(AudioMixerGroup mixerGroup)
    {
        if (mixerGroup == null)
        {
            return;
        }

        pausedMixerGroups.Add(mixerGroup);
        for (var i = 0; i < activeVoices.Count; i++)
        {
            var voice = activeVoices[i];
            if (voice.Source.outputAudioMixerGroup == mixerGroup)
            {
                PauseVoice(voice, PauseReason.MixerGroup);
            }
        }
    }

    public void Resume(AudioMixerGroup mixerGroup)
    {
        if (mixerGroup == null)
        {
            return;
        }

        pausedMixerGroups.Remove(mixerGroup);
        for (var i = 0; i < activeVoices.Count; i++)
        {
            var voice = activeVoices[i];
            if (voice.Source.outputAudioMixerGroup == mixerGroup)
            {
                ResumeVoice(voice, PauseReason.MixerGroup);
            }
        }
    }

    public bool IsMixerGroupPaused(AudioMixerGroup mixerGroup)
    {
        return mixerGroup != null && pausedMixerGroups.Contains(mixerGroup);
    }

    public int GetActiveVoiceCount(AudioMixerGroup mixerGroup)
    {
        if (mixerGroup == null)
        {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < activeVoices.Count; i++)
        {
            if (activeVoices[i].Source.outputAudioMixerGroup == mixerGroup)
            {
                count++;
            }
        }

        return count;
    }

    internal bool IsHandleValid(AudioHandle handle)
    {
        return TryGetVoice(handle, out _);
    }

    internal bool IsHandlePlaying(AudioHandle handle)
    {
        return TryGetVoice(handle, out var voice) && voice.Source.isPlaying;
    }

    private Voice AcquireVoice()
    {
        Voice voice;

        if (availableVoices.Count > 0)
        {
            voice = availableVoices.Pop();
        }
        else if (allVoices.Count < maximumCapacity)
        {
            voice = CreateVoice();
        }
        else
        {
            return null;
        }

        unchecked
        {
            voice.Generation++;
            if (voice.Generation == 0)
            {
                voice.Generation = 1;
            }
        }

        voice.InUse = true;
        voice.GameObject.SetActive(true);
        activeVoices.Add(voice);
        return voice;
    }

    private static void StopManagedMusic(
        AudioMixerGroup mixerGroup,
        float fadeOutDuration,
        bool stopAll)
    {
        var musicManager = MusicManager.Instance;
        if (musicManager == null)
        {
            return;
        }

        if (!stopAll)
        {
            musicManager.StopMixerGroup(mixerGroup, fadeOutDuration);
            return;
        }

        var resolvedFadeOutDuration = fadeOutDuration >= 0f
            ? fadeOutDuration
            : musicManager.CurrentPlaylist != null
                ? musicManager.CurrentPlaylist.TransitionDuration
                : 0f;
        musicManager.StopMusic(resolvedFadeOutDuration);
    }

    private Voice CreateVoice()
    {
        var id = allVoices.Count;
        var voiceObject = new GameObject($"Pooled Audio Voice {id + 1}");

        voiceObject.transform.SetParent(transform, false);
        var source = voiceObject.AddComponent<AudioSource>();
        source.playOnAwake = false;

        var voice = new Voice(id, voiceObject, source);
        allVoices.Add(voice);
        ResetVoice(voice);
        voiceObject.SetActive(false);
        return voice;
    }

    private void ConfigureVoice(Voice voice, AudioClip clip, AudioPlaySettings settings)
    {
        ResetVoice(voice);

        var rotation = IsZeroQuaternion(settings.Rotation) ? Quaternion.identity : settings.Rotation;
        var minDistance = settings.MinDistance > 0f ? settings.MinDistance : 1f;
        var maxDistance = settings.MaxDistance > 0f ? settings.MaxDistance : 500f;

        ConfigureSource(voice.Source, clip, settings, minDistance, maxDistance, settings.Loop);

        voice.Source.transform.SetPositionAndRotation(settings.WorldPosition, rotation);

        voice.BaseVolume = Mathf.Clamp01(settings.Volume);
        voice.Category = settings.Category;
        voice.Loop = settings.Loop;
        voice.ConfiguredFadeOutDuration = Mathf.Max(0f, settings.FadeOutDuration);
        voice.Follow = settings.PositionMode == FeedbackPositionMode.FollowTransform ? settings.FollowTransform : null;
        voice.WasFollowing = voice.Follow != null;

        if (voice.Follow != null)
        {
            voice.Source.transform.SetPositionAndRotation(voice.Follow.position, voice.Follow.rotation);
        }

        var fadeInDuration = Mathf.Max(0f, settings.FadeInDuration);
        if (fadeInDuration > 0f)
        {
            voice.FadeMultiplier = 0f;
            voice.FadeStartMultiplier = 0f;
            voice.FadeTargetMultiplier = 1f;
            voice.FadeDuration = fadeInDuration;
            voice.FadeElapsed = 0f;
            voice.Fading = true;
            voice.FadingOut = false;
        }
        else
        {
            voice.FadeMultiplier = 1f;
        }

        ApplyVolume(voice);
    }

    private static void ConfigureSource(
        AudioSource source,
        AudioClip clip,
        AudioPlaySettings settings,
        float minDistance,
        float maxDistance,
        bool loop)
    {
        source.enabled = true;
        source.clip = clip;
        source.outputAudioMixerGroup = settings.MixerGroup;
        var pitch = Mathf.Approximately(settings.Pitch, 0f) ? 1f : settings.Pitch;
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        source.loop = loop;
        source.spatialBlend = Mathf.Clamp01(settings.SpatialBlend);
        source.minDistance = minDistance;
        source.maxDistance = Mathf.Max(minDistance, maxDistance);
        source.rolloffMode = settings.RolloffMode;
        source.dopplerLevel = Mathf.Max(0f, settings.DopplerLevel);
    }

    private bool UpdateFade(Voice voice, float deltaTime)
    {
        if (!voice.Fading)
        {
            return false;
        }

        voice.FadeElapsed += deltaTime;
        var progress = voice.FadeDuration <= 0f ? 1f : Mathf.Clamp01(voice.FadeElapsed / voice.FadeDuration);
        voice.FadeMultiplier = Mathf.Lerp(voice.FadeStartMultiplier, voice.FadeTargetMultiplier, progress);
        ApplyVolume(voice);

        if (progress < 1f)
        {
            return false;
        }

        voice.Fading = false;
        if (!voice.FadingOut)
        {
            return false;
        }

        ReleaseVoice(voice);
        return true;
    }

    private void RequestStop(Voice voice, float fadeOutDuration)
    {
        if (!voice.InUse)
        {
            return;
        }

        fadeOutDuration = Mathf.Max(0f, fadeOutDuration);
        if (voice.FadingOut)
        {
            if (fadeOutDuration <= 0f || voice.FadeMultiplier <= 0f || voice.IsPaused)
            {
                voice.Source.Stop();
                ReleaseVoice(voice);
                return;
            }

            var remainingFade = Mathf.Max(0f, voice.FadeDuration - voice.FadeElapsed);
            if (remainingFade <= 0f)
            {
                voice.Source.Stop();
                ReleaseVoice(voice);
            }
            else if (fadeOutDuration < remainingFade)
            {
                voice.FadeStartMultiplier = voice.FadeMultiplier;
                voice.FadeDuration = fadeOutDuration;
                voice.FadeElapsed = 0f;
            }

            return;
        }

        if (fadeOutDuration <= 0f || voice.FadeMultiplier <= 0f || voice.IsPaused)
        {
            voice.Source.Stop();
            ReleaseVoice(voice);
            return;
        }

        voice.FadeStartMultiplier = voice.FadeMultiplier;
        voice.FadeTargetMultiplier = 0f;
        voice.FadeDuration = fadeOutDuration;
        voice.FadeElapsed = 0f;
        voice.Fading = true;
        voice.FadingOut = true;
    }

    private void PauseVoice(Voice voice, PauseReason reason)
    {
        if ((voice.PauseReasons & reason) != 0)
        {
            return;
        }

        var wasPaused = voice.IsPaused;

        // Let Update return a one-shot that ended between frames instead of
        // turning that already-finished source into a permanently paused voice.
        var externallySuspended = applicationPaused
                                  || !applicationHasFocus
                                  || AudioListener.pause
                                  || focusResumeGraceFrames > 0;
        if (!wasPaused
            && voice.HasStarted
            && !voice.Source.isPlaying
            && !voice.Loop
            && !externallySuspended)
        {
            return;
        }

        voice.PauseReasons |= reason;
        if (!wasPaused)
        {
            voice.Source.Pause();
        }
    }

    private static void ResumeVoice(Voice voice, PauseReason reason)
    {
        if ((voice.PauseReasons & reason) == 0)
        {
            return;
        }

        voice.PauseReasons &= ~reason;
        if (voice.IsPaused)
        {
            return;
        }

        voice.Source.UnPause();
    }

    private void ReleaseVoice(Voice voice)
    {
        if (!voice.InUse)
        {
            return;
        }

        voice.Source.Stop();
        ResetVoice(voice);
        voice.InUse = false;
        activeVoices.Remove(voice);
        voice.GameObject.SetActive(false);
        availableVoices.Push(voice);
    }

    private void ReleaseAllVoicesImmediate()
    {
        for (var i = activeVoices.Count - 1; i >= 0; i--)
        {
            ReleaseVoice(activeVoices[i]);
        }
    }

    private void ResetVoice(Voice voice)
    {
        ResetSource(voice.Source);

        voice.Source.transform.SetParent(transform, false);
        voice.Source.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        voice.Follow = null;
        voice.WasFollowing = false;
        voice.PauseReasons = PauseReason.None;
        voice.HasStarted = false;
        voice.AllowCompletionWhileUnfocused = false;
        voice.Loop = false;
        voice.BaseVolume = 1f;
        voice.Category = FeedbackAudioCategory.SoundEffects;
        voice.FadeMultiplier = 1f;
        voice.FadeStartMultiplier = 1f;
        voice.FadeTargetMultiplier = 1f;
        voice.FadeDuration = 0f;
        voice.FadeElapsed = 0f;
        voice.Fading = false;
        voice.FadingOut = false;
        voice.ConfiguredFadeOutDuration = 0f;
    }

    private static void ApplyVolume(Voice voice)
    {
        voice.Source.volume = Mathf.Clamp01(voice.BaseVolume * voice.FadeMultiplier
            * FeedbackSettings.AudioMultiplier(voice.Category));
    }

    private static void ResetSource(AudioSource source)
    {
        source.Stop();
        source.clip = null;
        source.outputAudioMixerGroup = null;
        source.volume = 1f;
        source.pitch = 1f;
        source.loop = false;
        source.spatialBlend = 0f;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 1f;
        source.playOnAwake = false;
    }

    private bool TryGetVoice(AudioHandle handle, out Voice voice)
    {
        if (handle.Owner != this)
        {
            voice = null;
            return false;
        }

        var voiceId = handle.VoiceId;
        if (voiceId >= 0 && voiceId < allVoices.Count)
        {
            voice = allVoices[voiceId];
            return voice.InUse && voice.Generation == handle.Generation;
        }

        voice = null;
        return false;
    }

    private void NormalizeCapacity()
    {
        maximumCapacity = Mathf.Max(1, maximumCapacity);
        initialCapacity = Mathf.Clamp(initialCapacity, 0, maximumCapacity);
    }

    private static bool IsZeroQuaternion(Quaternion rotation)
    {
        return Mathf.Approximately(rotation.x, 0f)
               && Mathf.Approximately(rotation.y, 0f)
               && Mathf.Approximately(rotation.z, 0f)
               && Mathf.Approximately(rotation.w, 0f);
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    private static void Warn(string message)
    {
        UnityEngine.Debug.LogWarning(message);
    }

    private sealed class Voice
    {
        public readonly int Id;
        public readonly GameObject GameObject;
        public readonly AudioSource Source;

        public uint Generation;
        public bool InUse;
        public PauseReason PauseReasons;
        public bool IsPaused => PauseReasons != PauseReason.None;
        public bool HasStarted;
        public bool AllowCompletionWhileUnfocused;
        public bool Loop;
        public Transform Follow;
        public bool WasFollowing;
        public float BaseVolume;
        public FeedbackAudioCategory Category;
        public float ConfiguredFadeOutDuration;
        public float FadeMultiplier;
        public float FadeStartMultiplier;
        public float FadeTargetMultiplier;
        public float FadeDuration;
        public float FadeElapsed;
        public bool Fading;
        public bool FadingOut;

        public Voice(int id, GameObject gameObject, AudioSource source)
        {
            Id = id;
            GameObject = gameObject;
            Source = source;
        }
    }
}
}
