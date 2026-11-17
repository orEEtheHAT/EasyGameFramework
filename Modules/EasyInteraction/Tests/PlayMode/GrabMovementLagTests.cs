using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class GrabMovementLagTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private SimulationMode oldSimulation;
        private float oldStep;
        private sealed class Sample
        {
            public InteractionActor Actor;
            public GrabAction Grab;
            public Action Tick;
            public Transform Root => Actor.transform;
            public Rigidbody Body => Grab.Body;
            public Vector3 Destination => Actor.View.position + Actor.View.forward * Grab.HoldDistance;
        }

        [SetUp] public void SetUp()
        {
            oldSimulation = Physics.simulationMode; oldStep = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = .02f;
        }

        [TearDown] public void TearDown()
        {
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
                objects.Clear();
            }
            finally { Physics.simulationMode = oldSimulation; Time.fixedDeltaTime = oldStep; }
        }

        private GameObject Go(string name, Vector3 position)
        {
            var go = new GameObject(name); go.transform.position = position; objects.Add(go); return go;
        }

        private Sample Create(float x, Action<GrabAction> configure = null, float yaw = 0, float pitch = 0)
        {
            var root = Go("Movement reference", new Vector3(x, 1000, 0)); root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var actor = root.AddComponent<InteractionActor>(); actor.AutomaticUpdate = false; actor.Range = 50; actor.RequireVisibility = false;
            var view = Go("View", root.transform.position).transform; view.SetParent(root.transform); view.localRotation = Quaternion.Euler(pitch, 0, 0); actor.View = view;
            var item = Go("Dynamic held item", view.position + view.forward * 2); item.transform.rotation = view.rotation;
            item.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            var body = item.AddComponent<Rigidbody>(); body.mass = 1; body.linearDamping = body.angularDamping = 0;
            var grab = item.AddComponent<GrabAction>(); grab.IgnoreOwnerCollisions = false; grab.GrabRange = 50; grab.BreakDistance = 20;
            configure?.Invoke(grab); Physics.SyncTransforms();
            Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);
            return new Sample { Actor = actor, Grab = grab, Tick = (Action)Delegate.CreateDelegate(typeof(Action), grab,
                typeof(GrabAction).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)) };
        }

        private static void Seed(params Sample[] samples)
        {
            foreach (var sample in samples) sample.Tick();
            Physics.Simulate(Time.fixedDeltaTime);
        }

        private static Vector3 Axis(int axis) => axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;

        private static Vector3 Force(Sample sample) => sample.Body.GetAccumulatedForce(Time.fixedDeltaTime);
        private static Vector3 Torque(Sample sample) => sample.Body.GetAccumulatedTorque(Time.fixedDeltaTime);

        [TestCase(.01f, 0, 1)] [TestCase(.01f, 0, -1)]
        [TestCase(.01f, 1, 1)] [TestCase(.01f, 1, -1)]
        [TestCase(.01f, 2, 1)] [TestCase(.01f, 2, -1)]
        [TestCase(.02f, 0, 1)] [TestCase(.02f, 0, -1)]
        [TestCase(.02f, 1, 1)] [TestCase(.02f, 1, -1)]
        [TestCase(.02f, 2, 1)] [TestCase(.02f, 2, -1)]
        public void AxisCoefficientsRemoveOrScaleSteadyWalkingDriftAtTheForceSolveSample(float dt, int axis, int sign)
        {
            Time.fixedDeltaTime = dt;
            var zero = Create(0, g => { var coefficients = Vector3.one; coefficients[axis] = 0; g.MovementLag = coefficients; }, 40, 15);
            var half = Create(10, g => { var coefficients = Vector3.one; coefficients[axis] = .5f; g.MovementLag = coefficients; }, 40, 15);
            var full = Create(20, g => g.MovementLag = Vector3.one, 40, 15);
            var samples = new[] { zero, half, full }; Seed(samples);
            var direction = zero.Root.rotation * Axis(axis) * sign;
            var errors = new float[3]; int measured = 0;
            int count = Mathf.CeilToInt(4 / dt);
            for (int step = 0; step < count; step++)
            {
                foreach (var sample in samples) sample.Root.position += direction * dt;
                // Sample after the reference advance, before force/Simulate: both states represent this solve time.
                // Comparing after Simulate to the unadvanced reference would introduce a one-step v*dt phase lead.
                if (step >= count - 50)
                {
                    for (int i = 0; i < samples.Length; i++) errors[i] += Vector3.Dot(samples[i].Destination - samples[i].Body.position, direction);
                    measured++;
                }
                foreach (var sample in samples) sample.Tick();
                Physics.Simulate(dt);
            }
            for (int i = 0; i < errors.Length; i++) errors[i] /= measured;
            Assert.Less(Mathf.Abs(errors[0]), .015f, "Axis zero must remove the persistent physical damping drift.");
            Assert.Greater(errors[2], .07f, "Positive control: explicit one must retain the full walking lag.");
            Assert.Less(Mathf.Abs(errors[1] - errors[2] * .5f), .012f, "Fractional coefficients must give intermediate physical walking lag.");
            foreach (var sample in samples) { Assert.IsTrue(sample.Grab.IsHeld); Assert.IsFalse(sample.Body.isKinematic); Assert.IsTrue(sample.Body.detectCollisions); }
        }

        [TestCase(GrabRotation.Free)] [TestCase(GrabRotation.FollowView)] [TestCase(GrabRotation.KeepInitial)]
        public void WalkingCompensationAlsoAppliesToLegacyRotationModesWithoutConstrainingPureFree(GrabRotation mode)
        {
            var sample = Create(0, g => { g.PoseMode = GrabPoseMode.PreservePickupPose; g.RotationMode = mode; g.MovementLag = Vector3.zero; });
            Seed(sample); if (mode == GrabRotation.Free) sample.Body.angularVelocity = Vector3.up * 2;
            float error = 0;
            for (int i = 0; i < 200; i++)
            {
                sample.Root.position += Vector3.forward * Time.fixedDeltaTime;
                if (i >= 150) error += sample.Destination.z - sample.Body.position.z;
                sample.Tick(); Physics.Simulate(Time.fixedDeltaTime);
            }
            Assert.Less(Mathf.Abs(error / 50), .015f); Assert.IsTrue(sample.Grab.IsHeld);
            if (mode == GrabRotation.Free) Assert.Greater(sample.Body.angularVelocity.magnitude, 1.9f, "Pure Free mode must remain free while walking compensation is active.");
        }

        [TestCase(0f)] [TestCase(1f)]
        public void WalkingZeroHalfAndOneStayIndependentAtBothFollowSpeedExtremes(float followSpeed)
        {
            var zero = Create(0, g => { g.FollowSpeed = followSpeed; g.MovementLag = Vector3.zero; }, 40, 15);
            var half = Create(10, g => { g.FollowSpeed = followSpeed; g.MovementLag = Vector3.one * .5f; }, 40, 15);
            var full = Create(20, g => { g.FollowSpeed = followSpeed; g.MovementLag = Vector3.one; }, 40, 15);
            var samples = new[] { zero, half, full }; Seed(samples);
            var direction = zero.Root.forward; var errors = new float[3];
            for (int step = 0; step < 250; step++)
            {
                foreach (var sample in samples) sample.Root.position += direction * Time.fixedDeltaTime;
                if (step >= 200)
                    for (int i = 0; i < samples.Length; i++) errors[i] += Vector3.Dot(samples[i].Destination - samples[i].Body.position, direction) / 50;
                foreach (var sample in samples) sample.Tick();
                Physics.Simulate(Time.fixedDeltaTime);
            }
            Assert.Less(Mathf.Abs(errors[0]), .02f, "Walking zero must remove steady damping drift even when Follow Speed changes.");
            Assert.Greater(errors[2], .05f, "Walking one must retain measurable physical lag at both ends of Follow Speed.");
            Assert.Less(Mathf.Abs(errors[1] - errors[2] * .5f), .015f);
            foreach (var sample in samples) { Assert.IsTrue(sample.Grab.IsHeld); Assert.AreEqual(followSpeed, sample.Grab.FollowSpeed); }
        }

        [Test]
        public void DefaultsBoundsAndResolvedMovementReferenceAreExplicit()
        {
            var sample = Create(0); var grab = sample.Grab;
            Assert.AreEqual(Vector3.one * .5f, grab.MovementLag); Assert.AreEqual(.5f, grab.FollowSpeed);
            Assert.IsNull(grab.MovementReference); Assert.AreSame(sample.Root, grab.GetMovementReference(sample.Actor));
            grab.MovementLag = new Vector3(-2, 3, float.NaN); Assert.AreEqual(new Vector3(0, 1, 1), grab.MovementLag);
            var external = Go("External frame", sample.Destination).transform;
            grab.HoldPoint = external; Assert.AreSame(external, grab.GetMovementReference(sample.Actor));
            grab.MovementReference = sample.Root; Assert.AreSame(sample.Root, grab.GetMovementReference(sample.Actor));
            grab.MovementReference = null; grab.HoldPoint = null; sample.Actor.HandAnchor = external;
            Assert.AreSame(external, grab.GetMovementReference(sample.Actor));
        }

        [Test]
        public void WalkingMasksDoNotCompensateCameraTurningAndOnlySelectedTranslationChanges()
        {
            var baseline = Create(0);
            var walkingOff = Create(10, g => g.MovementLag = Vector3.zero);
            var translationBaseline = Create(20);
            var translationOff = Create(30, g => g.MovementLag = Vector3.zero);
            Seed(baseline, walkingOff, translationBaseline, translationOff);
            baseline.Actor.View.localRotation = walkingOff.Actor.View.localRotation = Quaternion.Euler(0, 10, 0);
            translationBaseline.Root.position += Vector3.forward * .02f; translationOff.Root.position += Vector3.forward * .02f;
            foreach (var sample in new[] { baseline, walkingOff, translationBaseline, translationOff }) sample.Tick();
            Assert.Greater(Force(baseline).magnitude, 1, "Positive control: the turned target produces an actual physical response.");
            Assert.Less((Force(baseline) - Force(walkingOff)).magnitude, .003f, "Walking settings must not classify an in-place camera turn as walking.");
            Assert.Less((Torque(baseline) - Torque(walkingOff)).magnitude, .003f);
            Assert.Greater(Force(translationOff).z, Force(translationBaseline).z + 1);
            Physics.Simulate(Time.fixedDeltaTime);
            Assert.Less((baseline.Body.linearVelocity - walkingOff.Body.linearVelocity).magnitude, .001f);
            Assert.Less((baseline.Body.angularVelocity - walkingOff.Body.angularVelocity).magnitude, .001f);
            Assert.Greater(translationOff.Body.linearVelocity.z, translationBaseline.Body.linearVelocity.z + .1f,
                "Selected walking compensation must change real momentum while in-place turning stays independent.");
        }

        [Test]
        public void StoppingStillUsesBoundedPhysicalMotionAndSettlesWithoutTeleporting()
        {
            var sample = Create(0, g => g.MovementLag = Vector3.zero); Seed(sample);
            for (int i = 0; i < 150; i++) { sample.Root.position += Vector3.forward * .02f; sample.Tick(); Physics.Simulate(.02f); }
            for (int i = 0; i < 150; i++)
            {
                var position = sample.Body.position; sample.Tick();
                Assert.Less(Vector3.Distance(position, sample.Body.position), .0001f, "Compensation must apply forces rather than write the body's pose.");
                Assert.LessOrEqual(Force(sample).magnitude, 80.002f); Physics.Simulate(.02f);
            }
            Assert.Less(Vector3.Distance(sample.Body.position, sample.Destination), .02f); Assert.Less(sample.Body.linearVelocity.magnitude, .03f);
            Assert.IsTrue(sample.Grab.IsHeld);
        }

        [Test]
        public void CompensationKeepsWorldContactsAccelerationCapAndRawBreakDistance()
        {
            var sample = Create(0, g => g.MovementLag = Vector3.zero); Seed(sample);
            var wall = Go("World wall", new Vector3(0, 1000, 2.7f)).AddComponent<BoxCollider>(); wall.size = new Vector3(6, 6, .2f); Physics.SyncTransforms();
            for (int i = 0; i < 100; i++)
            {
                sample.Root.position += Vector3.forward * .02f; sample.Tick(); Assert.LessOrEqual(Force(sample).magnitude, 80.002f); Physics.Simulate(.02f);
            }
            Assert.IsTrue(sample.Grab.IsHeld); Assert.IsFalse(sample.Body.isKinematic); Assert.IsTrue(sample.Body.detectCollisions);
            Assert.Less(sample.Body.position.z, 2.5f, "Velocity tracking must not drive a dynamic body through the wall.");
            Assert.IsFalse(Physics.GetIgnoreCollision(sample.Grab.GetComponent<Collider>(), wall));
            sample.Grab.BreakDistance = .5f; sample.Root.position += Vector3.forward * 20;
            var before = sample.Body.position; sample.Tick(); Assert.IsFalse(sample.Grab.IsHeld); Assert.IsTrue(sample.Body.useGravity);
            Assert.Less(Vector3.Distance(before, sample.Body.position), .0001f);
        }

        [Test]
        public void StaticExternalAnchorDoesNotReceivePlayerVelocityAndResetDoesNotWriteBodyState()
        {
            var sample = Create(0, g => g.MovementLag = Vector3.zero);
            var anchor = Go("Static world hold point", sample.Destination).transform; sample.Grab.HoldPoint = anchor; sample.Grab.ResetMotionTracking(); Seed(sample);
            Assert.AreSame(anchor, sample.Grab.GetMovementReference(sample.Actor));
            for (int i = 0; i < 60; i++) { sample.Root.position += Vector3.forward * .02f; sample.Tick(); Physics.Simulate(.02f); }
            Assert.Less(Vector3.Distance(sample.Body.position, anchor.position), .005f); Assert.Less(sample.Body.linearVelocity.magnitude, .005f);
            var teleport = new Vector3(5, 0, 0); sample.Root.position += teleport; anchor.position += teleport; sample.Body.position += teleport;
            sample.Body.linearVelocity = Vector3.zero; sample.Body.angularVelocity = Vector3.zero;
            var position = sample.Body.position; var rotation = sample.Body.rotation;
            sample.Grab.ResetMotionTracking();
            Assert.AreEqual(position, sample.Body.position); Assert.AreEqual(rotation, sample.Body.rotation); Assert.AreEqual(Vector3.zero, sample.Body.linearVelocity);
            sample.Tick(); Assert.Less(Force(sample).magnitude, .005f, "Resetting a teleported rig must seed history rather than fabricate velocity.");
            Physics.Simulate(.02f);
            anchor.SetParent(sample.Root, true); sample.Grab.ResetMotionTracking();
            Assert.AreSame(sample.Root, sample.Grab.GetMovementReference(sample.Actor), "Explicit reset must re-resolve a changed rig hierarchy.");
            sample.Tick(); Assert.Less(Force(sample).magnitude, .005f, "Reparenting without a world-pose change must not fabricate reference motion.");
            Physics.Simulate(.02f);
            sample.Root.position += Vector3.forward * .02f; sample.Tick();
            Assert.Greater(Force(sample).z, 1, "A linked moving anchor must actually exercise velocity compensation."); Physics.Simulate(.02f);
            anchor.SetParent(null, true); sample.Body.position = anchor.position; sample.Body.linearVelocity = Vector3.zero; sample.Body.angularVelocity = Vector3.zero;
            sample.Grab.ResetMotionTracking(); Seed(sample);
            Assert.AreSame(anchor, sample.Grab.GetMovementReference(sample.Actor));
            for (int i = 0; i < 30; i++) { sample.Root.position += Vector3.forward * .02f; sample.Tick(); Physics.Simulate(.02f); }
            Assert.Less(Vector3.Distance(sample.Body.position, anchor.position), .005f, "Detached anchors must not retain a cached player reference velocity.");
        }

        [Test]
        public void SharedOwnersAverageWalkingSpeedAndTransferAndRegrabReseedHistory()
        {
            var sample = Create(0, g => { g.MovementLag = Vector3.zero; g.MaxParticipants = 2; });
            var receiverRoot = Go("Second holder", sample.Root.position + Vector3.right);
            var receiver = receiverRoot.AddComponent<InteractionActor>(); receiver.AutomaticUpdate = false; receiver.Range = 50; receiver.RequireVisibility = false; receiver.View = receiverRoot.transform;
            Assert.IsTrue(receiver.TryExecuteDirect(sample.Grab), receiver.LastFailure.Reason);
            sample.Body.position = (sample.Destination + receiver.View.position + receiver.View.forward * 2) * .5f;
            sample.Grab.ResetMotionTracking(); Seed(sample);
            float error = 0;
            for (int i = 0; i < 200; i++)
            {
                sample.Root.position += Vector3.forward * .02f; receiverRoot.transform.position += Vector3.forward * .02f;
                var destination = (sample.Destination + receiver.View.position + receiver.View.forward * 2) * .5f;
                if (i >= 150) error += destination.z - sample.Body.position.z;
                sample.Tick(); Physics.Simulate(.02f);
            }
            Assert.Less(Mathf.Abs(error / 50), .015f, "Two holders moving together must contribute one averaged velocity, not twice the speed.");
            Assert.AreEqual(2, sample.Grab.OwnerCount);
            sample.Body.position = receiver.View.position + receiver.View.forward * 2; sample.Body.linearVelocity = Vector3.zero; sample.Body.angularVelocity = Vector3.zero;
            sample.Actor.Cancel(sample.Grab); sample.Tick(); Assert.Less(Force(sample).magnitude, .005f, "Removing an owner must not generate a derivative spike."); Physics.Simulate(.02f);
            sample.Root.position = receiverRoot.transform.position;
            sample.Body.position = receiver.View.position + receiver.View.forward * 2; sample.Body.linearVelocity = Vector3.zero;
            Assert.IsTrue(sample.Grab.TryTransferTo(sample.Actor)); sample.Tick(); Assert.Less(Force(sample).magnitude, .005f); Physics.Simulate(.02f);
            sample.Actor.Cancel(sample.Grab); sample.Root.position += Vector3.right * 5;
            sample.Body.position = sample.Destination; sample.Body.rotation = sample.Actor.View.rotation; sample.Body.linearVelocity = Vector3.zero; sample.Body.angularVelocity = Vector3.zero;
            Assert.IsTrue(sample.Actor.TryExecuteDirect(sample.Grab)); sample.Tick(); Assert.Less(Force(sample).magnitude, .005f, "A new session cannot inherit the previous reference velocity.");
        }

        [Test]
        public void InspectionDistanceAndManualRotationRemainSpringDrivenRatherThanWalkingFeedforward()
        {
            var off = Create(0, g => g.MovementLag = Vector3.zero);
            var baseline = Create(10); Seed(off, baseline);
            foreach (var sample in new[] { off, baseline })
            {
                sample.Grab.MaxParticipants = 2;
                var secondRoot = Go("Angled second holder", sample.Root.position + Vector3.right);
                secondRoot.transform.rotation = Quaternion.Euler(0, 25, 0);
                var second = secondRoot.AddComponent<InteractionActor>(); second.View = secondRoot.transform;
                second.AutomaticUpdate = false; second.RequireVisibility = false; second.Range = 50;
                Assert.IsTrue(second.TryExecuteDirect(sample.Grab), second.LastFailure.Reason);
                sample.Body.position = (sample.Destination + second.View.position + second.View.forward * 2) * .5f;
                sample.Body.linearVelocity = Vector3.zero; sample.Body.angularVelocity = Vector3.zero; sample.Grab.ResetMotionTracking();
            }
            Seed(off, baseline);
            off.Grab.SetInspection(true); baseline.Grab.SetInspection(true); off.Tick(); baseline.Tick();
            Assert.Greater(Force(baseline).magnitude, .1f); Assert.Less((Force(off) - Force(baseline)).magnitude, .005f);
            Assert.Greater(Torque(baseline).magnitude, .01f, "Positive control: inspection changes the composite hand-line orientation.");
            Assert.Less((Torque(off) - Torque(baseline)).magnitude, .005f, "Walking masks must not alter the physical inspection response.");
            Physics.Simulate(.02f);
            off.Grab.SetInspection(false); baseline.Grab.SetInspection(false); off.Grab.AdjustDistance(.5f); baseline.Grab.AdjustDistance(.5f);
            off.Grab.Rotate(new Vector2(15, 5)); baseline.Grab.Rotate(new Vector2(15, 5)); off.Tick(); baseline.Tick();
            Assert.Less((Force(off) - Force(baseline)).magnitude, .005f);
            Assert.Greater(Torque(baseline).magnitude, .01f); Assert.Less((Torque(off) - Torque(baseline)).magnitude, .005f, "Walking masks must not alter manual rotation.");
        }

        [Test]
        public void WarmMovingReferenceBookkeepingAndForceSubmissionHaveNoMainThreadAllocations()
        {
            var sample = Create(0, g => g.MovementLag = new Vector3(.2f, .4f, 0)); Seed(sample);
            var tick = sample.Tick; var root = sample.Root; var delta = Vector3.right * .001f;
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
                for (int i = 0; i < 64; i++) { root.position += delta; tick(); }
                long events;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) Assert.Inconclusive("GC.Alloc unavailable for the measured scope.");
                    for (int i = 0; i < 128; i++) { root.position += delta; tick(); }
                    recorder.Stop(); events = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }
                Assert.AreEqual(0, events, "128 warmed moving-reference updates and force submissions; Physics.Simulate and first acquisition are outside this scope.");
            }
            finally { Profiler.enabled = previous; }
        }
    }
}
