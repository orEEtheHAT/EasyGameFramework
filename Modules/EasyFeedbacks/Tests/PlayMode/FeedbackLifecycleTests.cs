using System;
using System.Collections;
using System.Reflection;
using EasyFeedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Unity.Profiling;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace EasyGameFramework.Tests.Feedbacks
{
public sealed partial class FeedbackIntegrationTests
{
    [UnityTest]
    public IEnumerator RemovingAudioManagerDisposesOnlyItsPoolAndAllowsReplacement()
    {
        var root = audio.gameObject;
        var unrelated = Own(new GameObject("Unrelated child"));
        unrelated.transform.SetParent(root.transform);
        var options = AudioPlaySettings.Default;
        options.Loop = true;
        var clip = Clip("Component removal");
        var oldHandle = audio.Play(clip, options);
        var pool = root.GetComponentsInChildren<AudioSource>(true);
        Assert.That(pool.Length, Is.GreaterThan(0));
        Object.Destroy(audio);
        yield return null;
        yield return null; // Pool destruction requested by OnDestroy is deferred.
        Assert.That(root != null && unrelated != null, Is.True);
        Assert.That(oldHandle.IsValid, Is.False);
        foreach (var source in pool) Assert.That(source == null, Is.True);
        audio = root.AddComponent<AudioManager>();
        var replacement = audio.Play(clip, options);
        oldHandle.Stop(0f);
        Assert.That(replacement.IsValid, Is.True);
        Assert.That(root.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(audio.TotalVoices));
    }

    [UnityTest]
    public IEnumerator RemovingVisualManagerDisposesOnlyItsPoolAndAllowsReplacement()
    {
        var root = visuals.gameObject;
        var unrelated = Own(new GameObject("Unrelated visual child"));
        unrelated.transform.SetParent(root.transform);
        var resource = Particles();
        var oldHandle = visuals.Play(resource, default, VisualFeedbackLifetime.LoopUntilStopped);
        var pool = root.GetComponentsInChildren<ParticleSystem>(true);
        Assert.That(pool.Length, Is.EqualTo(1));
        Object.Destroy(visuals);
        yield return null;
        yield return null;
        Assert.That(root != null && unrelated != null, Is.True);
        Assert.That(oldHandle.IsValid, Is.False);
        foreach (var particles in pool) Assert.That(particles == null, Is.True);
        visuals = root.AddComponent<VisualFeedbackManager>();
        var replacement = visuals.Play(resource, default, VisualFeedbackLifetime.LoopUntilStopped);
        oldHandle.Stop();
        Assert.That(replacement.IsValid, Is.True);
        Assert.That(root.GetComponentsInChildren<ParticleSystem>(true).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator DuplicateManagersDoNotDisposeTheActiveOwnersPools()
    {
        var options = AudioPlaySettings.Default;
        options.Loop = true;
        var audioHandle = audio.Play(Clip("Duplicate manager"), options);
        var visualHandle = visuals.Play(Particles(), default, VisualFeedbackLifetime.LoopUntilStopped);
        var duplicate = Own(new GameObject("Duplicate bootstrap"));
        duplicate.AddComponent<AudioManager>();
        duplicate.AddComponent<VisualFeedbackManager>();
        yield return null;
        Assert.That(AudioManager.Instance, Is.SameAs(audio));
        Assert.That(VisualFeedbackManager.Instance, Is.SameAs(visuals));
        Assert.That(audioHandle.IsValid && visualHandle.IsValid, Is.True);
        Assert.That(audio.ActiveVoices, Is.EqualTo(1));
        Assert.That(visuals.ActiveInstances, Is.EqualTo(1));
    }

    [Test]
    public void SubsystemResetRestoresPreferencesPauseAndHandleGeneration()
    {
        var preset = HapticPreset();
        var oldHandle = FeedbackService.Play(preset, default(FeedbackContext).WithGamepad(first));
        var settings = FeedbackSettings.Current;
        settings.MasterVolume = 0.2f;
        FeedbackSettings.Apply(settings);
        var notifications = 0;
        FeedbackSettings.Changed += _ => notifications++;
        FeedbackService.PauseAll();
        FeedbackService.StopAll(true);
        InvokeReset(typeof(FeedbackSettings));
        InvokeReset(typeof(FeedbackService));
        InvokeReset(typeof(FeedbackHandle).Assembly.GetType("EasyFeedbacks.FeedbackHandleRegistry", true));
        Assert.That(FeedbackSettings.Current.MasterVolume, Is.EqualTo(1f));
        Assert.That(FeedbackService.AreAllPaused, Is.False);
        Assert.That(oldHandle.IsValid, Is.False);
        FeedbackSettings.Apply(settings);
        Assert.That(notifications, Is.Zero, "Previous play-session listeners must be removed.");
        var next = FeedbackService.Play(preset, default(FeedbackContext).WithGamepad(first));
        oldHandle.Stop(0f);
        Assert.That(next.IsValid, Is.True, "A handle from the previous session must not stop a reused slot.");
    }

    [Test]
    public void ImmediateStopsReuseAggregateSlotsWithinTheSameFrame()
    {
        var preset = HapticPreset();
        var context = default(FeedbackContext).WithGamepad(first);
        var firstHandle = FeedbackService.Play(preset, context);
        var registry = typeof(FeedbackHandle).Assembly.GetType("EasyFeedbacks.FeedbackHandleRegistry", true);
        var count = registry.GetField("nextUnusedSlot", BindingFlags.Static | BindingFlags.NonPublic);
        var before = (int)count.GetValue(null);
        firstHandle.Stop(0f);
        for (var i = 0; i < 1000; i++)
            FeedbackService.Play(preset, context).Stop(0f);
        Assert.That((int)count.GetValue(null), Is.EqualTo(before));
        Assert.That(haptics.ActiveVibrations, Is.Zero);
        var next = FeedbackService.Play(preset, context);
        firstHandle.Stop(0f);
        Assert.That(next.IsValid, Is.True);
    }

    [Test]
    public void WarmAudioPresetLaunchStopDoesNotAllocateManagedMemory()
    {
        var preset = Own(ScriptableObject.CreateInstance<FeedbackPreset>());
        var entry = new AudioFeedbackEntry();
        Set(entry, "clips", new System.Collections.Generic.List<AudioClip> { Clip("Warm loop") });
        Set(entry, "loop", true);
        Set(preset, "audioEntries", new System.Collections.Generic.List<AudioFeedbackEntry> { entry });
        var context = default(FeedbackContext).WithoutGamepad();
        FeedbackService.Prewarm(preset);
        for (var i = 0; i < 128; i++) FeedbackService.Play(preset, context).Stop(0f);
        // GC.GetAllocatedBytesForCurrentThread is not calibrated on every Unity
        // runtime. Count actual main-thread GC.Alloc events and require both
        // empty and retained-allocation controls before trusting a zero result.
        var previousProfilerEnabled = Profiler.enabled;
        var retained = new object[4];
        long emptyControl = -1, positiveControl = -1, allocationEvents = -1;
        try
        {
            Profiler.enabled = true;
            var options = ProfilerRecorderOptions.WrapAroundWhenCapacityReached
                          | ProfilerRecorderOptions.SumAllSamplesInFrame
                          | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using (var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1, options))
            {
                long Sample(Action action)
                {
                    recorder.Reset();
                    recorder.Start();
                    try { action(); }
                    finally { recorder.Stop(); }
                    return recorder.Count > 0 ? recorder.GetSample(0).Count : 0;
                }

                Action empty = () => { };
                Action positive = () =>
                {
                    for (var i = 0; i < retained.Length; i++) retained[i] = new byte[256 + i];
                };
                Action measured = () =>
                {
                    for (var i = 0; i < 1000; i++) FeedbackService.Play(preset, context).Stop(0f);
                };
                Assert.That(recorder.Valid, Is.True, "A valid main-thread GC.Alloc recorder is required.");
                Sample(positive); // Warm the sampling path before calibration.
                emptyControl = Sample(empty);
                positiveControl = Sample(positive);
                Assert.That(emptyControl, Is.Zero, "The empty control must not allocate.");
                Assert.That(positiveControl, Is.EqualTo(4), "The positive control must detect all four retained arrays.");
                allocationEvents = Sample(measured);
            }
        }
        finally
        {
            Profiler.enabled = previousProfilerEnabled;
            GC.KeepAlive(retained);
        }

        Debug.Log($"FeedbackWarmAudioGC: calls=1000, warmups=128, mainThreadEvents={allocationEvents}, emptyControl={emptyControl}, positiveControl={positiveControl}, Unity={Application.unityVersion}");
        Assert.That(allocationEvents, Is.Zero);
    }
    [UnityTest]
    public IEnumerator AggregateHandleRemainsValidUntilItsAudioFadeCompletes()
    {
        var preset = Own(ScriptableObject.CreateInstance<FeedbackPreset>());
        var entry = new AudioFeedbackEntry();
        Set(entry, "clips", new System.Collections.Generic.List<AudioClip> { Clip("Aggregate fade") });
        Set(entry, "loop", true);
        Set(entry, "fadeOutDuration", 0.08f);
        Set(preset, "audioEntries", new System.Collections.Generic.List<AudioFeedbackEntry> { entry });
        var handle = FeedbackService.Play(preset, default(FeedbackContext).WithoutGamepad());
        handle.Stop();
        Assert.That(handle.IsValid, Is.True, "A configured fade still owns its aggregate slot.");
        yield return WaitUntil(() => !handle.IsValid, 2f);
        Assert.That(handle.IsValid, Is.False);
        Assert.That(audio.ActiveVoices, Is.Zero);
    }

    private static void InvokeReset(Type type)
    {
        var method = type.GetMethod("ResetRuntimeState", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(null, null);
    }
}
}
