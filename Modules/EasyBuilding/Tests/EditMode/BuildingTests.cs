using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests
{
    public sealed class BuildingTests
    {
        private readonly List<Object> cleanup = new List<Object>();
        private BuildingSystem system;
        private BuildingDefinition definition;
        private Vector3 origin = new Vector3(10000, 0, 10000);
        private PlacementRequest At(float x = 0, float z = 0) => new PlacementRequest(origin + new Vector3(x, 0, z), Quaternion.identity);

        [SetUp]
        public void SetUp()
        {
            system = Make("System").AddComponent<BuildingSystem>();
            var prefab = Make("Prefab"); prefab.SetActive(false);
            var collider = prefab.AddComponent<BoxCollider>(); collider.center = new Vector3(0, 0.5f, 0);
            prefab.layer = 31;
            var socket = Make("Top socket"); socket.transform.SetParent(prefab.transform, false); socket.transform.localPosition = Vector3.up;
            socket.AddComponent<BuildingSocket>();
            definition = Definition(prefab);
            system.Select(definition);
        }
        [TearDown]
        public void TearDown()
        {
            if (system != null)
            {
                var state = system.CaptureState();
                foreach (var record in state.Buildings)
                    if (system.TryGet(record.InstanceId, out var building)) system.Remove(building);
            }
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear(); Physics.SyncTransforms();
        }
        private GameObject Make(string name) { var value = new GameObject(name); cleanup.Add(value); return value; }
        private BuildingDefinition Definition(GameObject prefab)
        {
            var value = ScriptableObject.CreateInstance<BuildingDefinition>(); cleanup.Add(value);
            value.Prefab = prefab; value.RequireSurface = false; value.ObstacleLayers = 1 << 31; value.SurfaceLayers = 1 << 31;
            return value;
        }
        private BuildingSocket Socket(Vector3 point)
        { var go = Make("Scene socket"); go.transform.position = point; return go.AddComponent<BuildingSocket>(); }
        private BuildingZone Zone()
        {
            var zone = Make("Zone").AddComponent<BuildingZone>(); zone.transform.position = origin;
            zone.Size = new Vector3(4, 4, 4); return zone;
        }
        private Wallet Paid()
        {
            definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost("wood", 3) };
            var wallet = new Wallet(); system.CostProvider = wallet; return wallet;
        }
        private PlacedBuilding Confirm(PlacementRequest request)
        {
            Assert.That(system.TryConfirm(request, out var building, out var result), Is.True, result.Reason);
            return building;
        }

        [Test] public void FreeKeepsPoseAndConsumesSelection()
        {
            var building = Confirm(At(0.27f, 0.31f));
            Assert.That(building.transform.position, Is.EqualTo(At(0.27f, 0.31f).Position));
            Assert.That(system.Selected, Is.Null); Assert.That(system.Count, Is.EqualTo(1));
        }
        [Test] public void RestrictedFreePlacementKeepsExactPoseAndRechecksZoneAtCommit()
        {
            definition.Placement = PlacementMode.Free; definition.RequireZone = true; definition.ZoneId = "Plot"; definition.GridSize = 50;
            var zone = Zone(); var request = At(0.37f, 0.61f); var preview = system.Check(request);
            Assert.That(preview.IsValid, Is.True, preview.Reason); Assert.That(preview.Pose.position, Is.EqualTo(request.Position));
            Assert.That(system.Check(At(2.1f)).Failure, Is.EqualTo(PlacementFailure.OutsideZone));
            zone.transform.position += Vector3.right * 20;
            Assert.That(system.TryConfirm(request, out _, out var rejected), Is.False); Assert.That(rejected.Failure, Is.EqualTo(PlacementFailure.OutsideZone));
            zone.transform.position -= Vector3.right * 20;
            var placed = Confirm(request); Assert.That(placed.transform.position, Is.EqualTo(request.Position)); Assert.That(system.Selected, Is.Null);
        }
        [Test] public void GridSnapsBeforeCheckingWholeFootprintInZone()
        {
            definition.Placement = PlacementMode.Grid; definition.RequireZone = true; definition.ZoneId = "Plot"; Zone();
            Assert.That(system.Check(At(1.4f)).Pose.position.x, Is.EqualTo(origin.x + 1));
            Assert.That(system.Check(At(1.6f)).Failure, Is.EqualTo(PlacementFailure.OutsideZone));
            Assert.That(system.Check(At(1.4f)).IsValid, Is.True);
        }
        [Test] public void ZoneMatchesIdAndTracksDisableAndTransformChanges()
        {
            definition.RequireZone = true; definition.ZoneId = "Plot"; var zone = Zone();
            Assert.That(system.Check(At()).IsValid, Is.True);
            zone.ZoneId = "Other"; Assert.That(system.Check(At()).Failure, Is.EqualTo(PlacementFailure.OutsideZone));
            zone.ZoneId = "Plot"; zone.enabled = false; Assert.That(system.Check(At()).IsValid, Is.False);
            zone.enabled = true; zone.transform.position += Vector3.right * 20; Assert.That(system.Check(At()).IsValid, Is.False);
        }
        [Test] public void RotatedFootprintMustFitZone()
        {
            definition.RequireZone = true; definition.BoundsSize = new Vector3(3, 1, 1); Zone().Size = new Vector3(4, 4, 2);
            Assert.That(system.Check(At()).IsValid, Is.True);
            Assert.That(system.Check(new PlacementRequest(origin, Quaternion.Euler(0, 90, 0))).Failure, Is.EqualTo(PlacementFailure.OutsideZone));
        }
        [Test] public void MissingSurfaceBlocksAndMovingGroundIsRechecked()
        {
            definition.RequireSurface = true;
            Assert.That(system.Check(At()).Failure, Is.EqualTo(PlacementFailure.MissingSurface));
            var ground = Make("Ground"); ground.layer = 31; ground.transform.position = origin + Vector3.down * 0.5f;
            ground.AddComponent<BoxCollider>().size = new Vector3(10, 1, 10);
            Assert.That(system.Check(At()).IsValid, Is.True);
            ground.transform.position += Vector3.down * 10;
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.False);
            Assert.That(result.Failure, Is.EqualTo(PlacementFailure.MissingSurface));
        }
        [Test] public void ObstaclesAreCheckedAtConfirmationEvenAfterValidPreview()
        {
            Assert.That(system.Check(At()).IsValid, Is.True);
            var obstacle = Make("Obstacle"); obstacle.layer = 31; obstacle.transform.position = origin + Vector3.up * 0.5f; obstacle.AddComponent<BoxCollider>();
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.False);
            Assert.That(result.Failure, Is.EqualTo(PlacementFailure.Obstructed)); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void DenseOverlapStillBlocksWithoutTruncation()
        {
            for (int i = 0; i < 80; i++) { var go = Make("Overlap"); go.layer = 31; go.transform.position = origin + Vector3.up * 0.5f; go.AddComponent<BoxCollider>(); }
            Assert.That(system.Check(At()).Failure, Is.EqualTo(PlacementFailure.Obstructed));
        }
        [Test] public void InvalidAndCollapsedZoneCannotGrantPermission()
        {
            definition.RequireZone = true; var zone = Zone();
            zone.Center = new Vector3(float.NaN, 0, 0); Assert.That(system.Check(At()).IsValid, Is.False);
            zone.Center = new Vector3(0, 2, 0); zone.transform.localScale = new Vector3(0, 1, 1);
            Assert.That(system.Check(At()).IsValid, Is.False);
        }
        [Test] public void TriggerColliderDoesNotBlock()
        {
            var trigger = Make("Trigger"); trigger.layer = 31; trigger.transform.position = origin + Vector3.up * 0.5f;
            trigger.AddComponent<BoxCollider>().isTrigger = true;
            Assert.That(system.Check(At()).IsValid, Is.True);
        }
        [Test] public void OverflowingPoseIsRejectedBeforePhysics()
        {
            Assert.That(system.Check(new PlacementRequest(origin, new Quaternion(float.MaxValue, 0, 0, 1))).Failure, Is.EqualTo(PlacementFailure.InvalidPose));
            definition.Placement = PlacementMode.Grid; definition.GridSize = 0.01f;
            Assert.That(system.Check(new PlacementRequest(Vector3.one * float.MaxValue, Quaternion.identity)).Failure, Is.EqualTo(PlacementFailure.InvalidPose));
        }
        [Test] public void InvalidPoseAndDefinitionReturnActionableReasons()
        {
            Assert.That(system.Check(new PlacementRequest(new Vector3(float.NaN, 0, 0), Quaternion.identity)).Failure, Is.EqualTo(PlacementFailure.InvalidPose));
            definition.Prefab = null;
            Assert.That(system.Check(At()).Reason, Does.Contain("Assign a prefab"));
        }
        [Test] public void SocketChecksCompatibilityOccupancyAndReleasesOnRemoval()
        {
            definition.Placement = PlacementMode.Socket; var socket = Socket(origin); var request = new PlacementRequest(origin, Quaternion.identity, socket);
            socket.SocketType = "Wrong"; Assert.That(system.Check(request).Failure, Is.EqualTo(PlacementFailure.IncompatibleSocket));
            socket.SocketType = "Block"; var first = Confirm(request); system.Select(definition);
            Assert.That(system.Check(request).Failure, Is.EqualTo(PlacementFailure.OccupiedSocket));
            Assert.That(system.Remove(first), Is.True); Assert.That(socket.IsOccupied, Is.False);
            Confirm(request);
        }
        [Test] public void ExternalDestructionReleasesSocketAndRegistry()
        {
            definition.Placement = PlacementMode.Socket; var socket = Socket(origin);
            var building = Confirm(new PlacementRequest(origin, Quaternion.identity, socket));
            Object.DestroyImmediate(building.gameObject);
            Assert.That(socket.IsOccupied, Is.False); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void SocketPoseUsesLatestTransformAndZoneAppliesToSockets()
        {
            definition.Placement = PlacementMode.Socket; definition.RequireZone = true; Zone(); var socket = Socket(origin);
            var request = new PlacementRequest(origin, Quaternion.identity, socket);
            Assert.That(system.Check(request).IsValid, Is.True);
            socket.transform.position += Vector3.right * 50;
            Assert.That(system.TryConfirm(request, out _, out var result), Is.False);
            Assert.That(result.Failure, Is.EqualTo(PlacementFailure.OutsideZone));
        }
        [Test] public void NearestSocketSearchTracksMovementAndDisable()
        {
            var a = Socket(origin); var b = Socket(origin + Vector3.right);
            Assert.That(system.FindSocket(origin), Is.SameAs(a));
            a.transform.position += Vector3.right * 20; Assert.That(system.FindSocket(origin), Is.SameAs(b));
            b.enabled = false; Assert.That(system.FindSocket(origin), Is.Null);
        }
        [Test] public void PaidWithoutProviderIsBlocked()
        {
            Paid(); system.CostProvider = null;
            Assert.That(system.Check(At()).Failure, Is.EqualTo(PlacementFailure.MissingCostProvider));
            Assert.That(system.TryConfirm(At(), out _, out _), Is.False); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void PreviewAndCancelNeverReserveOrSpend()
        {
            var wallet = Paid(); for (int i = 0; i < 100; i++) system.Check(At()); system.Cancel();
            Assert.That(wallet.ReserveCalls, Is.Zero); Assert.That(wallet.Funds, Is.EqualTo(10));
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.False); Assert.That(result.Failure, Is.EqualTo(PlacementFailure.NoSelection));
        }
        [Test] public void SuccessfulPaidPlacementAndRepeatedConfirmationSpendExactlyOnce()
        {
            var wallet = Paid(); Confirm(At());
            Assert.That(system.TryConfirm(At(5), out _, out _), Is.False);
            Assert.That(wallet.Funds, Is.EqualTo(7)); Assert.That(wallet.CommitCalls, Is.EqualTo(1));
            Assert.That(wallet.Reserved, Is.Zero); Assert.That(system.Count, Is.EqualTo(1));
        }
        [Test] public void PlacedObserverCanRemoveAnAlreadyCommittedBuilding()
        {
            var wallet = Paid();
            system.Placed += building => Assert.That(system.Remove(building), Is.True);
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.True, result.Reason);
            Assert.That(wallet.Funds, Is.EqualTo(7)); Assert.That(wallet.Reserved, Is.Zero); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void InsufficientFundsNeverReserve()
        {
            var wallet = Paid(); wallet.Funds = 1;
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.False);
            Assert.That(result.Failure, Is.EqualTo(PlacementFailure.CannotAfford)); Assert.That(wallet.ReserveCalls, Is.Zero);
        }
        [Test] public void DeclinedCommitRollsBackObjectSocketAndReservation()
        {
            var wallet = Paid(); wallet.FailCommit = true; definition.Placement = PlacementMode.Socket;
            var socket = Socket(origin);
            Assert.That(system.TryConfirm(new PlacementRequest(origin, Quaternion.identity, socket), out var building, out _), Is.False);
            Assert.That(building, Is.Null); Assert.That(socket.IsOccupied, Is.False); Assert.That(system.Count, Is.Zero);
            Assert.That(wallet.Funds, Is.EqualTo(10)); Assert.That(wallet.Reserved, Is.Zero);
        }
        [Test] public void EnvironmentChangedDuringReservationCancelsPayment()
        {
            var wallet = Paid();
            wallet.OnReserve = () => { var obstacle = Make("New obstacle"); obstacle.layer = 31; obstacle.transform.position = origin + Vector3.up * 0.5f; obstacle.AddComponent<BoxCollider>(); };
            Assert.That(system.TryConfirm(At(), out _, out var result), Is.False);
            Assert.That(result.Failure, Is.EqualTo(PlacementFailure.Obstructed)); Assert.That(wallet.Funds, Is.EqualTo(10)); Assert.That(wallet.Reserved, Is.Zero);
        }
        [Test] public void InvalidPrefabAfterReservationCancelsPayment()
        {
            var wallet = Paid();
            var source = definition.Prefab.GetComponentInChildren<BuildingSocket>(true);
            var duplicate = Object.Instantiate(source.gameObject, definition.Prefab.transform); cleanup.Add(duplicate);
            Assert.That(system.TryConfirm(At(), out _, out _), Is.False);
            Assert.That(wallet.Funds, Is.EqualTo(10)); Assert.That(wallet.Reserved, Is.Zero); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void ProviderReentryCannotDuplicatePlacement()
        {
            var wallet = Paid();
            wallet.OnReserve = () => { Assert.That(system.TryConfirm(At(5), out _, out var result), Is.False); Assert.That(result.Failure, Is.EqualTo(PlacementFailure.Busy)); system.Cancel(); };
            Confirm(At()); Assert.That(wallet.Funds, Is.EqualTo(7)); Assert.That(system.Count, Is.EqualTo(1));
        }
        [Test] public void CancellationDuringReservationDoesNotResurrectRepeatSelection()
        {
            var wallet = Paid(); system.DefaultSession.RepeatPlacement = true; wallet.OnReserve = system.Cancel;
            Confirm(At()); Assert.That(system.Selected, Is.Null);
            Assert.That(wallet.CommitCalls, Is.EqualTo(1)); Assert.That(wallet.Funds, Is.EqualTo(7)); Assert.That(wallet.Reserved, Is.Zero);
            Assert.That(system.TryConfirm(At(5), out _, out var result), Is.False); Assert.That(result.Failure, Is.EqualTo(PlacementFailure.NoSelection));
        }
        [Test] public void FailedReserveWithReservationIsStillCancelled()
        {
            var wallet = Paid(); wallet.FailReserve = true;
            Assert.That(system.TryConfirm(At(), out _, out _), Is.False);
            Assert.That(wallet.Reserved, Is.Zero); Assert.That(wallet.Funds, Is.EqualTo(10));
        }
        [Test] public void RestoreConnectedBuildingsInReverseOrderWithoutPayment()
        {
            var parent = Confirm(At());
            var childDefinition = Definition(definition.Prefab); childDefinition.Placement = PlacementMode.Socket;
            childDefinition.Payment = BuildingPayment.Paid; childDefinition.Costs = new[] { new BuildingCost("wood", 3) };
            var wallet = new Wallet(); system.CostProvider = wallet; system.Select(childDefinition);
            var child = Confirm(new PlacementRequest(origin, Quaternion.Euler(0, 90, 0), parent.GetComponentInChildren<BuildingSocket>()));
            string parentId = parent.InstanceId, childId = child.InstanceId; var saved = system.CaptureState(); saved.Buildings.Reverse();
            system.Remove(child); system.Remove(parent);
            Assert.That(system.TryRestore(saved, new[] { definition, childDefinition }, out var reason), Is.True, reason);
            Assert.That(system.TryGet(childId, out var restoredChild), Is.True); Assert.That(system.TryGet(parentId, out var restoredParent), Is.True);
            Assert.That(restoredChild.AttachedSocket.Building, Is.SameAs(restoredParent));
            Assert.That(restoredChild.AttachedSocket.Occupant, Is.SameAs(restoredChild));
            Assert.That(wallet.Funds, Is.EqualTo(7)); Assert.That(wallet.ReserveCalls, Is.EqualTo(1));
            Assert.That(Quaternion.Angle(restoredChild.transform.rotation, Quaternion.Euler(0, 90, 0)), Is.LessThan(0.01));
            system.Remove(restoredParent); Assert.That(restoredChild.AttachedSocket, Is.Null);
        }
        [Test] public void InvalidRestoreIsAtomicAndReleasesEarlierClaims()
        {
            var socket = Socket(origin); var state = new BuildingState();
            state.Buildings.Add(new BuildingRecord { InstanceId = "a", DefinitionId = definition.Id, Position = origin, Rotation = Quaternion.identity, SocketId = socket.Id });
            state.Buildings.Add(new BuildingRecord { InstanceId = "b", DefinitionId = definition.Id, Position = origin, Rotation = Quaternion.identity, SocketId = "missing" });
            Assert.That(system.TryRestore(state, new[] { definition }, out _), Is.False);
            Assert.That(system.Count, Is.Zero); Assert.That(socket.IsOccupied, Is.False);
        }
        [Test] public void DuplicateRestoreDoesNotAlterExistingWorld()
        {
            var building = Confirm(At()); var state = system.CaptureState();
            Assert.That(system.TryRestore(state, new[] { definition }, out _), Is.False);
            Assert.That(system.Count, Is.EqualTo(1)); Assert.That(system.TryGet(building.InstanceId, out var existing), Is.True); Assert.That(existing, Is.SameAs(building));
        }
        [Test] public void RestoreRejectsDuplicateIdsAndMissingDefinitions()
        {
            var building = Confirm(At()); var state = system.CaptureState(); system.Remove(building);
            Assert.That(system.TryRestore(state, Array.Empty<BuildingDefinition>(), out _), Is.False);
            state.Buildings.Add(state.Buildings[0]); Assert.That(system.TryRestore(state, new[] { definition }, out _), Is.False); Assert.That(system.Count, Is.Zero);
        }
        [Test] public void CaptureJsonRoundTripsStableIds()
        {
            var building = Confirm(At()); string id = building.InstanceId;
            var state = JsonUtility.FromJson<BuildingState>(JsonUtility.ToJson(system.CaptureState())); system.Remove(building);
            Assert.That(system.TryRestore(state, new[] { definition }, out var reason), Is.True, reason);
            Assert.That(system.TryGet(id, out var restored), Is.True); Assert.That(restored.Definition.Id, Is.EqualTo(definition.Id));
        }
        [Test] public void WarmPreviewDoesNotAllocateManagedMemory()
        {
            bool previous = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;
            try
            {
                var options = Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame |
                    Unity.Profiling.ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                using var bytes = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1);
                long calibrationBytes, calibrationCount, allocated, count; bool controlValid, measurementValid;
                using (var controlRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    long before = bytes.CurrentValue;
                    var control = new byte[4096];
                    calibrationBytes = bytes.CurrentValue - before; controlRecorder.Stop(); GC.KeepAlive(control);
                    controlValid = controlRecorder.Valid; calibrationCount = controlRecorder.Count == 0 ? 0 : controlRecorder.GetSample(0).Count;
                }
                // Warm under the same profiler state and keep NUnit/assertion allocations outside both windows.
                PlacementResult result = default;
                for (int i = 0; i < 100; i++) result = system.Check(At());
                using (var events = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Internal, "GC.Alloc", 1, options))
                {
                    long before = bytes.CurrentValue;
                    for (int i = 0; i < 1000; i++) result = system.Check(At());
                    allocated = bytes.CurrentValue - before; events.Stop();
                    measurementValid = events.Valid; count = events.Count == 0 ? 0 : events.GetSample(0).Count;
                }
                Assert.That(controlValid && measurementValid && bytes.Valid, Is.True);
                Assert.That(calibrationCount, Is.EqualTo(1)); Assert.That(calibrationBytes, Is.GreaterThanOrEqualTo(4096));
                Assert.That(result.IsValid, Is.True, result.Reason);
                // The frame byte counter is process-wide (including worker threads), not a scope counter.
                // Every managed allocation in Check must emit GC.Alloc on this thread; zero events is strict.
                TestContext.WriteLine($"1000 preview checks: main-thread GC.Alloc={count}; process-wide byte delta={allocated}; control={calibrationCount} event/{calibrationBytes} bytes.");
                Assert.That(count, Is.Zero, "GC.Alloc events in 1000 warmed preview checks.");
            }
            finally { UnityEngine.Profiling.Profiler.enabled = previous; }
        }

        private sealed class Wallet : IBuildingCostProvider
        {
            internal int Funds = 10, Reserved, ReserveCalls, CommitCalls;
            internal bool FailCommit, FailReserve;
            internal Action OnReserve;
            public bool CanAfford(BuildingDefinition value, out string reason) { reason = "Not enough wood."; return Funds - Reserved >= 3; }
            public bool TryReserve(BuildingDefinition value, out IBuildingCostReservation reservation, out string reason)
            {
                ReserveCalls++; Reserved += 3; reservation = new Reservation(this); reason = "Reservation declined."; OnReserve?.Invoke(); return !FailReserve;
            }
            private sealed class Reservation : IBuildingCostReservation
            {
                private readonly Wallet wallet; private bool closed;
                internal Reservation(Wallet wallet) => this.wallet = wallet;
                public bool TryCommit(out string reason)
                {
                    reason = "Commit declined."; if (closed || wallet.FailCommit) return false;
                    closed = true; wallet.Reserved -= 3; wallet.Funds -= 3; wallet.CommitCalls++; return true;
                }
                public void Cancel() { if (closed) return; closed = true; wallet.Reserved -= 3; }
            }
        }
    }
}
