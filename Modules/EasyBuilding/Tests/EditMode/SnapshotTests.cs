using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests
{
    public sealed class SnapshotTests
    {
        private readonly List<Object> objects = new List<Object>();
        private BuildingSystem world;
        private BuildingDefinition definition;
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void Setup()
        {
            world = Go("World").AddComponent<BuildingSystem>(); var prefab = Go("Prefab"); prefab.SetActive(false);
            Assert.That(prefab.AddComponent<SnapshotTestPart>(), Is.Not.Null,
                "The state-part fixture must live in a runtime test-support assembly so Unity can attach and clone it.");
            var socket = new GameObject("Socket"); socket.transform.SetParent(prefab.transform, false); socket.AddComponent<BuildingSocket>();
            definition = BuildingDefinition.Create("snapshot.definition", prefab); definition.RequireSurface = false; objects.Add(definition); SnapshotTestPart.Restores = 0;
        }
        [TearDown] public void Cleanup() { for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear(); }
        private PlacedBuilding Place(float x = 0)
        {
            Assert.That(world.TryPlace(definition, new PlacementRequest(new Vector3(81000 + x, 0, 0), Quaternion.identity), out var item, out var result), Is.True, result.Reason);
            Assert.That(item.GetComponent<SnapshotTestPart>(), Is.Not.Null, "Placement must retain the prefab's private state-part component.");
            return item;
        }
        private BuildingDefinition[] Catalog => new[] { definition };
        [Test] public void ValidateDoesNotMutateAndRepeatedReplacementRetainsPrivateStateAndClaims()
        {
            var original = Place(); original.GetComponent<SnapshotTestPart>().Value = 42;
            var saved = world.CaptureState(); saved.RedeemedParcels.Add("claimed.parcel"); string id = original.InstanceId;
            Place(3); world.Select(definition); int restored = 0, placed = 0, removed = 0; world.Restored += () => restored++; world.Placed += _ => placed++; world.Removed += _ => removed++;
            long revision = world.Revision;
            Assert.That(world.ValidateSnapshot(saved, Catalog, out var reason), Is.True, reason);
            Assert.That(world.Revision, Is.EqualTo(revision)); Assert.That(world.Count, Is.EqualTo(2)); Assert.That(world.Selected, Is.SameAs(definition)); Assert.That(SnapshotTestPart.Restores, Is.Zero);
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, null, out var report, out reason), Is.True, reason);
            Assert.That(report.Committed && report.IsComplete, Is.True); Assert.That(world.Count, Is.EqualTo(1)); Assert.That(world.Selected, Is.Null);
            Assert.That(world.TryGet(id, out var copy), Is.True); Assert.That(copy.GetComponent<SnapshotTestPart>().Value, Is.EqualTo(42));
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, out reason), Is.True, reason);
            var again = world.CaptureState(); Assert.That(again.Revision, Is.EqualTo(saved.Revision)); Assert.That(again.Buildings[0].InstanceId, Is.EqualTo(id));
            Assert.That(again.Buildings[0].Parts[0].Replicate, Is.False); CollectionAssert.AreEqual(saved.RedeemedParcels, again.RedeemedParcels);
            Assert.That(restored, Is.EqualTo(2)); Assert.That(placed + removed, Is.Zero);
            Assert.That(world.TryReplaceSnapshot(new BuildingState(), Catalog, out reason), Is.True, reason);
            Assert.That(world.Count, Is.Zero); Assert.That(world.CaptureState().RedeemedParcels, Is.Empty);
        }
        [Test] public void InvalidPayloadAndStageFailureKeepOriginalObjectsAndState()
        {
            var original = Place(); var saved = world.CaptureState(); long revision = world.Revision;
            saved.Buildings[0].Parts[0].Json = "invalid";
            Assert.That(world.ValidateSnapshot(saved, Catalog, out _), Is.False);
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, null, out var invalid, out _), Is.False); Assert.That(invalid.Committed, Is.False);
            saved = world.CaptureState(); definition.Prefab.GetComponent<SnapshotTestPart>().ThrowOnRestore = true;
            Assert.That(world.ValidateSnapshot(saved, Catalog, out _), Is.True, "Preflight checks the data; instance restoration can still reject before commit.");
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, null, out var staged, out _), Is.False); Assert.That(staged.Committed, Is.False);
            Assert.That(world.TryGet(original.InstanceId, out var same), Is.True); Assert.That(same, Is.SameAs(original)); Assert.That(world.Revision, Is.EqualTo(revision)); Assert.That(world.Count, Is.EqualTo(1));
        }
        [Test] public void MissingDefinitionsAndDeferredLinksAreExplicitAndRoundTrip()
        {
            var item = Place(); var saved = world.CaptureState(); saved.Buildings[0].DefinitionId = "missing.content";
            saved.Connections.Add(new BuildingConnectionRecord { Id = "pending", SourceId = item.InstanceId, TargetId = "unloaded.target", TargetSocketId = "socket", Primary = true });
            saved.RedeemedParcels.Add("private.claim"); var options = new BuildingRestoreOptions { PreserveUnknownDefinitions = true, DeferMissingConnections = true };
            Assert.That(world.ValidateSnapshot(saved, Catalog, out _), Is.False);
            Assert.That(world.ValidateSnapshot(saved, Catalog, options, out var preview, out var reason), Is.True, reason);
            Assert.That(preview.IsComplete, Is.False); Assert.That(preview.MissingDefinitions, Is.EqualTo(1)); Assert.That(preview.DeferredConnections, Is.EqualTo(1)); Assert.That(world.Count, Is.EqualTo(1));
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, options, out var applied, out reason), Is.True, reason);
            Assert.That(applied.Committed, Is.True); Assert.That(applied.IsComplete, Is.False); Assert.That(world.MissingBuildingCount, Is.EqualTo(1)); Assert.That(world.PendingConnectionCount, Is.EqualTo(1));
            var roundTrip = world.CaptureState(); Assert.That(roundTrip.Buildings[0].Parts[0].Key, Is.EqualTo("private.test")); Assert.That(roundTrip.Connections[0].Id, Is.EqualTo("pending")); CollectionAssert.AreEqual(saved.RedeemedParcels, roundTrip.RedeemedParcels);
            Assert.That(world.TryReplaceSnapshot(roundTrip, Catalog, options, out _, out reason), Is.True, reason); Assert.That(world.Count, Is.Zero); Assert.That(world.MissingBuildingCount, Is.EqualTo(1));
            roundTrip.Buildings[0].Position = new Vector3(float.NaN, 0, 0); Assert.That(world.ValidateSnapshot(roundTrip, Catalog, options, out _, out _), Is.False);
        }
        [Test] public void ReplacementReleasesOldSceneSocketAndRejectsMalformedConnections()
        {
            var sceneSocket = Go("Scene socket").AddComponent<BuildingSocket>(); sceneSocket.transform.position = new Vector3(81000, 0, 0);
            definition.Placement = PlacementMode.Socket;
            Assert.That(world.TryPlace(definition, new PlacementRequest(sceneSocket.transform.position, Quaternion.identity, sceneSocket), out var placed, out var result), Is.True, result.Reason);
            var saved = world.CaptureState(); Assert.That(world.ValidateSnapshot(saved, Catalog, out var reason), Is.True, reason);
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, out reason), Is.True, reason); Assert.That(sceneSocket.IsOccupied, Is.True);
            Assert.That(world.TryGet(saved.Buildings[0].InstanceId, out var replacement), Is.True); Assert.That(sceneSocket.Occupant, Is.SameAs(replacement));
            saved.Connections.Add(new BuildingConnectionRecord { Id = "duplicate", SourceId = saved.Buildings[0].InstanceId, TargetSocketId = sceneSocket.Id });
            Assert.That(world.ValidateSnapshot(saved, Catalog, out _), Is.False); Assert.That(world.TryReplaceSnapshot(saved, Catalog, out _), Is.False); Assert.That(sceneSocket.Occupant, Is.SameAs(replacement));
        }
        [Test] public void PostCommitCallbackFailureIsExplicitRatherThanClaimingRollback()
        {
            Place(); var saved = world.CaptureState(); saved.Buildings[0].Parts[0].Json = "77";
            world.Restored += () => throw new InvalidOperationException("observer failed"); bool laterObserver = false; world.Restored += () => laterObserver = true;
            Assert.That(world.TryReplaceSnapshot(saved, Catalog, null, out var report, out var reason), Is.False);
            Assert.That(report.Committed, Is.True); Assert.That(reason, Does.StartWith("Snapshot committed"));
            Assert.That(laterObserver, Is.True, "One failed observer must not skip other restore subscribers.");
            Assert.That(world.TryGet(saved.Buildings[0].InstanceId, out var item), Is.True); Assert.That(item.GetComponent<SnapshotTestPart>().Value, Is.EqualTo(77));
        }
        [Test] public void DestructiveOptionsAndAmbiguousClaimHistoryAreRejectedBeforeMutation()
        {
            var original = Place(); var saved = world.CaptureState();
            Assert.That(world.ValidateSnapshot(saved, Catalog, new BuildingRestoreOptions { SkipUnknownDefinitions = true }, out _, out _), Is.False);
            saved.RedeemedParcels.Add("same"); saved.RedeemedParcels.Add("same"); Assert.That(world.TryReplaceSnapshot(saved, Catalog, out _), Is.False);
            Assert.That(world.TryGet(original.InstanceId, out var same), Is.True); Assert.That(same, Is.SameAs(original));
            world.SetReadOnly(true); Assert.That(world.ValidateSnapshot(world.CaptureState(), Catalog, out _), Is.False);
        }
        [Test] public void ReplicationProjectionAndOmittedPrivatePartsCannotBecomeLocalSaves()
        {
            var original = Place(); var projection = world.CaptureState(true); Assert.That(projection.ReplicationOnly, Is.True);
            Assert.That(world.ValidateSnapshot(projection, Catalog, out _), Is.False); Assert.That(world.TryReplaceSnapshot(projection, Catalog, out _), Is.False);
            var incomplete = world.CaptureState(); incomplete.Buildings[0].Parts = Array.Empty<BuildingPartState>();
            Assert.That(world.ValidateSnapshot(incomplete, Catalog, out _), Is.False);
            Assert.That(world.TryGet(original.InstanceId, out var same), Is.True); Assert.That(same, Is.SameAs(original));
        }
    }
}
