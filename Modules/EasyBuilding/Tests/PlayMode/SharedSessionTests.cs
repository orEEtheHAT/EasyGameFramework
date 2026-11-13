using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyBuilding.PlayModeTests
{
    public sealed class SharedSessionTests
    {
        private readonly List<Object> objects = new List<Object>();
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private BuildingDefinition Definition()
        {
            var prefab = Go("Prefab"); prefab.SetActive(false);
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab; definition.RequireSurface = false; return definition;
        }
        [UnityTearDown] public IEnumerator Cleanup()
        { for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]); objects.Clear(); yield return null; }

        [UnityTest] public IEnumerator ActorAndPlacerBorrowOneSessionAcrossControllerDisable()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>();
            var controls = Go("Controls"); controls.SetActive(false); var world = controls.AddComponent<BuildingSystem>();
            var placer = controls.AddComponent<BuildingPlacer>(); placer.Actor = actor; placer.ShowHelp = false;
            var definition = Definition(); var shared = actor.GetSession(world); Assert.That(shared.Select(definition), Is.True);
            controls.SetActive(true); yield return null;
            Assert.That(placer.Session, Is.SameAs(shared)); Assert.That(placer.Session.Selected, Is.SameAs(definition));
            placer.enabled = false; Assert.That(shared.IsDisposed, Is.False); Assert.That(shared.Selected, Is.SameAs(definition));
            placer.enabled = true; Assert.That(placer.Session, Is.SameAs(shared));
            actor.enabled = false; Assert.That(shared.Selected, Is.Null); Assert.That(shared.IsDisposed, Is.False);
            actor.enabled = true; Assert.That(actor.GetSession(world), Is.SameAs(shared));
            Object.Destroy(placer); yield return null; Assert.That(shared.IsDisposed, Is.False);
            Object.Destroy(actor.gameObject); yield return null; Assert.That(shared.IsDisposed, Is.True);
        }
        [UnityTest] public IEnumerator ExplicitBindingValidatesWorldAndNeverDisposesBorrowedSessions()
        {
            var world = Go("World").AddComponent<BuildingSystem>(); var other = Go("Other world").AddComponent<BuildingSystem>();
            var placer = world.gameObject.AddComponent<BuildingPlacer>(); placer.ShowHelp = false;
            using var borrowed = world.CreateSession(); using var foreign = other.CreateSession();
            var definition = Definition(); borrowed.Select(definition);
            var original = placer.Session; original.Select(definition); placer.SetSession(original);
            placer.enabled = false;
            Assert.That(original.Selected, Is.SameAs(definition), "Explicitly binding the current default reference must also use borrowed lifecycle.");
            placer.enabled = true;
            Assert.Throws<ArgumentException>(() => placer.SetSession(foreign));
            placer.SetSession(borrowed); Assert.That(placer.Session, Is.SameAs(borrowed));
            placer.enabled = false; Assert.That(borrowed.Selected, Is.SameAs(definition)); Assert.That(borrowed.IsDisposed, Is.False);
            placer.enabled = true; borrowed.Dispose(); Assert.That(placer.Session, Is.Null); Assert.That(placer.ConfigurationError, Is.Not.Empty);
            placer.SetSession(null); Assert.That(placer.Session, Is.SameAs(world.DefaultSession));
            yield return null;
        }
        [UnityTest] public IEnumerator IndependentSessionsAndWorldLifetimesStaySeparate()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>(); var a = Go("A").AddComponent<BuildingSystem>(); var b = Go("B").AddComponent<BuildingSystem>();
            var first = actor.GetSession(a); var second = actor.GetSession(b); using var independent = a.CreateSession(actor);
            Assert.That(first, Is.Not.SameAs(second)); Assert.That(first, Is.Not.SameAs(independent)); Assert.That(first.System, Is.SameAs(a));
            first.Dispose(); var recreated = actor.GetSession(a); Assert.That(recreated, Is.Not.SameAs(first));
            Object.Destroy(a.gameObject); yield return null;
            Assert.That(recreated.IsDisposed, Is.True); Assert.That(independent.IsDisposed, Is.True); Assert.That(second.IsDisposed, Is.False);
        }
        [UnityTest] public IEnumerator SelectionHooksReportStableCompletionAndPermitNextSelection()
        {
            var world = Go("World").AddComponent<BuildingSystem>(); using var session = world.CreateSession();
            var a = Definition(); var b = Definition(); var values = new List<BuildingDefinition>(); int confirmed = 0, disposed = 0;
            session.SelectionChanged += value => values.Add(value); session.Confirmed += _ => { confirmed++; session.Select(b); }; session.Disposed += _ => disposed++;
            session.Select(a); var request = new PlacementRequest(new Vector3(71000, 0, 0), Quaternion.identity);
            Assert.That(session.TryConfirm(request, out _, out var result), Is.True, result.Reason);
            CollectionAssert.AreEqual(new[] { a, null, b }, values); Assert.That(confirmed, Is.EqualTo(1)); Assert.That(session.Selected, Is.SameAs(b));
            session.Cancel(); Assert.That(values[values.Count - 1], Is.Null); session.Dispose(); Assert.That(disposed, Is.EqualTo(1)); yield return null;
        }
        [UnityTest] public IEnumerator WorldDisposalRejectsSessionRecreationFromCallbacks()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>(); var world = Go("World").AddComponent<BuildingSystem>();
            var session = actor.GetSession(world); Exception rejected = null;
            session.Disposed += _ => { try { actor.GetSession(world); } catch (Exception exception) { rejected = exception; } };
            Object.Destroy(world.gameObject); yield return null;
            Assert.That(session.IsDisposed, Is.True); Assert.That(rejected, Is.TypeOf<ObjectDisposedException>());
            Assert.That(actor.GetSession(Go("New world").AddComponent<BuildingSystem>()).IsDisposed, Is.False);
        }
        [UnityTest] public IEnumerator ActorSwapAndAutomaticReconnectNeverFallBackFromDestroyedActor()
        {
            var world = Go("World").AddComponent<BuildingSystem>(); var a = Go("A").AddComponent<BuildingActor>(); var b = Go("B").AddComponent<BuildingActor>();
            var placer = world.gameObject.AddComponent<BuildingPlacer>(); placer.ShowHelp = false; placer.Actor = a;
            var first = placer.Session; Assert.That(first, Is.SameAs(a.GetSession(world)));
            placer.Actor = b; var second = placer.Session; Assert.That(second, Is.SameAs(b.GetSession(world))); Assert.That(first.IsDisposed, Is.False);
            second.Dispose(); var next = placer.Session; Assert.That(next, Is.Not.SameAs(second)); Assert.That(next, Is.SameAs(b.GetSession(world)));
            Object.Destroy(b.gameObject); yield return null;
            Assert.That(placer.Session, Is.Null); Assert.That(placer.ConfigurationError, Is.Not.Empty);
            placer.Actor = a; Assert.That(placer.Session, Is.SameAs(first)); Assert.That(placer.ConfigurationError, Is.Null);
        }
        [UnityTest] public IEnumerator CancelDuringCommittedPlacementPreventsRepeatAndRecursiveConfirmation()
        {
            var world = Go("World").AddComponent<BuildingSystem>(); using var session = world.CreateSession(); var definition = Definition();
            var request = new PlacementRequest(new Vector3(71000, 0, 0), Quaternion.identity); session.RepeatPlacement = true; session.Select(definition);
            int ends = 0, confirms = 0; session.SelectionChanged += value => { if (value == null) ends++; }; session.Confirmed += _ => confirms++;
            world.Placed += _ =>
            {
                Assert.That(session.TryConfirm(request, out _, out var recursive), Is.False); Assert.That(recursive.Failure, Is.EqualTo(PlacementFailure.Busy));
                session.Cancel();
            };
            Assert.That(session.TryConfirm(request, out _, out var result), Is.True, result.Reason);
            Assert.That(world.Count, Is.EqualTo(1)); Assert.That(session.Selected, Is.Null); Assert.That(ends, Is.EqualTo(1)); Assert.That(confirms, Is.EqualTo(1));
            yield return null;
        }
        [UnityTest] public IEnumerator WarmSharedSessionLookupDoesNotAllocate()
        {
            var world = Go("World").AddComponent<BuildingSystem>(); var actor = Go("Actor").AddComponent<BuildingActor>();
            var placer = world.gameObject.AddComponent<BuildingPlacer>(); placer.Actor = actor; placer.ShowHelp = false; placer.ExternalTargeting = true;
            var shared = actor.GetSession(world); placer.SetInputBlocked(actor, true); bool previous = UnityEngine.Profiling.Profiler.enabled;
            try
            {
                UnityEngine.Profiling.Profiler.enabled = true;
                var options = Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame | Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                long calibration;
                using (var control = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "GC.Alloc", 1, options))
                { GC.KeepAlive(new byte[256]); control.Stop(); calibration = control.Count == 0 ? 0 : control.GetSample(0).Count; }
                for (int i = 0; i < 100; i++) { actor.GetSession(world); GC.KeepAlive(placer.Session); if (!placer.InputBlocked) throw new InvalidOperationException("The live owner must hold its input gate."); }
                long allocations; bool same = true; long start = System.Diagnostics.Stopwatch.GetTimestamp();
                using (var recorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    for (int i = 0; i < 10000; i++) same &= ReferenceEquals(shared, actor.GetSession(world)) && ReferenceEquals(shared, placer.Session) && placer.InputBlocked;
                    recorder.Stop(); allocations = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                }
                double elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000000.0 / System.Diagnostics.Stopwatch.Frequency;
                Assert.That(calibration, Is.EqualTo(1)); Assert.That(same, Is.True); Assert.That(allocations, Is.Zero);
                TestContext.WriteLine($"10000 actor/placer lookup + active-owner block checks: {elapsed / 10000:F3} microseconds/iteration, {allocations} main-thread GC events.");
            }
            finally { UnityEngine.Profiling.Profiler.enabled = previous; }
            yield return null;
        }
        private sealed class EqualOwner { public override bool Equals(object value) => value is EqualOwner; public override int GetHashCode() => 0; }
        [UnityTest] public IEnumerator InputBlocksBelongToDistinctLiveOwners()
        {
            var placer = Go("Controls").AddComponent<BuildingPlacer>(); placer.ShowHelp = false; placer.ExternalTargeting = true;
            var a = new EqualOwner(); var b = new EqualOwner(); placer.SetInputBlocked(a, true); placer.SetInputBlocked(b, true);
            placer.SetInputBlocked(a, false); Assert.That(placer.InputBlocked, Is.True); placer.SetInputBlocked(b, false); Assert.That(placer.InputBlocked, Is.False);
            var owner = Go("Window").AddComponent<BuildingActor>(); placer.SetInputBlocked(owner, true); Assert.That(placer.InputBlocked, Is.True);
            owner.enabled = false; Assert.That(placer.InputBlocked, Is.False); owner.enabled = true; Assert.That(placer.InputBlocked, Is.False);
            placer.SetInputBlocked(owner, true); placer.SetInputBlocked(a, true); Object.Destroy(owner.gameObject); yield return null;
            Assert.That(placer.InputBlocked, Is.True, "Destroying one owner must retain the other request.");
            placer.SetInputBlocked(a, false); Assert.That(placer.InputBlocked, Is.False);
            placer.InputBlocked = true; placer.SetInputBlocked(b, true); placer.SetInputBlocked(b, false); Assert.That(placer.InputBlocked, Is.True);
            placer.InputBlocked = false; Assert.That(placer.InputBlocked, Is.False);
            var window = Go("Window object"); placer.SetInputBlocked(window.transform, true); window.SetActive(false); Assert.That(placer.InputBlocked, Is.False);
        }
    }
}
