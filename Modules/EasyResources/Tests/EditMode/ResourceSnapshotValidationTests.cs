using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyResources.Production;
using NUnit.Framework;
using UnityEngine;

namespace EasyResources.Tests
{
    public sealed class ResourceSnapshotValidationTests
    {
        private ResourceDefinition resource;
        private ResourceCost Cost(long amount) => new ResourceCost(new ResourceAmount(resource, amount));
        [SetUp] public void SetUp() => resource = ResourceDefinition.Create("Resource");
        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(resource);

        [Test]
        public void PreflightPreservesNamedHoldsDeferredEventsAndAuthoredData()
        {
            using var store = new ResourceStore(Cost(20), historyCapacity: 16);
            store.TryReserve(Cost(3), out var oldHold, out _, new ResourceReservationOptions("operation"));
            var saved = store.CaptureState(true);
            var savedJson = JsonUtility.ToJson(saved);
            var definitionJson = JsonUtility.ToJson(resource);
            var scope = store.DeferNotifications();
            store.TrySpend(Cost(1));
            var revision = store.Revision;
            var events = 0;
            store.Changed += () => events++;
            var options = new ResourceRestoreOptions(resumeReservations: true, replaceActiveReservations: true);
            var preview = store.ValidateState(saved, new[] { resource }, options);
            Assert.That(preview.RestoredReservationCount, Is.EqualTo(1));
            Assert.That(scope.IsActive && oldHold.IsActive, Is.True);
            Assert.That(store.GetTotal(resource), Is.EqualTo(19));
            Assert.That(store.GetReserved(resource), Is.EqualTo(3));
            Assert.That(store.Revision, Is.EqualTo(revision));
            Assert.That(events, Is.Zero);
            Assert.That(JsonUtility.ToJson(saved), Is.EqualTo(savedJson));
            Assert.That(JsonUtility.ToJson(resource), Is.EqualTo(definitionJson));

            var restored = store.RestoreState(saved, new[] { resource }, options);
            Assert.That(restored.RestoredReservationCount, Is.EqualTo(preview.RestoredReservationCount));
            Assert.That(scope.IsActive || oldHold.IsActive, Is.False);
            scope.Dispose();
            Assert.That(events, Is.EqualTo(1));
            Assert.That(store.TryGetReservation(oldHold.Id, "operation", out var resumed), Is.True);
            Assert.That(resumed.TryCommit(out _), Is.True);
            Assert.That(resumed.TryCommit(out _), Is.False);
            Assert.That(store.GetTotal(resource), Is.EqualTo(17));
        }

        [TestCase("precision")]
        [TestCase("unknown")]
        [TestCase("capacity")]
        [TestCase("duplicate")]
        [TestCase("held-total")]
        [TestCase("resume-policy")]
        public void PreflightAndRestoreRejectTheSameBadSnapshotWithoutSideEffects(string kind)
        {
            using var store = new ResourceStore(Cost(10), limits: new ResourceLimits(new[] { new ResourceLimit(resource, 20) }));
            store.TryReserve(Cost(3), out var hold, out _, new ResourceReservationOptions("owner"));
            var state = store.CaptureState(true);
            var revision = store.Revision;
            if (kind == "precision") state.Resources[0] = new ResourceStateEntry(resource.Id, 10, 2);
            if (kind == "unknown") state.Resources[0] = new ResourceStateEntry("missing", 10);
            if (kind == "capacity") state.Resources[0] = new ResourceStateEntry(resource.Id, 21);
            if (kind == "duplicate") state.Resources.Add(state.Resources[0]);
            if (kind == "held-total") state.Reservations[0].Resources[0] = new ResourceStateEntry(resource.Id, 11);
            var options = new ResourceRestoreOptions(resumeReservations: kind != "resume-policy", replaceActiveReservations: true);
            var events = 0;
            store.Changed += () => events++;
            var preflightError = Assert.Catch(() => store.ValidateState(state, new[] { resource }, options));
            var restoreError = Assert.Catch(() => store.RestoreState(state, new[] { resource }, options));
            Assert.That(preflightError.GetType(), Is.EqualTo(restoreError.GetType()));
            Assert.That(preflightError.Message, Is.EqualTo(restoreError.Message));
            Assert.That(store.Revision, Is.EqualTo(revision));
            Assert.That(store.GetTotal(resource), Is.EqualTo(10));
            Assert.That(store.GetReserved(resource), Is.EqualTo(3));
            Assert.That(hold.IsActive, Is.True);
            Assert.That(events, Is.Zero);
        }

        [Test]
        public void ChangedSnapshotMustPassValidationAgainAtRestore()
        {
            using var store = new ResourceStore(Cost(10));
            var state = store.CaptureState();
            store.ValidateState(state, new[] { resource });
            state.Resources[0] = new ResourceStateEntry(resource.Id, -1);
            Assert.Throws<ArgumentException>(() => store.RestoreState(state, new[] { resource }));
            Assert.That(store.GetTotal(resource), Is.EqualTo(10));
            Assert.That(store.Revision, Is.Zero);
        }

        [Test]
        public void PreflightRejectsReentryAndWrongThreadBeforeReadingTheCatalog()
        {
            using var store = new ResourceStore(Cost(10));
            var state = store.CaptureState();
            var enumerated = 0;
            IEnumerable<ResourceDefinition> Catalog() { enumerated++; yield return resource; }
            store.Changed += () => Assert.Throws<InvalidOperationException>(() => store.ValidateState(state, Catalog()));
            store.Add(resource, 1);
            Assert.Throws<InvalidOperationException>(() => Task.Run(() => store.ValidateState(state, Catalog())).GetAwaiter().GetResult());
            Assert.That(enumerated, Is.Zero);
        }

        [Test]
        public void NamedProductionResumesAfterWalletPreflightWithoutPayingAgain()
        {
            var output = ResourceDefinition.Create("Output");
            var recipe = ProductionRecipe.Create("Production", Cost(3), new ResourceCost(new ResourceAmount(output, 1)), 100);
            try
            {
                using var wallet = new ResourceStore(Cost(10));
                using var oldQueue = new ProductionQueue(wallet, ownerId: "station");
                Assert.That(oldQueue.TryEnqueue(recipe, 0, out _), Is.True);
                var state = wallet.CaptureState(true, 20);
                var jobs = oldQueue.CaptureState(20);
                var definitions = new[] { resource, output };
                var options = new ResourceRestoreOptions(resumeReservations: true, replaceActiveReservations: true, nowMilliseconds: 200);
                wallet.ValidateState(state, definitions, options);
                Assert.That(oldQueue.Count, Is.EqualTo(1));
                Assert.That(wallet.GetTotal(resource), Is.EqualTo(10));
                wallet.RestoreState(state, definitions, options);
                using var resumed = ProductionQueue.FromState(wallet, wallet, jobs, new[] { recipe }, definitions, 200);
                oldQueue.Dispose();
                Assert.That(wallet.GetReserved(resource), Is.EqualTo(3));
                Assert.That(resumed.Tick(280), Is.EqualTo(1));
                Assert.That(resumed.Tick(281), Is.Zero);
                Assert.That(wallet.GetTotal(resource), Is.EqualTo(7));
                Assert.That(wallet.GetTotal(output), Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
                UnityEngine.Object.DestroyImmediate(recipe);
            }
        }
    }
}
