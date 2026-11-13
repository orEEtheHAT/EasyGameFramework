using System;
using EasyResources;
using NUnit.Framework;
using UnityEngine;

namespace EasyBuilding.Integrations.Tests
{
    public sealed class BuildingResourceTests
    {
        private ResourceDefinition wood, stone;
        private ResourceStore a, b;
        private BuildingResourceEconomy economy;
        private BuildingBill Bill(long woodCharge, long stoneCredit = 0) => new BuildingBill(
            new[] { new BuildingCost(wood.Id, woodCharge) }, stoneCredit == 0 ? Array.Empty<BuildingCost>() : new[] { new BuildingCost(stone.Id, stoneCredit) }, null, BuildingAction.Place);
        [SetUp] public void Setup()
        {
            wood = ResourceDefinition.Create("Wood", "building-test-wood"); stone = ResourceDefinition.Create("Stone", "building-test-stone");
            a = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 2)), limits: new ResourceLimits(new[] { new ResourceLimit(stone, 2) }));
            b = new ResourceStore(new ResourceCost(new ResourceAmount(wood, 4)));
            economy = new BuildingResourceEconomy(new[] { a, b, a }, new[] { wood, stone });
        }
        [TearDown] public void Teardown()
        { a.Dispose(); b.Dispose(); UnityEngine.Object.DestroyImmediate(wood); UnityEngine.Object.DestroyImmediate(stone); }
        [Test] public void AggregatePaymentReservesAcrossStoresAndPublishesCommittedBalances()
        {
            Assert.That(economy.TryReserve(Bill(5, 1), out var hold, out var reason), Is.True, reason);
            Assert.That(a.GetTotal(wood), Is.EqualTo(2)); Assert.That(a.GetReserved(wood), Is.EqualTo(2)); Assert.That(b.GetReserved(wood), Is.EqualTo(3));
            long observedB = -1, observedStone = -1;
            a.Changed += () => { observedB = b.GetTotal(wood); observedStone = a.GetTotal(stone); };
            Assert.That(hold.TryCommit(out reason), Is.True, reason); hold.Cancel();
            Assert.That(observedB, Is.EqualTo(1)); Assert.That(observedStone, Is.EqualTo(1)); Assert.That(a.GetTotal(wood), Is.Zero);
            Assert.That(hold.TryCommit(out _), Is.False); Assert.That(a.ActiveReservationCount + b.ActiveReservationCount, Is.Zero);
        }
        [Test] public void ChangedRefundCapacityRollsBackEveryHeldStore()
        {
            Assert.That(economy.TryReserve(Bill(5, 1), out var hold, out var reason), Is.True, reason);
            a.Add(stone, 2);
            Assert.That(hold.TryCommit(out _), Is.False); hold.Cancel();
            Assert.That(a.GetTotal(wood), Is.EqualTo(2)); Assert.That(b.GetTotal(wood), Is.EqualTo(4));
            Assert.That(a.GetReserved(wood) + b.GetReserved(wood), Is.Zero); Assert.That(a.GetTotal(stone), Is.EqualTo(2));
        }
        [Test] public void DuplicateStoreReferencesNeverCreateFunds()
        {
            Assert.That(economy.CanApply(Bill(7), out _), Is.False);
            Assert.That(economy.TryReserve(Bill(7), out var hold, out _), Is.False); Assert.That(hold, Is.Null);
            Assert.That(a.ActiveReservationCount + b.ActiveReservationCount, Is.Zero);
        }
        [Test] public void FullLocalSnapshotReplacementNeverReservesOrSpendsAgain()
        {
            var root = new GameObject("Snapshot world"); var prefab = new GameObject("Prefab"); prefab.SetActive(false);
            var definition = BuildingDefinition.Create("paid.snapshot", prefab); definition.RequireSurface = false; definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost(wood.Id, 3) };
            try
            {
                var world = root.AddComponent<BuildingSystem>(); world.Economy = economy;
                Assert.That(world.TryPlace(definition, new PlacementRequest(new Vector3(83000, 0, 0), Quaternion.identity), out _, out var result), Is.True, result.Reason);
                long remaining = a.GetTotal(wood) + b.GetTotal(wood); var saved = world.CaptureState(); int notifications = 0;
                a.Changed += () => notifications++; b.Changed += () => notifications++;
                Assert.That(world.ValidateSnapshot(saved, new[] { definition }, out var reason), Is.True, reason);
                Assert.That(world.TryReplaceSnapshot(saved, new[] { definition }, out reason), Is.True, reason);
                Assert.That(world.TryReplaceSnapshot(saved, new[] { definition }, out reason), Is.True, reason);
                Assert.That(a.GetTotal(wood) + b.GetTotal(wood), Is.EqualTo(remaining)); Assert.That(a.ActiveReservationCount + b.ActiveReservationCount, Is.Zero); Assert.That(notifications, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(prefab); UnityEngine.Object.DestroyImmediate(definition); }
        }
        [Test] public void BillStorageIsCopiedAndCancellationIsIdempotent()
        {
            var rows = new[] { new BuildingCost(wood.Id, 5) }; var bill = new BuildingBill(rows, Array.Empty<BuildingCost>(), null, BuildingAction.Place);
            Assert.That(economy.TryReserve(bill, out var hold, out _), Is.True); rows[0] = new BuildingCost(wood.Id, 100);
            Assert.That(hold.TryCommit(out _), Is.True); hold.Cancel(); hold.Cancel(); Assert.That(b.GetTotal(wood), Is.EqualTo(1));
        }
        [Test] public void SameResourceRefundOffsetsChargeWithoutIntermediateGrant()
        {
            var bill = new BuildingBill(new[] { new BuildingCost(wood.Id, 10) }, new[] { new BuildingCost(wood.Id, 8) }, null, BuildingAction.Upgrade);
            Assert.That(economy.TryReserve(bill, out var hold, out var reason), Is.True, reason);
            Assert.That(hold.TryCommit(out reason), Is.True, reason); Assert.That(a.GetTotal(wood), Is.Zero); Assert.That(b.GetTotal(wood), Is.EqualTo(4));
        }
        [Test] public void RuntimeAliasMapsToTheRegisteredResource()
        {
            economy.AddAlias("wood", wood);
            var bill = new BuildingBill(new[] { new BuildingCost("wood", 3) }, Array.Empty<BuildingCost>(), null, BuildingAction.Place);
            Assert.That(economy.TryReserve(bill, out var hold, out var reason), Is.True, reason); hold.Cancel();
            Assert.That(a.GetAvailable(wood), Is.EqualTo(2)); Assert.That(b.GetAvailable(wood), Is.EqualTo(4));
        }
    }
}
