using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyInteraction.Tests
{
    internal static class OutlineTestAccess
    {
        internal const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static Action Tick(object instance, string method = "LateUpdate") => (Action)Delegate.CreateDelegate(typeof(Action), instance,
            instance.GetType().GetMethod(method, Fields));
        internal static Material Material(InteractionFocusHighlighter observer) =>
            (Material)typeof(InteractionFocusHighlighter).GetField("material", Fields).GetValue(observer);
        internal static object Geometry(InteractionTarget target) =>
            typeof(InteractionTarget).GetField("outlineGeometry", Fields).GetValue(target);
        internal static Mesh Mesh(InteractionTarget target)
        {
            var geometry = Geometry(target);
            if (geometry == null) return null;
            var parts = (IList)geometry.GetType().GetField("parts", Fields).GetValue(geometry);
            if (parts.Count == 0) return null;
            var part = parts[0];
            var lease = part.GetType().GetField("Lease", Fields).GetValue(part);
            return lease == null ? null : (Mesh)lease.GetType().GetField("Mesh", Fields).GetValue(lease);
        }
    }

    public sealed class FocusOutlineTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private InteractionActor actor;
        private InteractionFocusHighlighter highlighter;
        private Camera camera;

        private GameObject Go(string name, Vector3 position, bool cube = false)
        {
            var go = cube ? GameObject.CreatePrimitive(PrimitiveType.Cube) : new GameObject(name);
            go.name = name;
            go.transform.position = position;
            objects.Add(go);
            return go;
        }

        [SetUp]
        public void SetUp()
        {
            var root = Go("Outline observer", new Vector3(0, 1300, 0));
            camera = root.AddComponent<Camera>(); camera.enabled = false;
            actor = root.AddComponent<InteractionActor>();
            actor.View = root.transform; actor.AutomaticUpdate = false; actor.Range = 10;
            highlighter = root.AddComponent<InteractionFocusHighlighter>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private InteractionTarget Item(string name, Vector3 offset)
        {
            var target = Go(name, actor.View.position + offset, true).AddComponent<InteractionTarget>();
            Physics.SyncTransforms();
            return target;
        }

        private void Focus(InteractionTarget target)
        {
            Assert.IsTrue(actor.SelectTarget(target));
            highlighter.RefreshHighlight();
        }

        private void Draw() => OutlineTestAccess.Tick(highlighter)();

        [Test]
        public void FocusSwitchDisableAndReenableOwnOnlyTheirOutlines()
        {
            var first = Item("First", Vector3.forward * 2);
            var second = Item("Second", Vector3.forward * 3 + Vector3.right * 2);
            Focus(first); Draw();
            Assert.IsTrue(highlighter.IsHighlighted);
            Assert.AreSame(first, highlighter.HighlightedTarget);
            Focus(second); Draw();
            Assert.AreSame(second, highlighter.HighlightedTarget);
            highlighter.enabled = false;
            Assert.IsFalse(highlighter.IsHighlighted);
            Assert.IsNull(highlighter.HighlightedTarget);
            highlighter.enabled = true; Draw();
            Assert.AreSame(second, highlighter.HighlightedTarget);
            second.HighlightWhenFocused = false; highlighter.RefreshHighlight(); Draw();
            Assert.IsFalse(highlighter.IsHighlighted);
            Assert.AreEqual(0, highlighter.RenderedPartCount);
            second.HighlightWhenFocused = true; highlighter.RefreshHighlight(); Draw();
            Assert.AreSame(second, highlighter.HighlightedTarget, "A reenabled target checkbox must reacquire existing focus.");
            second.enabled = false; highlighter.RefreshHighlight(); Draw();
            Assert.IsFalse(highlighter.IsHighlighted);
            Assert.IsNull(highlighter.HighlightedTarget);
        }

        [Test]
        public void MultipleActorsReleaseIndependentlyAndDestroyedFocusDoesNotLinger()
        {
            var target = Item("Shared", Vector3.forward * 2);
            var root = Go("Other observer", actor.View.position + Vector3.right);
            root.AddComponent<Camera>().enabled = false;
            var other = root.AddComponent<InteractionActor>(); other.AutomaticUpdate = false; other.Range = 10; other.View = root.transform;
            var otherHighlight = root.AddComponent<InteractionFocusHighlighter>();
            Focus(target); Draw();
            Assert.IsTrue(other.SelectTarget(target)); otherHighlight.RefreshHighlight(); OutlineTestAccess.Tick(otherHighlight)();
            var sharedMesh = OutlineTestAccess.Mesh(target);
            highlighter.enabled = false;
            Assert.IsTrue(otherHighlight.IsHighlighted);
            Assert.AreSame(target, otherHighlight.HighlightedTarget);
            Assert.AreSame(sharedMesh, OutlineTestAccess.Mesh(target), "Observer disable must not dispose target-owned geometry.");
            other.enabled = false; OutlineTestAccess.Tick(otherHighlight)();
            Assert.IsFalse(otherHighlight.IsHighlighted);
            highlighter.enabled = true; Focus(target); Draw();
            Object.DestroyImmediate(target.gameObject);
            highlighter.RefreshHighlight(); Draw();
            Assert.IsNull(highlighter.HighlightedTarget);
            Assert.IsNull(highlighter.ObservedTarget);
            Assert.AreEqual(0, highlighter.RenderedPartCount);
        }

        [Test]
        public void CollectsStaticMeshPartsExcludesNestedTargetsAndHonoursExplicitParts()
        {
            var target = Item("Root", Vector3.forward * 2);
            var child = Go("Second mesh", target.transform.position + Vector3.up, true); child.transform.SetParent(target.transform, true);
            var nested = Go("Nested target", target.transform.position + Vector3.right, true); nested.transform.SetParent(target.transform, true);
            nested.AddComponent<InteractionTarget>();
            var skin = Go("Unsupported skin", target.transform.position); skin.transform.SetParent(target.transform, true); skin.AddComponent<SkinnedMeshRenderer>();
            target.RefreshOutlineRenderers(); Focus(target); Draw();
            Assert.AreEqual(2, highlighter.RendererCount);
            Assert.AreEqual(1, highlighter.UnsupportedRendererCount);
            var rootRenderer = target.GetComponent<Renderer>();
            target.OutlineRenderers = new[] { rootRenderer, rootRenderer }; Draw();
            Assert.AreEqual(1, highlighter.RendererCount, "Explicit duplicates must not double draw the shell.");
            Assert.AreEqual(0, highlighter.UnsupportedRendererCount);
            rootRenderer.enabled = false; highlighter.RefreshHighlight(); Draw(); Assert.IsFalse(highlighter.IsHighlighted);
            rootRenderer.enabled = true; rootRenderer.forceRenderingOff = true; highlighter.RefreshHighlight(); Draw(); Assert.IsFalse(highlighter.IsHighlighted);
            rootRenderer.forceRenderingOff = false; rootRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly; highlighter.RefreshHighlight(); Draw(); Assert.IsFalse(highlighter.IsHighlighted);
            rootRenderer.shadowCastingMode = ShadowCastingMode.On; highlighter.RefreshHighlight(); Draw(); Assert.IsTrue(highlighter.IsHighlighted);
        }

        [UnityTest]
        public IEnumerator SourceMaterialsStayUntouchedAndObserverMaterialIsReusedThenDestroyed()
        {
            var target = Item("Material ownership", Vector3.forward * 2);
            var source = target.GetComponent<Renderer>(); var original = source.sharedMaterial;
            Focus(target); Draw();
            var material = OutlineTestAccess.Material(highlighter); var mesh = OutlineTestAccess.Mesh(target);
            Assert.IsNotNull(material); Assert.Greater(highlighter.RenderedPartCount, 0); Assert.AreNotSame(original, material);
            for (int i = 0; i < 10; i++)
            {
                actor.SelectTarget(null); highlighter.RefreshHighlight(); Focus(target); Draw();
                Assert.AreSame(material, OutlineTestAccess.Material(highlighter)); Assert.AreSame(original, source.sharedMaterial);
            }
            highlighter.enabled = false;
            Assert.AreEqual(0, highlighter.RenderedPartCount); Assert.IsFalse(highlighter.IsHighlighted);
            Assert.AreSame(mesh, OutlineTestAccess.Mesh(target), "Geometry belongs to the target, not the disabled observer.");
            yield return null;
            Assert.IsFalse(material, "Only this observer's owned material must be destroyed on disable.");
            Assert.IsTrue(mesh); Assert.IsTrue(original); Assert.AreSame(original, source.sharedMaterial);
        }

        [Test]
        public void ResourcesShaderAndSafeWidthBoundsAreAvailableWithoutRendererFeature()
        {
            var shader = Resources.Load<Shader>("EasyInteraction/FocusOutline");
            Assert.IsNotNull(shader); Assert.AreEqual("EasyGameFramework/Interaction/Focus Outline", shader.name);
            Assert.IsTrue(highlighter.IsShaderAvailable);
            highlighter.Width = -1; Assert.AreEqual(.001f, highlighter.Width);
            highlighter.Width = 10; Assert.AreEqual(.1f, highlighter.Width);
            highlighter.Width = float.NaN; Assert.AreEqual(.015f, highlighter.Width);
            Assert.IsTrue(highlighter.HasRenderCamera); Assert.AreSame(camera, highlighter.ResolvedCamera);
        }

        [Test]
        public void FreshTargetCheckboxHighlightsWithoutAnAdditionalItemComponent()
        {
            var target = Item("Plain target", Vector3.forward * 2);
            Assert.IsTrue(target.HighlightWhenFocused);
            Assert.IsEmpty(target.OutlineRenderers);
            Assert.AreEqual(1, target.GetComponents<MonoBehaviour>().Length, "The target checkbox is the only item-side behaviour required.");
            Focus(target); Draw();
            Assert.AreSame(target, highlighter.HighlightedTarget); Assert.Greater(highlighter.RenderedPartCount, 0);
            target.HighlightWhenFocused = false; Draw();
            Assert.IsNull(highlighter.HighlightedTarget); Assert.AreEqual(0, highlighter.RenderedPartCount);
            Assert.IsNull(OutlineTestAccess.Mesh(target), "Opting out releases target-owned geometry.");
        }

        [Test]
        public void MissingExactViewCameraDoesNotUseAnUnrelatedCameraAndExplicitOverrideWorks()
        {
            var view = Go("View with no Camera", actor.View.position).transform; view.SetParent(actor.transform, true); actor.View = view;
            var target = Item("Camera routing target", Vector3.forward * 2); Focus(target); Draw();
            Assert.IsFalse(highlighter.HasRenderCamera); Assert.IsNull(highlighter.ResolvedCamera);
            Assert.IsFalse(highlighter.IsHighlighted); Assert.IsNull(highlighter.HighlightedTarget); Assert.AreEqual(0, highlighter.RenderedPartCount);
            Assert.IsNull(OutlineTestAccess.Material(highlighter), "Missing a render camera must not allocate an unused material.");
            highlighter.CameraOverride = camera; highlighter.RefreshHighlight(); Draw();
            Assert.AreSame(camera, highlighter.ResolvedCamera); Assert.IsTrue(highlighter.IsHighlighted); Assert.Greater(highlighter.RenderedPartCount, 0);
            highlighter.CameraOverride = null; highlighter.RefreshHighlight(); Draw();
            Assert.IsFalse(highlighter.HasRenderCamera); Assert.AreEqual(0, highlighter.RenderedPartCount);
        }

        [Test]
        public void WarmFocusRoutingAndDrawDoNotAllocateOnMainThread()
        {
            var target = Item("Warmed draw", Vector3.forward * 2); Focus(target);
            var draw = OutlineTestAccess.Tick(highlighter); draw(); Assert.IsNotNull(OutlineTestAccess.Material(highlighter));
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
                for (int i = 0; i < 64; i++) draw();
                long events;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    if (!recorder.Valid) Assert.Inconclusive("GC.Alloc unavailable for the measured scope.");
                    for (int i = 0; i < 128; i++) draw();
                    recorder.Stop(); events = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }
                Assert.AreEqual(0, events, "128 warmed observer routing and camera-specific outline submissions.");
            }
            finally { Profiler.enabled = previous; }
        }
    }
}
