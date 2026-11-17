using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class GrabPoseTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private SimulationMode oldSimulation;
        private float oldStep;
        private static readonly Type cleanupType = typeof(GrabAction).Assembly.GetType("EasyInteraction.GrabCollisionExclusions");

        private GameObject Go(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            objects.Add(go);
            return go;
        }

        private InteractionActor Actor(string name, Vector3 position, Quaternion rotation)
        {
            var result = Go(name, position).AddComponent<InteractionActor>();
            result.AutomaticUpdate = false;
            result.Range = 20;
            var view = Go(name + " view", position + Vector3.up * 1.3f).transform;
            view.SetParent(result.transform);
            view.rotation = rotation;
            result.View = view;
            return result;
        }

        [SetUp]
        public void SetUp()
        {
            oldSimulation = Physics.simulationMode;
            oldStep = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = .02f;
            actor = Actor("Owner", new Vector3(0, 1000, 0), Quaternion.identity);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                for (int i = objects.Count - 1; i >= 0; i--)
                    if (objects[i]) Object.DestroyImmediate(objects[i]);
                objects.Clear();
                CleanupCall()?.Invoke();
            }
            finally
            {
                Physics.simulationMode = oldSimulation;
                Time.fixedDeltaTime = oldStep;
            }
        }

        private GrabAction Item(Quaternion rotation)
        {
            var go = Go("Item", actor.View.position + Vector3.forward * 1.5f);
            go.transform.rotation = rotation;
            go.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 2;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            var grab = go.AddComponent<GrabAction>();
            grab.GrabRange = 20;
            grab.BreakDistance = 20;
            Physics.SyncTransforms();
            return grab;
        }

        private GrabAction AuthoredItem(Quaternion rotation)
        {
            var grab = Item(rotation);
            grab.PoseMode = GrabPoseMode.AuthoredPose;
            grab.LocalPosition = new Vector3(.2f, -.15f, .05f);
            grab.LocalEulerAngles = new Vector3(15, 70, -20);
            return grab;
        }

        private static Action Call(object instance, string method) => (Action)Delegate.CreateDelegate(typeof(Action), instance,
            instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic));

        private static Action CleanupCall()
        {
            var found = Object.FindObjectsByType(cleanupType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            return found.Length == 0 ? null : Call(found[0], "FixedUpdate");
        }

        private void Steps(GrabAction grab, int count = 150)
        {
            var tick = Call(grab, "FixedUpdate");
            var cleanup = CleanupCall();
            for (int i = 0; i < count; i++)
            {
                cleanup?.Invoke();
                tick();
                Physics.Simulate(Time.fixedDeltaTime);
            }
        }

        private void Begin(GrabAction grab) => Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);

        private static Vector3 ViewTarget(GrabAction grab, InteractionActor owner, float distance) =>
            owner.View.position + owner.View.rotation * (grab.LocalPosition + Vector3.forward * distance);

        private static Quaternion AuthoredRotation(GrabAction grab, Transform frame) => frame.rotation * Quaternion.Euler(grab.LocalEulerAngles);

        private static void AtPose(GrabAction grab, Vector3 position, Quaternion rotation, bool grip = false)
        {
            Assert.IsTrue(grab.IsHeld);
            Assert.Less(Vector3.Distance(grip ? grab.GripPosition : grab.Body.position, position), .03f, "The physical held point must reach the authored position.");
            Assert.Less(Quaternion.Angle(grab.Body.rotation, rotation), 1, "The physical root must reach the authored orientation.");
        }

        [TestCase(0, 0, 0)]
        [TestCase(70, -140, 35)]
        public void FreshGrabDefaultsReachViewRootPoseRegardlessOfPickupRotation(float x, float y, float z)
        {
            actor.View.rotation = Quaternion.Euler(10, 35, 5);
            var grab = Item(Quaternion.Euler(x, y, z));
            Assert.AreEqual(GrabPoseMode.AuthoredPose, grab.PoseMode);
            Assert.AreEqual(0, (int)GrabPoseMode.PreservePickupPose);
            Assert.AreEqual(1, (int)GrabPoseMode.AuthoredPose);
            Assert.AreEqual(Vector3.zero, grab.LocalPosition);
            Assert.AreEqual(Vector3.zero, grab.LocalEulerAngles);
            Assert.AreEqual(2, grab.HoldDistance);
            Assert.AreEqual(.5f, grab.FollowSpeed);
            Assert.IsTrue(grab.IgnoreOwnerCollisions);
            Assert.IsFalse(grab.Body.isKinematic);
            grab.Body.centerOfMass = new Vector3(.14f, -.09f, .11f);
            Begin(grab);
            Steps(grab);
            AtPose(grab, actor.View.position + actor.View.forward * 2, actor.View.rotation);
        }

        [TestCase(0, 0, 0)]
        [TestCase(80, -135, 25)]
        [TestCase(-30, 179, 170)]
        public void AuthoredRootPoseIsIndependentOfPickupRotationAndCentreOfMass(float x, float y, float z)
        {
            actor.View.rotation = Quaternion.Euler(10, 35, 5);
            var grab = AuthoredItem(Quaternion.Euler(x, y, z));
            grab.Body.centerOfMass = new Vector3(.14f, -.09f, .11f);
            var originalPosition = grab.Body.position;
            Begin(grab);
            Assert.Less(Vector3.Distance(grab.Body.position, originalPosition), .001f, "Acquisition must not teleport the Rigidbody.");
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, grab.HoldDistance), AuthoredRotation(grab, actor.View));
        }

        [Test]
        public void ExplicitGripPinsItsPositionAndDoesNotReplaceAuthoredRootRotation()
        {
            var grab = AuthoredItem(Quaternion.Euler(80, -120, 50));
            var grip = Go("Grip", grab.transform.position).transform;
            grip.SetParent(grab.transform);
            grip.localPosition = new Vector3(.16f, -.12f, .09f);
            grip.localRotation = Quaternion.Euler(75, -25, 130);
            grab.GripPoint = grip;
            Physics.SyncTransforms();
            Begin(grab);
            Steps(grab);
            var expectedRotation = AuthoredRotation(grab, actor.View);
            var expectedPosition = ViewTarget(grab, actor, grab.HoldDistance);
            AtPose(grab, expectedPosition, expectedRotation, true);
            Assert.Less(Vector3.Distance(grab.Body.position, expectedPosition - expectedRotation * grip.localPosition), .03f);
            Assert.Greater(Quaternion.Angle(grip.rotation, expectedRotation), 30, "Grip rotation is not the authored item rotation.");
        }

        [TestCase(GrabRotation.Free)]
        [TestCase(GrabRotation.KeepInitial)]
        [TestCase(GrabRotation.FollowView)]
        public void AuthoredPoseUsesAnchorPriorityAndIgnoresFrameScaleAndLegacyRotationChoice(GrabRotation legacyMode)
        {
            var grab = AuthoredItem(Quaternion.Euler(-50, 130, 25));
            grab.RotationMode = legacyMode;
            actor.View.localScale = new Vector3(3, .5f, 2);
            actor.HandAnchor = Go("Hand", actor.View.position + new Vector3(.5f, 0, 1.7f)).transform;
            actor.HandAnchor.rotation = Quaternion.Euler(-10, -35, 15);
            actor.HandAnchor.localScale = new Vector3(.2f, 4, 2);
            var hold = Go("Hold", actor.View.position + new Vector3(-.4f, .1f, 1.8f)).transform;
            hold.rotation = Quaternion.Euler(20, 65, -10);
            hold.localScale = new Vector3(5, .1f, 3);
            grab.HoldPoint = hold;
            Begin(grab);
            grab.SetInspection(true);
            grab.AdjustDistance(1);
            Steps(grab);
            AtPose(grab, hold.position + hold.rotation * grab.LocalPosition, AuthoredRotation(grab, hold));
            grab.HoldPoint = null;
            Steps(grab);
            AtPose(grab, actor.HandAnchor.position + actor.HandAnchor.rotation * grab.LocalPosition, AuthoredRotation(grab, actor.HandAnchor));
            actor.HandAnchor = null;
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, .65f), AuthoredRotation(grab, actor.View));
            Assert.AreEqual(legacyMode, grab.RotationMode, "Authored pose must not rewrite the retained legacy choice.");
            Assert.AreEqual(new Vector3(5, .1f, 3), hold.localScale);
        }

        [Test]
        public void ManualRotationInspectionAndDistanceComposeWithAuthoredPose()
        {
            var grab = AuthoredItem(Quaternion.Euler(0, -150, 0));
            Begin(grab);
            Steps(grab);
            grab.Rotate(new Vector2(25, 10));
            var manual = Quaternion.Euler(-10, 25, 0);
            grab.SetInspection(true);
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, .65f), AuthoredRotation(grab, actor.View) * manual);
            grab.AdjustDistance(.7f);
            grab.SetInspection(false);
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, 2.7f), AuthoredRotation(grab, actor.View) * manual);
            Assert.AreEqual(new Vector3(.2f, -.15f, .05f), grab.LocalPosition);
            Assert.AreEqual(new Vector3(15, 70, -20), grab.LocalEulerAngles);
        }

        [Test]
        public void TransferPreservesSessionRotationAndInspectionWhileRegrabStartsFresh()
        {
            actor.View.rotation = Quaternion.Euler(0, 30, 0);
            var receiver = Actor("Receiver", actor.transform.position + Vector3.right * .5f, Quaternion.Euler(0, -40, 0));
            var grab = AuthoredItem(Quaternion.Euler(80, 170, -20));
            Begin(grab);
            grab.Rotate(new Vector2(-30, 15));
            var manual = Quaternion.Euler(-15, -30, 0);
            grab.SetInspection(true);
            grab.AdjustDistance(.3f);
            Assert.IsTrue(grab.TryTransferTo(receiver));
            Assert.AreSame(receiver, grab.Owner);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsTrue(grab.IsInspecting);
            Assert.IsFalse(grab.Body.useGravity);
            Steps(grab);
            AtPose(grab, ViewTarget(grab, receiver, .65f), AuthoredRotation(grab, receiver.View) * manual);
            receiver.Cancel();
            Assert.IsFalse(grab.IsHeld);
            Assert.IsFalse(grab.IsInspecting);
            Assert.IsTrue(grab.Body.useGravity);
            grab.Body.position = actor.View.position + Vector3.forward;
            grab.Body.rotation = Quaternion.Euler(-90, -110, 35);
            grab.Body.linearVelocity = Vector3.zero;
            grab.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            Begin(grab);
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, grab.HoldDistance), AuthoredRotation(grab, actor.View));
            Assert.AreEqual(new Vector3(.2f, -.15f, .05f), grab.LocalPosition);
            Assert.AreEqual(new Vector3(15, 70, -20), grab.LocalEulerAngles);
        }

        [Test]
        public void TwoHandsJoiningBeforeSettleUseAuthoredBaselineAndLeavingUsesRemainingFrame()
        {
            var second = Actor("Second hand", actor.transform.position + Vector3.right, Quaternion.identity);
            var grab = AuthoredItem(Quaternion.Euler(-80, -150, 50));
            grab.MaxParticipants = 2;
            Begin(grab);
            grab.Rotate(new Vector2(20, -10));
            var manual = Quaternion.Euler(10, 20, 0);
            Assert.IsTrue(second.TryExecuteDirect(grab), second.LastFailure.Reason);
            Steps(grab);
            var firstPoint = ViewTarget(grab, actor, grab.HoldDistance);
            var secondPoint = ViewTarget(grab, second, grab.HoldDistance);
            AtPose(grab, (firstPoint + secondPoint) * .5f, AuthoredRotation(grab, actor.View) * manual);
            second.View.position += Vector3.forward * .7f;
            Steps(grab);
            var newSecondPoint = ViewTarget(grab, second, grab.HoldDistance);
            var expected = Quaternion.FromToRotation(secondPoint - firstPoint, newSecondPoint - firstPoint) * AuthoredRotation(grab, actor.View) * manual;
            AtPose(grab, (firstPoint + newSecondPoint) * .5f, expected);
            actor.Cancel();
            second.View.rotation = Quaternion.Euler(10, 50, 0);
            Steps(grab);
            Assert.AreSame(second, grab.Owner);
            AtPose(grab, ViewTarget(grab, second, grab.HoldDistance), AuthoredRotation(grab, second.View) * manual);
            Assert.IsFalse(grab.Body.useGravity);
            second.Cancel();
            Assert.IsTrue(grab.Body.useGravity);
        }

        [Test]
        public void AuthoredPoseKeepsWorldCollisionInertiaAndThrowRestoresBodySettings()
        {
            var grab = AuthoredItem(Quaternion.identity);
            grab.LocalPosition = Vector3.zero;
            grab.LocalEulerAngles = Vector3.zero;
            grab.Body.position = actor.View.position + Vector3.forward * .5f;
            var wall = Go("World wall", actor.View.position + Vector3.forward * 1.35f).AddComponent<BoxCollider>();
            wall.size = new Vector3(3, 3, .2f);
            Physics.SyncTransforms();
            Begin(grab);
            Steps(grab, 200);
            Assert.IsTrue(grab.IsHeld);
            Assert.IsFalse(grab.Body.isKinematic);
            Assert.IsTrue(grab.Body.detectCollisions);
            Assert.IsFalse(Physics.GetIgnoreCollision(grab.GetComponent<Collider>(), wall));
            Assert.Less(grab.Body.position.z - actor.View.position.z, 1.2f, "The wall must block a target on its other side.");
            wall.enabled = false;
            actor.View.rotation = Quaternion.Euler(0, 80, 0);
            var before = grab.Body.position;
            Steps(grab, 1);
            Assert.Less(Vector3.Distance(before, grab.Body.position), .1f, "Inertia and bounded forces must not teleport the item.");
            Steps(grab);
            AtPose(grab, ViewTarget(grab, actor, grab.HoldDistance), AuthoredRotation(grab, actor.View));
            var expectedVelocity = Vector3.ClampMagnitude(grab.Body.linearVelocity + actor.View.forward * grab.ThrowSpeed, 30);
            Assert.IsTrue(grab.Throw(actor));
            Assert.IsFalse(grab.IsHeld);
            Assert.IsNull(actor.ActiveAction);
            Assert.IsTrue(grab.Body.useGravity);
            Assert.AreEqual(RigidbodyInterpolation.None, grab.Body.interpolation);
            Assert.AreEqual(CollisionDetectionMode.Discrete, grab.Body.collisionDetectionMode);
            Assert.Less(Vector3.Distance(expectedVelocity, grab.Body.linearVelocity), .001f);
            Assert.AreEqual(Vector3.zero, grab.LocalPosition);
            Assert.AreEqual(Vector3.zero, grab.LocalEulerAngles);
            Assert.AreEqual(GrabPoseMode.AuthoredPose, grab.PoseMode);
        }

        [Test]
        public void ExplicitSerializedPreserveModeAndIndependentAuthoredSettingsSurvivePooling()
        {
            var first = Item(Quaternion.identity);
            JsonUtility.FromJsonOverwrite("{\"poseMode\":0,\"holdDistance\":1.6,\"holdOffset\":{\"x\":0.3,\"y\":-0.2,\"z\":0.1},\"rotationMode\":1}", first);
            Assert.AreEqual(0, (int)first.PoseMode);
            Assert.AreEqual(GrabPoseMode.PreservePickupPose, first.PoseMode);
            Assert.AreEqual(GrabRotation.KeepInitial, first.RotationMode);
            Assert.AreEqual(1.6f, first.HoldDistance);
            Assert.AreEqual(new Vector3(.3f, -.2f, .1f), first.HoldOffset);
            first.PoseMode = GrabPoseMode.AuthoredPose;
            first.LocalPosition = new Vector3(-.25f, .1f, .2f);
            first.LocalEulerAngles = new Vector3(0, -40, 10);
            Begin(first);
            Steps(first);
            AtPose(first, ViewTarget(first, actor, 1.6f), AuthoredRotation(first, actor.View));
            first.gameObject.SetActive(false);
            Assert.IsFalse(first.IsHeld);
            Assert.IsNull(actor.ActiveAction);
            var second = AuthoredItem(Quaternion.Euler(80, 150, 20));
            Begin(second);
            Steps(second);
            AtPose(second, ViewTarget(second, actor, second.HoldDistance), AuthoredRotation(second, actor.View));
            actor.Cancel();
            second.gameObject.SetActive(false);
            first.gameObject.SetActive(true);
            Begin(first);
            Steps(first);
            AtPose(first, ViewTarget(first, actor, 1.6f), AuthoredRotation(first, actor.View));
            Assert.AreEqual(new Vector3(-.25f, .1f, .2f), first.LocalPosition);
            Assert.AreEqual(new Vector3(0, -40, 10), first.LocalEulerAngles);
            Assert.AreEqual(new Vector3(.3f, -.2f, .1f), first.HoldOffset);
            Assert.AreEqual(GrabRotation.KeepInitial, first.RotationMode);
        }

        [Test]
        public void ExplicitPreservePickupPoseIgnoresAuthoredFieldsAndPinsCentreOfMass()
        {
            var initialView = Quaternion.Euler(0, 20, 0);
            actor.View.rotation = initialView;
            var initialItem = Quaternion.Euler(35, -90, 20);
            var grab = Item(initialItem);
            grab.PoseMode = GrabPoseMode.PreservePickupPose;
            Assert.AreEqual(GrabPoseMode.PreservePickupPose, grab.PoseMode);
            grab.LocalPosition = Vector3.one * 10;
            grab.LocalEulerAngles = new Vector3(0, 150, 0);
            grab.HoldOffset = new Vector3(.1f, -.1f, .05f);
            grab.Body.centerOfMass = new Vector3(.12f, -.1f, .08f);
            Begin(grab);
            Steps(grab);
            var expectedPoint = actor.View.position + actor.View.rotation * (grab.HoldOffset + Vector3.forward * grab.HoldDistance);
            Assert.Less(Vector3.Distance(grab.Body.worldCenterOfMass, expectedPoint), .03f);
            Assert.Less(Quaternion.Angle(grab.Body.rotation, initialItem), 1);
            actor.View.rotation = Quaternion.Euler(0, 55, 0);
            Steps(grab);
            expectedPoint = actor.View.position + actor.View.rotation * (grab.HoldOffset + Vector3.forward * grab.HoldDistance);
            Assert.Less(Vector3.Distance(grab.Body.worldCenterOfMass, expectedPoint), .03f);
            Assert.Less(Quaternion.Angle(grab.Body.rotation, actor.View.rotation * Quaternion.Inverse(initialView) * initialItem), 1);
        }
    }
}
