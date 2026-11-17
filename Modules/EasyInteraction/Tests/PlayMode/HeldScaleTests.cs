using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class HeldScaleTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private CapsuleCollider ownerCollider;
        private SimulationMode oldSimulation;
        private float oldStep;
        private readonly Vector3 origin = new Vector3(0, 1000, 0);
        private static readonly Type cleanupType = typeof(GrabAction).Assembly.GetType("EasyInteraction.GrabCollisionExclusions");

        private sealed class BodyState
        {
            public Vector3 Scale, Centre, Tensor;
            public Quaternion TensorRotation;
            public bool AutomaticCentre, AutomaticTensor, Gravity;
            public float Mass;
            public RigidbodyInterpolation Interpolation;
            public CollisionDetectionMode CollisionMode;
        }

        private GameObject Go(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            objects.Add(go);
            return go;
        }

        private InteractionActor Actor(string name, Vector3 position)
        {
            var root = Go(name, position);
            var result = root.AddComponent<InteractionActor>();
            result.AutomaticUpdate = false;
            result.Range = 20;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.8f;
            capsule.radius = .4f;
            capsule.center = Vector3.up * .9f;
            root.AddComponent<Rigidbody>().isKinematic = true;
            var view = Go(name + " view", position + Vector3.up * 1.3f).transform;
            view.SetParent(root.transform);
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
            actor = Actor("Owner", origin);
            ownerCollider = actor.GetComponent<CapsuleCollider>();
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

        private GrabAction Item(Vector3 scale, Transform parent = null)
        {
            var root = Go("Held item", actor.View.position + Vector3.forward * 2);
            if (parent) root.transform.SetParent(parent, true);
            root.transform.localScale = scale;
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(.4f, .5f, .3f);
            var body = root.AddComponent<Rigidbody>();
            body.mass = 3;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Extrapolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var grab = root.AddComponent<GrabAction>();
            grab.GrabRange = 20;
            grab.BreakDistance = 20;
            Physics.SyncTransforms();
            return grab;
        }

        private static void ManualMassProperties(Rigidbody body)
        {
            body.centerOfMass = new Vector3(.12f, -.08f, .17f);
            body.inertiaTensor = new Vector3(2, 3, 4);
            body.inertiaTensorRotation = Quaternion.Euler(15, -25, 35);
            Assert.IsFalse(body.automaticCenterOfMass);
            Assert.IsFalse(body.automaticInertiaTensor);
        }

        private static BodyState Capture(GrabAction grab)
        {
            var body = grab.Body;
            return new BodyState
            {
                Scale = grab.transform.localScale,
                Centre = body.centerOfMass,
                Tensor = body.inertiaTensor,
                TensorRotation = body.inertiaTensorRotation,
                AutomaticCentre = body.automaticCenterOfMass,
                AutomaticTensor = body.automaticInertiaTensor,
                Gravity = body.useGravity,
                Mass = body.mass,
                Interpolation = body.interpolation,
                CollisionMode = body.collisionDetectionMode
            };
        }

        private static void Near(Vector3 actual, Vector3 expected, string message) => Assert.Less(Vector3.Distance(actual, expected), .0005f, message);

        private static void AssertMassProperties(Rigidbody body, BodyState original, float multiplier)
        {
            Assert.AreEqual(original.Mass, body.mass, "Held scaling must not change mass.");
            Assert.AreEqual(original.AutomaticCentre, body.automaticCenterOfMass);
            Assert.AreEqual(original.AutomaticTensor, body.automaticInertiaTensor);
            if (!original.AutomaticCentre) Near(body.centerOfMass, original.Centre * multiplier, "Custom COM must use the captured, uniformly scaled value.");
            if (!original.AutomaticTensor)
            {
                Near(body.inertiaTensor, original.Tensor * (multiplier * multiplier), "Custom inertia must scale quadratically from its captured value.");
                Assert.Less(Quaternion.Angle(body.inertiaTensorRotation, original.TensorRotation), .05f);
            }
        }

        private static void AssertRestored(Rigidbody body, Transform root, BodyState original)
        {
            Near(root.localScale, original.Scale, "Final release must restore the exact captured local scale.");
            AssertMassProperties(body, original, 1);
            Assert.AreEqual(original.Gravity, body.useGravity);
            Assert.AreEqual(original.Interpolation, body.interpolation);
            Assert.AreEqual(original.CollisionMode, body.collisionDetectionMode);
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

        [Test]
        public void DefaultMultiplierLeavesOriginalScaleAndCustomMassPropertiesUnchanged()
        {
            var grab = Item(new Vector3(1.2f, .8f, 1.6f));
            ManualMassProperties(grab.Body);
            var original = Capture(grab);
            Assert.AreEqual(1, grab.HeldScaleMultiplier);
            Near(grab.UnheldLocalScale, original.Scale, "Unheld API returns the authored local scale.");
            Begin(grab);
            Near(grab.transform.localScale, original.Scale, "Multiplier one must preserve original geometry.");
            Near(grab.UnheldLocalScale, original.Scale, "The captured scale remains available during the hold.");
            AssertMassProperties(grab.Body, original, 1);
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
        }

        [Test]
        public void NonuniformParentColliderAndCustomMassPropertiesScaleFromOneCapturedSnapshot()
        {
            var parent = Go("Nonuniform parent", origin).transform;
            parent.localScale = new Vector3(2, .75f, 1.5f);
            var grab = Item(new Vector3(1.25f, .8f, 1.2f), parent);
            ManualMassProperties(grab.Body);
            var original = Capture(grab);
            var collider = grab.GetComponent<Collider>();
            var originalBounds = collider.bounds.size;
            grab.HeldScaleMultiplier = .4f;
            Begin(grab);
            Near(grab.transform.localScale, original.Scale * .4f, "Scale applies to the root local snapshot.");
            Near(collider.bounds.size, originalBounds * .4f, "Collider dimensions must shrink with the visual hierarchy.");
            AssertMassProperties(grab.Body, original, .4f);
            foreach (float multiplier in new[] { .75f, .3f, 1f, .5f })
            {
                grab.HeldScaleMultiplier = multiplier;
                Near(grab.transform.localScale, original.Scale * multiplier, "Runtime changes must not multiply the previously held scale.");
                Near(collider.bounds.size, originalBounds * multiplier, "Runtime collider scale follows the same captured root.");
                Near(grab.UnheldLocalScale, original.Scale, "The original snapshot must not be overwritten.");
                AssertMassProperties(grab.Body, original, multiplier);
            }
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
            Near(collider.bounds.size, originalBounds, "Restored colliders regain original dimensions.");
            Assert.AreEqual(new Vector3(2, .75f, 1.5f), parent.localScale);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void MassPropertyAutomationFlagsRemainIndependentAcrossScaleAndRestore(bool automaticCentre, bool automaticTensor)
        {
            var grab = Item(new Vector3(1.2f, .8f, 1.6f));
            var collider = grab.GetComponent<BoxCollider>();
            collider.center = new Vector3(.12f, .06f, -.09f);
            Physics.SyncTransforms();
            grab.Body.ResetCenterOfMass();
            grab.Body.ResetInertiaTensor();
            if (!automaticCentre) grab.Body.centerOfMass = new Vector3(.12f, -.08f, .17f);
            if (!automaticTensor)
            {
                grab.Body.inertiaTensor = new Vector3(2, 3, 4);
                grab.Body.inertiaTensorRotation = Quaternion.Euler(15, -25, 35);
            }
            var original = Capture(grab);
            var bounds = collider.bounds.size;
            Assert.AreEqual(automaticCentre, original.AutomaticCentre);
            Assert.AreEqual(automaticTensor, original.AutomaticTensor);
            grab.HeldScaleMultiplier = .5f;
            Begin(grab);
            AssertMassProperties(grab.Body, original, .5f);
            Near(collider.bounds.size, bounds * .5f, "Automatic properties must be recalculated for the smaller shape.");
            Assert.Greater(grab.Body.inertiaTensor.x, 0);
            Assert.Greater(grab.Body.inertiaTensor.y, 0);
            Assert.Greater(grab.Body.inertiaTensor.z, 0);
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
            Near(collider.bounds.size, bounds, "Original collider dimensions return.");
            Near(grab.Body.centerOfMass, original.Centre, "COM returns to its original value and automation mode.");
            Near(grab.Body.inertiaTensor, original.Tensor, "Inertia returns to its original value and automation mode.");
        }

        [Test]
        public void ScaledGripConvergesPhysicallyWithoutChangingAuthoredPoseOrGripConfiguration()
        {
            var grab = Item(new Vector3(1.4f, .8f, 1.1f));
            var original = Capture(grab);
            var grip = Go("Grip", grab.transform.position).transform;
            grip.SetParent(grab.transform);
            grip.localPosition = new Vector3(.2f, -.1f, .15f);
            grip.localRotation = Quaternion.Euler(35, 80, -10);
            grab.GripPoint = grip;
            grab.PoseMode = GrabPoseMode.AuthoredPose;
            grab.LocalPosition = new Vector3(.2f, -.1f, .1f);
            grab.LocalEulerAngles = new Vector3(10, 55, -20);
            grab.HeldScaleMultiplier = .5f;
            var start = grab.Body.position;
            Begin(grab);
            Near(grab.Body.position, start, "Acquisition scales geometry without teleporting the body.");
            Steps(grab);
            var rotation = actor.View.rotation * Quaternion.Euler(grab.LocalEulerAngles);
            var destination = actor.View.position + actor.View.rotation * (grab.LocalPosition + Vector3.forward * grab.HoldDistance);
            Assert.IsTrue(grab.IsHeld);
            Assert.Less(Vector3.Distance(grab.GripPosition, destination), .03f);
            Assert.Less(Quaternion.Angle(grab.Body.rotation, rotation), 1);
            var expectedRoot = destination - rotation * Vector3.Scale(grip.localPosition, original.Scale * .5f);
            Assert.Less(Vector3.Distance(grab.Body.position, expectedRoot), .03f, "The scaled grip offset determines the physical root position.");
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
            Assert.AreEqual(new Vector3(.2f, -.1f, .15f), grip.localPosition);
            Assert.Less(Quaternion.Angle(grip.localRotation, Quaternion.Euler(35, 80, -10)), .05f);
            Assert.AreEqual(new Vector3(.2f, -.1f, .1f), grab.LocalPosition);
            Assert.AreEqual(new Vector3(10, 55, -20), grab.LocalEulerAngles);
        }

        [Test]
        public void AdditionalOwnersAndTransferDoNotMultiplyScaleOrRestoreItBeforeFinalRelease()
        {
            var second = Actor("Receiver", origin + Vector3.right * .5f);
            var grab = Item(new Vector3(1.2f, .9f, 1.1f));
            ManualMassProperties(grab.Body);
            var original = Capture(grab);
            grab.HeldScaleMultiplier = .5f;
            grab.MaxParticipants = 2;
            Begin(grab);
            Assert.IsTrue(second.TryExecuteDirect(grab), second.LastFailure.Reason);
            Near(grab.transform.localScale, original.Scale * .5f, "An additional participant shares the scaled session.");
            actor.Cancel();
            Assert.AreSame(second, grab.Owner);
            Near(grab.transform.localScale, original.Scale * .5f, "One remaining owner retains held geometry.");
            Assert.IsTrue(grab.TryTransferTo(actor));
            Assert.AreSame(actor, grab.Owner);
            Near(grab.transform.localScale, original.Scale * .5f, "Transfer must retain one captured scale.");
            AssertMassProperties(grab.Body, original, .5f);
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
            Begin(grab);
            Near(grab.transform.localScale, original.Scale * .5f, "Regrab must capture the restored scale, not accumulate shrinkage.");
            actor.Cancel();
            AssertRestored(grab.Body, grab.transform, original);
        }

        [TestCase("drop")]
        [TestCase("throw")]
        [TestCase("actorDisable")]
        [TestCase("componentDisable")]
        [TestCase("componentDestroy")]
        [TestCase("pool")]
        [TestCase("poolDestroy")]
        public void EveryFinalExitRestoresScaleAndCustomBodyConfiguration(string exit)
        {
            var grab = Item(new Vector3(1.2f, .8f, 1.6f));
            var body = grab.Body;
            var root = grab.transform;
            ManualMassProperties(body);
            var original = Capture(grab);
            grab.HeldScaleMultiplier = .4f;
            Begin(grab);
            Near(root.localScale, original.Scale * .4f, "Fixture must begin scaled.");
            switch (exit)
            {
                case "drop": actor.Cancel(); break;
                case "throw": Assert.IsTrue(grab.Throw(actor)); break;
                case "actorDisable": actor.enabled = false; break;
                case "componentDisable": grab.enabled = false; break;
                case "componentDestroy": Object.DestroyImmediate(grab); break;
                case "pool": root.gameObject.SetActive(false); break;
                case "poolDestroy": root.gameObject.SetActive(false); break;
            }
            Assert.IsNull(actor.ActiveAction);
            if (exit == "pool" || exit == "poolDestroy")
            {
                Near(root.localScale, original.Scale, "Pooling restores the actual Transform even while inactive.");
                Assert.IsFalse(grab.IsHeld);
                // Native mass getters on an inactive Rigidbody need not expose its stored values.
                // Compare an unheld control, then assert the full active state BEFORE a new grab.
                var control = Go("Unheld native pooling control", origin + Vector3.right * 10);
                control.transform.localScale = original.Scale;
                control.AddComponent<BoxCollider>();
                var controlBody = control.AddComponent<Rigidbody>();
                controlBody.mass = original.Mass;
                controlBody.useGravity = original.Gravity;
                controlBody.interpolation = original.Interpolation;
                controlBody.collisionDetectionMode = original.CollisionMode;
                controlBody.centerOfMass = original.Centre;
                controlBody.inertiaTensor = original.Tensor;
                controlBody.inertiaTensorRotation = original.TensorRotation;
                control.SetActive(false);
                Near(body.centerOfMass, controlBody.centerOfMass, "Inactive mass getter must agree with a plain unheld Rigidbody control.");
                if (exit == "poolDestroy") Object.DestroyImmediate(grab);
                root.gameObject.SetActive(true);
                control.SetActive(true);
                Physics.SyncTransforms();
                AssertMassProperties(controlBody, original, 1);
                // Also proves the result survives deleting GrabAction while inactive, without OnEnable repair.
                AssertRestored(body, root, original);
                if (grab)
                {
                    Begin(grab);
                    Near(root.localScale, original.Scale * .4f, "A pooled regrab must not shrink twice.");
                    AssertMassProperties(body, original, .4f);
                    actor.Cancel();
                    AssertRestored(body, root, original);
                }
            }
            else AssertRestored(body, root, original);
        }

        [Test]
        public void ExpandingOnReleaseRetainsOwnerCollisionExclusionUntilActualSeparation()
        {
            var grab = Item(Vector3.one);
            var item = grab.GetComponent<BoxCollider>();
            item.size = Vector3.one * .8f;
            grab.Body.position = actor.View.position + Vector3.forward * .65f;
            grab.HeldScaleMultiplier = .25f;
            Physics.SyncTransforms();
            Begin(grab);
            Assert.IsTrue(Physics.GetIgnoreCollision(item, ownerCollider));
            Assert.IsFalse(Physics.ComputePenetration(item, grab.Body.position, grab.Body.rotation,
                ownerCollider, ownerCollider.transform.position, ownerCollider.transform.rotation, out _, out _), "The small held item is separated from the owner.");
            actor.Cancel();
            Near(grab.transform.localScale, Vector3.one, "The released object expands to its original dimensions.");
            Assert.IsTrue(Physics.ComputePenetration(item, grab.Body.position, grab.Body.rotation,
                ownerCollider, ownerCollider.transform.position, ownerCollider.transform.rotation, out _, out _), "Positive control: restoring the larger shape creates a real owner overlap.");
            CleanupCall()?.Invoke();
            Assert.IsTrue(Physics.GetIgnoreCollision(item, ownerCollider), "Restoring shape must happen before deciding whether exclusion can end.");
            grab.Body.position = actor.View.position + Vector3.forward * 2;
            Physics.SyncTransforms();
            CleanupCall()?.Invoke();
            Assert.IsFalse(Physics.GetIgnoreCollision(item, ownerCollider), "The owned exclusion returns to its original value after actual separation.");
        }
    }
}
