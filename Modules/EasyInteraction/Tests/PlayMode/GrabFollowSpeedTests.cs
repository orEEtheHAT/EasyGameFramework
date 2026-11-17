using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class GrabFollowSpeedTests
    {
        private const float Dt = .02f;
        private readonly List<GameObject> objects = new List<GameObject>();
        private SimulationMode oldSimulation;
        private float oldStep;

        private sealed class Sample
        {
            public InteractionActor Actor;
            public GrabAction Grab;
            public Action Tick;
            public int StableSteps, SettledStep = -1;
            public Rigidbody Body => Grab.Body;
            public Vector3 Goal => Actor.View.position + Actor.View.rotation * (Grab.LocalPosition + Vector3.forward * Grab.HoldDistance);
            public Quaternion GoalRotation => Actor.View.rotation * Quaternion.Euler(Grab.LocalEulerAngles);
        }

        [SetUp] public void SetUp()
        {
            oldSimulation = Physics.simulationMode; oldStep = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script; Time.fixedDeltaTime = Dt;
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
            var item = new GameObject(name); item.transform.position = position; objects.Add(item); return item;
        }

        private Sample Create(float x, float followSpeed)
        {
            var actor = Go("Follow owner", new Vector3(x, 1000, 0)).AddComponent<InteractionActor>();
            actor.AutomaticUpdate = false; actor.RequireVisibility = false; actor.Range = 20;
            var view = Go("View", actor.transform.position).transform; view.SetParent(actor.transform); actor.View = view;
            var item = Go("Dynamic follow cube", view.position + new Vector3(.25f, -.25f, 1));
            item.transform.rotation = Quaternion.Euler(8, -20, 5); item.transform.localScale = Vector3.one * 1.1f;
            item.AddComponent<BoxCollider>();
            var body = item.AddComponent<Rigidbody>(); body.mass = 3; body.linearDamping = 0; body.angularDamping = .05f;
            body.useGravity = true; body.interpolation = RigidbodyInterpolation.None; body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            var grab = item.AddComponent<GrabAction>(); grab.IgnoreOwnerCollisions = false; grab.GrabRange = 20; grab.BreakDistance = 5;
            grab.PoseMode = GrabPoseMode.AuthoredPose; grab.LocalPosition = new Vector3(.25f, -.25f, 0); grab.LocalEulerAngles = new Vector3(8, -20, 5);
            grab.HoldDistance = 1; grab.HeldScaleMultiplier = .65f; grab.MovementLag = Vector3.zero; grab.FollowSpeed = followSpeed;
            Physics.SyncTransforms(); Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);
            var sample = new Sample { Actor = actor, Grab = grab,
                Tick = (Action)Delegate.CreateDelegate(typeof(Action), grab, typeof(GrabAction).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic)) };
            return sample;
        }

        private static Vector3 Tick(Sample sample)
        {
            var body = sample.Body; var position = body.position; var rotation = body.rotation;
            var velocity = body.linearVelocity; var angular = body.angularVelocity; var before = body.GetAccumulatedForce(Dt);
            sample.Tick();
            Assert.IsTrue(sample.Grab.IsHeld, sample.Actor.LastFailure.Reason);
            Assert.IsFalse(body.isKinematic); Assert.IsTrue(body.detectCollisions); Assert.AreEqual(RigidbodyConstraints.None, body.constraints);
            Assert.IsNull(body.transform.parent);
            Assert.Less(Vector3.Distance(position, body.position), .0001f); Assert.Less(Quaternion.Angle(rotation, body.rotation), .001f);
            Assert.Less((velocity - body.linearVelocity).magnitude, .0001f); Assert.Less((angular - body.angularVelocity).magnitude, .0001f);
            var acceleration = (body.GetAccumulatedForce(Dt) - before) / body.mass;
            Assert.IsTrue(float.IsFinite(acceleration.sqrMagnitude)); Assert.LessOrEqual(acceleration.magnitude, 80.002f);
            return acceleration;
        }

        private static void Step(params Sample[] samples)
        {
            foreach (var sample in samples) Tick(sample);
            Physics.Simulate(Dt);
        }

        [Test]
        public void FreshFollowSpeedDefaultsAndBoundsStayFiniteWithoutMutatingTheHeldBody()
        {
            var root = Go("Fresh follow configuration", new Vector3(10, 1000, 0));
            var fresh = root.AddComponent<GrabAction>(); Assert.AreEqual(.5f, fresh.FollowSpeed);
            fresh.FollowSpeed = -2; Assert.AreEqual(0, fresh.FollowSpeed);
            fresh.FollowSpeed = 3; Assert.AreEqual(1, fresh.FollowSpeed);
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            { fresh.FollowSpeed = invalid; Assert.AreEqual(.5f, fresh.FollowSpeed); }

            var sample = Create(0, .5f); var position = sample.Body.position; var rotation = sample.Body.rotation;
            sample.Body.linearVelocity = Vector3.right * .5f; var velocity = sample.Body.linearVelocity;
            sample.Body.angularVelocity = Vector3.up; var angular = sample.Body.angularVelocity;
            foreach (float value in new[] { 0f, 1f, float.NaN })
            {
                sample.Grab.FollowSpeed = value;
                Assert.AreEqual(position, sample.Body.position); Assert.AreEqual(rotation, sample.Body.rotation);
                Assert.AreEqual(velocity, sample.Body.linearVelocity); Assert.AreEqual(angular, sample.Body.angularVelocity);
            }
            Step(sample); Assert.IsTrue(sample.Grab.IsHeld); Assert.AreEqual(.5f, sample.Grab.FollowSpeed);
        }

        [TestCase(false)] [TestCase(true)]
        public void FollowSpeedExtremesProduceClearlyDifferentPositionAndOrientationAfterTenLookSteps(bool pitch)
        {
            var soft = Create(0, 0); var firm = Create(10, 1); var samples = new[] { soft, firm };
            for (int i = 0; i < 3; i++) Step(samples);
            var startSoftPosition = soft.Body.position; var startFirmPosition = firm.Body.position;
            var startSoftRotation = soft.Body.rotation; var startFirmRotation = firm.Body.rotation;
            for (int i = 0; i < 10; i++)
            {
                float angle = (i + 1) * 90 * Dt;
                foreach (var sample in samples)
                    if (pitch) sample.Actor.View.localRotation = Quaternion.Euler(angle, 0, 0);
                    else sample.Actor.transform.rotation = Quaternion.Euler(0, angle, 0);
                Step(samples);
            }
            float softPositionError = Vector3.Distance(soft.Body.position, soft.Goal), firmPositionError = Vector3.Distance(firm.Body.position, firm.Goal);
            float softAngleError = Quaternion.Angle(soft.Body.rotation, soft.GoalRotation), firmAngleError = Quaternion.Angle(firm.Body.rotation, firm.GoalRotation);
            float softTravel = Vector3.Distance(soft.Body.position, startSoftPosition), firmTravel = Vector3.Distance(firm.Body.position, startFirmPosition);
            float softTurn = Quaternion.Angle(startSoftRotation, soft.Body.rotation), firmTurn = Quaternion.Angle(startFirmRotation, firm.Body.rotation);
            TestContext.WriteLine($"pitch={pitch}; positionError soft/firm={softPositionError}/{firmPositionError}; angleError={softAngleError}/{firmAngleError}; travel={softTravel}/{firmTravel}; turn={softTurn}/{firmTurn}");
            Assert.Greater(softPositionError - firmPositionError, .08f, "The one follow control must have a visibly substantial positional effect.");
            Assert.Less(firmPositionError, softPositionError * .7f);
            Assert.Greater(firmTravel - softTravel, .08f);
            Assert.Greater(softAngleError - firmAngleError, 6, "The same control must also change the driven orientation response.");
            Assert.Less(firmAngleError, softAngleError * .7f); Assert.Greater(firmTurn - softTurn, 6);
            for (int i = 0; i < 200; i++)
            {
                Step(samples);
                foreach (var sample in samples)
                {
                    bool settled = Vector3.Distance(sample.Body.position, sample.Goal) < .02f
                        && Quaternion.Angle(sample.Body.rotation, sample.GoalRotation) < 1 && sample.Body.linearVelocity.magnitude < .05f;
                    sample.StableSteps = settled ? sample.StableSteps + 1 : 0;
                    if (sample.StableSteps >= 10 && sample.SettledStep < 0) sample.SettledStep = i + 1;
                }
            }
            Assert.Greater(soft.SettledStep, 0); Assert.Greater(firm.SettledStep, 0);
            Assert.Less(firm.SettledStep + 10, soft.SettledStep, "Firmer follow must also return promptly after look input stops.");
            Assert.AreEqual(0, soft.Grab.FollowSpeed); Assert.AreEqual(1, firm.Grab.FollowSpeed);
            foreach (var sample in samples)
            {
                Assert.AreEqual(Vector3.zero, sample.Grab.MovementLag); Assert.AreEqual(.65f, sample.Grab.HeldScaleMultiplier); Assert.AreEqual(3, sample.Body.mass);
            }
        }

        [TestCase(0f)] [TestCase(1f)]
        public void BothFollowExtremesKeepTheSameForceLimitWorldContactAndReleaseRestoration(float followSpeed)
        {
            var sample = Create(0, followSpeed); var item = sample.Grab.GetComponent<Collider>();
            var wall = Go("World follow wall", new Vector3(0, 1000, 2.2f)).AddComponent<BoxCollider>(); wall.size = new Vector3(6, 6, .2f);
            Physics.SyncTransforms(); sample.Grab.HoldDistance = 4.8f;
            float maximumCentreZ = sample.Body.position.z;
            var first = Tick(sample); Assert.Greater(first.magnitude, 79.9f, "Positive control: this displacement must exercise the fixed acceleration bound.");
            Physics.Simulate(Dt);
            for (int i = 0; i < 200; i++)
            {
                Step(sample); maximumCentreZ = Mathf.Max(maximumCentreZ, sample.Body.position.z);
            }
            Assert.Less(maximumCentreZ, wall.bounds.min.z, "A real world wall must block both soft and firm dynamic follow.");
            Assert.IsFalse(Physics.GetIgnoreCollision(item, wall)); Assert.AreEqual(followSpeed, sample.Grab.FollowSpeed);
            sample.Actor.Cancel(sample.Grab);
            Assert.IsFalse(sample.Grab.IsHeld); Assert.AreEqual(0, sample.Grab.OwnerCount);
            Assert.IsTrue(sample.Body.useGravity); Assert.AreEqual(RigidbodyInterpolation.None, sample.Body.interpolation);
            Assert.AreEqual(CollisionDetectionMode.Discrete, sample.Body.collisionDetectionMode); Assert.AreEqual(Vector3.one * 1.1f, sample.Grab.transform.localScale);
            Assert.AreEqual(3, sample.Body.mass); Assert.IsFalse(Physics.GetIgnoreCollision(item, wall));
        }
    }
}
