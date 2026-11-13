using System;
using EasyBuilding.Cells;
using EasyBuilding.Paths;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests
{
    public sealed class CellAndPathTests
    {
        private GameObject root;
        private BuildingCellWorld world;
        private BuildingCellPalette palette;
        [SetUp] public void Setup()
        {
            root = new GameObject("Cells"); world = root.AddComponent<BuildingCellWorld>(); palette = ScriptableObject.CreateInstance<BuildingCellPalette>();
            palette.Types = new[] { new BuildingCellType { Id = "stone" }, new BuildingCellType { Id = "glass" } }; world.Palette = palette;
        }
        [TearDown] public void Cleanup() { Object.DestroyImmediate(root); Object.DestroyImmediate(palette); }
        [Test] public void NegativeChunkCoordinatesRoundTripAfterPaletteReorder()
        {
            var p = new Vector3Int(-1, -17, 16);
            Assert.That(BuildingCellWorld.ChunkOf(p), Is.EqualTo(new Vector3Int(-1, -2, 1)));
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(p, 1), new BuildingCellEdit(Vector3Int.zero, 2) }, out _, out var reason), Is.True, reason);
            var saved = JsonUtility.FromJson<BuildingCellState>(JsonUtility.ToJson(world.Capture()));
            Array.Reverse(palette.Types);
            Assert.That(world.TryRestore(saved, out reason), Is.True, reason);
            Assert.That(world.Get(p), Is.EqualTo(2)); Assert.That(world.Get(Vector3Int.zero), Is.EqualTo(1)); Assert.That(world.ChunkCount, Is.EqualTo(2));
        }
        [Test] public void PaidStrokeAndUndoAreAtomicAndSingleUse()
        {
            var ledger = new Wallet(); world.Economy = ledger; palette.Types[0].Costs = new[] { new BuildingCost("wood", 3) };
            var edits = new[] { new BuildingCellEdit(Vector3Int.zero, 1), new BuildingCellEdit(Vector3Int.right, 1) };
            ledger.Decline = true; Assert.That(world.TryApply(edits, out _, out _), Is.False); Assert.That(world.ChunkCount, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(10));
            ledger.Decline = false; Assert.That(world.TryApply(edits, out var stroke, out var reason), Is.True, reason); Assert.That(ledger.Wood, Is.EqualTo(4));
            Assert.That(world.TryUndo(stroke, out reason), Is.True, reason); Assert.That(world.ChunkCount, Is.Zero); Assert.That(ledger.Wood, Is.EqualTo(10));
            Assert.That(world.TryUndo(stroke, out _), Is.False); Assert.That(ledger.Wood, Is.EqualTo(10));
        }
        [Test] public void CellPermissionChangedDuringReservationCancelsBeforePayment()
        {
            var rule = new Permission(); var ledger = new Wallet { OnReserve = () => rule.Allowed = false };
            world.Rule = rule; world.Economy = ledger; palette.Types[0].Costs = new[] { new BuildingCost("wood", 3) };
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(Vector3Int.zero, 1) }, out _, out _), Is.False);
            Assert.That(ledger.Wood, Is.EqualTo(10)); Assert.That(world.ChunkCount, Is.Zero);
            ledger.OnReserve = null; rule.Allowed = true;
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(Vector3Int.zero, 1) }, out _, out _), Is.True);
            Assert.That(ledger.Wood, Is.EqualTo(7));
        }
        private sealed class Permission : IBuildingCellRule
        {
            public bool Allowed = true;
            public bool Allows(BuildingCellWorld world, in BuildingCellEdit edit, IBuildingActor actor, out string reason)
            { reason = Allowed ? null : "Permission changed."; return Allowed; }
        }
        [Test] public void DuplicateCellsAndMalformedSnapshotsDoNotPartiallyMutate()
        {
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(Vector3Int.zero, 1), new BuildingCellEdit(Vector3Int.zero, 2) }, out _, out _), Is.False);
            Assert.That(world.ChunkCount, Is.Zero);
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(Vector3Int.zero, 1) }, out _, out _), Is.True);
            var saved = world.Capture(); saved.Chunks.Add(new BuildingCellChunkState { Position = Vector3Int.right, Data = "broken" });
            Assert.That(world.TryRestore(saved, out _), Is.False); Assert.That(world.Get(Vector3Int.zero), Is.EqualTo(1)); Assert.That(world.ChunkCount, Is.EqualTo(1));
        }
        [TestCase(BuildingPathShape.Road, false)]
        [TestCase(BuildingPathShape.Pipe, false)]
        [TestCase(BuildingPathShape.Pipe, true)]
        public void PathMeshUsesOutwardWindingAndFiniteTransportedFrames(BuildingPathShape shape, bool smooth)
        {
            var points = new[] { Vector3.zero, new Vector3(0, 0, 4), new Vector3(1, 3, 6), new Vector3(1, 6, 6) };
            var settings = new BuildingPathSettings { Shape = shape, Smooth = smooth, Width = 0.2f };
            Assert.That(BuildingPathGeometry.TryCreate(points, settings, out var mesh, out float length, out var reason), Is.True, reason);
            try
            {
                Assert.That(length, Is.GreaterThan(10)); var vertices = mesh.vertices; var triangles = mesh.triangles; var normals = mesh.normals;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                    var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    Assert.That(float.IsFinite(normal.sqrMagnitude), Is.True); Assert.That(Vector3.Dot(normal, normals[a] + normals[b] + normals[c]), Is.GreaterThan(0));
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        [Test] public void PathRejectsSingularAndExcessiveSamplesWithoutMeshAllocation()
        {
            Assert.That(BuildingPathGeometry.TryCreate(new[] { Vector3.zero, Vector3.zero }, new BuildingPathSettings(), out var mesh, out _, out _), Is.False); Assert.That(mesh, Is.Null);
            var points = new Vector3[100]; for (int i = 0; i < points.Length; i++) points[i] = Vector3.forward * i;
            Assert.That(BuildingPathGeometry.TryCreate(points, new BuildingPathSettings { Smooth = true, SamplesPerSegment = 64 }, out mesh, out _, out _), Is.False); Assert.That(mesh, Is.Null);
        }
        private sealed class Wallet : IBuildingEconomy
        {
            public long Wood = 10; public bool Decline; public Action OnReserve;
            public bool CanApply(in BuildingBill bill, out string reason) { reason = null; return true; }
            public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
            {
                reason = null; long delta = 0;
                if (bill.Charges != null) foreach (var cost in bill.Charges) delta -= cost.Amount;
                if (bill.Credits != null) foreach (var cost in bill.Credits) delta += cost.Amount;
                reservation = new Reservation(this, delta); OnReserve?.Invoke(); return true;
            }
            private sealed class Reservation : IBuildingCostReservation
            {
                private Wallet owner; private readonly long delta;
                public Reservation(Wallet owner, long delta) { this.owner = owner; this.delta = delta; }
                public bool TryCommit(out string reason) { reason = null; if (owner == null || owner.Decline || owner.Wood + delta < 0) return false; owner.Wood += delta; owner = null; return true; }
                public void Cancel() => owner = null;
            }
        }
    }
}
