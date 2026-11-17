using System;
using System.Collections;
using System.Collections.Generic;
using EasyInteraction.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests.Editor
{
    public sealed class GrabPoseAuthoringTests
    {
        private readonly List<GameObject> roots = new List<GameObject>();

        private GameObject Root(string name)
        {
            var root = new GameObject(name);
            roots.Add(root);
            return root;
        }

        private static void ClearOwnedUndo(GameObject root)
        {
            if (!root) return;
            Undo.FlushUndoRecordObjects();
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component) Undo.ClearUndo(component);
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) Undo.ClearUndo(child.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            GrabPoseAuthoring.End();
            foreach (var root in roots) ClearOwnedUndo(root);
            for (int i = roots.Count - 1; i >= 0; i--)
                if (roots[i]) Object.DestroyImmediate(roots[i]);
            roots.Clear();
            Undo.IncrementCurrentGroup();
        }

        private GrabAction ItemWithScaledParent(bool grip)
        {
            var parent = Root("Scaled item parent").transform;
            parent.position = new Vector3(4, 2, -3);
            parent.rotation = Quaternion.Euler(15, 35, -10);
            parent.localScale = new Vector3(2, .7f, 1.3f);
            var root = new GameObject("Authored item");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(.2f, -.3f, .4f);
            root.transform.localRotation = Quaternion.Euler(10, -30, 25);
            root.transform.localScale = new Vector3(.8f, 1.2f, .6f);
            var grab = root.AddComponent<GrabAction>();
            grab.PoseMode = GrabPoseMode.AuthoredPose;
            grab.HeldScaleMultiplier = .4f;
            grab.LocalPosition = new Vector3(.18f, -.12f, -.3f);
            grab.LocalEulerAngles = new Vector3(10, 75, -15);
            if (grip)
            {
                var intermediary = new GameObject("Scaled grip parent").transform;
                intermediary.SetParent(root.transform, false);
                intermediary.localPosition = new Vector3(.05f, -.03f, .07f);
                intermediary.localRotation = Quaternion.Euler(20, 10, 5);
                intermediary.localScale = new Vector3(.7f, 1.5f, 1.1f);
                var point = new GameObject("Grip").transform;
                point.SetParent(intermediary, false);
                point.localPosition = new Vector3(.2f, -.1f, .15f);
                point.localRotation = Quaternion.Euler(45, -50, 70);
                grab.GripPoint = point;
            }
            return grab;
        }

        [TestCase(false, true), TestCase(true, true), TestCase(true, false)]
        public void PoseRoundTripUsesRootGripParentAndHeldScaleWithoutMovingAnyTransform(bool grip, bool view)
        {
            var grab = ItemWithScaledParent(grip);
            var frame = new Pose(new Vector3(-2, 3, 1), Quaternion.Euler(-5, 50, 15));
            var original = new Pose(grab.transform.position, grab.transform.rotation);
            var originalScale = grab.transform.localScale;
            var rootPose = GrabPoseUtility.GetRootPose(grab, frame, view);
            var expectedPoint = frame.position + frame.rotation * (grab.LocalPosition + (view ? Vector3.forward * grab.HoldDistance : Vector3.zero));
            var localGrip = grip ? grab.transform.InverseTransformPoint(grab.GripPoint.position) : Vector3.zero;
            var actualPoint = GrabPoseUtility.GetRootMatrix(grab, rootPose).MultiplyPoint3x4(localGrip);
            Assert.Less(Vector3.Distance(expectedPoint, actualPoint), .0001f);
            Assert.Less(Quaternion.Angle(frame.rotation * Quaternion.Euler(grab.LocalEulerAngles), rootPose.rotation), .01f);
            var local = GrabPoseUtility.GetLocalPose(grab, rootPose, frame, view);
            Assert.Less(Vector3.Distance(grab.LocalPosition, local.position), .0001f);
            Assert.Less(Quaternion.Angle(Quaternion.Euler(grab.LocalEulerAngles), local.rotation), .01f);
            Assert.AreEqual(original.position, grab.transform.position);
            Assert.AreEqual(original.rotation, grab.transform.rotation);
            Assert.AreEqual(originalScale, grab.transform.localScale);
        }

        [Test]
        public void SessionUsesHoldPointThenActorHandThenViewIgnoringFrameScale()
        {
            var grab = ItemWithScaledParent(false);
            var actor = Root("Preview Actor").AddComponent<InteractionActor>();
            actor.View = Root("Authored View").transform;
            actor.View.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(10, 20, 0));
            actor.View.localScale = new Vector3(4, .5f, 3);
            actor.HandAnchor = Root("Hand").transform;
            actor.HandAnchor.SetPositionAndRotation(new Vector3(2, 1, -1), Quaternion.Euler(5, 60, 10));
            grab.HoldPoint = Root("Hold point").transform;
            grab.HoldPoint.SetPositionAndRotation(new Vector3(5, 3, 1), Quaternion.Euler(25, -35, 15));
            grab.HoldPoint.localScale = new Vector3(.3f, 5, 2);
            GrabPoseAuthoring.Begin(grab, actor);
            AssertPreview(grab, grab.HoldPoint, false);
            grab.HoldPoint = null;
            AssertPreview(grab, actor.HandAnchor, false);
            actor.HandAnchor = null;
            AssertPreview(grab, actor.View, true);
            GrabPoseAuthoring.SetPreviewFrame(actor.View, false);
            AssertPreview(grab, actor.View, false);
            var snapshot = new Pose(new Vector3(-3, 1, 2), Quaternion.Euler(0, -50, 0));
            GrabPoseAuthoring.SetSceneViewFrame(snapshot);
            Assert.IsTrue(GrabPoseAuthoring.TryGetPreviewPose(out var fallback));
            var expected = GrabPoseUtility.GetRootPose(grab, snapshot, true);
            Assert.Less(Vector3.Distance(expected.position, fallback.position), .0001f);
            Assert.Less(Quaternion.Angle(expected.rotation, fallback.rotation), .01f);
            Assert.IsNull(new SerializedObject(grab).FindProperty("previewActor"));
        }

        private static void AssertPreview(GrabAction grab, Transform frame, bool view)
        {
            Assert.IsTrue(GrabPoseAuthoring.TryGetPreviewPose(out var actual));
            var expected = GrabPoseUtility.GetRootPose(grab, new Pose(frame.position, frame.rotation), view);
            Assert.Less(Vector3.Distance(expected.position, actual.position), .0001f);
            Assert.Less(Quaternion.Angle(expected.rotation, actual.rotation), .01f);
        }

        [Test]
        public void ApplyingHandlePoseChangesOnlyAuthoredDataAndSupportsUndoRedo()
        {
            var grab = ItemWithScaledParent(true);
            var originalRoot = new Pose(grab.transform.position, grab.transform.rotation);
            var originalScale = grab.transform.localScale;
            var originalLocal = grab.LocalPosition;
            var originalEuler = grab.LocalEulerAngles;
            bool originalHidden = Tools.hidden;
            GrabPoseAuthoring.Begin(grab);
            GrabPoseAuthoring.SetSceneViewFrame(new Pose(new Vector3(1, 2, 3), Quaternion.Euler(0, 25, 0)));
            var desired = new Pose(new Vector3(2, 1.5f, 4), Quaternion.Euler(20, -60, 10));
            Undo.IncrementCurrentGroup();
            Assert.IsTrue(GrabPoseAuthoring.ApplyRootPose(desired));
            Undo.FlushUndoRecordObjects();
            Assert.IsTrue(GrabPoseAuthoring.TryGetPreviewPose(out var actual));
            Assert.Less(Vector3.Distance(desired.position, actual.position), .0001f);
            Assert.Less(Quaternion.Angle(desired.rotation, actual.rotation), .01f);
            Undo.PerformUndo();
            Assert.AreEqual(originalLocal, grab.LocalPosition);
            Assert.AreEqual(originalEuler, grab.LocalEulerAngles);
            Undo.PerformRedo();
            Assert.IsTrue(GrabPoseAuthoring.TryGetPreviewPose(out actual));
            Assert.Less(Vector3.Distance(desired.position, actual.position), .0001f);
            Assert.Less(Quaternion.Angle(desired.rotation, actual.rotation), .01f);
            Assert.AreEqual(originalRoot.position, grab.transform.position);
            Assert.AreEqual(originalRoot.rotation, grab.transform.rotation);
            Assert.AreEqual(originalScale, grab.transform.localScale);
            Assert.IsNull(grab.HoldPoint);
            GrabPoseAuthoring.End();
            Assert.AreEqual(originalHidden, Tools.hidden);
        }

        [UnityTest]
        public IEnumerator ChangingModeEndsPreviewAndRestoresStandardTools()
        {
            var grab = Root("Changing mode item").AddComponent<GrabAction>();
            bool previous = Tools.hidden;
            GrabPoseAuthoring.Begin(grab);
            Assert.IsTrue(GrabPoseAuthoring.IsActive);
            grab.PoseMode = GrabPoseMode.PreservePickupPose;
            yield return null;
            Assert.IsFalse(GrabPoseAuthoring.IsActive);
            Assert.AreEqual(previous, Tools.hidden);
        }

        [Test]
        public void PrefabContentsStorePoseWithoutMovingRootOrSavingPreviewFrame()
        {
            var source = Root("Prefab item");
            var sourceGrab = source.AddComponent<GrabAction>();
            sourceGrab.PoseMode = GrabPoseMode.AuthoredPose;
            var path = "Assets/EGF_GrabPose_" + Guid.NewGuid().ToString("N") + ".prefab";
            GameObject contents = null;
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, path);
                contents = PrefabUtility.LoadPrefabContents(path);
                var grab = contents.GetComponent<GrabAction>();
                var originalRoot = new Pose(contents.transform.position, contents.transform.rotation);
                var frame = new Pose(new Vector3(0, 1, -2), Quaternion.Euler(0, 30, 0));
                GrabPoseAuthoring.Begin(grab);
                GrabPoseAuthoring.SetSceneViewFrame(frame);
                var desired = new Pose(new Vector3(.4f, .6f, 1), Quaternion.Euler(15, 75, -10));
                Assert.IsTrue(GrabPoseAuthoring.ApplyRootPose(desired));
                Assert.AreEqual(originalRoot.position, contents.transform.position);
                Assert.AreEqual(originalRoot.rotation, contents.transform.rotation);
                Assert.IsNull(grab.HoldPoint);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                GrabPoseAuthoring.End();
                ClearOwnedUndo(contents);
                PrefabUtility.UnloadPrefabContents(contents);
                contents = null;
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<GrabAction>();
                var result = GrabPoseUtility.GetRootPose(saved, frame, true);
                Assert.Less(Vector3.Distance(desired.position, result.position), .0001f);
                Assert.Less(Quaternion.Angle(desired.rotation, result.rotation), .01f);
                Assert.IsNull(saved.HoldPoint);
                Assert.IsNull(new SerializedObject(saved).FindProperty("previewActor"));
            }
            finally
            {
                GrabPoseAuthoring.End();
                if (contents)
                {
                    ClearOwnedUndo(contents);
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
