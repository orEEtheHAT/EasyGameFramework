using System.Collections.Generic;
using EasyCombat;
using NUnit.Framework;
using UnityEngine;

namespace EasyBuilding.Integrations.Tests
{
    public sealed class BuildingHealthTests
    {
        private readonly List<Object> objects = new List<Object>();
        private BuildingSystem world;
        private BuildingDefinition definition;
        private GameObject prefab;
        private Wallet wallet;
        private readonly Vector3 origin = new Vector3(60000, 0, 0);
        [SetUp] public void Setup()
        {
            var root = new GameObject("World"); objects.Add(root); world = root.AddComponent<BuildingSystem>();
            prefab = new GameObject("Prefab"); objects.Add(prefab); prefab.SetActive(false);
            prefab.AddComponent<BuildingHealth>().RepairCostPerHealth = new[] { new BuildingCost("wood", 1) };
            definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab; definition.RequireSurface = false;
            wallet = new Wallet(); world.Economy = wallet;
        }
        [TearDown] public void Teardown()
        {
            if (world != null) Object.DestroyImmediate(world.gameObject);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]); objects.Clear();
        }
        private PlacedBuilding Place()
        { Assert.That(world.TryPlace(definition, new PlacementRequest(origin, Quaternion.identity), out var building, out var result), Is.True, result.Reason); return building; }
        [Test] public void RepairSpendsOnlyAfterValidationAndDoesNotReplaceTheBuilding()
        {
            var building = Place(); var health = building.GetComponent<Health>(); health.RestoreState(new HealthSnapshot(100, 40));
            wallet.Fail = true; Assert.That(world.TryRepair(building, 20, out _), Is.False); Assert.That(health.Current, Is.EqualTo(40)); Assert.That(wallet.Balance, Is.EqualTo(100));
            wallet.Fail = false; Assert.That(world.TryRepair(building, 20, out var result), Is.True, result.Reason);
            Assert.That(health.Current, Is.EqualTo(60)); Assert.That(wallet.Balance, Is.EqualTo(80)); Assert.That(building.PaidCosts, Is.Empty);
            var state = world.CaptureState(); string id = building.InstanceId; world.Remove(building);
            Assert.That(world.TryRestore(state, new[] { definition }, out var reason), Is.True, reason); world.TryGet(id, out var restored);
            Assert.That(restored.GetComponent<Health>().Current, Is.EqualTo(60));
        }
        [Test] public void RepairInsideDamageNotificationIsRejectedBeforePayment()
        {
            var building = Place(); var health = building.GetComponent<Health>(); bool attempted = false, repaired = false;
            health.Damaged += damage => { attempted = true; repaired = world.TryRepair(building, 10, out _); };
            health.ApplyDamage(new DamageInfo(20));
            Assert.That(attempted, Is.True); Assert.That(repaired, Is.False); Assert.That(wallet.Balance, Is.EqualTo(100));
            Assert.That(health.Current, Is.EqualTo(80));
        }
        [Test] public void UpgradeKeepsDamagePercentageWithTheNewHealthMaximum()
        {
            var building = Place(); building.GetComponent<Health>().RestoreState(new HealthSnapshot(100, 40));
            var strongerPrefab = Object.Instantiate(prefab); objects.Add(strongerPrefab); strongerPrefab.GetComponent<Health>().SetMaximum(200);
            var stronger = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(stronger); stronger.Prefab = strongerPrefab; stronger.RequireSurface = false;
            definition.Upgrades = new[] { stronger };
            Assert.That(world.TryUpgrade(building, stronger, out var upgraded, out var result), Is.True, result.Reason);
            Assert.That(upgraded.GetComponent<Health>().Maximum, Is.EqualTo(200)); Assert.That(upgraded.GetComponent<Health>().Current, Is.EqualTo(80));
        }
        private sealed class Wallet : IBuildingEconomy
        {
            internal long Balance = 100;
            internal bool Fail;
            public bool CanApply(in BuildingBill bill, out string reason) { reason = null; long cost = 0; foreach (var row in bill.Charges) cost += row.Amount; return Balance >= cost; }
            public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
            { long cost = 0; foreach (var row in bill.Charges) cost += row.Amount; reason = null; reservation = new Hold(this, cost); return Balance >= cost; }
            private sealed class Hold : IBuildingCostReservation
            {
                private readonly Wallet owner; private readonly long amount; private bool done;
                internal Hold(Wallet owner, long amount) { this.owner = owner; this.amount = amount; }
                public bool TryCommit(out string reason) { reason = "Declined"; if (done || owner.Fail || owner.Balance < amount) return false; owner.Balance -= amount; done = true; return true; }
                public void Cancel() => done = true;
            }
        }
    }
}
