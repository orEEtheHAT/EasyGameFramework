using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class GrabComfortTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private CapsuleCollider capsule;
        private SimulationMode oldSimulation;
        private float oldStep;
        private static readonly Type cleanupType = typeof(GrabAction).Assembly.GetType("EasyInteraction.GrabCollisionExclusions");
        private GameObject Go(string name, Vector3 position)
        {
            var go = new GameObject(name); go.transform.position = position; objects.Add(go); return go;
        }
        [SetUp] public void SetUp()
        {
            oldSimulation = Physics.simulationMode; oldStep = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = .02f;
            actor = Go("Owner", new Vector3(0, 1000, 0)).AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false;
            capsule = actor.gameObject.AddComponent<CapsuleCollider>(); capsule.height = 1.8f; capsule.radius = .4f; capsule.center = Vector3.up * .9f;
            actor.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            var eye = Go("View", actor.transform.position + Vector3.up * 1.3f); eye.transform.SetParent(actor.transform);
            actor.View = eye.transform;
        }
        [TearDown] public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Cleanup(); Physics.simulationMode = oldSimulation; Time.fixedDeltaTime = oldStep;
        }
        private GrabAction Crate(float distance = 2)
        {
            var go = Go("Crate", actor.View.position + Vector3.forward * distance);
            go.AddComponent<BoxCollider>().size = Vector3.one * .6f;
            go.AddComponent<Rigidbody>().mass = 3;
            var grab = go.AddComponent<GrabAction>(); grab.RotationMode = GrabRotation.Stabilized;
            Physics.SyncTransforms(); return grab;
        }
        private static Action Call(object instance, string method) => (Action)Delegate.CreateDelegate(typeof(Action), instance,
            instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic));
        private static Action CleanupCall()
        {
            var found = Object.FindObjectsByType(cleanupType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            return found.Length == 0 ? null : Call(found[0], "FixedUpdate");
        }
        private static void Cleanup() => CleanupCall()?.Invoke();
        private void Steps(GrabAction grab, int count)
        {
            var tick = Call(grab, "FixedUpdate"); var cleanup = CleanupCall();
            for (int i = 0; i < count; i++) { cleanup?.Invoke(); tick(); Physics.Simulate(Time.fixedDeltaTime); }
        }
        private void Begin(GrabAction grab) => Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);

        [Test] public void CompoundPairsPreservePreexistingIgnoreAndKeepWorldContacts()
        {
            var grab = Crate(); var first = grab.GetComponent<Collider>();
            var child = Go("Compound child", grab.transform.position + Vector3.up * .3f); child.transform.SetParent(grab.transform);
            var second = child.AddComponent<SphereCollider>(); second.radius = .2f;
            var extra = Go("Owner child", actor.View.position); extra.transform.SetParent(actor.transform);
            var ownerChild = extra.AddComponent<SphereCollider>(); ownerChild.radius = .2f;
            var wall = Go("World", actor.View.position + Vector3.right * 3).AddComponent<BoxCollider>();
            Physics.IgnoreCollision(first, capsule, true); Begin(grab);
            foreach (var item in new[] { first, second }) foreach (var owner in new Collider[] { capsule, ownerChild }) Assert.IsTrue(Physics.GetIgnoreCollision(item, owner));
            Assert.IsFalse(Physics.GetIgnoreCollision(first, wall));
            actor.Cancel(); Cleanup();
            Assert.IsTrue(Physics.GetIgnoreCollision(first, capsule));
            Assert.IsFalse(Physics.GetIgnoreCollision(second, capsule));
            Assert.IsFalse(Physics.GetIgnoreCollision(first, ownerChild));
        }

        [Test] public void DropInsideOwnerWaitsForSeparationAndRegrabRetainsOwnership()
        {
            var grab = Crate(.1f); var item = grab.GetComponent<Collider>(); Begin(grab);
            actor.Cancel(); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            Begin(grab); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            grab.Body.position = actor.View.position + Vector3.forward * 2; Physics.SyncTransforms(); Cleanup();
            Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule), "The new holder still owns the pair.");
            actor.Cancel(); Cleanup(); Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule));
        }

        [Test] public void DestroyedGrabComponentDefersUntilLiveBodySeparates()
        {
            var grab = Crate(.1f); var item = grab.GetComponent<Collider>(); var body = grab.Body; Begin(grab);
            Object.DestroyImmediate(grab); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            body.position += Vector3.forward * 3; Physics.SyncTransforms(); Cleanup();
            Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule));
        }

        [Test] public void ReleaseUsesPhysicsPoseWhenInterpolatedTransformIsStillOutsideOwner()
        {
            var grab = Crate(); var item = grab.GetComponent<Collider>(); Begin(grab);
            grab.Body.position = actor.View.position + Vector3.forward * .15f;
            Assert.IsTrue(Physics.ComputePenetration(item, grab.Body.position, grab.Body.rotation,
                capsule, capsule.transform.position, capsule.transform.rotation, out _, out _));
            actor.Cancel(); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            grab.Body.position = actor.View.position + Vector3.forward * 2; Cleanup();
            Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule));
        }

        [Test] public void PoolingAndChangingCompoundCollidersDoNotLeaveStalePairs()
        {
            var grab = Crate(); var item = grab.GetComponent<Collider>(); Begin(grab);
            var child = Go("Added collider", grab.transform.position); child.transform.SetParent(grab.transform);
            var added = child.AddComponent<SphereCollider>(); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(added, capsule));
            added.enabled = false; Cleanup(); added.enabled = true; Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(added, capsule));
            grab.gameObject.SetActive(false); Cleanup(); grab.gameObject.SetActive(true); Physics.SyncTransforms();
            Begin(grab); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            actor.Cancel(); Cleanup(); Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule)); Assert.IsFalse(Physics.GetIgnoreCollision(added, capsule));
        }

        [Test] public void MultipleOwnersOfOnePlayerRootReleaseOnlyTheirOwnExclusion()
        {
            var second = Go("Other actor", actor.View.position).AddComponent<InteractionActor>(); second.View = actor.View; second.PlayerRoot = actor.PlayerRoot; second.AutomaticUpdate = false;
            var grab = Crate(); grab.MaxParticipants = 2; Begin(grab); Assert.IsTrue(second.TryExecuteDirect(grab));
            var item = grab.GetComponent<Collider>(); actor.Cancel(); Cleanup(); Assert.IsTrue(Physics.GetIgnoreCollision(item, capsule));
            second.Cancel(); Cleanup(); Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule));
        }

        [Test] public void TransferExcludesReceiverAndRestoresPreviousOwner()
        {
            var second = Go("Receiver", actor.transform.position + Vector3.right).AddComponent<InteractionActor>(); second.AutomaticUpdate = false;
            var otherCollider = second.gameObject.AddComponent<CapsuleCollider>(); second.View = actor.View;
            var grab = Crate(); Begin(grab); Assert.IsTrue(grab.TryTransferTo(second)); Cleanup();
            var item = grab.GetComponent<Collider>(); Assert.IsFalse(Physics.GetIgnoreCollision(item, capsule)); Assert.IsTrue(Physics.GetIgnoreCollision(item, otherCollider));
            second.Cancel(); Cleanup(); Assert.IsFalse(Physics.GetIgnoreCollision(item, otherCollider));
        }

        [Test] public void CentredOffsetAndExplicitAnchorPriorityConverge()
        {
            var grab = Crate(); grab.PoseMode = GrabPoseMode.PreservePickupPose; Begin(grab); Steps(grab, 100);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * 2), .02f);
            grab.HoldOffset = new Vector3(.25f, -.15f, .1f); actor.View.rotation = Quaternion.Euler(0, 35, 0); Steps(grab, 150);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.rotation * new Vector3(.25f, -.15f, 2.1f)), .025f);
            actor.HandAnchor = Go("Hand", actor.View.position + Vector3.forward).transform;
            grab.HoldPoint = Go("Explicit hold", actor.View.position + Vector3.forward * 1.8f).transform;
            grab.HoldPoint.rotation = Quaternion.Euler(0, 90, 0); grab.HoldDistance = 3;
            var grip = Go("Grip", grab.transform.position + Vector3.right * .2f); grip.transform.SetParent(grab.transform); grab.GripPoint = grip.transform;
            Steps(grab, 150);
            Assert.Less(Vector3.Distance(grab.GripPosition, grab.HoldPoint.position + grab.HoldPoint.rotation * grab.HoldOffset), .03f);
            grab.HoldPoint = null; Steps(grab, 150);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.HandAnchor.position + actor.HandAnchor.rotation * grab.HoldOffset), .03f);
        }

        [TestCase(.01f), TestCase(.02f), TestCase(1f / 30f)]
        public void SpinAndHalfTurnSettleAtDifferentPhysicsSteps(float dt)
        {
            Time.fixedDeltaTime = dt; var grab = Crate(); Begin(grab); grab.Body.angularVelocity = Vector3.up * 6;
            actor.View.rotation = Quaternion.Euler(0, 179, 0); Steps(grab, Mathf.CeilToInt(2.5f / dt));
            actor.View.rotation = Quaternion.Euler(0, -179, 0); Steps(grab, Mathf.CeilToInt(2.5f / dt));
            Assert.IsTrue(grab.IsHeld); Assert.Less(grab.Body.angularVelocity.magnitude, .035f);
            Assert.Less(Quaternion.Angle(grab.Body.rotation, actor.View.rotation), .5f);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * 2), .035f);
            grab.Rotate(new Vector2(35, 15)); grab.SetInspection(true); Steps(grab, Mathf.CeilToInt(2f / dt));
            Assert.Less(grab.Body.angularVelocity.magnitude, .04f); Assert.Greater(Quaternion.Angle(grab.Body.rotation, actor.View.rotation), 20);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * .65f), .035f);
        }

        [Test] public void ChangingFollowSpeedWhileHeldPreservesMomentumAndUsesBoundedPhysicalMotion()
        {
            var grab = Crate(); Begin(grab); Steps(grab, 100);
            grab.Body.linearVelocity = new Vector3(.3f, -.2f, .1f); grab.Body.angularVelocity = Vector3.up * .2f;
            var original = grab.Body.position; var rotation = grab.Body.rotation;
            var velocity = grab.Body.linearVelocity; var angular = grab.Body.angularVelocity;
            foreach (float speed in new[] { 0f, 1f, .5f })
            {
                grab.FollowSpeed = speed;
                Assert.AreEqual(original, grab.Body.position); Assert.AreEqual(rotation, grab.Body.rotation);
                Assert.AreEqual(velocity, grab.Body.linearVelocity); Assert.AreEqual(angular, grab.Body.angularVelocity);
            }
            actor.View.rotation = Quaternion.Euler(0, 90, 0); Call(grab, "FixedUpdate")();
            Assert.AreEqual(original, grab.Body.position); Assert.AreEqual(velocity, grab.Body.linearVelocity);
            Assert.LessOrEqual(grab.Body.GetAccumulatedForce(Time.fixedDeltaTime).magnitude / grab.Body.mass, 80.002f);
            Physics.Simulate(Time.fixedDeltaTime);
            Assert.Greater(Vector3.Distance(grab.Body.position, original), .001f);
            Assert.Less(Vector3.Distance(grab.Body.position, original), .1f, "Only forces may move the body.");
            Steps(grab, 150);
            Assert.IsTrue(grab.IsHeld); Assert.IsFalse(grab.Body.isKinematic);
            Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * 2), .03f);
            Assert.AreEqual(0, (int)GrabRotation.Free); Assert.AreEqual(1, (int)GrabRotation.KeepInitial); Assert.AreEqual(2, (int)GrabRotation.FollowView);
        }

        [Test] public void FreeModeKeepsItsUnconstrainedAngularMotion()
        {
            var grab = Crate(); grab.PoseMode = GrabPoseMode.PreservePickupPose; grab.RotationMode = GrabRotation.Free; Begin(grab); grab.Body.angularVelocity = Vector3.up * 6;
            Steps(grab, 50); Assert.Greater(grab.Body.angularVelocity.magnitude, 4);
        }

        [UnityTest] public IEnumerator PauseAndAbruptViewTurnResumeWithoutAStoredImpulse()
        {
            float previousScale = Time.timeScale;
            try
            {
                Time.timeScale = 1; Physics.simulationMode = SimulationMode.FixedUpdate;
                var grab = Crate(); Begin(grab);
                for (int i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
                Time.timeScale = 0; var before = grab.Body.position;
                actor.View.rotation = Quaternion.Euler(0, 180, 0);
                for (int i = 0; i < 4; i++) yield return null;
                Assert.Less(Vector3.Distance(before, grab.Body.position), .001f);
                Time.timeScale = 1;
                for (int i = 0; i < 140; i++) yield return new WaitForFixedUpdate();
                Assert.IsTrue(grab.IsHeld); Assert.Less(grab.Body.angularVelocity.magnitude, .04f);
                Assert.Less(Vector3.Distance(grab.GripPosition, actor.View.position + actor.View.forward * 2), .035f);
            }
            finally { Time.timeScale = previousScale; }
        }

        [Test] public void WorldWallStillBlocksStableHoldAndThrowRestoresOwnerPair()
        {
            var grab = Crate(); Begin(grab); grab.HoldDistance = 4;
            var wall = Go("Wall", actor.View.position + Vector3.forward * 2.8f).AddComponent<BoxCollider>(); wall.size = new Vector3(6, 6, .2f); Physics.SyncTransforms();
            Steps(grab, 160); Assert.Less(grab.Body.position.z, 2.5f); Assert.IsTrue(grab.IsHeld);
            Assert.IsTrue(grab.Throw(actor)); Cleanup(); Assert.IsFalse(Physics.GetIgnoreCollision(grab.GetComponent<Collider>(), capsule));
            Assert.IsTrue(grab.Body.useGravity); Assert.IsFalse(Physics.GetIgnoreCollision(grab.GetComponent<Collider>(), wall));
        }

        [TestCase(GrabPoseMode.PreservePickupPose), TestCase(GrabPoseMode.AuthoredPose)]
        public void WarmStableHoldAndCollisionMaintenanceHaveNoMainThreadAllocations(GrabPoseMode poseMode)
        {
            var grab = Crate(); grab.PoseMode = poseMode; grab.LocalPosition = new Vector3(.2f, -.1f, .1f); grab.LocalEulerAngles = new Vector3(15, 70, -20);
            grab.HeldScaleMultiplier = .5f;
            Begin(grab); var tick = Call(grab, "FixedUpdate"); var cleanup = CleanupCall();
            bool previous = Profiler.enabled;
            try
            {
                Profiler.enabled = true;
                var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                using (var control = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!control.Valid || !Profiler.enabled) Assert.Inconclusive("Main-thread GC.Alloc unavailable.");
                    GC.KeepAlive(new byte[512]); control.Stop();
                    if (control.Count == 0 || control.GetSample(0).Count == 0) Assert.Inconclusive("GC.Alloc missed the positive control.");
                }
                for (int i = 0; i < 64; i++) { cleanup(); tick(); }
                long events;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) Assert.Inconclusive("GC.Alloc unavailable for the measured scope.");
                    for (int i = 0; i < 128; i++) { cleanup(); tick(); }
                    recorder.Stop(); events = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }
                Assert.That(events, Is.Zero, "128 warmed hold and collision-maintenance steps.");
            }
            finally { Profiler.enabled = previous; }
        }
    }
}
