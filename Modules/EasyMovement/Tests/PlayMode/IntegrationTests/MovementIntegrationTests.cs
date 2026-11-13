using System;
using System.Collections;
using System.Collections.Generic;
using EasyMovement.FirstPerson;
using ECM2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyMovement.IntegrationTests
{
    public sealed class MovementIntegrationTests
    {
        private GameObject root, ownerObject, secondOwner;
        private ProbeCharacter character;
        private FirstPersonCharacterControllerInput reader;
        private InputActionAsset actions;
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.UpdateMode updateMode;
        private InputSettings.BackgroundBehavior background;
        private InputSettings.EditorInputBehaviorInPlayMode editorBehavior;
        private Action tickReader;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            updateMode = InputSystem.settings.updateMode;
            background = InputSystem.settings.backgroundBehavior;
            editorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            actions = ScriptableObject.CreateInstance<InputActionAsset>();
            actions.devices = new InputDevice[] { keyboard, mouse };
            var map = actions.AddActionMap("Player");
            map.AddAction("Move", InputActionType.Value).AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            map.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            map.AddAction("Crouch", InputActionType.Button, "<Keyboard>/c");
            map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            map.AddAction("ToggleNoclip", InputActionType.Button, "<Keyboard>/f8");
            root = new GameObject("EasyMovement regression character");
            root.SetActive(false);
            character = root.AddComponent<ProbeCharacter>();
            // Runtime AddComponent does not run the Editor Reset callback that initializes ECM2's serialized settings.
            character.InitializeEditorDefaults();
            root.GetComponent<CharacterMovement>()._advanced.Reset();
            character.enableAutoSimulation = false;
            character.canEverCrouch = true;
            var pivot = new GameObject("Look pivot").transform;
            pivot.SetParent(root.transform, false);
            character.LookPivot = pivot;
            reader = root.AddComponent<FirstPersonCharacterControllerInput>();
            reader.inputActionsAsset = actions;
            tickReader = (Action)Delegate.CreateDelegate(typeof(Action), reader,
                typeof(FirstPersonCharacterControllerInput).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
            ownerObject = new GameObject("Block owner");
            secondOwner = new GameObject("Second block owner");
            root.SetActive(true);
            yield return null;
            Keys();
            tickReader();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (root) Object.Destroy(root);
                if (ownerObject) Object.Destroy(ownerObject);
                if (secondOwner) Object.Destroy(secondOwner);
                yield return null;
                if (actions) Object.Destroy(actions);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            }
            finally
            {
                InputSystem.settings.updateMode = updateMode;
                InputSystem.settings.backgroundBehavior = background;
                InputSystem.settings.editorInputBehaviorInPlayMode = editorBehavior;
            }
        }

        private void Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        [Test]
        public void OverlappingHandlesIncludingSameOwnerAreIndependentAndIdempotent()
        {
            var changes = new List<bool>();
            character.InputBlockedChanged += changes.Add;
            var first = character.AcquireInputBlock(ownerObject);
            var sameOwner = character.AcquireInputBlock(ownerObject);
            var other = character.AcquireInputBlock(secondOwner);
            first.Dispose(); first.Dispose(); other.Dispose();
            Assert.That(character.InputBlocked, Is.True);
            Assert.That(changes, Is.EqualTo(new[] { true }));
            sameOwner.Dispose(); sameOwner.Dispose();
            Assert.That(character.InputBlocked, Is.False);
            Assert.That(changes, Is.EqualTo(new[] { true, false }));
        }

        [UnityTest]
        public IEnumerator DisabledAndDestroyedOwnersExpireWithoutExplicitDispose()
        {
            var changes = new List<bool>();
            character.InputBlockedChanged += changes.Add;
            var behaviour = ownerObject.AddComponent<AudioSource>();
            var first = character.AcquireInputBlock(behaviour);
            var other = character.AcquireInputBlock(secondOwner);
            reader.enabled = false;
            behaviour.enabled = false;
            character.TickBlockCleanup();
            Assert.That(character.InputBlocked, Is.True);
            Object.Destroy(secondOwner);
            yield return null;
            yield return null;
            Assert.That(changes, Is.EqualTo(new[] { true, false }), "Update must publish expired owners without a reader polling the property.");
            Assert.That(character.InputBlocked, Is.False);
            behaviour.enabled = true;
            Assert.That(character.InputBlocked, Is.False, "Expired owners never reactivate old handles.");
            first.Dispose(); other.Dispose();
            var hierarchy = character.AcquireInputBlock(ownerObject.transform);
            ownerObject.SetActive(false);
            Assert.That(character.InputBlocked, Is.False, "A property read cleans up immediately, even before Update.");
            hierarchy.Dispose();
            Assert.Throws<ArgumentNullException>(() => character.AcquireInputBlock(null));
            character.AcquireInputBlock(ownerObject).Dispose();
            Assert.That(character.InputBlocked, Is.False);
        }

        [Test]
        public void ControllerDisableAndPoolingInvalidateOldHandlesOnly()
        {
            var old = character.AcquireInputBlock(ownerObject);
            character.enabled = false;
            Assert.That(character.InputBlocked, Is.False);
            character.AcquireInputBlock(ownerObject).Dispose();
            character.enabled = true;
            var current = character.AcquireInputBlock(ownerObject);
            old.Dispose();
            Assert.That(character.InputBlocked, Is.True);
            root.SetActive(false);
            Assert.That(character.InputBlocked, Is.False);
            root.SetActive(true);
            var pooled = character.AcquireInputBlock(secondOwner);
            current.Dispose();
            Assert.That(character.InputBlocked, Is.True);
            pooled.Dispose();
        }

        [Test]
        public void BlockedReaderDoesNotOverwriteInheritedScriptedMotorOrLook()
        {
            using var block = character.AcquireInputBlock(ownerObject);
            Character inheritedMotor = character;
            inheritedMotor.SetMovementMode(Character.MovementMode.Flying);
            inheritedMotor.SetMovementDirection(Vector3.forward);
            inheritedMotor.SetRotation(Quaternion.Euler(0, 35, 0));
            Keys(Key.A, Key.Space, Key.C, Key.LeftShift, Key.F8);
            InputSystem.QueueStateEvent(mouse, new MouseState { delta = new Vector2(80, 20) });
            InputSystem.Update();
            tickReader();
            Assert.That(inheritedMotor.GetMovementDirection(), Is.EqualTo(Vector3.forward));
            Assert.That(Quaternion.Angle(inheritedMotor.GetPendingRotation(), Quaternion.Euler(0, 35, 0)), Is.LessThan(.001f));
            Assert.That(reader.GetLookInput(), Is.EqualTo(Vector2.zero));
            Assert.That(character.IsNoclipActive, Is.False);
            Assert.That(character.jumpInputPressed, Is.False);
            Assert.That(character.crouchInputPressed, Is.False);
            Assert.That(character.CaptureState().SprintRequested, Is.False);
            var before = character.GetPosition();
            character.Simulate(.1f);
            Assert.That(character.GetPosition().z, Is.GreaterThan(before.z), "The motor still simulates external commands.");
            reader.enabled = false;
            Assert.That(inheritedMotor.GetMovementDirection(), Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void BlockReleasesManualHoldsOnceAndBlockedCallbacksLeaveScriptedHoldsAlone()
        {
            Keys(Key.Space, Key.C, Key.LeftShift);
            Assert.That(character.jumpInputPressed && character.crouchInputPressed && character.CaptureState().SprintRequested, Is.True);
            using var block = character.AcquireInputBlock(ownerObject);
            Assert.That(character.jumpInputPressed || character.crouchInputPressed || character.CaptureState().SprintRequested, Is.False);
            character.Jump(); character.Crouch(); character.Sprint();
            Keys(); // canceled callbacks must not cancel these external requests
            tickReader();
            reader.enabled = false;
            Assert.That(character.jumpInputPressed && character.crouchInputPressed && character.CaptureState().SprintRequested, Is.True);
        }

        [Test]
        public void HeldButtonsRequireReleaseAfterUnblockWhileMovementResumes()
        {
            var block = character.AcquireInputBlock(ownerObject);
            Keys(Key.W, Key.Space, Key.C, Key.LeftShift, Key.F8);
            block.Dispose();
            tickReader();
            Assert.That(character.GetMovementDirection().z, Is.GreaterThan(0));
            Assert.That(character.jumpInputPressed || character.crouchInputPressed || character.CaptureState().SprintRequested || character.IsNoclipActive, Is.False);
            Keys();
            Keys(Key.Space, Key.C, Key.LeftShift);
            Assert.That(character.jumpInputPressed && character.crouchInputPressed && character.CaptureState().SprintRequested, Is.True);
            Keys(); Keys(Key.F8);
            Assert.That(character.IsNoclipActive, Is.True);
        }

        [Test]
        public void NoclipKeepsModeButHeldVerticalInputAndBoostWaitForRelease()
        {
            Assert.That(character.TrySetNoclip(true), Is.True);
            Keys(Key.Space, Key.LeftShift);
            tickReader();
            Assert.That(character.GetMovementDirection().y, Is.EqualTo(1));
            float boosted = character.GetMaxSpeed();
            var block = character.AcquireInputBlock(ownerObject);
            character.SetMovementDirection(Vector3.right);
            tickReader();
            Assert.That(character.IsNoclipActive, Is.True);
            Assert.That(character.GetMovementDirection(), Is.EqualTo(Vector3.right));
            block.Dispose(); tickReader();
            Assert.That(character.GetMovementDirection().y, Is.Zero);
            Assert.That(character.GetMaxSpeed(), Is.LessThan(boosted));
            Keys(); Keys(Key.Space, Key.LeftShift); tickReader();
            Assert.That(character.GetMovementDirection().y, Is.EqualTo(1));
            Assert.That(character.GetMaxSpeed(), Is.EqualTo(boosted));
            character.enabled = false;
            Assert.That(character.IsNoclipActive, Is.False);
            Assert.That(character.characterMovement.detectCollisions, Is.True);
        }

        [Test]
        public void DisablingReaderClearsItsOwnDirectionAndPreservesDifferentExternalDirection()
        {
            Keys(Key.W); tickReader();
            reader.enabled = false;
            Assert.That(character.GetMovementDirection(), Is.EqualTo(Vector3.zero));
            reader.enabled = true; tickReader();
            character.SetMovementDirection(Vector3.right);
            reader.enabled = false;
            Assert.That(character.GetMovementDirection(), Is.EqualTo(Vector3.right));
        }

        [Test]
        public void UnblockedSprintCrouchAndCustomGaitKeepTheirExistingPriority()
        {
            character.SetMovementMode(Character.MovementMode.Walking);
            Keys(Key.W, Key.LeftShift); tickReader(); character.TickGait();
            Assert.That(character.IsSprinting(), Is.True);
            Assert.That(character.GetMaxSpeed(), Is.EqualTo(character.MaxSprintSpeed));
            Keys(Key.W, Key.LeftShift, Key.C); character.ApplyCrouch(); character.TickGait();
            Assert.That(character.IsCrouched(), Is.True);
            Assert.That(character.IsSprinting(), Is.False);
            JsonUtility.FromJsonOverwrite("{\"customStates\":[{\"stateId\":\"Injured\",\"maxSpeed\":2}]}", character);
            Assert.That(character.TrySetCustomState("Injured"), Is.True);
            Assert.That(character.ActiveStateId, Is.EqualTo("Crouch"));
            Keys(Key.W, Key.LeftShift); character.ApplyCrouch(); character.TickGait();
            Assert.That(character.ActiveStateId, Is.EqualTo("Injured"));
            Assert.That(character.GetMaxSpeed(), Is.EqualTo(2));
            character.ClearCustomState(); character.TickGait();
            Assert.That(character.IsSprinting(), Is.True);
        }

        [Test]
        public void ControllerDisabledCallbacksAndReaderReconnectCannotReplayHeldButtons()
        {
            Keys(Key.Space, Key.C, Key.LeftShift);
            character.enabled = false;
            Assert.That(character.jumpInputPressed || character.crouchInputPressed || character.CaptureState().SprintRequested, Is.False);
            character.enabled = true; // same-frame pooling must not leave reader-owned holds behind
            character.Jump(); character.Crouch(); character.Sprint();
            Keys();
            Assert.That(character.jumpInputPressed && character.crouchInputPressed && character.CaptureState().SprintRequested, Is.True);
            character.StopJumping(); character.UnCrouch(); character.StopSprinting();
            character.enabled = false;
            Keys(Key.Space, Key.C, Key.LeftShift, Key.F8);
            tickReader();
            Assert.That(character.jumpInputPressed || character.crouchInputPressed || character.CaptureState().SprintRequested, Is.False);
            character.enabled = true; tickReader();
            reader.enabled = false; reader.enabled = true;
            tickReader();
            Assert.That(character.jumpInputPressed || character.crouchInputPressed || character.CaptureState().SprintRequested || character.IsNoclipActive, Is.False);
            Keys(); tickReader(); Keys(Key.Space, Key.C, Key.LeftShift);
            Assert.That(character.jumpInputPressed && character.crouchInputPressed && character.CaptureState().SprintRequested, Is.True);
        }

        [Test]
        public void WarmIdleAndBlockedInputPollingAllocateNoManagedMemory()
        {
            bool wasProfiling = UnityEngine.Profiling.Profiler.enabled;
            try
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                long positive = MovementAllocationProbe.Count(() => GC.KeepAlive(new byte[512]), 1);
                Action poll = () => { tickReader(); character.TickBlockCleanup(); };
                for (int i = 0; i < 128; i++) poll();
                long idleEvents = MovementAllocationProbe.Count(poll, 1000);
                using var block = character.AcquireInputBlock(ownerObject);
                for (int i = 0; i < 128; i++) poll();
                long blockedEvents = MovementAllocationProbe.Count(poll, 1000);
                TestContext.WriteLine($"1000 reader/cleanup calls: idle={idleEvents} main-thread GC.Alloc events; blocked={blockedEvents}; positive control={positive}.");
                Assert.That(positive, Is.EqualTo(1), "The recorder must detect the deliberate buffer allocation.");
                Assert.That(idleEvents, Is.Zero, "Idle reader and owner sweep.");
                Assert.That(blockedEvents, Is.Zero, "Blocked reader and owner sweep.");
            }
            finally { UnityEngine.Profiling.Profiler.enabled = wasProfiling; }
        }

        [Test]
        public void StateJsonRoundTripRestoresPoseViewAndGaitWithoutResumingMotionOrClearingBlocks()
        {
            JsonUtility.FromJsonOverwrite("{\"customStates\":[{\"stateId\":\"Injured\",\"maxSpeed\":2}]}", character);
            character.SetPosition(new Vector3(4, 6, 8));
            character.SetRotation(Quaternion.Euler(0, 35, 0));
            character.AddControlYawInput(15);
            character.AddControlPitchInput(25);
            character.Crouch(); character.Sprint();
            Assert.That(character.TrySetCustomState("Injured"), Is.True);
            var saved = JsonUtility.FromJson<FirstPersonCharacterState>(JsonUtility.ToJson(character.CaptureState()));
            using var block = character.AcquireInputBlock(ownerObject);
            character.SetPosition(Vector3.zero);
            character.AddControlYawInput(90);
            character.AddControlPitchInput(-40);
            character.ClearCustomState(); character.UnCrouch(); character.StopSprinting();
            character.Jump(); character.SetMovementDirection(Vector3.forward); character.SetVelocity(Vector3.one);
            character.characterMovement.interpolation = RigidbodyInterpolation.None;
            Assert.That(character.TryRestoreState(saved, out string error), Is.True, error);
            var restored = character.CaptureState();
            Assert.That(restored.Position, Is.EqualTo(saved.Position));
            Assert.That(Quaternion.Angle(restored.Rotation, saved.Rotation), Is.LessThan(.001f));
            Assert.That(restored.ViewPitch, Is.EqualTo(saved.ViewPitch).Within(.001f));
            Assert.That(restored.CustomStateId, Is.EqualTo("Injured"));
            Assert.That(restored.CrouchRequested && restored.SprintRequested, Is.True);
            Assert.That(character.InputBlocked, Is.True);
            Assert.That(character.GetMovementDirection(), Is.EqualTo(Vector3.zero));
            Assert.That(character.GetVelocity(), Is.EqualTo(Vector3.zero));
            Assert.That(character.jumpInputPressed, Is.False);
            Assert.That(character.characterMovement.interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        }

        [Test]
        public void SavePreflightDoesNotChangeLivePoseMotionRequestsOrBlocks()
        {
            reader.enabled = false;
            using var block = character.AcquireInputBlock(ownerObject);
            character.SetPosition(new Vector3(1, 2, 3));
            character.AddControlYawInput(17);
            character.SetMovementDirection(Vector3.right);
            character.SetVelocity(new Vector3(2, 3, 4));
            character.Jump(); character.Crouch(); character.Sprint();
            string before = JsonUtility.ToJson(character.CaptureState());
            var candidate = character.CaptureState();
            candidate.Position = new Vector3(20, 30, 40);
            candidate.ViewPitch = -35;
            int notifications = 0;
            Action<bool> changed = _ => ++notifications;
            character.InputBlockedChanged += changed;
            try
            {
                Assert.That(character.ValidateState(candidate, out string error), Is.True, error);
                Assert.That(error, Is.Null);
                Assert.That(JsonUtility.ToJson(character.CaptureState()), Is.EqualTo(before));
                Assert.That(character.GetMovementDirection(), Is.EqualTo(Vector3.right));
                Assert.That(character.GetVelocity(), Is.EqualTo(new Vector3(2, 3, 4)));
                Assert.That(character.jumpInputPressed, Is.True);
                Assert.That(character.InputBlocked, Is.True);
                Assert.That(notifications, Is.Zero);
            }
            finally { character.InputBlockedChanged -= changed; }
        }

        [Test]
        public void SavePreflightAndRestoreShareErrorsForInvalidDataAndCapabilities()
        {
            reader.enabled = false;
            character.SetPosition(new Vector3(2, 4, 6));
            void Reject(FirstPersonCharacterState state)
            {
                string before = JsonUtility.ToJson(character.CaptureState());
                Assert.That(character.ValidateState(state, out string preflight), Is.False);
                Assert.That(preflight, Is.Not.Null.And.Not.Empty);
                Assert.That(character.TryRestoreState(state, out string restore), Is.False);
                Assert.That(restore, Is.EqualTo(preflight));
                Assert.That(JsonUtility.ToJson(character.CaptureState()), Is.EqualTo(before));
            }
            Reject(null);
            var invalid = character.CaptureState(); invalid.Version++; Reject(invalid);
            invalid = character.CaptureState(); invalid.Position.x = float.NaN; Reject(invalid);
            invalid = character.CaptureState(); invalid.Rotation = new Quaternion(0, 0, 0, 0); Reject(invalid);
            invalid = character.CaptureState(); invalid.ViewPitch = 181; Reject(invalid);
            invalid = character.CaptureState(); invalid.CustomStateId = "unconfigured"; Reject(invalid);
            invalid = character.CaptureState(); invalid.CrouchRequested = true; character.canEverCrouch = false; Reject(invalid);
            character.canEverCrouch = true;
            invalid = character.CaptureState(); invalid.SprintRequested = true;
            JsonUtility.FromJsonOverwrite("{\"allowSprint\":false}", character); Reject(invalid);
        }

        [Test]
        public void RestoreRevalidatesReadinessAfterSuccessfulPreflight()
        {
            var saved = character.CaptureState();
            Assert.That(character.ValidateState(saved, out _), Is.True);
            character.enabled = false;
            Assert.That(character.ValidateState(saved, out string disabled), Is.False);
            Assert.That(character.TryRestoreState(saved, out string disabledRestore), Is.False);
            Assert.That(disabledRestore, Is.EqualTo(disabled));
            character.enabled = true;
            Assert.That(character.ValidateState(saved, out _), Is.True);
            Assert.That(character.TrySetNoclip(true), Is.True);
            Assert.That(character.ValidateState(saved, out string noclip), Is.False);
            Assert.That(character.TryRestoreState(saved, out string noclipRestore), Is.False);
            Assert.That(noclipRestore, Is.EqualTo(noclip));
            Assert.That(character.IsNoclipActive, Is.True);
            Assert.That(character.TrySetNoclip(false), Is.True);
            Assert.That(character.ValidateState(saved, out _), Is.True);
            Assert.That(character.TryRestoreState(saved, out _), Is.True);
        }

        [Test]
        public void StateValidationRejectsWholeSnapshotBeforeMutation()
        {
            character.SetPosition(new Vector3(2, 3, 4));
            var before = character.CaptureState();
            Assert.That(character.TryRestoreState(null, out _), Is.False);
            var invalid = character.CaptureState(); invalid.Position = new Vector3(float.NaN, 0, 0);
            Assert.That(character.TryRestoreState(invalid, out _), Is.False);
            invalid = character.CaptureState(); invalid.Rotation = new Quaternion(0, 0, 0, 0);
            Assert.That(character.TryRestoreState(invalid, out _), Is.False);
            invalid = character.CaptureState(); invalid.ViewPitch = float.PositiveInfinity;
            Assert.That(character.TryRestoreState(invalid, out _), Is.False);
            invalid = character.CaptureState(); invalid.Version++;
            Assert.That(character.TryRestoreState(invalid, out _), Is.False);
            invalid = character.CaptureState(); invalid.Position = Vector3.zero; invalid.CustomStateId = "missing";
            Assert.That(character.TryRestoreState(invalid, out _), Is.False);
            Assert.That(character.GetPosition(), Is.EqualTo(before.Position));
            Assert.That(Quaternion.Angle(character.GetRotation(), before.Rotation), Is.LessThan(.001f));
            Assert.That(character.TrySetNoclip(true), Is.True);
            Assert.That(character.TryRestoreState(before, out _), Is.False);
            Assert.That(character.IsNoclipActive, Is.True);
        }
    }
}
