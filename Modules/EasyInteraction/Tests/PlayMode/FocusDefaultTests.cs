using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class FocusDefaultTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private readonly Vector3 origin = new Vector3(100, 1500, 0);

        private GameObject New(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            objects.Add(go);
            return go;
        }

        private InteractionActor NewActor(string name)
        {
            var result = New(name, origin).AddComponent<InteractionActor>();
            result.AutomaticUpdate = false;
            result.View = result.transform;
            return result;
        }

        private Vector3 Position(float yaw, float distance) => origin + Quaternion.Euler(0, yaw, 0) * Vector3.forward * distance;

        private EventAction TargetAt(float yaw, float distance)
        {
            var target = New("Target", Position(yaw, distance));
            target.AddComponent<BoxCollider>().size = Vector3.one * .15f;
            return target.AddComponent<EventAction>();
        }

        [SetUp]
        public void SetUp() => actor = NewActor("Fresh actor");

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void FreshActorUsesFocusAndSelectsAnOffAxisTargetWithoutAThinRayHit()
        {
            Assert.AreEqual(InteractionSelectionMode.Focus, actor.SelectionMode);
            Assert.AreEqual(60, actor.FocusAngle);
            Assert.AreEqual(3, actor.Range);
            var action = TargetAt(20, 2.4f);
            Physics.SyncTransforms();
            Assert.IsFalse(Physics.Raycast(actor.View.position, actor.View.forward, actor.Range), "The default must assist a target that the thin ray misses.");
            Assert.AreSame(action.Target, actor.RefreshTarget());
            int executed = 0;
            action.OnExecuteEvent.AddListener(() => executed++);
            Assert.IsTrue(actor.TryExecute(action));
            Assert.AreEqual(1, executed);
        }

        [Test]
        public void DefaultFocusStillRejectsOutsideConeRangeBehindAndOccludedTargets()
        {
            var action = TargetAt(40, 2.4f);
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget(), "Outside the default 60-degree cone.");
            action.transform.position = Position(180, 2);
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget(), "Behind the actor.");
            action.transform.position = Position(20, 3.3f);
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget(), "Outside the default three-metre range.");
            action.transform.position = Position(20, 2.4f);
            var wall = New("Occluding wall", Position(20, 1.2f)).AddComponent<BoxCollider>();
            wall.size = new Vector3(.5f, 1, .3f);
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget(), "Focus must retain ordinary line-of-sight checks.");
            wall.enabled = false;
            Physics.SyncTransforms();
            Assert.AreSame(action.Target, actor.RefreshTarget());
        }

        [Test]
        public void ExplicitlySerializedRaycastSurvivesRoundTripAndRetainsThinRaySelection()
        {
            Assert.AreEqual(0, (int)InteractionSelectionMode.Raycast);
            Assert.AreEqual(1, (int)InteractionSelectionMode.Focus);
            Assert.AreEqual(InteractionSelectionMode.Focus, actor.SelectionMode);
            JsonUtility.FromJsonOverwrite("{\"selectionMode\":0}", actor);
            Assert.AreEqual(InteractionSelectionMode.Raycast, actor.SelectionMode);
            var saved = JsonUtility.ToJson(actor);
            var restored = NewActor("Restored actor");
            Assert.AreEqual(InteractionSelectionMode.Focus, restored.SelectionMode);
            JsonUtility.FromJsonOverwrite(saved, restored);
            restored.View = restored.transform;
            Assert.AreEqual(InteractionSelectionMode.Raycast, restored.SelectionMode);
            TargetAt(20, 2.4f);
            Physics.SyncTransforms();
            Assert.IsNull(actor.RefreshTarget());
            Assert.IsNull(restored.RefreshTarget(), "A saved Raycast actor must not acquire off-axis Focus targets.");
            var centre = TargetAt(0, 2.4f);
            Physics.SyncTransforms();
            Assert.AreSame(centre.Target, actor.RefreshTarget());
            Assert.AreSame(centre.Target, restored.RefreshTarget());
        }
    }
}
