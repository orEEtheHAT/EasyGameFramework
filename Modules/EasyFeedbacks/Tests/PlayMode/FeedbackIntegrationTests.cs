using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EasyFeedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.TestTools;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace EasyGameFramework.Tests.Feedbacks
{

public sealed class TestPlayerContext : MonoBehaviour, IFeedbackContextProvider
{
    public Gamepad Gamepad;
    public FeedbackContext Received;
    public int Calls;

    public FeedbackContext Prepare(FeedbackContext context)
    {
        Calls++;
        Received = context;
        return context.WithDefaultGamepad(Gamepad);
    }
}

public sealed partial class FeedbackIntegrationTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly Dictionary<int, Vector2> motors = new Dictionary<int, Vector2>();
    private AudioManager audio;
    private GamepadVibrationManager haptics;
    private VisualFeedbackManager visuals;
    private Gamepad first;
    private Gamepad second;
    private Gamepad previousGamepad;
    private FeedbackSettingsState previousSettings;
    private float previousTimeScale;
    private bool previousListenerPause;

    [SetUp]
    public unsafe void SetUp()
    {
        previousSettings = FeedbackSettings.Capture();
        previousTimeScale = Time.timeScale;
        previousListenerPause = AudioListener.pause;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        FeedbackSettings.Apply(FeedbackSettingsState.Default);
        Assert.That(AudioManager.Instance, Is.Null, "Tests require their own empty test scene.");
        Assert.That(GamepadVibrationManager.Instance, Is.Null);
        Assert.That(VisualFeedbackManager.Instance, Is.Null);
        audio = Own(new GameObject("Test Audio")).AddComponent<AudioManager>();
        haptics = Own(new GameObject("Test Haptics")).AddComponent<GamepadVibrationManager>();
        visuals = Own(new GameObject("Test Visuals")).AddComponent<VisualFeedbackManager>();
        previousGamepad = Gamepad.current;
        first = InputSystem.AddDevice<Gamepad>("FeedbackTestPlayerOne");
        second = InputSystem.AddDevice<Gamepad>("FeedbackTestPlayerTwo");
        motors[first.deviceId] = Vector2.zero;
        motors[second.deviceId] = Vector2.zero;
        InputSystem.onDeviceCommand += CaptureMotorCommand;
        // Exercises real playback lifecycles even when the test runner is not the focused window.
        AudioManager.BeginEditorPreviewPlayback();
        GamepadVibrationManager.BeginEditorPreviewPlayback();
    }

    [TearDown]
    public unsafe void TearDown()
    {
        FeedbackService.StopAll(true);
        while (FeedbackService.AreAllPaused) FeedbackService.ResumeAll();
        for (var i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        }
        owned.Clear();
        AudioManager.EndEditorPreviewPlayback();
        GamepadVibrationManager.EndEditorPreviewPlayback();
        InputSystem.onDeviceCommand -= CaptureMotorCommand;
        if (first != null && first.added) InputSystem.RemoveDevice(first);
        if (second != null && second.added) InputSystem.RemoveDevice(second);
        if (previousGamepad != null && previousGamepad.added) previousGamepad.MakeCurrent();
        motors.Clear();
        FeedbackSettings.Restore(previousSettings);
        Time.timeScale = previousTimeScale;
        AudioListener.pause = previousListenerPause;
    }

    private unsafe long? CaptureMotorCommand(InputDevice device, InputDeviceCommand* command)
    {
        if (!motors.ContainsKey(device.deviceId) || command->type != new FourCC('R', 'M', 'B', 'L'))
            return null;
        var values = (float*)((byte*)command + InputDeviceCommand.BaseCommandSize);
        motors[device.deviceId] = new Vector2(values[0], values[1]);
        return InputDeviceCommand.GenericSuccess;
    }

    [Test]
    public void ContextPlayerDefaultPreservesAllAuthoredValuesAndExplicitGamepad()
    {
        var source = Own(new GameObject("Source")).transform;
        var follow = Own(new GameObject("Follow")).transform;
        var context = new FeedbackContext(new Vector3(3, 4, 5), Quaternion.Euler(5, 20, 30),
            source, follow, 0.6f, 0.3f, new Vector3(2, 3, 4), FeedbackPositionMode.FollowTransform);
        var prepared = context.WithDefaultGamepad(first);
        AssertContext(context, prepared);
        Assert.That(prepared.TargetGamepad, Is.SameAs(first));
        var explicitContext = prepared.WithDefaultGamepad(second);
        AssertContext(prepared, explicitContext);
        Assert.That(explicitContext.TargetGamepad, Is.SameAs(first));
        Assert.That(explicitContext.WithGamepad(second).TargetGamepad, Is.SameAs(second));
        var defaultContext = default(FeedbackContext).WithDefaultGamepad(first);
        Assert.That(defaultContext.Intensity, Is.EqualTo(1f));
        Assert.That(defaultContext.AudioMultiplier, Is.EqualTo(1f));
        Assert.That(defaultContext.VisualScale, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void PlayerWithoutGamepadDoesNotVibrateAnotherPlayersCurrentGamepad()
    {
        second.MakeCurrent();
        var provider = Own(new GameObject("Keyboard player context")).AddComponent<TestPlayerContext>();
        var preset = HapticPreset();
        var emitter = Own(new GameObject("Keyboard player emitter")).AddComponent<FeedbackEmitter>();
        emitter.Preset = preset;
        emitter.ContextProvider = provider;
        var handle = emitter.PlayFeedback();
        Assert.That(handle.IsValid, Is.False);
        Assert.That(haptics.ActiveVibrations, Is.Zero);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0f, 0f);

        var source = Own(new GameObject("Origin")).transform;
        var follow = Own(new GameObject("Follow")).transform;
        var original = new FeedbackContext(new Vector3(1, 3, 5), Quaternion.Euler(10, 20, 30),
            source, follow, .6f, .3f, new Vector3(2, 3, 4), FeedbackPositionMode.FollowTransform);
        var none = provider.Prepare(original);
        Assert.That(none.HasExplicitGamepadTarget, Is.True);
        Assert.That(none.TargetGamepad, Is.Null);
        AssertContext(original, none);
        provider.Gamepad = first;
        var stillNone = provider.Prepare(none);
        Assert.That(stillNone.HasExplicitGamepadTarget, Is.True);
        Assert.That(stillNone.TargetGamepad, Is.Null);
        AssertContext(original, stillNone);
        Assert.That(FeedbackService.Play(preset, stillNone).IsValid, Is.False);
        Assert.That(original.WithGamepad(second).WithoutGamepad().WithDefaultGamepad(first).TargetGamepad, Is.Null);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0f, 0f);

        var targeted = original.WithGamepad(second);
        Assert.That(targeted.HasExplicitGamepadTarget, Is.True);
        var preserved = provider.Prepare(targeted);
        Assert.That(preserved.TargetGamepad, Is.SameAs(second));
        Assert.That(preserved.HasExplicitGamepadTarget, Is.True);
        AssertContext(targeted, preserved);
        var explicitOverride = none.WithGamepad(first);
        Assert.That(explicitOverride.HasExplicitGamepadTarget, Is.True);
        var targetedHandle = FeedbackService.Play(preset, explicitOverride);
        Assert.That(targetedHandle.IsValid, Is.True);
        AssertMotor(first, .6f, .3f);
        AssertMotor(second, 0f, 0f);
    }

    [Test]
    public void LegacyUnaddressedAndWithGamepadNullStillUseCurrentGamepad()
    {
        second.MakeCurrent();
        var preset = HapticPreset();
        var contexts = new[]
        {
            default(FeedbackContext),
            FeedbackContext.From(Own(new GameObject("Legacy source")).transform),
            default(FeedbackContext).WithoutGamepad().WithGamepad(null)
        };
        foreach (var context in contexts)
        {
            Assert.That(context.HasExplicitGamepadTarget, Is.False);
            Assert.That(context.TargetGamepad, Is.Null);
            var handle = FeedbackService.Play(preset, context);
            Assert.That(handle.IsValid, Is.True);
            AssertMotor(first, 0f, 0f);
            AssertMotor(second, 1f, .5f);
            handle.Stop(0f);
        }
        var direct = haptics.Play(.7f, .2f, .2f);
        Assert.That(direct.IsValid, Is.True);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, .7f, .2f);
    }

    [Test]
    public void SettingsCaptureRestoreValidatesWithoutMutatingSnapshots()
    {
        var initial = FeedbackSettings.Capture();
        var values = initial;
        values.MasterVolume = -2f;
        values.MusicVolume = 4f;
        values.SoundEffectsVolume = float.NaN;
        values.HapticsIntensity = float.NegativeInfinity;
        values.HapticsEnabled = false;
        var events = 0;
        Action<FeedbackSettingsState> handler = value => events++;
        FeedbackSettings.Changed += handler;
        try
        {
            FeedbackSettings.Restore(values);
            var actual = FeedbackSettings.Capture();
            Assert.That(actual.MasterVolume, Is.Zero);
            Assert.That(actual.MusicVolume, Is.EqualTo(1f));
            Assert.That(actual.SoundEffectsVolume, Is.EqualTo(1f));
            Assert.That(actual.HapticsIntensity, Is.EqualTo(1f));
            Assert.That(actual.HapticsEnabled, Is.False);
            Assert.That(initial.MasterVolume, Is.EqualTo(1f));
            FeedbackSettings.Apply(actual);
            Assert.That(events, Is.EqualTo(1), "Applying identical validated state should not notify again.");
            FeedbackSettings.Restore(initial);
            Assert.That(FeedbackSettings.Current.HapticsEnabled, Is.True);
            Assert.That(events, Is.EqualTo(2));
        }
        finally { FeedbackSettings.Changed -= handler; }
    }

    [Test]
    public void SettingsProfileOnlySuppliesInitialValues()
    {
        var profile = Own(ScriptableObject.CreateInstance<FeedbackSettingsProfile>());
        var initial = profile.Values;
        initial.MasterVolume = 0.65f;
        Set(profile, "values", initial);
        profile.Apply();
        Assert.That(FeedbackSettings.Current.MasterVolume, Is.EqualTo(0.65f));
        var changed = FeedbackSettings.Current;
        changed.MasterVolume = 0.2f;
        FeedbackSettings.Apply(changed);
        Assert.That(profile.Values.MasterVolume, Is.EqualTo(0.65f));
        Assert.That(FeedbackSettings.Current.MasterVolume, Is.EqualTo(0.2f));
    }

    [Test]
    public void AudioPreferencesAffectActiveFutureAndReusedVoicesWithoutChangingAuthoredVolume()
    {
        var firstClip = Clip("SFX");
        var secondClip = Clip("Music");
        var options = AudioPlaySettings.Default;
        options.Volume = 0.8f;
        options.Loop = true;
        var sfx = audio.Play(firstClip, options);
        var values = FeedbackSettings.Current;
        values.MasterVolume = 0.5f;
        values.SoundEffectsVolume = 0.25f;
        values.MusicVolume = 0.6f;
        FeedbackSettings.Apply(values);
        Assert.That(Source(firstClip).volume, Is.EqualTo(0.1f).Within(0.0001f));
        options.Category = FeedbackAudioCategory.Music;
        var music = audio.Play(secondClip, options);
        Assert.That(Source(secondClip).volume, Is.EqualTo(0.24f).Within(0.0001f));
        music.SetVolume(0.4f);
        Assert.That(Source(secondClip).volume, Is.EqualTo(0.12f).Within(0.0001f));
        FeedbackSettings.Apply(FeedbackSettingsState.Default);
        Assert.That(Source(firstClip).volume, Is.EqualTo(0.8f).Within(0.0001f));
        Assert.That(Source(secondClip).volume, Is.EqualTo(0.4f).Within(0.0001f));
        var count = audio.TotalVoices;
        music.Stop(0f);
        Assert.That(music.IsValid, Is.False);
        options.Category = FeedbackAudioCategory.SoundEffects;
        var reused = audio.Play(secondClip, options);
        music.Stop(0f);
        FeedbackSettings.Apply(values);
        Assert.That(reused.IsValid, Is.True, "Stale handles must not stop a reused slot.");
        Assert.That(Source(secondClip).volume, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(audio.TotalVoices, Is.EqualTo(count));
        Assert.That(sfx.IsValid, Is.True);
    }

    [UnityTest]
    public IEnumerator MutedLoopKeepsPauseOwnershipAndCanFinishItsStopFade()
    {
        var clip = Clip("Loop");
        var options = AudioPlaySettings.Default;
        options.Loop = true;
        var handle = audio.Play(clip, options);
        yield return null;
        Assert.That(handle.IsPlaying, Is.True, "The pooled AudioSource must actually start.");
        handle.Pause();
        FeedbackService.PauseAll();
        var values = FeedbackSettings.Current;
        values.MasterVolume = 0f;
        FeedbackSettings.Apply(values);
        FeedbackService.ResumeAll();
        Assert.That(handle.IsPlaying, Is.False, "Global resume must respect the handle pause.");
        Assert.That(handle.IsValid, Is.True);
        handle.Resume();
        Assert.That(Source(clip).volume, Is.Zero);
        handle.Stop(0.05f);
        yield return WaitUntil(() => !handle.IsValid, 2f);
        Assert.That(handle.IsValid, Is.False);
        Assert.That(audio.ActiveVoices, Is.Zero);
    }

    [UnityTest]
    public IEnumerator MutedAudioOneShotCompletesAndReturnsItsVoice()
    {
        var state = FeedbackSettings.Current;
        state.SoundEffectsVolume = 0f;
        FeedbackSettings.Apply(state);
        var clip = Clip("Silent one shot", 0.08f);
        var handle = audio.Play(clip);
        Assert.That(handle.IsValid, Is.True);
        Assert.That(Source(clip).volume, Is.Zero);
        yield return WaitUntil(() => !handle.IsValid, 3f);
        Assert.That(handle.IsValid, Is.False);
        Assert.That(audio.ActiveVoices, Is.Zero);
    }

    [UnityTest]
    public IEnumerator MusicPreferencesPreservePlaylistSelectionAndExistingSetVolume()
    {
        var clip = Clip("Playlist track", 4f);
        var playlist = Own(ScriptableObject.CreateInstance<MusicPlaylist>());
        Set(playlist, "tracks", new List<AudioClip> { clip });
        Set(playlist, "transitionDuration", 0f);
        Set(playlist, "volume", 0.8f);
        var manager = Own(new GameObject("Test Music")).AddComponent<MusicManager>();
        manager.SetFallbackPlaylist(playlist);
        yield return WaitUntil(() => manager.CurrentTrack == clip, 2f);
        Assert.That(manager.CurrentTrack, Is.SameAs(clip));
        manager.SetVolume(0.5f);
        var state = FeedbackSettings.Current;
        state.MasterVolume = 0.5f;
        state.MusicVolume = 0.25f;
        state.SoundEffectsVolume = 0f;
        FeedbackSettings.Apply(state);
        Assert.That(Source(clip).volume, Is.EqualTo(0.05f).Within(0.0001f));
        Assert.That(manager.CurrentPlaylist, Is.SameAs(playlist));
        Assert.That(manager.Volume, Is.EqualTo(0.5f));
        state.MusicVolume = 0f;
        FeedbackSettings.Apply(state);
        Assert.That(manager.CurrentTrack, Is.SameAs(clip));
        Assert.That(Source(clip).volume, Is.Zero);
        state.MusicVolume = 1f;
        FeedbackSettings.Apply(state);
        Assert.That(Source(clip).volume, Is.EqualTo(0.2f).Within(0.0001f));
        audio.enabled = false;
        Assert.That(manager.IsMusicStopped, Is.True);
        Assert.That(audio.ActiveVoices, Is.Zero);
        audio.enabled = true;
        manager.ResumeMusic();
        yield return WaitUntil(() => manager.CurrentTrack == clip, 2f);
        Assert.That(manager.CurrentTrack, Is.SameAs(clip));
        Assert.That(Source(clip).volume, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void HapticsPreferencesPreserveTwoVirtualDeviceTargetsAndIndependentPause()
    {
        second.MakeCurrent();
        var firstHandle = haptics.Play(0.8f, 0.4f, 1f, true, gamepad: first);
        var secondHandle = haptics.Play(0.2f, 0.6f, 1f, true, gamepad: second);
        AssertMotor(first, 0.8f, 0.4f);
        AssertMotor(second, 0.2f, 0.6f);
        var state = FeedbackSettings.Current;
        state.HapticsIntensity = 0.5f;
        FeedbackSettings.Apply(state);
        AssertMotor(first, 0.4f, 0.2f);
        AssertMotor(second, 0.1f, 0.3f);
        firstHandle.Pause();
        state.HapticsEnabled = false;
        FeedbackSettings.Apply(state);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0f, 0f);
        Assert.That(firstHandle.IsValid && secondHandle.IsValid, Is.True);
        state.HapticsEnabled = true;
        FeedbackSettings.Apply(state);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0.1f, 0.3f);
        firstHandle.Resume();
        AssertMotor(first, 0.4f, 0.2f);
        firstHandle.Stop(0f);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0.1f, 0.3f);
    }

    [UnityTest]
    public IEnumerator MutedHapticOneShotFinishesAndMutedLoopStopsSafely()
    {
        var state = FeedbackSettings.Current;
        state.HapticsEnabled = false;
        FeedbackSettings.Apply(state);
        var oneShot = haptics.Play(1f, 1f, 0.05f, gamepad: first);
        var loop = haptics.Play(1f, 1f, 1f, true, fadeOut: 0.05f, gamepad: second);
        Assert.That(oneShot.IsValid && loop.IsValid, Is.True);
        loop.Stop();
        yield return WaitUntil(() => !oneShot.IsValid && !loop.IsValid, 2f);
        Assert.That(oneShot.IsValid || loop.IsValid, Is.False);
        Assert.That(haptics.ActiveVibrations, Is.Zero);
        AssertMotor(first, 0f, 0f);
        AssertMotor(second, 0f, 0f);
    }

    [Test]
    public void EmitterUsesPlayerProviderAndPreservesItsAuthoredContext()
    {
        second.MakeCurrent();
        var preset = HapticPreset();
        var emitter = Own(new GameObject("Emitter")).AddComponent<FeedbackEmitter>();
        var provider = Own(new GameObject("Player provider")).AddComponent<TestPlayerContext>();
        provider.Gamepad = first;
        emitter.ContextProvider = provider;
        emitter.Preset = preset;
        emitter.transform.position = new Vector3(2, 4, 6);
        emitter.Follow = Own(new GameObject("Follow")).transform;
        emitter.Positioning = FeedbackPositionMode.FollowTransform;
        emitter.Intensity = 0.5f;
        emitter.AudioMultiplier = 0.3f;
        emitter.VisualScale = new Vector3(2, 3, 4);
        emitter.StopOnDisable = true;
        var launch = emitter.PlayFeedback();
        Assert.That(provider.Calls, Is.EqualTo(1));
        AssertContext(FeedbackContext.From(emitter.transform, emitter.Follow, 0.5f, 0.3f,
            emitter.VisualScale, FeedbackPositionMode.FollowTransform), provider.Received);
        AssertMotor(first, 0.5f, 0.25f);
        AssertMotor(second, 0f, 0f);
        emitter.enabled = false;
        Assert.That(launch.IsValid, Is.False);
        AssertMotor(first, 0f, 0f);
        emitter.enabled = true;
        provider.enabled = false;
        emitter.PlayFeedback();
        Assert.That(provider.Calls, Is.EqualTo(1));
        AssertMotor(second, 0.5f, 0.25f);
    }

    [Test]
    public void PresetStopsAllLayersAndDoesNotLetOldHandleStopAReusedLaunch()
    {
        var preset = HapticPreset();
        var clip = Clip("Preset audio");
        var audioEntry = new AudioFeedbackEntry();
        Set(audioEntry, "clips", new List<AudioClip> { clip });
        Set(audioEntry, "loop", true);
        Set(preset, "audioEntries", new List<AudioFeedbackEntry> { audioEntry });
        var particle = Particles();
        var visualEntry = new VisualFeedbackEntry();
        Set(visualEntry, "particleSystemPrefab", particle);
        Set(visualEntry, "lifetime", VisualFeedbackLifetime.LoopUntilStopped);
        Set(visualEntry, "stopMode", VisualFeedbackStopMode.ClearImmediately);
        Set(preset, "visualEntries", new List<VisualFeedbackEntry> { visualEntry });
        FeedbackService.Prewarm(preset);
        var context = FeedbackContext.From(Own(new GameObject("Preset source")).transform).WithGamepad(first);
        var handle = FeedbackService.Play(preset, context);
        Assert.That(handle.IsValid && handle.CanPause, Is.True);
        Assert.That(audio.ActiveVoices, Is.EqualTo(1));
        Assert.That(visuals.ActiveInstances, Is.EqualTo(1));
        Assert.That(haptics.ActiveVibrations, Is.EqualTo(1));
        handle.Pause();
        Assert.That(Source(clip).isPlaying, Is.False);
        AssertMotor(first, 0f, 0f);
        handle.Resume();
        handle.Stop(0f);
        Assert.That(handle.IsValid, Is.False);
        Assert.That(audio.ActiveVoices + visuals.ActiveInstances + haptics.ActiveVibrations, Is.Zero);
        var total = visuals.TotalInstances;
        var reused = FeedbackService.Play(preset, context);
        handle.Stop(0f);
        Assert.That(reused.IsValid, Is.True);
        Assert.That(visuals.TotalInstances, Is.EqualTo(total));
    }

    [UnityTest]
    public IEnumerator ParticleOneShotFollowsPausesAndReturnsToPool()
    {
        var source = Own(new GameObject("Particle follow")).transform;
        var resource = Particles(false);
        var context = FeedbackContext.From(source, source);
        var handle = visuals.Play(resource, context, positionMode: FeedbackPositionMode.FollowTransform);
        var clone = visuals.GetComponentsInChildren<ParticleSystem>(true).Single();
        source.position = new Vector3(4, 5, 6);
        yield return null;
        Assert.That(clone.transform.position, Is.EqualTo(source.position));
        handle.Pause();
        var time = clone.time;
        yield return new WaitForSecondsRealtime(0.12f);
        Assert.That(handle.IsValid, Is.True);
        Assert.That(clone.time, Is.EqualTo(time).Within(0.001f));
        handle.Resume();
        yield return WaitUntil(() => !handle.IsValid, 2f);
        Assert.That(handle.IsValid, Is.False);
        Assert.That(visuals.AvailableInstances, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator VfxComponentTimingRespectsPauseStopTailAndOriginalDeadline()
    {
        // No graph asset: this tests the VFX component/pool lifecycle, not rendered GPU particles.
        var resourceObject = Own(new GameObject("VFX resource"));
        resourceObject.SetActive(false);
        var resource = resourceObject.AddComponent<VisualEffect>();
        var handle = visuals.Play(resource, default, oneShotDuration: 0.2f, stopTailDuration: 5f);
        handle.Pause();
        yield return new WaitForSecondsRealtime(0.25f);
        Assert.That(handle.IsValid, Is.True);
        handle.Resume();
        handle.Stop();
        yield return WaitUntil(() => !handle.IsValid, 2f);
        Assert.That(handle.IsValid, Is.False, "A stop tail must not extend the original one-shot deadline.");
        Assert.That(visuals.ActiveInstances, Is.Zero);
    }

    [UnityTest]
    public IEnumerator DisableAndDestroyReleaseVoicesAndSilenceTargetedMotors()
    {
        var options = AudioPlaySettings.Default;
        options.Loop = true;
        var audioHandle = audio.Play(Clip("Disable loop"), options);
        var vibrationHandle = haptics.Play(1f, 0.5f, 1f, true, gamepad: first);
        var visualHandle = visuals.Play(Particles(), default, VisualFeedbackLifetime.LoopUntilStopped);
        AssertMotor(first, 1f, 0.5f);
        audio.enabled = false;
        haptics.enabled = false;
        visuals.enabled = false;
        Assert.That(audioHandle.IsValid || vibrationHandle.IsValid || visualHandle.IsValid, Is.False);
        AssertMotor(first, 0f, 0f);
        haptics.enabled = true;
        var next = haptics.Play(0.5f, 0.5f, 1f, true, gamepad: second);
        AssertMotor(second, 0.5f, 0.5f);
        Object.Destroy(haptics.gameObject);
        yield return null;
        Assert.That(next.IsValid, Is.False);
        Assert.That(GamepadVibrationManager.Instance, Is.Null);
        AssertMotor(second, 0f, 0f);
    }

    private AudioClip Clip(string name, float duration = 2f)
    {
        var count = Mathf.CeilToInt(44100 * duration);
        var clip = Own(AudioClip.Create(name, count, 1, 44100, false));
        var samples = new float[count];
        for (var i = 0; i < count; i++) samples[i] = Mathf.Sin(i * 2f * Mathf.PI * 440f / 44100) * 0.01f;
        Assert.That(clip.SetData(samples, 0), Is.True);
        return clip;
    }

    private FeedbackPreset HapticPreset()
    {
        var profile = Own(ScriptableObject.CreateInstance<GamepadVibrationProfile>());
        Set(profile, "lowFrequencyMotor", 1f);
        Set(profile, "highFrequencyMotor", 0.5f);
        var entry = new GamepadVibrationFeedbackEntry();
        Set(entry, "profile", profile);
        Set(entry, "loop", true);
        Set(entry, "fadeIn", 0f);
        Set(entry, "fadeOut", 0f);
        var preset = Own(ScriptableObject.CreateInstance<FeedbackPreset>());
        Set(preset, "gamepadVibrationEntries", new List<GamepadVibrationFeedbackEntry> { entry });
        return preset;
    }

    private ParticleSystem Particles(bool loop = true)
    {
        var gameObject = Own(new GameObject("Particle resource"));
        gameObject.SetActive(false);
        var particle = gameObject.AddComponent<ParticleSystem>();
        var main = particle.main;
        main.playOnAwake = false;
        main.loop = loop;
        main.duration = loop ? 0.1f : 0.35f;
        main.startLifetime = loop ? 0.1f : 0.25f;
        main.startSpeed = 0.1f;
        var emission = particle.emission;
        emission.rateOverTime = 20f;
        return particle;
    }

    private AudioSource Source(AudioClip clip)
    {
        return audio.GetComponentsInChildren<AudioSource>(true).Single(source => source.clip == clip);
    }

    private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

    private static void Set(object target, string field, object value)
    {
        var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(info, Is.Not.Null, $"Missing authoring field {target.GetType().Name}.{field}");
        info.SetValue(target, value);
    }

    private void AssertMotor(Gamepad device, float low, float high)
    {
        Assert.That(motors[device.deviceId].x, Is.EqualTo(low).Within(0.0001f));
        Assert.That(motors[device.deviceId].y, Is.EqualTo(high).Within(0.0001f));
    }

    private static void AssertContext(FeedbackContext expected, FeedbackContext actual)
    {
        Assert.That(actual.WorldPosition, Is.EqualTo(expected.WorldPosition));
        Assert.That(actual.Rotation, Is.EqualTo(expected.Rotation));
        Assert.That(actual.Source, Is.SameAs(expected.Source));
        Assert.That(actual.Follow, Is.SameAs(expected.Follow));
        Assert.That(actual.Positioning, Is.EqualTo(expected.Positioning));
        Assert.That(actual.Intensity, Is.EqualTo(expected.Intensity));
        Assert.That(actual.AudioMultiplier, Is.EqualTo(expected.AudioMultiplier));
        Assert.That(actual.VisualScale, Is.EqualTo(expected.VisualScale));
    }

    private static IEnumerator WaitUntil(Func<bool> complete, float timeout)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!complete() && Time.realtimeSinceStartup < deadline) yield return null;
    }
}
}
