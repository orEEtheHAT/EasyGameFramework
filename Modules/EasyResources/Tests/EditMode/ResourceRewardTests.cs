using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyResources.Networking;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Tests
{
    public sealed class ResourceRewardTests
    {
        private ResourceDefinition coin, energy;
        private ResourceDatabase database;
        private readonly List<ResourceRewardProfile> profiles = new List<ResourceRewardProfile>();
        private ResourceRewardProfile Profile(params ResourceRewardRange[] rows)
        {
            var profile = ResourceRewardProfile.Create("Reward", database, rows);
            profiles.Add(profile);
            return profile;
        }

        private sealed class BitsRandom : System.Random
        {
            private readonly Queue<int> chunks = new Queue<int>();
            public int Calls { get; private set; }
            public BitsRandom(params ulong[] samples)
            {
                foreach (var sample in samples)
                {
                    chunks.Enqueue((int)(sample >> 42));
                    chunks.Enqueue((int)((sample >> 21) & ((1 << 21) - 1)));
                    chunks.Enqueue((int)(sample & ((1 << 21) - 1)));
                }
            }
            public override int Next(int maxValue)
            {
                Calls++;
                if (maxValue != 1 << 21 || chunks.Count == 0)
                    throw new InvalidOperationException("Unexpected random draw.");
                return chunks.Dequeue();
            }
        }
        private sealed class InvalidRandom : System.Random
        {
            public override int Next(int maxValue) => maxValue;
        }

        [SetUp]
        public void SetUp()
        {
            coin = ResourceDefinition.Create("Coin");
            energy = ResourceDefinition.Create("Energy", decimalPlaces: 2);
            database = ResourceDatabase.Create("Resources", coin, energy);
        }
        [TearDown]
        public void TearDown()
        {
            foreach (var profile in profiles) UnityEngine.Object.DestroyImmediate(profile);
            profiles.Clear();
            UnityEngine.Object.DestroyImmediate(database);
            UnityEngine.Object.DestroyImmediate(coin);
            UnityEngine.Object.DestroyImmediate(energy);
        }

        [Test]
        public void InclusiveEndpointsUseSmallestUnitsAndFixedRowsDoNotUseRandom()
        {
            var profile = Profile(new ResourceRewardRange(coin, 2, 5), ResourceRewardRange.FromQuantity(energy, 1.25m, 1.27m));
            Assert.That(profile.Ranges[1].Min, Is.EqualTo(125));
            Assert.That(profile.Ranges[1].Max, Is.EqualTo(127));
            var low = profile.Roll(new BitsRandom(0, 0));
            var high = profile.Roll(new BitsRandom(3, 2));
            Assert.That(low.GetAmount(coin), Is.EqualTo(2));
            Assert.That(high.GetAmount(coin), Is.EqualTo(5));
            Assert.That(low.GetAmount(energy), Is.EqualTo(125));
            Assert.That(high.GetAmount(energy), Is.EqualTo(127));
            var fixedRandom = new BitsRandom();
            var fixedReward = Profile(new ResourceRewardRange(coin, 7, 7), new ResourceRewardRange(energy, 0, 0)).Roll(fixedRandom);
            Assert.That(fixedReward.GetAmount(coin), Is.EqualTo(7));
            Assert.That(fixedReward.GetAmount(energy), Is.Zero);
            Assert.That(fixedRandom.Calls, Is.Zero);
        }

        [Test]
        public void FullInt64AndNearMaximumRangesDoNotOverflowOrLoseEndpoint()
        {
            var full = Profile(new ResourceRewardRange(coin, 0, long.MaxValue));
            Assert.That(full.Roll(new BitsRandom(0)).GetAmount(coin), Is.Zero);
            Assert.That(full.Roll(new BitsRandom(long.MaxValue)).GetAmount(coin), Is.EqualTo(long.MaxValue));
            var edge = Profile(new ResourceRewardRange(coin, long.MaxValue - 1, long.MaxValue));
            Assert.That(edge.Roll(new BitsRandom(0)).GetAmount(coin), Is.EqualTo(long.MaxValue - 1));
            Assert.That(edge.Roll(new BitsRandom(1)).GetAmount(coin), Is.EqualTo(long.MaxValue));
            using var store = new ResourceStore();
            Assert.That(store.TryGrant(full.Roll(new BitsRandom(long.MaxValue)), out _), Is.True);
            Assert.That(store.GetTotal(coin), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void RejectionSamplingDiscardsTheBiasedTailAndChecksCustomRandom()
        {
            var profile = Profile(new ResourceRewardRange(coin, 0, 2));
            var random = new BitsRandom(long.MaxValue, 2); // 2^63 % 3 == 2: last two samples must be rejected.
            Assert.That(profile.Roll(random).GetAmount(coin), Is.EqualTo(2));
            Assert.That(random.Calls, Is.EqualTo(6));
            Assert.Throws<InvalidOperationException>(() => profile.Roll(new InvalidRandom()));
        }

        [Test]
        public void SeededRandomAndDefaultUnityRandomAreReproducibleWithinBounds()
        {
            var profile = Profile(new ResourceRewardRange(coin, 2, 5), new ResourceRewardRange(energy, 125, 127));
            var a = new System.Random(1729); var b = new System.Random(1729);
            for (var i = 0; i < 40; i++)
            {
                var first = profile.Roll(a); var second = profile.Roll(b);
                Assert.That(first.GetAmount(coin), Is.EqualTo(second.GetAmount(coin)).And.InRange(2, 5));
                Assert.That(first.GetAmount(energy), Is.EqualTo(second.GetAmount(energy)).And.InRange(125, 127));
            }
            var previous = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(42); var first = profile.Roll();
                UnityEngine.Random.InitState(42); var second = profile.Roll();
                Assert.That(second.GetAmount(coin), Is.EqualTo(first.GetAmount(coin)).And.InRange(2, 5));
                Assert.That(second.GetAmount(energy), Is.EqualTo(first.GetAmount(energy)).And.InRange(125, 127));
            }
            finally { UnityEngine.Random.state = previous; }
        }

        [TestCase(-1, 2)]
        [TestCase(3, 2)]
        public void InvalidBoundsAreRejected(long min, long max) => Assert.Throws<ArgumentException>(() => new ResourceRewardRange(coin, min, max));

        [Test]
        public void QuantityConversionRejectsNegativePrecisionLossAndOverflow()
        {
            Assert.Throws<ArgumentException>(() => ResourceRewardRange.FromQuantity(energy, -1m, 2m));
            Assert.Throws<ArgumentException>(() => ResourceRewardRange.FromQuantity(energy, 1.001m, 2m));
            Assert.Throws<OverflowException>(() => ResourceRewardRange.FromQuantity(energy, 0, decimal.MaxValue));
            Assert.Throws<ArgumentException>(() => new ResourceRewardRange(null, 0, 1));
            Assert.Throws<ArgumentNullException>(() => ResourceRewardRange.FromQuantity(null, 0, 1));
            Assert.Throws<ArgumentNullException>(() => ResourceRewardProfile.Create("Missing", database, null));
        }

        [Test]
        public void LateInvalidSerializedRowFailsBeforeConsumingRandom()
        {
            var profile = Profile(new ResourceRewardRange(coin, 1, 2), new ResourceRewardRange(energy, 0, 1));
            var data = new SerializedObject(profile);
            data.FindProperty("ranges").GetArrayElementAtIndex(1).FindPropertyRelative("resource").objectReferenceValue = null;
            data.ApplyModifiedPropertiesWithoutUndo();
            var random = new BitsRandom();
            Assert.Throws<ArgumentException>(() => profile.Roll(random));
            Assert.That(random.Calls, Is.Zero);
        }

        [TestCase(-1, 2)]
        [TestCase(3, 2)]
        public void InvalidSerializedBoundsFailBeforeRandom(long min, long max)
        {
            var profile = Profile(new ResourceRewardRange(coin, 1, 2), new ResourceRewardRange(energy, 0, 1));
            var data = new SerializedObject(profile);
            var row = data.FindProperty("ranges").GetArrayElementAtIndex(1);
            row.FindPropertyRelative("min").longValue = min; row.FindPropertyRelative("max").longValue = max;
            data.ApplyModifiedPropertiesWithoutUndo();
            var random = new BitsRandom();
            Assert.Throws<ArgumentException>(() => profile.Roll(random));
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void DuplicateRowsRollIndependentlyThenSumAndMaximaMustFit()
        {
            var profile = Profile(new ResourceRewardRange(coin, 1, 2), new ResourceRewardRange(coin, 1, 2));
            Assert.That(profile.Roll(new BitsRandom(0, 1)).GetAmount(coin), Is.EqualTo(3));
            var data = new SerializedObject(profile);
            data.FindProperty("ranges").GetArrayElementAtIndex(0).FindPropertyRelative("max").longValue = long.MaxValue;
            data.ApplyModifiedPropertiesWithoutUndo();
            var random = new BitsRandom();
            Assert.Throws<OverflowException>(() => profile.Roll(random));
            Assert.That(random.Calls, Is.Zero);
            Assert.That(Assert.Throws<OverflowException>(() => Profile(new ResourceRewardRange(coin, 0, long.MaxValue - 1),
                new ResourceRewardRange(coin, 0, 1), new ResourceRewardRange(coin, 0, 1))).Message, Does.Contain("Coin"));
        }

        [Test]
        public void DistinctAssetsWithDuplicateIdsAreRejectedEvenForZeroRows()
        {
            var duplicate = ResourceDefinition.Create("Copy", stableId: coin.Id);
            try { Assert.Throws<ArgumentException>(() => Profile(new ResourceRewardRange(coin, 0, 0), new ResourceRewardRange(duplicate, 0, 0))); }
            finally { UnityEngine.Object.DestroyImmediate(duplicate); }
        }

        [Test]
        public void EmptyOrAllZeroRewardSucceedsWithoutEventsOrRevision()
        {
            using var store = new ResourceStore();
            var events = 0; store.Changed += () => events++;
            foreach (var profile in new[] { Profile(), Profile(new ResourceRewardRange(coin, 0, 0)) })
            {
                var reward = profile.Roll(new BitsRandom());
                Assert.That(reward.IsEmpty, Is.True);
                Assert.That(store.TryGrant(reward, out var failure), Is.True);
                Assert.That(failure.HasFailure, Is.False);
            }
            Assert.That(store.Revision, Is.Zero); Assert.That(events, Is.Zero);
        }

        [TestCase("capacity", ResourceFailureCode.ResourceCapacityExceeded)]
        [TestCase("weight", ResourceFailureCode.WeightCapacityExceeded)]
        [TestCase("volume", ResourceFailureCode.VolumeCapacityExceeded)]
        [TestCase("filter", ResourceFailureCode.ResourceNotAccepted)]
        [TestCase("overflow", ResourceFailureCode.AmountOverflow)]
        public void FailedGrantNeverPartiallyCreditsAnyRow(string kind, ResourceFailureCode code)
        {
            var limits = kind == "capacity" ? new ResourceLimits(new[] { new ResourceLimit(energy, 1) }) :
                kind == "weight" ? new ResourceLimits(maximumWeight: 1) :
                kind == "volume" ? new ResourceLimits(maximumVolume: 1) :
                kind == "filter" ? new ResourceLimits(acceptedResources: new[] { coin }) : ResourceLimits.Unlimited;
            var initial = kind == "overflow" ? new ResourceCost(new ResourceAmount(energy, long.MaxValue)) : null;
            using var store = new ResourceStore(initial, limits: limits);
            var reward = Profile(new ResourceRewardRange(coin, 2, 2), new ResourceRewardRange(energy, 2, 2)).Roll();
            var revision = store.Revision; var events = 0; store.Changed += () => events++;
            Assert.That(store.TryGrant(reward, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(code));
            Assert.That(store.GetTotal(coin), Is.Zero);
            Assert.That(store.GetTotal(energy), Is.EqualTo(kind == "overflow" ? long.MaxValue : 0));
            Assert.That(store.Revision, Is.EqualTo(revision)); Assert.That(events, Is.Zero);
        }

        [Test]
        public void GrantPublishesOnceAfterAllRowsAndRejectsReentry()
        {
            var reward = Profile(new ResourceRewardRange(coin, 2, 2), new ResourceRewardRange(energy, 125, 125)).Roll();
            using var store = new ResourceStore();
            var changed = 0; var detailed = 0;
            store.DetailedChanged += change =>
            {
                Assert.That(change.Kind, Is.EqualTo(ResourceChangeKind.Add));
                Assert.That(change.Context.OwnerId, Is.EqualTo("event-1"));
                Assert.That(store.GetTotal(coin), Is.EqualTo(2)); Assert.That(store.GetTotal(energy), Is.EqualTo(125)); detailed++;
            };
            store.Changed += () =>
            {
                Assert.Throws<InvalidOperationException>(() => store.TryGrant(reward, out _)); changed++;
            };
            Assert.That(store.TryGrant(reward, out _, new ResourceOperationContext("reward", "event-1")), Is.True);
            Assert.That(changed, Is.EqualTo(1)); Assert.That(detailed, Is.EqualTo(2)); Assert.That(store.Revision, Is.EqualTo(1));
        }

        [Test]
        public void RolledSnapshotIsIndependentAndCanRetryWithoutRerollOrAssetMutation()
        {
            var source = new[] { new ResourceRewardRange(coin, 2, 5) };
            var profile = Profile(source); var json = EditorJsonUtility.ToJson(profile); var definitionJson = EditorJsonUtility.ToJson(coin);
            source[0] = new ResourceRewardRange(coin, 100, 100);
            var reward = profile.Roll(new BitsRandom(3));
            Assert.That(profile.Roll(new BitsRandom(0)).GetAmount(coin), Is.EqualTo(2));
            Assert.That(reward.GetAmount(coin), Is.EqualTo(5));
            using var store = new ResourceStore(limits: new ResourceLimits(new[] { new ResourceLimit(coin, 4) }));
            Assert.That(store.TryGrant(reward, out _), Is.False);
            store.TrySetLimits(ResourceLimits.Unlimited, out _);
            Assert.That(store.TryGrant(reward, out _), Is.True);
            Assert.That(store.GetTotal(coin), Is.EqualTo(5));
            Assert.That(EditorJsonUtility.ToJson(profile), Is.EqualTo(json));
            Assert.That(EditorJsonUtility.ToJson(coin), Is.EqualTo(definitionJson));
            Assert.Throws<NotSupportedException>(() => ((IList<ResourceRewardRange>)profile.Ranges)[0] = default);
        }

        [Test]
        public void ReadOnlyDisposedWrongThreadAndNullGrantFollowExistingStoreRules()
        {
            var reward = Profile(new ResourceRewardRange(coin, 1, 1)).Roll();
            using var replica = new ResourceReplica(new[] { coin });
            Assert.That(replica.Store.TryGrant(reward, out var failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(ResourceFailureCode.ReadOnly));
            var store = new ResourceStore();
            Assert.Throws<ArgumentNullException>(() => store.TryGrant(null, out _));
            Assert.Throws<InvalidOperationException>(() => Task.Run(() => store.TryGrant(reward, out _)).GetAwaiter().GetResult());
            store.Dispose(); Assert.Throws<ObjectDisposedException>(() => store.TryGrant(reward, out _));
        }

        [Test]
        public void PreparedGrantHasNoManagedAllocationAfterWarmupWithPositiveControl()
        {
            var reward = Profile(new ResourceRewardRange(coin, 1, 1), new ResourceRewardRange(energy, 2, 2)).Roll();
            using var store = new ResourceStore();
            for (var i = 0; i < 64; i++) store.TryGrant(reward, out _);
            const ProfilerRecorderOptions options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            long positive, measured;
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
            {
                GC.KeepAlive(new byte[1024]); recorder.Stop();
                positive = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options))
            {
                for (var i = 0; i < 256; i++) store.TryGrant(reward, out _);
                recorder.Stop(); measured = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
            Assert.That(positive, Is.GreaterThan(0), "GC recorder must see the deliberate allocation.");
            Assert.That(measured, Is.Zero);
            Assert.That(store.GetTotal(coin), Is.EqualTo(320));
            Assert.That(store.GetTotal(energy), Is.EqualTo(640));
        }
    }
}
