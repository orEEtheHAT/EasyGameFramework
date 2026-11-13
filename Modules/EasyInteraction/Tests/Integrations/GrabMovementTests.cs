using System;
using System.Collections.Generic;
using System.Reflection;
using EasyInteraction.Integrations.Movement;
using EasyMovement.FirstPerson;
using ECM2;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Integrations
{
    public sealed class GrabMovementTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private FirstPersonCharacterController character;
        private EasyMovementInteractionMotor motor;
        private CharacterMovement movement;
        private SimulationMode previousMode;
        private float previousStep;
        private readonly Vector3 origin = new Vector3(0, 1200, 0);
        private GameObject Go(string name, Vector3 position)
        { var go = new GameObject(name); go.transform.position = position; objects.Add(go); return go; }
        [SetUp] public void SetUp()
        {
            previousMode = Physics.simulationMode; previousStep = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = .02f;
            var root = Go("Real ECM2 owner", origin); root.SetActive(false);
            character = root.AddComponent<FirstPersonCharacterController>();
            actor = root.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false;
            motor = root.AddComponent<EasyMovementInteractionMotor>(); motor.character = character;
            root.SetActive(true); character.enabled = false; // The test drives the real Move explicitly.
            movement = character.characterMovement; movement.enablePhysicsInteraction = true;
            actor.View = Go("Eye", origin + Vector3.up * 1.3f).transform; actor.View.SetParent(root.transform);
            Physics.SyncTransforms();
        }
        [TearDown] public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Cleanup(); Physics.simulationMode = previousMode; Time.fixedDeltaTime = previousStep;
        }
        private GrabAction Crate(float distance)
        {
            var go = Go("Held rigidbody", actor.View.position + Vector3.forward * distance);
            go.AddComponent<BoxCollider>().size = Vector3.one * 1.1f; go.AddComponent<Rigidbody>().mass = 3;
            var grab = go.AddComponent<GrabAction>(); grab.RotationMode = GrabRotation.Stabilized;
            Physics.SyncTransforms(); return grab;
        }
        private static Action Call(object instance, string name) => (Action)Delegate.CreateDelegate(typeof(Action), instance,
            instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance));
        private static void Cleanup()
        {
            var type = typeof(GrabAction).Assembly.GetType("EasyInteraction.GrabCollisionExclusions");
            foreach (var item in Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None)) Call(item, "FixedUpdate")();
        }
        private void Begin(GrabAction grab) => Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);

        [Test] public void OverlappingSpinningHeldBodyCannotDepenetrateOrPushRealEcmPlayer()
        {
            var grab = Crate(.25f); var item = grab.GetComponent<Collider>();
            movement.Move(Vector3.zero, .02f);
            Assert.Greater(Vector3.Distance(movement.GetPosition(), origin), .05f, "Positive control: unfiltered ECM2 resolves the real overlap.");
            movement.SetPosition(origin); grab.Body.position = origin + Vector3.up * 1.3f + Vector3.forward * .25f;
            grab.Body.linearVelocity = Vector3.zero; Physics.SyncTransforms();
            actor.HandAnchor = Go("Close hold point", grab.Body.position).transform; Begin(grab);
            grab.Body.angularVelocity = Vector3.up * 6;
            var tick = Call(grab, "FixedUpdate");
            for (int i = 0; i < 100; i++)
            {
                Cleanup(); tick(); movement.Move(Vector3.zero, .02f); Physics.Simulate(.02f);
                Assert.Less(Vector3.Distance(movement.GetPosition(), origin), .001f);
            }
            Assert.IsTrue(Physics.GetIgnoreCollision(item, character.GetComponent<CapsuleCollider>()));
            Assert.Less(grab.Body.angularVelocity.magnitude, .04f);
            Assert.IsTrue(grab.IsHeld);
        }

        [Test] public void EcmSweepsPassThroughHeldCompoundBodyButStillStopAtWorldWall()
        {
            var grab = Crate(1); var child = Go("Compound", grab.transform.position + Vector3.right * .3f);
            child.transform.SetParent(grab.transform); var childCollider = child.AddComponent<SphereCollider>();
            Begin(grab); Cleanup(); Assert.IsTrue(movement.colliderFilterCallback(childCollider));
            var wall = Go("World wall", origin + Vector3.forward * 3).AddComponent<BoxCollider>(); wall.size = new Vector3(8, 8, .2f);
            Physics.SyncTransforms();
            for (int i = 0; i < 50; i++) movement.Move(Vector3.forward * 4, .02f);
            Assert.Greater(movement.GetPosition().z, 2); Assert.Less(movement.GetPosition().z, 2.7f);
            Assert.IsFalse(movement.colliderFilterCallback(wall));
        }

        [Test] public void DropInsidePlayerRetainsQueryExclusionUntilSeparation()
        {
            var grab = Crate(.2f); var item = grab.GetComponent<Collider>(); Begin(grab); actor.Cancel(); Cleanup();
            Assert.IsTrue(movement.colliderFilterCallback(item)); movement.Move(Vector3.zero, .02f);
            Assert.Less(Vector3.Distance(movement.GetPosition(), origin), .001f);
            grab.Body.position += Vector3.forward * 3; Physics.SyncTransforms(); Cleanup();
            Assert.IsNull(movement.colliderFilterCallback); Assert.IsFalse(Physics.GetIgnoreCollision(item, character.GetComponent<CapsuleCollider>()));
        }

        [Test] public void ThrowFromOverlapLeavesPlayerStillThenRestoresCollisions()
        {
            var grab = Crate(.2f); Begin(grab); Assert.IsTrue(grab.Throw(actor));
            for (int i = 0; i < 35; i++) { Cleanup(); movement.Move(Vector3.zero, .02f); Physics.Simulate(.02f); }
            Assert.Less(Vector3.Distance(movement.GetPosition(), origin), .001f);
            Assert.IsNull(movement.colliderFilterCallback);
            Assert.IsFalse(Physics.GetIgnoreCollision(grab.GetComponent<Collider>(), character.GetComponent<CapsuleCollider>()));
        }

        [Test] public void PriorEcmIgnoreSetsAndCallbackSurviveAllOwnedLeases()
        {
            var grab = Crate(2); var item = grab.GetComponent<Collider>();
            var unrelated = Go("Prior filtered obstacle", origin + Vector3.right * 3).AddComponent<BoxCollider>();
            CharacterMovement.ColliderFilterCallback previous = c => c == unrelated;
            movement.colliderFilterCallback = previous; movement.IgnoreCollision(grab.Body, true);
            using var independent = motor.AcquireCollisionExclusion(grab.Body);
            Begin(grab); actor.Cancel(); Cleanup(); Assert.IsTrue(movement.colliderFilterCallback(item)); Assert.IsTrue(movement.colliderFilterCallback(unrelated));
            independent.Dispose(); independent.Dispose(); Assert.AreSame(previous, movement.colliderFilterCallback);
            grab.transform.position = origin + Vector3.up * 1.3f + Vector3.forward * .2f;
            grab.Body.position = grab.transform.position; Physics.SyncTransforms();
            movement.Move(Vector3.zero, .02f); Assert.Less(Vector3.Distance(movement.GetPosition(), origin), .001f, "The preexisting ECM2 rigidbody ignore must survive.");
            movement.IgnoreCollision(grab.Body, false);
            var capsule = character.GetComponent<CapsuleCollider>();
            Assert.IsTrue(Physics.ComputePenetration(item, item.transform.position, item.transform.rotation,
                capsule, capsule.transform.position, capsule.transform.rotation, out _, out float depth),
                $"Control must overlap: body={grab.Body.position}, collider={item.transform.position}, player={movement.GetPosition()}.");
            Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule));
            movement.Move(Vector3.zero, .02f);
            Assert.Greater(Vector3.Distance(movement.GetPosition(), origin), .05f, $"Unfiltered control depth={depth}, body={grab.Body.position}, collider={item.transform.position}.");
        }

        [Test] public void PoolingClearsQueryLeaseAndLaterForeignCallbackIsNotOverwritten()
        {
            var grab = Crate(.2f); Begin(grab); grab.gameObject.SetActive(false); Cleanup(); Assert.IsNull(movement.colliderFilterCallback);
            grab.gameObject.SetActive(true); Physics.SyncTransforms(); Begin(grab);
            CharacterMovement.ColliderFilterCallback foreign = c => false;
            movement.colliderFilterCallback = foreign;
            grab.Body.position += Vector3.forward * 3; Physics.SyncTransforms(); actor.Cancel(); Cleanup();
            Assert.AreSame(foreign, movement.colliderFilterCallback);
        }

        [Test] public void LargePlayerSensorDoesNotExtendReleaseGraceOrLoseItsTriggerPairs()
        {
            var sensorObject = Go("Player sensor", actor.View.position); sensorObject.transform.SetParent(character.transform);
            var sensor = sensorObject.AddComponent<SphereCollider>(); sensor.radius = 5; sensor.isTrigger = true;
            var grab = Crate(.2f); var item = grab.GetComponent<Collider>(); Begin(grab);
            Assert.IsFalse(Physics.GetIgnoreCollision(item, sensor));
            actor.Cancel(); Cleanup(); Assert.IsTrue(movement.colliderFilterCallback(item));
            grab.Body.position = actor.View.position + Vector3.forward * 2; Physics.SyncTransforms(); Cleanup();
            Assert.IsNull(movement.colliderFilterCallback, "Only solid owner overlap may delay query restoration.");
            Assert.IsFalse(Physics.GetIgnoreCollision(item, sensor));
            Assert.IsFalse(Physics.GetIgnoreCollision(item, character.GetComponent<CapsuleCollider>()));
        }
    }
}
