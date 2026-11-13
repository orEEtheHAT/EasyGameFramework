using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests
{
    public sealed class OperationTests
    {
        private readonly List<Object> objects = new List<Object>();
        private BuildingSystem world;
        private BuildingDefinition definition;
        private GameObject prefab;
        private readonly Vector3 origin = new Vector3(30000, 0, 30000);
        private PlacementRequest At(float x = 0, float z = 0, BuildingSpace space = null) => new PlacementRequest(origin + new Vector3(x, 0, z), Quaternion.identity, space: space);
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private T Asset<T>() where T : ScriptableObject { var asset = ScriptableObject.CreateInstance<T>(); objects.Add(asset); return asset; }
        private BuildingDefinition Definition()
        {
            var value = Asset<BuildingDefinition>(); value.Prefab = prefab; value.RequireSurface = false; value.ObstacleLayers = 1 << 31;
            return value;
        }
        [SetUp] public void SetUp()
        {
            world = Go("World").AddComponent<BuildingSystem>();
            prefab = Go("Prefab"); prefab.SetActive(false); prefab.layer = 31;
            prefab.AddComponent<BoxCollider>().center = new Vector3(0, 0.5f, 0);
            var top = new GameObject("Top"); top.transform.SetParent(prefab.transform, false); top.transform.localPosition = Vector3.up; top.AddComponent<BuildingSocket>();
            definition = Definition();
        }
        [TearDown] public void TearDown()
        {
            if (world != null) Object.DestroyImmediate(world.gameObject);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Physics.SyncTransforms();
        }
        private PlacedBuilding Place(BuildingDefinition value, PlacementRequest request, IBuildingActor actor = null)
        {
            Assert.That(world.TryPlace(value, request, out var building, out var result, actor), Is.True, result.Reason); return building;
        }
        private Ledger Paid()
        { definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost("wood", 3) }; var ledger = new Ledger(); world.Economy = ledger; return ledger; }

        [Test] public void RegionInterestUnloadsWithoutDemolitionAndRestoresIdsAndReceipts()
        {
            var ledger = Paid(); definition.DemolitionRefund = 1; var near = Place(definition, At()); var far = Place(definition, At(100));
            string nearId = near.InstanceId, farId = far.InstanceId; int removed = 0; world.Removed += _ => removed++;
            using var regions = new BuildingRegions(world, new[] { definition }, 10);
            regions.SetInterest(origin, 0);
            Assert.That(world.Count, Is.EqualTo(1)); Assert.That(world.IsUnloaded(farId), Is.True); Assert.That(regions.ArchivedCount, Is.EqualTo(1));
            Assert.That(removed, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(94)); Assert.That(regions.CaptureAll().Buildings.Count, Is.EqualTo(2));
            Assert.That(world.Check(definition, At(100)).Failure, Is.EqualTo(PlacementFailure.RuleRejected));
            regions.SetInterest(origin + Vector3.right * 100, 0);
            while (regions.IsLoading) regions.Tick(1, 1000, 1);
            Assert.That(regions.Failure, Is.Null); Assert.That(world.TryGet(farId, out var restored), Is.True); Assert.That(world.TryGet(nearId, out _), Is.False);
            Assert.That(world.IsUnloaded(farId), Is.False); Assert.That(ledger.Wood, Is.EqualTo(94));
            Assert.That(world.TryDemolish(restored, out var result), Is.True, result.Reason); Assert.That(ledger.Wood, Is.EqualTo(97));
        }
        [Test] public void UnloadRejectsSplittingConnectionsAndDeferredResolutionPublishesRevision()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            string parentId = parent.InstanceId, childId = child.InstanceId;
            Assert.That(world.TryUnload(new[] { parentId }, out _, out _), Is.False); Assert.That(world.Count, Is.EqualTo(2));
            Assert.That(world.TryUnload(new[] { parentId, childId }, out var saved, out var reason), Is.True, reason);
            var childState = new BuildingState(); var parentState = new BuildingState();
            foreach (var record in saved.Buildings) (record.InstanceId == childId ? childState : parentState).Buildings.Add(record);
            childState.Connections.AddRange(saved.Connections);
            Assert.That(world.TryRestore(childState, new[] { definition, childDefinition }, new BuildingRestoreOptions { DeferMissingConnections = true }, out reason), Is.True, reason);
            Assert.That(world.PendingConnectionCount, Is.EqualTo(1)); long revision = world.Revision; BuildingChangeSet connection = null;
            world.Changed += change => { if (change.Connections.Count != 0) connection = change; };
            Assert.That(world.TryRestore(parentState, new[] { definition, childDefinition }, out reason), Is.True, reason);
            Assert.That(world.PendingConnectionCount, Is.Zero); Assert.That(world.Connections.Count, Is.EqualTo(1));
            Assert.That(connection, Is.Not.Null); Assert.That(connection.Revision, Is.GreaterThan(revision)); Assert.That(connection.Revision, Is.EqualTo(world.Revision));
        }
        [Test] public void WorkQueueRetainsUnloadedConstruction()
        {
            definition.WorkRequired = 5; var site = Place(definition, At()); string id = site.InstanceId;
            var queue = Go("Work queue").AddComponent<BuildingWorkQueue>(); queue.System = world; queue.Enqueue(site);
            Assert.That(world.TryUnload(new[] { id }, out var snapshot, out var reason), Is.True, reason);
            Assert.That(queue.TryFindWork(origin, 5, null, out _), Is.False); Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(world.TryRestore(snapshot, new[] { definition }, out reason), Is.True, reason);
            Assert.That(queue.TryFindWork(origin, 5, null, out var restored), Is.True); Assert.That(restored.InstanceId, Is.EqualTo(id));
        }
        [Test] public void RegionInterestCombinesSeveralPlayersAndPreservesArchivedClearances()
        {
            var a = Place(definition, At()); var b = Place(definition, At(100));
            var reserved = Definition(); reserved.Clearances = new[] { new BuildingVolume(Vector3.right * 50, Vector3.one) };
            Place(reserved, At(200));
            using var regions = new BuildingRegions(world, new[] { definition, reserved }, 10);
            regions.SetInterest(new[] { new BuildingInterest(origin, 0), new BuildingInterest(origin + Vector3.right * 100, 0) });
            Assert.That(world.Count, Is.EqualTo(2)); Assert.That(regions.ArchivedCount, Is.EqualTo(1));
            Assert.That(world.TryGet(a.InstanceId, out _), Is.True); Assert.That(world.TryGet(b.InstanceId, out _), Is.True);
            Assert.That(world.Check(definition, At(250)).Failure, Is.EqualTo(PlacementFailure.RuleRejected));
            Assert.That(regions.CaptureAll().Buildings.Count, Is.EqualTo(3));
        }
        [Test] public void RegionCatalogCanBeRefreshedAfterModRegistration()
        {
            var building = Place(definition, At()); string id = building.InstanceId;
            Assert.That(world.TryUnload(new[] { id }, out var saved, out var reason), Is.True, reason);
            using var regions = new BuildingRegions(world, Array.Empty<BuildingDefinition>(), 10);
            regions.Add(saved); Assert.That(world.Check(definition, At(100)).Failure, Is.EqualTo(PlacementFailure.RuleRejected));
            regions.RefreshCatalog(new[] { definition }); Assert.That(world.Check(definition, At(100)).IsValid, Is.True);
            regions.SetInterest(origin, 0); while (regions.IsLoading) regions.Tick(1, 1000, 1);
            Assert.That(regions.Failure, Is.Null); Assert.That(world.TryGet(id, out _), Is.True);
        }
        [Test] public void SessionsHaveIndependentSelectionAndLifetime()
        {
            var other = Definition(); using var a = world.CreateSession(); using var b = world.CreateSession();
            a.Select(definition); b.Select(other); a.Cancel();
            Assert.That(a.Selected, Is.Null); Assert.That(b.Selected, Is.SameAs(other));
            Assert.That(b.TryConfirm(At(), out _, out var result), Is.True, result.Reason);
            Assert.That(b.TryConfirm(At(5), out _, out _), Is.False); Assert.That(world.Count, Is.EqualTo(1));
        }
        [TestCase(BuildingSurface.Wall)]
        [TestCase(BuildingSurface.Ceiling)]
        public void SurfaceAlignmentAndSupportProbesHandleWallAndCeiling(BuildingSurface surface)
        {
            Vector3 normal = surface == BuildingSurface.Wall ? Vector3.right : Vector3.down;
            var ground = Go("Surface"); ground.layer = 31; ground.transform.position = origin - normal * 0.5f;
            ground.AddComponent<BoxCollider>().size = surface == BuildingSurface.Wall ? new Vector3(1, 10, 10) : new Vector3(10, 1, 10);
            definition.RequireSurface = true; definition.SurfaceLayers = 1 << 31; definition.Surfaces = surface; definition.AlignToSurface = true;
            definition.SupportPoints = new[] { Vector3.forward * 0.4f, Vector3.back * 0.4f };
            var request = new PlacementRequest(origin, Quaternion.identity, surfaceNormal: normal);
            var result = world.Check(definition, request); Assert.That(result.IsValid, Is.True, result.Reason);
            Assert.That(Vector3.Dot(result.Pose.rotation * Vector3.up, normal), Is.GreaterThan(0.9999f));
            definition.Surfaces = BuildingSurface.Floor; Assert.That(world.Check(definition, request).Failure, Is.EqualTo(PlacementFailure.Slope));
        }
        [Test] public void SphericalGridAndOriginShiftPreserveLocalAndAbsolutePosition()
        {
            var space = Go("Planet").AddComponent<BuildingSpace>(); space.System = world; space.transform.position = origin;
            space.Gravity = BuildingGravity.Radial; space.SphereRadius = 10; space.SphereStepDegrees = 15;
            definition.Placement = PlacementMode.Grid; definition.AlignToSurface = true;
            var request = new PlacementRequest(origin + new Vector3(8, 4, 2), Quaternion.identity, space: space);
            var result = world.Check(definition, request); Assert.That(result.IsValid, Is.True, result.Reason);
            Assert.That(Vector3.Distance(result.Pose.position, origin), Is.EqualTo(10).Within(0.005f));
            Assert.That(Vector3.Dot(result.Pose.rotation * Vector3.up, space.UpAt(result.Pose.position)), Is.GreaterThan(0.999f));
            var building = Place(definition, request); var local = building.transform.localPosition; var absolute = space.OriginOffset.Add(building.transform.position);
            space.ShiftOrigin(new Vector3(20000, 0, 20000));
            Assert.That(building.transform.localPosition, Is.EqualTo(local)); var shifted = space.OriginOffset.Add(building.transform.position);
            Assert.That(shifted.X, Is.EqualTo(absolute.X).Within(0.005)); Assert.That(shifted.Z, Is.EqualTo(absolute.Z).Within(0.005));
        }
        [Test] public void MoveIgnoresOwnCollidersAndPreservesObjectAndId()
        {
            var building = Place(definition, At()); string id = building.InstanceId;
            Assert.That(world.TryMove(building, At(0.2f), out var result), Is.True, result.Reason);
            Assert.That(world.TryGet(id, out var same), Is.True); Assert.That(same, Is.SameAs(building));
            Assert.That(building.transform.position, Is.EqualTo(At(0.2f).Position));
        }
        [Test] public void FailedMoveLeavesPoseAndConnectionsUntouched()
        {
            var first = Place(definition, At()); Place(definition, At(3));
            Assert.That(world.TryMove(first, At(3), out _), Is.False); Assert.That(first.transform.position, Is.EqualTo(origin)); Assert.That(world.Count, Is.EqualTo(2));
        }
        [Test] public void BatchCanSwapExistingBuildings()
        {
            var a = Place(definition, At()); var b = Place(definition, At(3));
            Assert.That(world.BeginBatch().Move(a, At(3)).Move(b, At()).TryCommit(out var result), Is.True, result.Validation.Reason);
            Assert.That(a.transform.position, Is.EqualTo(At(3).Position)); Assert.That(b.transform.position, Is.EqualTo(origin));
        }
        [Test] public void PaidBatchCommitsOnceAndCannotBeRepeated()
        {
            var ledger = Paid(); var batch = world.BeginBatch().Place(definition, At()).Place(definition, At(3));
            Assert.That(batch.TryCommit(out var result), Is.True, result.Validation.Reason);
            Assert.That(ledger.Wood, Is.EqualTo(94)); Assert.That(ledger.Commits, Is.EqualTo(1)); Assert.That(world.Count, Is.EqualTo(2));
            Assert.That(batch.TryCommit(out _), Is.False); Assert.That(ledger.Wood, Is.EqualTo(94));
        }
        [Test] public void OverlappingBatchDoesNotReserveOrPublishAnything()
        {
            var ledger = Paid();
            Assert.That(world.BeginBatch().Place(definition, At()).Place(definition, At(0.5f)).TryCommit(out _), Is.False);
            Assert.That(ledger.Reserves, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(100)); Assert.That(world.Count, Is.Zero);
        }
        [Test] public void FailedBatchCommitLeavesAllWorldAndFundsUnchanged()
        {
            var ledger = Paid(); ledger.Fail = true;
            Assert.That(world.BeginBatch().Place(definition, At()).Place(definition, At(3)).TryCommit(out _), Is.False);
            Assert.That(world.Count, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(100)); Assert.That(ledger.Reserved, Is.Zero);
        }
        [Test] public void DemolitionRefundUsesPaidReceiptAndRemoveDoesNotRefund()
        {
            var ledger = Paid(); definition.DemolitionRefund = 1;
            var a = Place(definition, At()); definition.Costs[0] = new BuildingCost("wood", 50);
            Assert.That(world.TryDemolish(a, out var result), Is.True, result.Reason); Assert.That(ledger.Wood, Is.EqualTo(100));
            var b = Place(definition, At()); Assert.That(world.Remove(b), Is.True); Assert.That(ledger.Wood, Is.EqualTo(50));
        }
        [Test] public void UpgradePreservesIdAndExistingChildConnection()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>();
            var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var upgradedDefinition = Definition(); definition.Upgrades = new[] { upgradedDefinition }; string id = parent.InstanceId;
            Assert.That(world.TryUpgrade(parent, upgradedDefinition, out var replacement, out var result), Is.True, result.Reason);
            Assert.That(replacement.InstanceId, Is.EqualTo(id)); Assert.That(replacement.Definition, Is.SameAs(upgradedDefinition));
            Assert.That(child.AttachedSocket.Building, Is.SameAs(replacement)); Assert.That(world.Count, Is.EqualTo(2));
        }
        [Test] public void IncompatibleUpgradeFailsBeforeSpending()
        {
            var ledger = Paid(); var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var wrongPrefab = Object.Instantiate(prefab); objects.Add(wrongPrefab); wrongPrefab.GetComponentInChildren<BuildingSocket>(true).RegenerateId();
            var target = Definition(); target.Prefab = wrongPrefab; target.Payment = BuildingPayment.Paid; target.Costs = new[] { new BuildingCost("wood", 5) }; definition.Upgrades = new[] { target };
            Assert.That(world.TryUpgrade(parent, target, out _, out _), Is.False);
            Assert.That(ledger.Wood, Is.EqualTo(97)); Assert.That(ledger.Reserved, Is.Zero); Assert.That(world.Count, Is.EqualTo(2));
        }
        [Test] public void OwnershipChecksMoveUpgradeAndDemolition()
        {
            var owner = Go("Owner").AddComponent<BuildingActor>(); owner.SetIdentity("owner", "blue");
            var stranger = Go("Stranger").AddComponent<BuildingActor>(); stranger.SetIdentity("stranger", "red");
            definition.Access = BuildingAccess.Owner; var building = Place(definition, At(), owner);
            Assert.That(world.TryMove(building, At(3), out var denied, stranger), Is.False); Assert.That(denied.Failure, Is.EqualTo(PlacementFailure.PermissionDenied));
            Assert.That(world.TryDemolish(building, out _, stranger), Is.False);
            Assert.That(world.TryMove(building, At(3), out _, owner), Is.True);
        }
        [Test] public void BatchLimitCountsAllPlannedObjects()
        {
            var limit = Asset<BuildingLimitRule>(); limit.Maximum = 1; definition.Rules = new BuildingRule[] { limit };
            Assert.That(world.BeginBatch().Place(definition, At()).Place(definition, At(3)).TryCommit(out var result), Is.False);
            Assert.That(result.Validation.Failure, Is.EqualTo(PlacementFailure.RuleRejected)); Assert.That(world.Count, Is.Zero);
            Place(definition, At()); Assert.That(world.Check(definition, At(3)).IsValid, Is.False);
        }
        [Test] public void CapabilityAndGroupRulesAreComposableAndCyclesFailCleanly()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>(); actor.SetIdentity("builder");
            var capability = Asset<BuildingCapabilityRule>(); capability.Required = new[] { "steel" };
            var group = Asset<BuildingRuleGroup>(); group.Rules = new BuildingRule[] { capability }; definition.Rules = new BuildingRule[] { group };
            Assert.That(world.Check(definition, At(), actor).IsValid, Is.False); actor.Grant("steel"); Assert.That(world.Check(definition, At(), actor).IsValid, Is.True);
            group.Rules = new BuildingRule[] { group }; Assert.That(world.Check(definition, At(), actor).IsValid, Is.False);
        }
        [Test] public void CompoundFootprintLeavesItsEmptyCornerUsable()
        {
            Object.DestroyImmediate(prefab.GetComponent<BoxCollider>());
            definition.Volumes = new[] { new BuildingVolume(new Vector3(0, 0.5f, 0), new Vector3(1, 1, 3)), new BuildingVolume(new Vector3(1, 0.5f, -1), Vector3.one) };
            Place(definition, At()); var small = Definition();
            Assert.That(world.Check(small, At(1, 1)).IsValid, Is.True);
            Assert.That(world.Check(small, At(1, -1)).IsValid, Is.False);
        }
        [Test] public void FootprintBlocksEvenWhenPrefabHasNoCollider()
        {
            Object.DestroyImmediate(prefab.GetComponent<BoxCollider>()); Place(definition, At());
            Assert.That(world.Check(definition, At()).Failure, Is.EqualTo(PlacementFailure.Obstructed));
        }
        [Test] public void ClearancesCanOverlapEachOtherButNotSolidVolumes()
        {
            definition.Clearances = new[] { new BuildingVolume(new Vector3(1, 0.5f, 0), Vector3.one) };
            var other = Definition(); other.Clearances = new[] { new BuildingVolume(new Vector3(-1, 0.5f, 0), Vector3.one) };
            Assert.That(world.BeginBatch().Place(definition, At()).Place(other, At(2)).TryCommit(out var result), Is.True, result.Validation.Reason);
            Assert.That(world.Check(Definition(), At(1)).IsValid, Is.False);
        }
        [Test] public void LocalGridAndRestoreFollowMovingSpace()
        {
            var space = Go("Ship").AddComponent<BuildingSpace>(); space.System = world; space.transform.position = origin; space.transform.rotation = Quaternion.Euler(0, 35, 0);
            definition.Placement = PlacementMode.Grid;
            var request = new PlacementRequest(space.transform.TransformPoint(new Vector3(1.2f, 0, 0.2f)), space.transform.rotation, space: space);
            var building = Place(definition, request); var local = space.transform.InverseTransformPoint(building.transform.position);
            Assert.That(Vector3.Distance(local, new Vector3(1, 0, 0)), Is.LessThan(0.01));
            var state = world.CaptureState(); string id = building.InstanceId; world.Remove(building);
            space.transform.position += new Vector3(50, 2, -10);
            Assert.That(world.TryRestore(state, new[] { definition }, out var reason), Is.True, reason);
            world.TryGet(id, out var restored);
            Assert.That(Vector3.Distance(restored.transform.position, space.transform.TransformPoint(local)), Is.LessThan(0.01));
        }
        [Test] public void HexAndIsometricSnappingIsIdempotent()
        {
            foreach (BuildingGridLayout layout in Enum.GetValues(typeof(BuildingGridLayout)))
                for (int i = 0; i < 100; i++)
                {
                    var point = new Vector3(i * 0.23f - 10, 2, i * -0.71f + 4);
                    var first = BuildingGeometry.Snap(point, 1, Vector3.zero, layout, BuildingGridPlane.XZ);
                    var second = BuildingGeometry.Snap(first, 1, Vector3.zero, layout, BuildingGridPlane.XZ);
                    Assert.That(Vector3.Distance(first, second), Is.LessThan(0.0001f)); Assert.That(first.y, Is.EqualTo(2));
                }
        }
        [Test] public void OrientedBoxIntersectionAgreesWithPhysicsAwayFromContactTolerance()
        {
            var obstacle = Go("OBB"); obstacle.layer = 31; var collider = obstacle.AddComponent<BoxCollider>();
            var random = new System.Random(41);
            for (int i = 0; i < 100; i++)
            {
                var a = new BuildingBox(origin, Vector3.one, Quaternion.Euler(0, i * 11, 0));
                var b = new BuildingBox(origin + new Vector3((float)random.NextDouble() * 8 - 4, 0, (float)random.NextDouble() * 8 - 4), Vector3.one * 0.6f, Quaternion.Euler(0, i * 17, 0));
                obstacle.transform.SetPositionAndRotation(b.Center, b.Rotation); collider.size = b.HalfSize * 2; Physics.SyncTransforms();
                bool native = Physics.CheckBox(a.Center, a.HalfSize, a.Rotation, 1 << 31, QueryTriggerInteraction.Ignore);
                Assert.That(BuildingGeometry.Intersects(a, b, 0), Is.EqualTo(native));
            }
        }
        [Test] public void ReentrantObserversReceiveChangesInCommitOrder()
        {
            var revisions = new List<long>(); bool removed = false;
            world.Changed += changes =>
            {
                if (removed || changes.Upserts.Count == 0) return;
                removed = true; world.TryGet(changes.Upserts[0].InstanceId, out var building); world.Remove(building);
            };
            world.Changed += changes => revisions.Add(changes.Revision);
            Place(definition, At());
            CollectionAssert.AreEqual(new long[] { 1, 2 }, revisions); Assert.That(world.Count, Is.Zero);
        }
        [Test] public void DeferredConnectionResolvesWhenItsOwnerLoadsLater()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var state = world.CaptureState(); var parentRecord = state.Buildings.Find(x => x.InstanceId == parent.InstanceId); var childRecord = state.Buildings.Find(x => x.InstanceId == child.InstanceId);
            string childId = child.InstanceId; world.Remove(child); world.Remove(parent);
            var first = new BuildingState(); first.Buildings.Add(childRecord); first.Connections.AddRange(state.Connections);
            Assert.That(world.TryRestore(first, new[] { definition, childDefinition }, new BuildingRestoreOptions { DeferMissingConnections = true }, out var reason), Is.True, reason);
            Assert.That(world.PendingConnectionCount, Is.EqualTo(1));
            var second = new BuildingState(); second.Buildings.Add(parentRecord);
            Assert.That(world.TryRestore(second, new[] { definition }, out reason), Is.True, reason);
            world.TryGet(childId, out var restored); Assert.That(restored.AttachedSocket, Is.Not.Null); Assert.That(world.PendingConnectionCount, Is.Zero);
        }
        [Test] public void BlueprintRemapsBothEndsAndCanBePlacedRepeatedly()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var blueprint = world.CaptureBlueprint(new[] { child, parent }, new Pose(origin, Quaternion.identity));
            Assert.That(world.TryPlaceBlueprint(blueprint, new Pose(origin + Vector3.right * 10, Quaternion.Euler(0, 90, 0)), new[] { definition, childDefinition }, out var first), Is.True, first.Validation.Reason);
            Assert.That(first.Buildings[0].AttachedSocket.Building, Is.SameAs(first.Buildings[1]));
            Assert.That(first.Buildings[0].InstanceId, Is.Not.EqualTo(child.InstanceId));
            Assert.That(world.TryPlaceBlueprint(blueprint, new Pose(origin + Vector3.right * 20, Quaternion.identity), new[] { definition, childDefinition }, out var second), Is.True, second.Validation.Reason);
            Assert.That(second.Buildings[0].InstanceId, Is.Not.EqualTo(first.Buildings[0].InstanceId));
            Assert.That(world.Connections.Count, Is.EqualTo(3));
        }
        [Test] public void InvalidBlueprintConnectionNeverCommitsItsGroupOrPayment()
        {
            var ledger = Paid(); var parent = Place(definition, At());
            var blueprint = world.CaptureBlueprint(new[] { parent }, new Pose(origin, Quaternion.identity));
            blueprint.Connections.Add(new BuildingConnectionRecord { Id = "bad", SourceId = parent.InstanceId, TargetId = "missing", TargetSocketId = "missing" });
            Assert.That(world.TryPlaceBlueprint(blueprint, new Pose(origin + Vector3.right * 5, Quaternion.identity), new[] { definition }, out _), Is.False);
            Assert.That(world.Count, Is.EqualTo(1)); Assert.That(ledger.Wood, Is.EqualTo(97)); Assert.That(ledger.Commits, Is.EqualTo(1));
        }
        [Test] public void ChangeSetContainsOnlyAffectedConnections()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            BuildingChangeSet last = null; world.Changed += change => last = change;
            Place(definition, At(10)); Assert.That(last.Connections, Is.Empty);
            world.Remove(child); Assert.That(last.RemovedConnections.Count, Is.EqualTo(1)); Assert.That(last.Connections, Is.Empty);
        }
        [Test] public void PatternBudgetsAndPathJointsAreDeterministic()
        {
            var output = new List<BuildingPlacement>();
            BuildingPatterns.Path(definition, new[] { Vector3.zero, Vector3.right * 2, Vector3.right * 2 + Vector3.forward * 2 }, 1, output);
            Assert.That(output.Count, Is.EqualTo(5)); Assert.That(output[2].Request.Position, Is.EqualTo(Vector3.right * 2));
            BuildingPatterns.Rectangle(definition, new Pose(origin, Quaternion.identity), 3, 3, Vector2.one, output, true);
            Assert.That(output.Count, Is.EqualTo(8));
            BuildingPatterns.Brush(definition, new Pose(origin, Quaternion.identity), 1, 1, output); Assert.That(output.Count, Is.EqualTo(5));
            Assert.Throws<ArgumentException>(() => BuildingPatterns.Line(definition, Vector3.zero, Vector3.right * 10000, 1, output, maximum: 10));
            Assert.That(output.Count, Is.EqualTo(5));
        }
        [Test] public void LargeIntegerCostsRemainExact()
        {
            var economy = new ExactEconomy(); world.Economy = economy;
            definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost("wood", 9007199254740993L) };
            Place(definition, At()); Assert.That(economy.Amount, Is.EqualTo(9007199254740993L));
        }
        [Test] public void ConstructionDeliversOncePausesAndCompletesWithSameId()
        {
            var ledger = Paid(); definition.WorkRequired = 2; definition.ConstructionPayment = BuildingConstructionPayment.Delivery;
            var site = Place(definition, At()); string id = site.InstanceId;
            Assert.That(site.IsComplete, Is.False); Assert.That(ledger.Wood, Is.EqualTo(100));
            Assert.That(world.TryWork(site, 1, out _, out _), Is.False);
            Assert.That(world.TryDeliver(site, new[] { new BuildingCost("wood", 1) }, out var result), Is.True, result.Reason);
            Assert.That(world.TryDeliver(site, new[] { new BuildingCost("wood", 3) }, out _), Is.False);
            Assert.That(world.TryDeliver(site, new[] { new BuildingCost("wood", 2) }, out result), Is.True, result.Reason);
            Assert.That(world.TryPauseConstruction(site, true, out result), Is.True, result.Reason);
            Assert.That(world.TryWork(site, 1, out _, out _), Is.False);
            Assert.That(world.TryPauseConstruction(site, false, out _), Is.True);
            Assert.That(world.TryWork(site, 1, out var working, out result), Is.True, result.Reason); Assert.That(working, Is.SameAs(site));
            Assert.That(world.TryWork(site, 1, out var completed, out result), Is.True, result.Reason);
            Assert.That(completed.IsComplete, Is.True); Assert.That(completed.InstanceId, Is.EqualTo(id)); Assert.That(world.Count, Is.EqualTo(1));
            Assert.That(ledger.Wood, Is.EqualTo(97)); Assert.That(ledger.Reserved, Is.Zero);
        }
        [Test] public void ConstructionFailureAndCancellationUseOnlyDeliveredReceipt()
        {
            var ledger = Paid(); definition.WorkRequired = 2; definition.ConstructionPayment = BuildingConstructionPayment.Delivery;
            var site = Place(definition, At()); ledger.Fail = true;
            Assert.That(world.TryDeliver(site, new[] { new BuildingCost("wood", 1) }, out _), Is.False);
            Assert.That(site.Construction.Remaining("wood"), Is.EqualTo(3)); Assert.That(ledger.Wood, Is.EqualTo(100)); Assert.That(ledger.Reserved, Is.Zero);
            ledger.Fail = false; Assert.That(world.TryDeliver(site, new[] { new BuildingCost("wood", 1) }, out _), Is.True);
            definition.Costs[0] = new BuildingCost("wood", 100);
            Assert.That(world.TryCancelConstruction(site, out var result), Is.True, result.Reason);
            Assert.That(world.Count, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(100));
        }
        [Test] public void ConstructionRestoreKeepsWorkMaterialsAndQueueUsesStableId()
        {
            var ledger = Paid(); definition.WorkRequired = 2;
            var site = Place(definition, At()); string id = site.InstanceId;
            Assert.That(world.TryWork(site, 0.5f, out _, out _), Is.True);
            var snapshot = JsonUtility.FromJson<BuildingState>(JsonUtility.ToJson(world.CaptureState())); world.Remove(site);
            Assert.That(world.TryRestore(snapshot, new[] { definition }, out var reason), Is.True, reason); world.TryGet(id, out site);
            Assert.That(site.Construction.Work, Is.EqualTo(0.5f)); Assert.That(site.Construction.MaterialsReady, Is.True); Assert.That(ledger.Wood, Is.EqualTo(97));
            var queue = Go("Queue").AddComponent<BuildingWorkQueue>(); queue.System = world; queue.Enqueue(site);
            var worker = Go("Worker").AddComponent<BuildingWorker>(); worker.transform.position = origin; worker.Queue = queue;
            Assert.That(worker.Tick(2), Is.True); Assert.That(queue.Count, Is.Zero); Assert.That(world.TryGet(id, out var complete) && complete.IsComplete, Is.True);
        }
        [Test] public void HistoryUndoRedoRestoresPaidGroupAndStableIds()
        {
            var ledger = Paid(); using var history = new BuildingHistory(world, new[] { definition });
            Assert.That(world.BeginBatch().Place(definition, At()).Place(definition, At(3)).TryCommit(out var placed), Is.True);
            string id = placed.Buildings[0].InstanceId; Assert.That(ledger.Wood, Is.EqualTo(94));
            Assert.That(history.TryUndo(out var reason), Is.True, reason); Assert.That(world.Count, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(100));
            Assert.That(history.TryRedo(out reason), Is.True, reason); Assert.That(world.Count, Is.EqualTo(2)); Assert.That(ledger.Wood, Is.EqualTo(94));
            Assert.That(world.TryGet(id, out _), Is.True);
            Assert.That(history.TryUndo(out reason), Is.True, reason); Assert.That(ledger.Wood, Is.EqualTo(100));
        }
        [Test] public void HistoryUndoMoveAndDemolitionRestoresLinks()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            string parentId = parent.InstanceId, childId = child.InstanceId;
            using var history = new BuildingHistory(world, new[] { definition, childDefinition });
            Assert.That(world.TryRemove(child, out _), Is.True);
            Assert.That(history.TryUndo(out var reason), Is.True, reason);
            world.TryGet(childId, out child); world.TryGet(parentId, out parent); Assert.That(child.AttachedSocket.Building, Is.SameAs(parent));
            Assert.That(history.TryRedo(out reason), Is.True, reason); Assert.That(world.TryGet(childId, out _), Is.False);
        }
        [Test] public void FailedHistoryPaymentLeavesHistoryAndWorldUnchanged()
        {
            var ledger = Paid(); using var history = new BuildingHistory(world, new[] { definition });
            var building = Place(definition, At()); string id = building.InstanceId; ledger.Fail = true;
            Assert.That(history.TryUndo(out _), Is.False); Assert.That(history.UndoCount, Is.EqualTo(1)); Assert.That(world.TryGet(id, out var same), Is.True);
            Assert.That(same, Is.SameAs(building)); Assert.That(ledger.Wood, Is.EqualTo(97)); Assert.That(ledger.Reserved, Is.Zero);
        }
        [Test] public void PackedBuildingKeepsReceiptAndCannotBeRedeemedTwiceEvenAfterSave()
        {
            var ledger = Paid(); definition.DemolitionRefund = 1; var building = Place(definition, At()); string id = building.InstanceId;
            Assert.That(world.TryPack(new[] { building }, new Pose(origin, Quaternion.identity), out var parcel, out var result), Is.True, result.Reason);
            Assert.That(world.Count, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(97));
            bool attempted = false, replayed = false;
            world.Changed += change => { if (attempted || (change.Action & BuildingAction.Pack) == 0) return; attempted = true; replayed = world.TryUnpack(parcel, new Pose(origin + Vector3.right * 20, Quaternion.identity), new[] { definition }, out _); };
            Assert.That(world.TryUnpack(parcel, new Pose(origin + Vector3.right * 5, Quaternion.identity), new[] { definition }, out var unpacked), Is.True, unpacked.Validation.Reason);
            Assert.That(replayed, Is.False); Assert.That(unpacked.Buildings[0].InstanceId, Is.EqualTo(id)); Assert.That(ledger.Wood, Is.EqualTo(97));
            var state = world.CaptureState(); world.Remove(unpacked.Buildings[0]);
            Assert.That(world.TryRestore(state, new[] { definition }, out var reason), Is.True, reason);
            Assert.That(world.TryUnpack(parcel, new Pose(origin, Quaternion.identity), new[] { definition }, out _), Is.False);
            world.TryGet(id, out building); Assert.That(world.TryDemolish(building, out result), Is.True, result.Reason); Assert.That(ledger.Wood, Is.EqualTo(100));
        }
        [Test] public void ReplicaAppliesLateJoinAndDeltasExactlyOnceWithoutEconomy()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            string childId = child.InstanceId;
            var replica = Go("Replica").AddComponent<BuildingSystem>(); replica.SetReadOnly(true);
            var catalog = new[] { definition, childDefinition };
            Assert.That(replica.TryApplyReplicaSnapshot(world.CaptureState(true), catalog, out var reason), Is.True, reason);
            Assert.That(replica.Count, Is.EqualTo(2)); Assert.That(replica.Connections.Count, Is.EqualTo(1));
            BuildingChangeSet delta = null; world.Changed += change => delta = BuildingChanges.Copy(change, true);
            world.Remove(parent);
            Assert.That(replica.TryApplyReplicaDelta(delta, catalog, out reason), Is.True, reason);
            Assert.That(replica.Count, Is.EqualTo(1)); Assert.That(replica.Connections.Count, Is.Zero); Assert.That(replica.TryGet(childId, out _), Is.True);
            Assert.That(replica.TryApplyReplicaDelta(delta, catalog, out _), Is.False);
            Assert.That(replica.TryPlace(definition, At(100), out _, out _), Is.False);
        }
        [Test] public void InvalidReplicaPatchKeepsExistingObjectsAndRevision()
        {
            var replica = Go("Replica").AddComponent<BuildingSystem>(); replica.SetReadOnly(true);
            var original = Place(definition, At());
            Assert.That(replica.TryApplyReplicaSnapshot(world.CaptureState(true), new[] { definition }, out _), Is.True);
            replica.TryGet(original.InstanceId, out var copy); long revision = replica.Revision;
            var bad = new BuildingChangeSet { BeforeRevision = revision, Revision = revision + 1 };
            bad.Removed.Add(original.InstanceId); bad.Upserts.Add(new BuildingRecord { InstanceId = "bad", DefinitionId = definition.Id, Rotation = Quaternion.identity, Scale = Vector3.zero });
            Assert.That(replica.TryApplyReplicaDelta(bad, new[] { definition }, out _), Is.False);
            Assert.That(replica.Revision, Is.EqualTo(revision)); Assert.That(replica.TryGet(original.InstanceId, out var same), Is.True); Assert.That(same, Is.SameAs(copy));
        }
        [Test] public void StructuralLoadAndLossOfFoundationUseBudgetedCollapse()
        {
            prefab.AddComponent<BuildingStructuralMember>();
            var parent = Place(definition, At()); parent.GetComponent<BuildingStructuralMember>().Foundation = true;
            var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var structure = Go("Structure").AddComponent<BuildingStructure>(); structure.System = world; structure.CollapseDelay = 0;
            structure.Evaluate(); Assert.That(structure.TryGetSupport(parent, out var support), Is.True);
            Assert.That(support.Load, Is.EqualTo(2)); Assert.That(support.Status, Is.EqualTo(BuildingSupportStatus.Supported));
            Assert.That(child.GetComponent<BuildingStructuralMember>().SetExtraLoad(5, out _), Is.True);
            parent.GetComponent<BuildingStructuralMember>().Capacity = 3; structure.Evaluate(); structure.TryGetSupport(parent, out support);
            Assert.That(support.Status, Is.EqualTo(BuildingSupportStatus.Overloaded)); structure.TryGetSupport(child, out support);
            Assert.That(support.Status, Is.EqualTo(BuildingSupportStatus.Unsupported));
            Assert.That(structure.ProcessCollapses(float.MaxValue, 1), Is.EqualTo(1)); Assert.That(world.Count, Is.EqualTo(1));
        }
        [Test] public void UtilityPathsRespectDirectionCapacityAndClosedPorts()
        {
            var top = prefab.GetComponentInChildren<BuildingSocket>(true); top.gameObject.AddComponent<BuildingUtilityPort>().Direction = BuildingPortDirection.Output;
            var bottom = new GameObject("Bottom"); bottom.transform.SetParent(prefab.transform, false); bottom.AddComponent<BuildingSocket>();
            bottom.AddComponent<BuildingUtilityPort>().Direction = BuildingPortDirection.Input;
            var a = Place(definition, At()); var b = Place(definition, new PlacementRequest(origin + Vector3.up, Quaternion.identity));
            var output = a.transform.Find("Top").GetComponent<BuildingSocket>(); var input = b.transform.Find("Bottom").GetComponent<BuildingSocket>();
            Assert.That(world.TryConnect(output, input, out var reason), Is.True, reason);
            var network = new BuildingUtilityNetwork(world); var path = new List<BuildingUtilityStep>();
            Assert.That(network.TryFindPath(a, b, "Block", path), Is.True); Assert.That(path.Count, Is.EqualTo(1));
            Assert.That(network.TryFindPath(b, a, "Block", path), Is.False);
            Assert.That(network.TryFindPath(a, b, "Block", path, 2), Is.False);
            output.GetComponent<BuildingUtilityPort>().Open = false;
            Assert.That(network.TryFindPath(a, b, "Block", path), Is.False);
        }
        [Test] public void MissingModContentRoundTripsAndResolvesAfterRegistration()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            string childId = child.InstanceId; var snapshot = world.CaptureState(); world.Remove(child); world.Remove(parent);
            Assert.That(world.TryRestore(snapshot, new[] { definition }, new BuildingRestoreOptions { PreserveUnknownDefinitions = true }, out var reason), Is.True, reason);
            Assert.That(world.Count, Is.EqualTo(1)); Assert.That(world.MissingBuildingCount, Is.EqualTo(1));
            var savedAgain = world.CaptureState(); Assert.That(savedAgain.Buildings.Count, Is.EqualTo(2)); Assert.That(savedAgain.Connections.Count, Is.EqualTo(1));
            Assert.That(world.TryResolveMissingBuildings(new[] { definition, childDefinition }, out int restored, out reason), Is.True, reason);
            Assert.That(restored, Is.EqualTo(1)); Assert.That(world.MissingBuildingCount, Is.Zero);
            world.TryGet(childId, out child); Assert.That(child.AttachedSocket, Is.Not.Null);
        }
        [Test] public void MigrationFailureLeavesInputUntouchedAndRegistryRejectsWholeDuplicatePack()
        {
            var building = Place(definition, At()); var snapshot = world.CaptureState(); definition.SaveVersion = 3;
            var migrations = new BuildingMigrations(); migrations.Register(definition.Id, 1, 2, record => record.OwnerId = "migrated");
            Assert.That(migrations.TryMigrate(snapshot, new[] { definition }, out _, out _), Is.False);
            Assert.That(snapshot.Buildings[0].OwnerId, Is.Empty); Assert.That(snapshot.Buildings[0].DefinitionVersion, Is.EqualTo(1));
            migrations.Register(definition.Id, 2, 3, record => record.TeamId = "team");
            Assert.That(migrations.TryMigrate(snapshot, new[] { definition }, out var current, out var reason), Is.True, reason);
            Assert.That(current.Buildings[0].DefinitionVersion, Is.EqualTo(3)); Assert.That(current.Buildings[0].OwnerId, Is.EqualTo("migrated"));
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition }); var extra = Definition();
            Assert.Throws<ArgumentException>(() => registry.Register("bad", new[] { extra, definition }));
            Assert.That(registry.Definitions.Count, Is.EqualTo(1)); Assert.That(registry.TryGet(extra.Id, out _), Is.False);
        }
        [Test] public void BudgetedLoadingResolvesReverseLinksAndResumesAfterCancellation()
        {
            var parent = Place(definition, At()); var childDefinition = Definition(); childDefinition.Placement = PlacementMode.Socket;
            var socket = parent.GetComponentInChildren<BuildingSocket>(); var child = Place(childDefinition, new PlacementRequest(socket.transform.position, Quaternion.identity, socket));
            var snapshot = world.CaptureState(); snapshot.Buildings.Sort((a, b) => a.InstanceId == child.InstanceId ? -1 : 1);
            string childId = child.InstanceId; world.Remove(child); world.Remove(parent);
            var queue = new BuildingLoadQueue(world); var job = queue.Enqueue(snapshot, new[] { definition, childDefinition });
            Assert.That(queue.Tick(1, 1000, 1), Is.EqualTo(1)); Assert.That(job.ProcessedCount, Is.EqualTo(1)); Assert.That(world.PendingConnectionCount, Is.EqualTo(1));
            job.Cancel(); queue.Tick(); Assert.That(job.Cancelled, Is.True); Assert.That(world.Count, Is.EqualTo(1));
            var resumed = queue.Enqueue(job.CaptureRemaining(), new[] { definition, childDefinition }); queue.Tick(2, 1000, 1);
            Assert.That(resumed.IsDone, Is.True); Assert.That(resumed.Failure, Is.Null); Assert.That(world.Count, Is.EqualTo(2));
            world.TryGet(childId, out child); Assert.That(child.AttachedSocket, Is.Not.Null);
        }
        [Test] public void AuthorityReplaySpendsOnceRejectsChangedPayloadAndPersistsWatermark()
        {
            var ledger = Paid(); var actor = Go("Actor").AddComponent<BuildingActor>(); actor.transform.position = origin; actor.SetIdentity("player", "team");
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition });
            var authority = new BuildingAuthority(world, registry) { Clock = () => 0 }; var session = authority.OpenSession("authenticated-session", actor);
            var command = new BuildingCommand { Sequence = 1, Edits = new[] { new BuildingCommandEdit { DefinitionId = definition.Id, Position = origin } } };
            var first = authority.Execute(session, command); Assert.That(first.Success, Is.True, first.Reason);
            Assert.That(authority.Execute(session, command).Success, Is.True); Assert.That(world.Count, Is.EqualTo(1)); Assert.That(ledger.Wood, Is.EqualTo(97));
            command.Edits[0].Position += Vector3.right * 5; Assert.That(authority.Execute(session, command).Failure, Is.EqualTo(PlacementFailure.StaleRequest));
            var replay = authority.CaptureReplayState(); authority.Dispose();
            using var resumed = new BuildingAuthority(world, registry, replay); var restoredSession = resumed.OpenSession("authenticated-session", actor);
            Assert.That(resumed.Execute(restoredSession, command).Failure, Is.EqualTo(PlacementFailure.StaleRequest)); Assert.That(ledger.Wood, Is.EqualTo(97));
        }
        [Test] public void AuthorityValidatesTargetVersionsAndActualSocketRange()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>(); actor.transform.position = origin; actor.SetIdentity("player", "team");
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition });
            using var authority = new BuildingAuthority(world, registry) { Clock = () => 0 }; var session = authority.OpenSession("session", actor);
            var building = Place(definition, At());
            var command = new BuildingCommand { Sequence = 1, Edits = new[] { new BuildingCommandEdit { Kind = BuildingCommandEditKind.Move, InstanceId = building.InstanceId, ExpectedVersion = building.Version - 1, Position = origin + Vector3.right * 2 } } };
            Assert.That(authority.Execute(session, command).Failure, Is.EqualTo(PlacementFailure.StaleRequest)); Assert.That(building.transform.position, Is.EqualTo(origin));
            var remote = Go("Remote socket"); remote.transform.position = origin + Vector3.right * 100; var socket = remote.AddComponent<BuildingSocket>();
            command = new BuildingCommand { Sequence = 2, Edits = new[] { new BuildingCommandEdit { DefinitionId = definition.Id, Position = origin, SocketId = socket.Id } } };
            Assert.That(authority.Execute(session, command).Failure, Is.EqualTo(PlacementFailure.PermissionDenied)); Assert.That(world.Count, Is.EqualTo(1));
        }
        [Test] public void AuthorityJournalSignalsGapsAndWorkBudgetPreventsInstantConstruction()
        {
            definition.WorkRequired = 100; var actor = Go("Actor").AddComponent<BuildingActor>(); actor.transform.position = origin; actor.SetIdentity("player", "team");
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition });
            using var authority = new BuildingAuthority(world, registry) { Clock = () => 0, MaximumJournalEntries = 1 }; var session = authority.OpenSession("session", actor);
            var site = Place(definition, At());
            var command = new BuildingCommand { Sequence = 1, Kind = BuildingCommandKind.Work, InstanceId = site.InstanceId, ExpectedVersion = site.Version, Work = 99 };
            Assert.That(authority.Execute(session, command).Failure, Is.EqualTo(PlacementFailure.LimitExceeded)); Assert.That(site.Construction.Work, Is.Zero);
            command.Sequence = 2; command.Work = 1;
            Assert.That(authority.Execute(session, command).Success, Is.True);
            var changes = new List<BuildingChangeSet>(); Assert.That(authority.TryGetChangesSince(0, changes), Is.False);
            Assert.That(authority.TryGetChangesSince(world.Revision - 1, changes), Is.True); Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Previous, Is.Empty); Assert.That(changes[0].Payment.Charges, Is.Empty);
        }
        [Test] public void MaterialVariantIsAtomicPreservesObjectAndSurvivesRestore()
        {
            var ledger = Paid(); var firstMaterial = new Material(Shader.Find("EasyBuilding/Preview")); var secondMaterial = new Material(firstMaterial);
            objects.Add(firstMaterial); objects.Add(secondMaterial);
            var renderer = prefab.AddComponent<MeshRenderer>(); renderer.sharedMaterial = firstMaterial;
            var appearance = prefab.AddComponent<BuildingAppearance>();
            appearance.Variants = new[] { new BuildingMaterialVariant { Id = "stone", Costs = new[] { new BuildingCost("wood", 2) }, Materials = new[] { new BuildingMaterialBinding { Renderer = renderer, Materials = new[] { secondMaterial } } } } };
            var building = Place(definition, At()); string id = building.InstanceId;
            ledger.Fail = true; Assert.That(world.TrySetVariant(building, "stone", out _), Is.False);
            Assert.That(building.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(firstMaterial)); Assert.That(ledger.Wood, Is.EqualTo(97));
            ledger.Fail = false; Assert.That(world.TrySetVariant(building, "stone", out var result), Is.True, result.Reason);
            Assert.That(world.TryGet(id, out var same) && same == building, Is.True); Assert.That(ledger.Wood, Is.EqualTo(95));
            Assert.That(building.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(secondMaterial)); Assert.That(renderer.sharedMaterial, Is.SameAs(firstMaterial));
            var snapshot = world.CaptureState(); world.Remove(building);
            Assert.That(world.TryRestore(snapshot, new[] { definition }, out var reason), Is.True, reason); world.TryGet(id, out building);
            Assert.That(building.GetComponent<BuildingAppearance>().CurrentVariant, Is.EqualTo("stone"));
            Assert.That(building.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(secondMaterial));
        }
        [Test] public void PaletteKeepsFavoritesAndHotbarForTemporarilyMissingMods()
        {
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition }); var extra = Definition();
            using var session = world.CreateSession(); using var palette = new BuildingPalette(registry, session);
            palette.ToggleFavorite(extra.Id); palette.Filter(favoritesOnly: true); Assert.That(palette.Visible, Is.Empty);
            registry.Register("mod", new[] { extra }); Assert.That(palette.Visible.Count, Is.EqualTo(1));
            Assert.That(palette.AssignSlot(3, extra.Id), Is.True); Assert.That(palette.SelectSlot(3), Is.True); Assert.That(session.Selected, Is.SameAs(extra));
            var state = palette.CaptureState(); registry.Unregister("mod"); palette.RestoreState(state);
            Assert.That(palette.GetSlot(3), Is.Null); Assert.That(palette.GetSlotId(3), Is.EqualTo(extra.Id));
            registry.Register("mod", new[] { extra }); Assert.That(palette.GetSlot(3), Is.SameAs(extra)); Assert.That(palette.IsFavorite(extra.Id), Is.True);
        }
        [Test] public void AuthorityChecksSnappedPoseAndSharesWorkBudgetAcrossSessions()
        {
            var actor = Go("Actor").AddComponent<BuildingActor>(); actor.transform.position = origin; actor.SetIdentity("player", "team");
            var registry = new BuildingRegistry(); registry.Register("base", new[] { definition });
            using var authority = new BuildingAuthority(world, registry) { Clock = () => 0 }; var a = authority.OpenSession("one", actor); var b = authority.OpenSession("two", actor);
            definition.Placement = PlacementMode.Grid; definition.GridSize = 1000; definition.GridOrigin = origin + Vector3.right * 100;
            var place = new BuildingCommand { Sequence = 1, Edits = new[] { new BuildingCommandEdit { DefinitionId = definition.Id, Position = origin } } };
            Assert.That(authority.Execute(a, place).Success, Is.False); Assert.That(world.Count, Is.Zero);
            definition.Placement = PlacementMode.Free; definition.WorkRequired = 100; var site = Place(definition, At());
            var work = new BuildingCommand { Sequence = 2, Kind = BuildingCommandKind.Work, InstanceId = site.InstanceId, ExpectedVersion = site.Version, Work = 2 };
            Assert.That(authority.Execute(a, work).Success, Is.True);
            work.ExpectedVersion = site.Version; work.Sequence = 1; work.Work = 1;
            Assert.That(authority.Execute(b, work).Failure, Is.EqualTo(PlacementFailure.LimitExceeded)); Assert.That(site.Construction.Work, Is.EqualTo(2));
        }
        private sealed class ExactEconomy : IBuildingEconomy, IBuildingCostReservation
        {
            internal long Amount;
            public bool CanApply(in BuildingBill bill, out string reason) { reason = null; return true; }
            public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
            { Amount = bill.Charges[0].Amount; reservation = this; reason = null; return true; }
            public bool TryCommit(out string reason) { reason = null; return true; }
            public void Cancel() { }
        }
        private sealed class Ledger : IBuildingEconomy
        {
            internal int Wood = 100, Reserved, Commits, Reserves;
            internal bool Fail;
            private static int Sum(IReadOnlyList<BuildingCost> costs)
            { int total = 0; if (costs != null) foreach (var cost in costs) { if (cost.ResourceId != "wood") throw new InvalidOperationException("Unknown resource"); total = checked(total + (int)cost.Amount); } return total; }
            public bool CanApply(in BuildingBill bill, out string reason) { reason = "Not enough wood"; return Wood - Reserved + Sum(bill.Credits) >= Sum(bill.Charges); }
            public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
            {
                reservation = null; if (!CanApply(bill, out reason)) return false;
                int charge = Sum(bill.Charges), credit = Sum(bill.Credits); Reserved += Math.Max(0, charge - credit); Reserves++;
                reservation = new Hold(this, charge, credit); return true;
            }
            private sealed class Hold : IBuildingCostReservation
            {
                private readonly Ledger owner; private readonly int charge, credit; private bool done;
                internal Hold(Ledger owner, int charge, int credit) { this.owner = owner; this.charge = charge; this.credit = credit; }
                public bool TryCommit(out string reason) { reason = "Declined"; if (done || owner.Fail) return false; owner.Wood += credit - charge; owner.Reserved -= Math.Max(0, charge - credit); owner.Commits++; done = true; return true; }
                public void Cancel() { if (done) return; owner.Reserved -= Math.Max(0, charge - credit); done = true; }
            }
        }
    }
}
