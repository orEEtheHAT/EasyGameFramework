using System;
using System.Collections.Generic;
using EasyGameFramework.Editor;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction.Integrations.Movement;
using EasyMovement.FirstPerson;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyInteraction.Tests.Editor
{
    public sealed class InteractionPlayerSetupTests
    {
        private Scene scene;
        private readonly List<GameObject> ownedRoots = new List<GameObject>();
        private GameObject Track(GameObject root)
        {
            ownedRoots.Add(root);
            return root;
        }

        private GameObject NewRoot(string name) => Track(new GameObject(name));
        [SetUp]
        public void SetUp()
        {
            // Test Runner owns its (possibly untitled and unsaved) scene. Do not replace or save it.
            scene = SceneManager.GetActiveScene();
            ownedRoots.Clear();
            Undo.IncrementCurrentGroup();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                // Undo/Redo is asserted inside tests while the entire target hierarchy is alive.
                // Never replay a global group range as cleanup (including an empty fixture group).
                Undo.FlushUndoRecordObjects();
                foreach (var root in ownedRoots)
                    if (root)
                    {
                        foreach (var component in root.GetComponentsInChildren<Component>(true))
                            if (component)
                                Undo.ClearUndo(component);
                        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                            Undo.ClearUndo(transform.gameObject);
                    }
            }
            finally
            {
                for (int i = ownedRoots.Count - 1; i >= 0; i--)
                    if (ownedRoots[i])
                        UnityEngine.Object.DestroyImmediate(ownedRoots[i]);
                ownedRoots.Clear();
                Undo.IncrementCurrentGroup();
            }
        }

        [Test]
        public void OneSetupCallWiresStandardPlayerAndIsIdempotent()
        {
            var character = NewRoot("Player").AddComponent<FirstPersonCharacterController>();
            var actor = InteractionPlayerSetup.Configure(character);
            Assert.AreEqual(InteractionSelectionMode.Focus, actor.SelectionMode);
            Assert.AreSame(actor, InteractionPlayerSetup.Configure(character));
            Assert.AreEqual(1, character.GetComponentsInChildren<InteractionActor>(true).Length);
            Assert.AreEqual(1, actor.GetComponents<EasyInteractionInput>().Length);
            var input = actor.GetComponent<EasyInteractionInput>();
            Assert.AreSame(actor, input.Actor);
            Assert.AreSame(character.GetComponent<PlayerInputSettings>(), input.Player);
            Assert.AreSame(input.Player, character.GetComponent<EasyMovementInput>().Player);
            Assert.AreSame(input, actor.GetComponent<InteractionPresenter>().InputSource);
            Assert.AreSame(character, actor.GetComponent<EasyMovementInteractionMotor>().character);
            Assert.AreSame(actor.GetComponent<EasyMovementInteractionMotor>(), actor.Motor);
            Assert.AreSame(character.gameObject.transform, actor.PlayerRoot);
        }

        [Test]
        public void SetupReusesCameraActorAndPreservesItsExplicitHandAnchor()
        {
            var character = NewRoot("Player").AddComponent<FirstPersonCharacterController>();
            var eye = new GameObject("Existing camera actor");
            eye.transform.SetParent(character.gameObject.transform);
            var actor = eye.AddComponent<InteractionActor>();
            var legacy = eye.AddComponent<InteractionInput>();
            var hand = new GameObject("Configured hand").transform;
            hand.SetParent(character.gameObject.transform);
            actor.HandAnchor = hand;
            Assert.AreSame(actor, InteractionPlayerSetup.Configure(character));
            Assert.IsNull(character.GetComponent<InteractionActor>());
            Assert.AreSame(hand, actor.HandAnchor);
            Assert.IsFalse(legacy.enabled);
            Assert.AreSame(character.gameObject.transform, actor.PlayerRoot);
        }

        [Test]
        public void AmbiguousActorsProduceAnActionableErrorBeforeAddingInput()
        {
            var character = NewRoot("Player").AddComponent<FirstPersonCharacterController>();
            for (int i = 0; i < 2; i++)
            {
                var child = new GameObject("Actor " + i);
                child.transform.SetParent(character.gameObject.transform);
                child.AddComponent<InteractionActor>();
            }

            var exception = Assert.Throws<InvalidOperationException>(() => InteractionPlayerSetup.Configure(character));
            StringAssert.Contains("multiple child Interaction Actors", exception.Message);
            Assert.IsNull(character.GetComponent<PlayerInputSettings>());
        }

        [Test]
        public void NewGrabAndLinearHandleHaveUsableDefaults()
        {
            var grab = NewRoot("Grab").AddComponent<GrabAction>();
            Assert.AreEqual(GrabPoseMode.AuthoredPose, grab.PoseMode);
            Assert.AreEqual(Vector3.zero, grab.LocalPosition);
            Assert.AreEqual(Vector3.zero, grab.LocalEulerAngles);
            Assert.AreEqual(.5f, grab.FollowSpeed);
            Assert.AreEqual(1, grab.HeldScaleMultiplier);
            Assert.IsFalse(grab.Body.isKinematic);
            Assert.AreEqual(GrabRotation.Stabilized, grab.RotationMode);
            Assert.IsTrue(grab.IgnoreOwnerCollisions);
            Assert.AreEqual(Vector3.zero, grab.HoldOffset);
            Assert.AreEqual(2, grab.HoldDistance);
            var handle = Track(EasyInteraction.Editor.InteractionSetup.CreateLinearHandle(Vector3.zero));
            var action = handle.GetComponent<MechanicalAction>();
            Assert.AreEqual(MechanicalMotion.Linear, action.Motion);
            Assert.AreEqual(InteractionInputGesture.Analog, action.InputGesture);
            action.SetValue(1);
            Assert.AreEqual(Vector3.right, handle.transform.localPosition);
        // All fixture objects stay alive until scoped teardown has cleared their Undo records.
        }

        [Test]
        public void SetupKeepsAnExistingExplicitRaycastSelection()
        {
            var character = NewRoot("Raycast player").AddComponent<FirstPersonCharacterController>();
            var actor = character.gameObject.AddComponent<InteractionActor>();
            actor.SelectionMode = InteractionSelectionMode.Raycast;
            Assert.AreSame(actor, InteractionPlayerSetup.Configure(character));
            Assert.AreSame(actor, InteractionPlayerSetup.Configure(character));
            Assert.AreEqual(InteractionSelectionMode.Raycast, actor.SelectionMode);
        }

        [Test]
        public void ExplicitPreserveSerializedModeSurvivesReloadWithoutChangingEnumValues()
        {
            var grab = NewRoot("Serialized grab").AddComponent<GrabAction>();
            Assert.AreEqual(GrabPoseMode.AuthoredPose, grab.PoseMode);
            Assert.AreEqual(1, new SerializedObject(grab).FindProperty("poseMode").intValue);
            Assert.AreEqual(0, (int)GrabPoseMode.PreservePickupPose);
            Assert.AreEqual(1, (int)GrabPoseMode.AuthoredPose);
            grab.PoseMode = GrabPoseMode.PreservePickupPose;
            grab.RotationMode = GrabRotation.Free;
            grab.HoldOffset = new Vector3(.1f, -.2f, .3f);
            grab.LocalEulerAngles = new Vector3(0, 45, 0);
            var serialized = EditorJsonUtility.ToJson(grab);
            grab.PoseMode = GrabPoseMode.AuthoredPose;
            grab.RotationMode = GrabRotation.Stabilized;
            grab.HoldOffset = Vector3.zero;
            EditorJsonUtility.FromJsonOverwrite(serialized, grab);
            Assert.AreEqual(GrabPoseMode.PreservePickupPose, grab.PoseMode);
            Assert.AreEqual(0, new SerializedObject(grab).FindProperty("poseMode").intValue);
            Assert.AreEqual(GrabRotation.Free, grab.RotationMode);
            Assert.AreEqual(new Vector3(.1f, -.2f, .3f), grab.HoldOffset);
            Assert.AreEqual(new Vector3(0, 45, 0), grab.LocalEulerAngles);
        }

        [Test]
        public void ConfigureIsOneUndoAndRedoRestoresTheWholePlayerWiring()
        {
            var root = NewRoot("Undo player");
            var character = root.AddComponent<FirstPersonCharacterController>();
            int originalComponents = root.GetComponents<Component>().Length;
            var actor = InteractionPlayerSetup.Configure(character);
            Assert.IsNotNull(actor.GetComponent<EasyMovementInteractionMotor>());
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.IsTrue(character, "Undo must keep the preexisting character.");
            Assert.AreEqual(originalComponents, root.GetComponents<Component>().Length);
            Assert.IsNull(root.GetComponent<InteractionActor>());
            Assert.IsNull(root.GetComponent<PlayerInputSettings>());
            Assert.IsNull(root.GetComponent<EasyMovementInput>());
            Undo.PerformRedo();
            actor = root.GetComponent<InteractionActor>();
            Assert.IsNotNull(actor);
            var input = actor.GetComponent<EasyInteractionInput>();
            var motor = actor.GetComponent<EasyMovementInteractionMotor>();
            Assert.AreSame(actor, input.Actor);
            Assert.AreSame(root.GetComponent<PlayerInputSettings>(), input.Player);
            Assert.AreSame(character, motor.character);
            Assert.AreSame(motor, actor.Motor, "Actor serialized motor after redo: " + new SerializedObject(actor).FindProperty("motor").objectReferenceValue);
            Assert.AreSame(character.gameObject.transform, actor.PlayerRoot);
            Assert.AreSame(input, actor.GetComponent<InteractionPresenter>().InputSource);
        }

        [Test]
        public void UndoRestoresAuthoredCameraReferencesAndLegacyEnabledState()
        {
            var character = NewRoot("Existing player").AddComponent<FirstPersonCharacterController>();
            var eye = new GameObject("Existing camera actor");
            eye.transform.SetParent(character.gameObject.transform);
            var actor = eye.AddComponent<InteractionActor>();
            var legacy = eye.AddComponent<InteractionInput>();
            var presenter = eye.AddComponent<InteractionPresenter>();
            presenter.InputSource = legacy;
            var motor = eye.AddComponent<EasyMovementInteractionMotor>();
            actor.Motor = null;
            motor.character = null;
            legacy.enabled = true;
            actor.PlayerRoot = eye.transform;
            var viewProperty = new SerializedObject(actor).FindProperty("view");
            viewProperty.objectReferenceValue = null;
            viewProperty.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsNull(actor.Motor);
            Assert.IsNull(motor.character);
            Assert.IsTrue(legacy.enabled);
            InteractionPlayerSetup.Configure(character);
            Assert.IsFalse(legacy.enabled);
            Assert.AreSame(character, motor.character);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.AreSame(eye.transform, actor.PlayerRoot);
            Assert.IsNull(new SerializedObject(actor).FindProperty("view").objectReferenceValue);
            Assert.IsNull(actor.Motor);
            Assert.IsNull(motor.character);
            Assert.IsTrue(legacy.enabled);
            Assert.AreSame(legacy, presenter.InputSource);
            Assert.IsNull(eye.GetComponent<EasyInteractionInput>());
            Undo.PerformRedo();
            Assert.IsFalse(legacy.enabled);
            Assert.AreSame(character, motor.character);
            Assert.AreSame(motor, actor.Motor);
            Assert.AreSame(eye.GetComponent<EasyInteractionInput>(), presenter.InputSource);
        }

        [Test]
        public void LinearHandleUndoRedoRestoresComponentsAndConfiguredValues()
        {
            var position = new Vector3(4, 5, 6);
            var previousRoots = scene.GetRootGameObjects();
            var handle = Track(EasyInteraction.Editor.InteractionSetup.CreateLinearHandle(position));
            Assert.IsNotNull(handle.GetComponent<MechanicalAction>());
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.IsFalse(handle);
            foreach (var previousRoot in previousRoots)
                Assert.IsTrue(previousRoot, "Undo must not delete a preexisting Test Runner scene object.");
            Undo.PerformRedo();
            GameObject restored = null;
            int restoredCount = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (Array.IndexOf(previousRoots, root) < 0 && root.name == "Linear handle")
                {
                    // Redo may recreate the native object; retain its current managed wrapper for cleanup.
                    restored = Track(root);
                    restoredCount++;
                }

            Assert.AreEqual(1, restoredCount);
            var action = restored.GetComponent<MechanicalAction>();
            Assert.IsNotNull(action);
            Assert.IsNotNull(restored.GetComponent<InteractionTarget>());
            var referencePose = typeof(MechanicalAction).GetField("initialPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(action);
            Assert.AreEqual(position, restored.transform.position, $"Cached initialPosition={referencePose}; localPosition={restored.transform.localPosition}; value={action.Value}; axis={action.Axis}; maximum={action.Maximum}");
            Assert.AreEqual(new Vector3(.4f, .4f, .2f), restored.transform.localScale);
            Assert.AreEqual("Slide", action.ActionLabel);
            Assert.AreEqual(MechanicalMotion.Linear, action.Motion);
            Assert.AreEqual(Vector3.right, action.Axis);
            Assert.AreEqual(0, action.Minimum);
            Assert.AreEqual(1, action.Maximum);
        }
    }
}
