using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyFeedbacks.Editor
{

/// <summary>
/// Editor-only preview player. It intentionally does not use a scene AudioManager.
/// </summary>
[InitializeOnLoad]
internal static class FeedbackPresetPreview
{
    private static readonly List<PreviewVoice> Voices = new List<PreviewVoice>();
    private static double lastUpdateTime;

    static FeedbackPresetPreview()
    {
        EditorApplication.update += Update;
        EditorApplication.quitting += StopImmediate;
        AssemblyReloadEvents.beforeAssemblyReload += StopImmediate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    public static bool IsPlaying => Voices.Count > 0;

    public static void Play(FeedbackPreset preset)
    {
        StopImmediate();

        if (preset == null)
        {
            return;
        }

        var entries = preset.AudioEntries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null || !entry.IsEnabled)
            {
                continue;
            }

            var clip = entry.GetRandomClip();
            if (clip == null)
            {
                continue;
            }

            var previewObject = new GameObject($"Feedback Preview - {clip.name}")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            var source = previewObject.AddComponent<AudioSource>();
            ConfigureSource(source, entry, clip, entry.GetRandomPitch(), entry.Loop);

            var fadeIn = Mathf.Max(0f, entry.FadeInDuration);
            var voice = new PreviewVoice(
                previewObject,
                source,
                entry.GetRandomVolume(),
                Mathf.Max(0f, entry.FadeOutDuration),
                entry.Loop,
                fadeIn);

            source.volume = fadeIn > 0f ? 0f : voice.BaseVolume;
            Voices.Add(voice);
            source.Play();
            voice.HasStarted = source.isPlaying;
        }

        lastUpdateTime = EditorApplication.timeSinceStartup;
    }

    public static void Stop()
    {
        for (var i = Voices.Count - 1; i >= 0; i--)
        {
            var voice = Voices[i];
            if (voice.FadeOutDuration <= 0f || voice.FadeMultiplier <= 0f)
            {
                DestroyVoiceAt(i);
                continue;
            }

            voice.Stopping = true;
            voice.FadeStartMultiplier = voice.FadeMultiplier;
            voice.FadeElapsed = 0f;
        }
    }

    public static void StopImmediate()
    {
        for (var i = Voices.Count - 1; i >= 0; i--)
        {
            DestroyVoiceAt(i);
        }
    }

    private static void Update()
    {
        var now = EditorApplication.timeSinceStartup;
        var deltaTime = Mathf.Max(0f, (float)(now - lastUpdateTime));
        lastUpdateTime = now;

        for (var i = Voices.Count - 1; i >= 0; i--)
        {
            var voice = Voices[i];
            if (voice.Source == null)
            {
                DestroyVoiceAt(i);
                continue;
            }

            if (voice.Source.isPlaying)
            {
                voice.HasStarted = true;
            }

            if (voice.Stopping)
            {
                if (voice.HasStarted && !voice.Source.isPlaying)
                {
                    DestroyVoiceAt(i);
                    continue;
                }

                voice.FadeElapsed += deltaTime;
                var progress = Mathf.Clamp01(voice.FadeElapsed / voice.FadeOutDuration);
                voice.FadeMultiplier = Mathf.Lerp(voice.FadeStartMultiplier, 0f, progress);
                ApplyVolume(voice);

                if (progress >= 1f)
                {
                    DestroyVoiceAt(i);
                }

                continue;
            }

            if (voice.FadeInDuration > 0f && voice.FadeMultiplier < 1f)
            {
                voice.FadeElapsed += deltaTime;
                voice.FadeMultiplier = Mathf.Clamp01(voice.FadeElapsed / voice.FadeInDuration);
                ApplyVolume(voice);
            }

            if (!voice.Loop && voice.HasStarted && !voice.Source.isPlaying)
            {
                DestroyVoiceAt(i);
            }
        }
    }

    private static void DestroyVoiceAt(int index)
    {
        var voice = Voices[index];
        Voices.RemoveAt(index);

        if (voice.GameObject != null)
        {
            if (voice.Source != null)
            {
                voice.Source.Stop();
            }

            Object.DestroyImmediate(voice.GameObject);
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
        {
            StopImmediate();
        }
    }

    private static void ConfigureSource(
        AudioSource source,
        AudioFeedbackEntry entry,
        AudioClip clip,
        float pitch,
        bool loop)
    {
        source.playOnAwake = false;
        source.clip = clip;
        source.outputAudioMixerGroup = entry.MixerGroup;
        source.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        source.loop = loop;
        // Inspector preview is intentionally non-spatialized so it is centered and
        // independent of the active scene or Scene View AudioListener transform.
        source.spatialBlend = 0f;
        source.minDistance = Mathf.Max(0.01f, entry.MinDistance);
        source.maxDistance = Mathf.Max(source.minDistance, entry.MaxDistance);
        source.rolloffMode = entry.RolloffMode;
        source.dopplerLevel = Mathf.Max(0f, entry.DopplerLevel);
    }

    private static void ApplyVolume(PreviewVoice voice)
    {
        voice.Source.volume = Mathf.Clamp01(voice.BaseVolume * voice.FadeMultiplier);
    }

    private sealed class PreviewVoice
    {
        public readonly GameObject GameObject;
        public readonly AudioSource Source;
        public readonly float BaseVolume;
        public readonly float FadeOutDuration;
        public readonly float FadeInDuration;
        public readonly bool Loop;

        public bool HasStarted;
        public bool Stopping;
        public float FadeMultiplier;
        public float FadeStartMultiplier;
        public float FadeElapsed;

        public PreviewVoice(
            GameObject gameObject,
            AudioSource source,
            float baseVolume,
            float fadeOutDuration,
            bool loop,
            float fadeInDuration)
        {
            GameObject = gameObject;
            Source = source;
            BaseVolume = baseVolume;
            FadeOutDuration = fadeOutDuration;
            Loop = loop;
            FadeInDuration = fadeInDuration;
            FadeMultiplier = fadeInDuration > 0f ? 0f : 1f;
        }
    }
}
}
