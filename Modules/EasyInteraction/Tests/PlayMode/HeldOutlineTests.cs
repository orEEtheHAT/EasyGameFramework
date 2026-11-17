using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    public sealed class HeldOutlineTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<Object> assets = new List<Object>();
        private InteractionActor actor, other;
        private InteractionFocusHighlighter highlighter, otherHighlighter;
        private readonly Vector3 origin = new Vector3(0, 1900, 0);

        private GameObject Go(string name, Vector3 position, bool cube = false)
        {
            var go = cube ? GameObject.CreatePrimitive(PrimitiveType.Cube) : new GameObject(name);
            go.name = name; go.transform.position = position; objects.Add(go); return go;
        }

        private InteractionActor Actor(string name, Vector3 position, out InteractionFocusHighlighter observer)
        {
            var root = Go(name, position); root.AddComponent<Camera>().enabled = false;
            var result = root.AddComponent<InteractionActor>();
            result.AutomaticUpdate = false; result.Range = 20; result.RequireVisibility = false; result.View = root.transform;
            observer = root.AddComponent<InteractionFocusHighlighter>();
            return result;
        }

        [SetUp]
        public void SetUp()
        {
            actor = Actor("Holder and observer", origin, out highlighter);
            other = Actor("Other observer", origin + Vector3.right, out otherHighlighter);
            highlighter.OutlineColor = Color.cyan; otherHighlighter.OutlineColor = Color.magenta;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
            foreach (var asset in assets) if (asset) Object.DestroyImmediate(asset);
            assets.Clear();
        }

        private InteractionTarget Item(string name, Vector3 offset, out GrabAction grab)
        {
            var root = Go(name, origin + offset, true);
            grab = root.AddComponent<GrabAction>(); grab.GrabRange = 20; grab.BreakDistance = 20;
            Physics.SyncTransforms(); return root.GetComponent<InteractionTarget>();
        }

        private static void Focus(InteractionActor observer, InteractionFocusHighlighter router, InteractionTarget target)
        {
            Assert.IsTrue(observer.SelectTarget(target)); router.RefreshHighlight();
        }

        private void FocusBoth(InteractionTarget target)
        {
            Focus(actor, highlighter, target); Focus(other, otherHighlighter, target);
        }

        private void RouteAndDraw()
        {
            OutlineTestAccess.Tick(highlighter)(); OutlineTestAccess.Tick(otherHighlighter)();
        }

        private void VisibleForBoth(InteractionTarget target)
        {
            RouteAndDraw();
            Assert.IsFalse(highlighter.IsSuppressedWhileHeld); Assert.IsFalse(otherHighlighter.IsSuppressedWhileHeld);
            Assert.IsTrue(highlighter.IsHighlighted); Assert.IsTrue(otherHighlighter.IsHighlighted);
            Assert.AreSame(target, highlighter.HighlightedTarget); Assert.AreSame(target, otherHighlighter.HighlightedTarget);
            Assert.Greater(highlighter.RenderedPartCount, 0); Assert.Greater(otherHighlighter.RenderedPartCount, 0);
        }

        private void SuppressedForBoth(InteractionTarget target)
        {
            RouteAndDraw();
            if (actor.CurrentTarget == target) Assert.IsTrue(highlighter.IsSuppressedWhileHeld);
            if (other.CurrentTarget == target) Assert.IsTrue(otherHighlighter.IsSuppressedWhileHeld);
            Assert.IsFalse(highlighter.IsHighlighted); Assert.IsFalse(otherHighlighter.IsHighlighted);
            Assert.IsNull(highlighter.HighlightedTarget); Assert.IsNull(otherHighlighter.HighlightedTarget);
            Assert.AreEqual(0, highlighter.RenderedPartCount); Assert.AreEqual(0, otherHighlighter.RenderedPartCount);
            Assert.AreSame(target, otherHighlighter.ObservedTarget, "The other actor still observes the held target; suppression must be global.");
        }

        private void Begin(GrabAction grab) => Assert.IsTrue(actor.TryExecuteDirect(grab), actor.LastFailure.Reason);

        [Test]
        public void HeldItemIsHiddenForAllObserversAndDropReusesTheSameMaterialAndMesh()
        {
            var target = Item("Held outlined item", Vector3.forward * 2, out var grab);
            FocusBoth(target); VisibleForBoth(target);
            var material = OutlineTestAccess.Material(highlighter); var otherMaterial = OutlineTestAccess.Material(otherHighlighter);
            var mesh = OutlineTestAccess.Mesh(target);
            var sourceMaterial = target.GetComponent<Renderer>().sharedMaterial; var sourceMesh = target.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(material); Assert.IsNotNull(otherMaterial); Assert.AreNotSame(material, otherMaterial); Assert.IsNotNull(mesh);
            Begin(grab);
            Assert.IsTrue(highlighter.IsSuppressedWhileHeld); Assert.IsTrue(otherHighlighter.IsSuppressedWhileHeld);
            Assert.IsFalse(highlighter.IsHighlighted); Assert.IsFalse(otherHighlighter.IsHighlighted);
            Assert.IsNull(highlighter.HighlightedTarget); Assert.IsNull(otherHighlighter.HighlightedTarget);
            SuppressedForBoth(target);
            actor.SelectTarget(null); SuppressedForBoth(target);
            Assert.IsTrue(grab.IsHeld, "A holder who no longer focuses the item must still suppress other actors' outlines.");
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            Assert.AreSame(mesh, OutlineTestAccess.Mesh(target));
            actor.Cancel(grab); RouteAndDraw();
            Assert.IsFalse(otherHighlighter.IsSuppressedWhileHeld); Assert.IsTrue(otherHighlighter.IsHighlighted);
            Assert.IsNull(highlighter.HighlightedTarget); Assert.AreEqual(0, highlighter.RenderedPartCount);
            Assert.AreSame(target, otherHighlighter.HighlightedTarget); Assert.Greater(otherHighlighter.RenderedPartCount, 0);
            Focus(actor, highlighter, target); VisibleForBoth(target);
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            Assert.AreSame(mesh, OutlineTestAccess.Mesh(target));
            Assert.AreSame(sourceMaterial, target.GetComponent<Renderer>().sharedMaterial); Assert.AreSame(sourceMesh, target.GetComponent<MeshFilter>().sharedMesh);
        }

        [Test]
        public void TransferAndAdditionalHoldersStaySuppressedUntilTheLastRelease()
        {
            var target = Item("Shared outlined item", Vector3.forward * 2, out var grab);
            FocusBoth(target); VisibleForBoth(target);
            var material = OutlineTestAccess.Material(highlighter); var otherMaterial = OutlineTestAccess.Material(otherHighlighter); var mesh = OutlineTestAccess.Mesh(target);
            Begin(grab); SuppressedForBoth(target);
            Assert.IsTrue(grab.TryTransferTo(other)); Assert.AreSame(other, grab.Owner); SuppressedForBoth(target);
            other.Cancel(grab); VisibleForBoth(target);
            grab.MaxParticipants = 2; Begin(grab); Assert.IsTrue(other.TryExecuteDirect(grab), other.LastFailure.Reason);
            Assert.AreEqual(2, grab.OwnerCount); SuppressedForBoth(target);
            actor.Cancel(grab); Assert.AreEqual(1, grab.OwnerCount); SuppressedForBoth(target);
            other.Cancel(grab); VisibleForBoth(target);
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            Assert.AreSame(mesh, OutlineTestAccess.Mesh(target));
        }

        [Test]
        public void FocusChangesWhileHoldingDoNotLeaveDrawsOrSuppressAnotherItem()
        {
            var held = Item("Held", Vector3.forward * 2, out var grab);
            var next = Item("Other focused item", Vector3.forward * 2 + Vector3.right * 3, out _);
            FocusBoth(held); VisibleForBoth(held);
            var material = OutlineTestAccess.Material(highlighter); var mesh = OutlineTestAccess.Mesh(held);
            Begin(grab); SuppressedForBoth(held);
            Focus(actor, highlighter, next); other.SelectTarget(null); RouteAndDraw();
            Assert.AreSame(next, highlighter.HighlightedTarget); Assert.Greater(highlighter.RenderedPartCount, 0);
            Assert.IsNull(otherHighlighter.HighlightedTarget); Assert.AreEqual(0, otherHighlighter.RenderedPartCount);
            actor.Cancel(grab); RouteAndDraw();
            Assert.AreSame(next, highlighter.HighlightedTarget, "Dropping another item must not replace this observer's current focus.");
            Assert.IsNull(otherHighlighter.HighlightedTarget); Assert.AreEqual(0, otherHighlighter.RenderedPartCount);
            Focus(other, otherHighlighter, held); RouteAndDraw();
            Assert.AreSame(held, otherHighlighter.HighlightedTarget); Assert.AreSame(next, highlighter.HighlightedTarget);
            Assert.Greater(otherHighlighter.RenderedPartCount, 0);
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(mesh, OutlineTestAccess.Mesh(held));
        }

        [TestCase("throw")]
        [TestCase("cancel")]
        [TestCase("actorDisable")]
        [TestCase("grabDisable")]
        [TestCase("grabDestroy")]
        [TestCase("pool")]
        public void ReleaseAndLifecycleExitsDoNotLeaveSuppressionOrStaleDraws(string exit)
        {
            var target = Item("Lifecycle item", Vector3.forward * 2, out var grab);
            FocusBoth(target); VisibleForBoth(target);
            var material = OutlineTestAccess.Material(highlighter); var otherMaterial = OutlineTestAccess.Material(otherHighlighter); var mesh = OutlineTestAccess.Mesh(target);
            Begin(grab); SuppressedForBoth(target);
            switch (exit)
            {
                case "throw": Assert.IsTrue(grab.Throw(actor)); break;
                case "cancel": actor.Cancel(grab); break;
                case "actorDisable": actor.enabled = false; break;
                case "grabDisable": grab.enabled = false; break;
                case "grabDestroy": Object.DestroyImmediate(grab); break;
                case "pool": target.gameObject.SetActive(false); break;
            }
            Assert.IsNull(actor.ActiveAction);
            if (exit == "pool")
            {
                RouteAndDraw();
                Assert.AreEqual(0, highlighter.RenderedPartCount); Assert.AreEqual(0, otherHighlighter.RenderedPartCount);
                Assert.IsNull(OutlineTestAccess.Mesh(target), "Disabling the target must release its geometry lease.");
                Assert.AreSame(material, OutlineTestAccess.Material(highlighter), "An observer material survives an unrelated item being pooled.");
                Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
                target.gameObject.SetActive(true); Physics.SyncTransforms(); FocusBoth(target);
            }
            RouteAndDraw();
            Assert.IsFalse(otherHighlighter.IsSuppressedWhileHeld); Assert.IsTrue(otherHighlighter.IsHighlighted);
            Assert.AreSame(target, otherHighlighter.HighlightedTarget); Assert.Greater(otherHighlighter.RenderedPartCount, 0);
            if (exit == "actorDisable") { Assert.IsNull(highlighter.HighlightedTarget); Assert.AreEqual(0, highlighter.RenderedPartCount); }
            else { Assert.AreSame(target, highlighter.HighlightedTarget); Assert.Greater(highlighter.RenderedPartCount, 0); }
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            if (exit != "pool") Assert.AreSame(mesh, OutlineTestAccess.Mesh(target), "Only target disable/opt-out releases target geometry.");
        }

        [UnityTest]
        public IEnumerator TargetOptOutWhileHeldReleasesGeometryButKeepsObserverMaterialsAndRebuildsOnlyAfterDrop()
        {
            var target = Item("Unique mesh lifecycle", Vector3.forward * 2, out var grab);
            var filter = target.GetComponent<MeshFilter>(); var source = Object.Instantiate(filter.sharedMesh); assets.Add(source);
            filter.sharedMesh = source; target.RefreshOutlineRenderers(); FocusBoth(target); VisibleForBoth(target);
            var material = OutlineTestAccess.Material(highlighter); var otherMaterial = OutlineTestAccess.Material(otherHighlighter); var mesh = OutlineTestAccess.Mesh(target);
            Begin(grab); SuppressedForBoth(target);
            target.HighlightWhenFocused = false; RouteAndDraw();
            Assert.AreEqual(0, highlighter.RenderedPartCount); Assert.AreEqual(0, otherHighlighter.RenderedPartCount);
            Assert.IsNull(OutlineTestAccess.Mesh(target));
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            yield return null;
            Assert.IsFalse(mesh, "Opt-out releases the sole private mesh lease."); Assert.IsTrue(source, "The application-owned source survives.");
            target.HighlightWhenFocused = true; SuppressedForBoth(target);
            Assert.IsNull(OutlineTestAccess.Mesh(target), "A held target must not rebuild drawing geometry before it can be rendered.");
            actor.Cancel(grab); VisibleForBoth(target);
            Assert.IsNotNull(OutlineTestAccess.Mesh(target));
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            Assert.AreSame(source, filter.sharedMesh);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WarmHeldAndUnheldRoutingAndDrawDoNotAllocate(bool held)
        {
            var target = Item("Warmed outline", Vector3.forward * 2, out var grab);
            FocusBoth(target); VisibleForBoth(target);
            var material = OutlineTestAccess.Material(highlighter); var otherMaterial = OutlineTestAccess.Material(otherHighlighter); var mesh = OutlineTestAccess.Mesh(target);
            var draw = OutlineTestAccess.Tick(highlighter); var otherDraw = OutlineTestAccess.Tick(otherHighlighter);
            if (held) Begin(grab);
            bool previous = Profiler.enabled;
            try
            {
                Profiler.enabled = true;
                var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                using (var control = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!control.Valid || !Profiler.enabled) Assert.Inconclusive("Main-thread GC.Alloc unavailable.");
                    System.GC.KeepAlive(new byte[512]); control.Stop();
                    if (control.Count == 0 || control.GetSample(0).Count == 0) Assert.Inconclusive("GC.Alloc missed the positive control.");
                }
                for (int i = 0; i < 64; i++) { draw(); otherDraw(); }
                long events;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) Assert.Inconclusive("GC.Alloc unavailable for the measured scope.");
                    for (int i = 0; i < 128; i++) { draw(); otherDraw(); }
                    recorder.Stop(); events = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }
                Assert.AreEqual(0, events, "128 warmed observer routing and camera-specific draw/suppression ticks for two observers.");
            }
            finally { Profiler.enabled = previous; }
            Assert.AreEqual(held, highlighter.IsSuppressedWhileHeld); Assert.AreEqual(held, otherHighlighter.IsSuppressedWhileHeld);
            Assert.AreEqual(!held, highlighter.IsHighlighted); Assert.AreEqual(!held, otherHighlighter.IsHighlighted);
            Assert.AreEqual(held ? 0 : 1, highlighter.RenderedPartCount); Assert.AreEqual(held ? 0 : 1, otherHighlighter.RenderedPartCount);
            Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(otherMaterial, OutlineTestAccess.Material(otherHighlighter));
            Assert.AreSame(mesh, OutlineTestAccess.Mesh(target));
        }
    }
}
