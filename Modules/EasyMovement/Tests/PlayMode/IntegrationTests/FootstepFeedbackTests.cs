using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EasyFeedbacks;
using EasyFootsteps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyMovement.IntegrationTests
{
    public sealed class FootstepFeedbackTests
    {
        private readonly List<Object> owned = new List<Object>();
        private CharacterStepController steps;
        private FootstepContextProviderProbe provider;
        private Transform origin;
        private GamepadVibrationManager haptics;
        private Gamepad first, second, previous;
        private Vector2 firstMotor, secondMotor;
        private FeedbackSettingsState previousSettings;

        [SetUp]
        public unsafe void SetUp()
        {
            Assert.That(GamepadVibrationManager.Instance, Is.Null, "Run in the test runner's isolated scene.");
            firstMotor = secondMotor = Vector2.zero;
            previous = Gamepad.current; previousSettings = FeedbackSettings.Capture();
            FeedbackSettings.Apply(FeedbackSettingsState.Default);
            first = InputSystem.AddDevice<Gamepad>("FootstepTestPlayerOne");
            second = InputSystem.AddDevice<Gamepad>("FootstepTestPlayerTwo");
            InputSystem.onDeviceCommand += Motor;
            haptics = Own(new GameObject("Footstep haptic test manager")).AddComponent<GamepadVibrationManager>();
            GamepadVibrationManager.BeginEditorPreviewPlayback();
            var player = Own(new GameObject("Footstep player"));
            steps = player.AddComponent<CharacterStepController>(); steps.GenerateSteps = false; steps.ApplyHeadBob = false;
            provider = Own(new GameObject("Footstep context")).AddComponent<FootstepContextProviderProbe>();
            provider.DefaultGamepad = first; steps.ContextProvider = provider;
            origin = Own(new GameObject("Footstep origin")).transform;
            origin.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(10, 20, 30));
            Set(steps, "_stepSoundOrigin", origin); Set(steps, "_defaultFeedback", HapticPreset());
            second.MakeCurrent();
        }

        [TearDown]
        public unsafe void TearDown()
        {
            try
            {
                if (haptics) haptics.StopAll(true);
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
                owned.Clear();
            }
            finally
            {
                GamepadVibrationManager.EndEditorPreviewPlayback();
                InputSystem.onDeviceCommand -= Motor;
                if (first != null && first.added) InputSystem.RemoveDevice(first);
                if (second != null && second.added) InputSystem.RemoveDevice(second);
                if (previous != null && previous.added) previous.MakeCurrent();
                FeedbackSettings.Restore(previousSettings);
            }
        }

        private unsafe long? Motor(InputDevice device, InputDeviceCommand* command)
        {
            // Only the two scoped virtual devices are intercepted. This makes no assertion about physical motors.
            if (device != first && device != second || command->type != new FourCC('R', 'M', 'B', 'L')) return null;
            var values = (float*)((byte*)command + InputDeviceCommand.BaseCommandSize);
            if (device == first) firstMotor = new Vector2(values[0], values[1]);
            else secondMotor = new Vector2(values[0], values[1]);
            return InputDeviceCommand.GenericSuccess;
        }

        [Test]
        public void ActiveProviderReceivesAuthoredStepContextAndTargetsItsPlayer()
        {
            steps.PlayExternalStep("Walk", -1);
            Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.That(provider.Received.WorldPosition, Is.EqualTo(origin.position));
            Assert.That(provider.Received.Rotation, Is.EqualTo(origin.rotation));
            Assert.That(provider.Received.Source, Is.SameAs(origin));
            Assert.That(provider.Received.Follow, Is.Null);
            Assert.That(provider.Received.Intensity, Is.EqualTo(1));
            Assert.That(provider.Received.AudioMultiplier, Is.EqualTo(.75f));
            Assert.That(provider.Received.VisualScale, Is.EqualTo(Vector3.one));
            Assert.That(firstMotor, Is.EqualTo(new Vector2(.6f, .3f)));
            Assert.That(secondMotor, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void ExplicitNoneSuppressesAnotherPlayersCurrentGamepad()
        {
            provider.ExplicitNone = true;
            steps.PlayExternalStep("Sprint", 1);
            Assert.That(provider.Returned.HasExplicitGamepadTarget, Is.True);
            Assert.That(provider.Returned.TargetGamepad, Is.Null);
            Assert.That(provider.Received.AudioMultiplier, Is.EqualTo(1));
            Assert.That(firstMotor, Is.EqualTo(Vector2.zero)); Assert.That(secondMotor, Is.EqualTo(Vector2.zero));
            Assert.That(haptics.ActiveVibrations, Is.Zero);
        }

        [Test]
        public void ExplicitOtherTargetReturnedByProviderIsNotReplaced()
        {
            first.MakeCurrent(); provider.ExplicitGamepad = second;
            steps.PlayExternalStep("Crouch", -1);
            Assert.That(provider.Returned.TargetGamepad, Is.SameAs(second));
            Assert.That(provider.Received.AudioMultiplier, Is.EqualTo(.45f));
            Assert.That(firstMotor, Is.EqualTo(Vector2.zero)); Assert.That(secondMotor, Is.EqualTo(new Vector2(.6f, .3f)));
        }

        [UnityTest]
        public IEnumerator InactiveAndDestroyedProvidersRetainLegacyFallback()
        {
            provider.enabled = false; steps.PlayExternalStep("Walk", 1);
            Assert.That(provider.Calls, Is.Zero); Assert.That(secondMotor.x, Is.GreaterThan(0)); Assert.That(firstMotor, Is.EqualTo(Vector2.zero));
            haptics.StopAll(true); provider.enabled = true; provider.gameObject.SetActive(false);
            steps.PlayExternalStep("Walk", 1);
            Assert.That(provider.Calls, Is.Zero); Assert.That(secondMotor.x, Is.GreaterThan(0));
            haptics.StopAll(true); Object.Destroy(provider.gameObject); yield return null;
            steps.PlayExternalStep("Walk", 1);
            Assert.That(secondMotor.x, Is.GreaterThan(0)); Assert.That(firstMotor, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void MissingAndInvalidProvidersRetainLegacyFallback()
        {
            steps.ContextProvider = null; steps.PlayExternalStep("Walk", 1);
            Assert.That(secondMotor.x, Is.GreaterThan(0)); Assert.That(firstMotor, Is.EqualTo(Vector2.zero));
            haptics.StopAll(true);
            steps.ContextProvider = Own(new GameObject("Invalid context assignment")).AddComponent<InvalidFootstepContextProvider>();
            steps.PlayExternalStep("Walk", -1);
            Assert.That(secondMotor.x, Is.GreaterThan(0)); Assert.That(provider.Calls, Is.Zero);
        }

        [Test]
        public void WarmProviderPreparationAddsNoManagedAllocationsToExternalStepPath()
        {
            // Empty output isolates step/context dispatch, not audio/VFX/haptic playback allocation.
            Set(steps, "_defaultFeedback", Own(ScriptableObject.CreateInstance<FeedbackPreset>()));
            bool wasProfiling = UnityEngine.Profiling.Profiler.enabled;
            try
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                long positive = MovementAllocationProbe.Count(() => GC.KeepAlive(new byte[512]), 1);
                Action emit = () => steps.PlayExternalStep("Walk", 1);
                steps.ContextProvider = null;
                for (int i = 0; i < 128; i++) emit();
                long baseline = MovementAllocationProbe.Count(emit, 1000);
                steps.ContextProvider = provider; provider.ExplicitNone = true;
                for (int i = 0; i < 128; i++) emit();
                long withProvider = MovementAllocationProbe.Count(emit, 1000);
                TestContext.WriteLine($"1000 warm external steps: baseline={baseline} main-thread GC.Alloc events; provider={withProvider}; positive control={positive}. Empty preset; output playback is excluded.");
                Assert.That(positive, Is.EqualTo(1), "The recorder must detect the deliberate buffer allocation.");
                Assert.That(provider.Calls, Is.EqualTo(1128));
                Assert.That(baseline, Is.Zero);
                Assert.That(withProvider, Is.Zero, "No thresholds or baseline subtraction: every allocation in the measured path fails.");
            }
            finally { UnityEngine.Profiling.Profiler.enabled = wasProfiling; }
        }

        private FeedbackPreset HapticPreset()
        {
            var profile = Own(ScriptableObject.CreateInstance<GamepadVibrationProfile>());
            Set(profile, "lowFrequencyMotor", .6f); Set(profile, "highFrequencyMotor", .3f);
            var entry = new GamepadVibrationFeedbackEntry(); Set(entry, "profile", profile);
            Set(entry, "loop", true); Set(entry, "fadeIn", 0f); Set(entry, "fadeOut", 0f);
            var preset = Own(ScriptableObject.CreateInstance<FeedbackPreset>());
            Set(preset, "gamepadVibrationEntries", new List<GamepadVibrationFeedbackEntry> { entry }); return preset;
        }
        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        private static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name); field.SetValue(target, value);
        }
    }
}
