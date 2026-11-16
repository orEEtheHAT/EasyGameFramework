using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace EasyResources.Tests
{
    public sealed class ResourceDatabaseScalingTests
    {
        private const ProfilerRecorderOptions RecorderOptions = ProfilerRecorderOptions.SumAllSamplesInFrame |
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread;

        [Serializable]
        private sealed class RollMeasurement
        {
            public int databaseRecords, rewardRows, warmupIterations, batches, iterationsPerBatch;
            public long allocationEvents, checksum;
            public double medianMillisecondsPerBatch, maxMillisecondsPerBatch;
        }

        [Serializable]
        private sealed class ScalingReport
        {
            public string unity, cpu, utc;
            public bool editor;
            public int databaseRecords, lookupIterations;
            public long positiveControlAllocationEvents, warmedLookupAllocationEvents;
            public double lookupMilliseconds;
            public RollMeasurement oneRecordRoll, thousandRecordRoll;
            public string note;
        }

        private static long SampleCount(ProfilerRecorder recorder) => recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;

        [Test]
        public void ThousandRecordWarmLookupsAllocateNothingAndOneRowRollKeepsSameWork()
        {
            const int records = 1000, lookupIterations = 32000;
            var resources = new ResourceDefinition[records];
            ResourceDatabase small = null, large = null;
            ResourceRewardProfile smallProfile = null, largeProfile = null;
            try
            {
                for (var i = 0; i < records; i++) resources[i] = ResourceDefinition.Create("Scaling " + i);
                small = ResourceDatabase.Create("One record", resources[0]);
                large = ResourceDatabase.Create("Thousand records", resources);
                smallProfile = ResourceRewardProfile.Create("One-row reward", small, new ResourceRewardRange(resources[0], 3, 7));
                largeProfile = ResourceRewardProfile.Create("One-row reward", large, new ResourceRewardRange(resources[0], 3, 7));
                var last = resources[records - 1]; var lastId = last.Id;
                for (var i = 0; i < 256; i++)
                {
                    large.ValidateConfiguration(); large.Contains(last); large.TryGet(lastId, out _);
                    large.TryGet("absent-scaling-id", out _);
                }

                var report = new ScalingReport
                {
                    unity = Application.unityVersion, cpu = SystemInfo.processorType,
                    utc = DateTime.UtcNow.ToString("o"), editor = Application.isEditor,
                    databaseRecords = records, lookupIterations = lookupIterations,
                    note = "Equal one-row roll work and seeds; Roll owns an allocating snapshot. Timings are observations, not a tight performance threshold. Lookups and validation are measured after warmup."
                };
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, RecorderOptions))
                {
                    GC.KeepAlive(new byte[1024]); recorder.Stop();
                    report.positiveControlAllocationEvents = SampleCount(recorder);
                }
                var valid = true;
                using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, RecorderOptions))
                {
                    var started = Stopwatch.GetTimestamp();
                    for (var i = 0; i < lookupIterations; i++)
                    {
                        large.ValidateConfiguration();
                        valid &= large.Contains(last);
                        valid &= large.TryGet(lastId, out var found) && ReferenceEquals(found, last);
                        valid &= !large.TryGet("absent-scaling-id", out _);
                    }
                    var elapsed = Stopwatch.GetTimestamp() - started;
                    recorder.Stop();
                    report.warmedLookupAllocationEvents = SampleCount(recorder);
                    report.lookupMilliseconds = elapsed * 1000d / Stopwatch.Frequency;
                }
                report.oneRecordRoll = MeasureRoll(smallProfile, 1);
                report.thousandRecordRoll = MeasureRoll(largeProfile, records);
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Audits/EasyResources/Database-20261116"));
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "scaling.json"), JsonUtility.ToJson(report, true));

                Assert.That(valid, Is.True, "Every measured lookup must return the expected direct record.");
                Assert.That(report.positiveControlAllocationEvents, Is.GreaterThan(0));
                Assert.That(report.warmedLookupAllocationEvents, Is.Zero);
                Assert.That(report.thousandRecordRoll.checksum, Is.EqualTo(report.oneRecordRoll.checksum));
                Assert.That(report.thousandRecordRoll.allocationEvents, Is.EqualTo(report.oneRecordRoll.allocationEvents),
                    "Unrelated database records must not add per-roll allocation work.");
                Assert.That(report.oneRecordRoll.allocationEvents, Is.GreaterThan(0), "Roll creates an independent owned result.");
            }
            finally
            {
                if (smallProfile != null) UnityEngine.Object.DestroyImmediate(smallProfile);
                if (largeProfile != null) UnityEngine.Object.DestroyImmediate(largeProfile);
                if (small != null) UnityEngine.Object.DestroyImmediate(small);
                if (large != null) UnityEngine.Object.DestroyImmediate(large);
                foreach (var resource in resources) if (resource != null) UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        private static RollMeasurement MeasureRoll(ResourceRewardProfile profile, int records)
        {
            const int batches = 8, iterations = 128, warmup = 64;
            var random = new System.Random(1729);
            for (var i = 0; i < warmup; i++) profile.Roll(random);
            var resource = profile.Ranges[0].Resource;
            var times = new double[batches];
            long checksum = 0, allocations = 0;
            for (var batch = 0; batch < batches; batch++)
            {
                using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, RecorderOptions);
                var started = Stopwatch.GetTimestamp();
                for (var i = 0; i < iterations; i++) checksum += profile.Roll(random).GetAmount(resource);
                var elapsed = Stopwatch.GetTimestamp() - started;
                recorder.Stop(); allocations += SampleCount(recorder);
                times[batch] = elapsed * 1000d / Stopwatch.Frequency;
            }
            Array.Sort(times);
            return new RollMeasurement
            {
                databaseRecords = records, rewardRows = 1, warmupIterations = warmup,
                batches = batches, iterationsPerBatch = iterations, allocationEvents = allocations,
                checksum = checksum, medianMillisecondsPerBatch = times[batches / 2],
                maxMillisecondsPerBatch = times[batches - 1]
            };
        }

        [Test]
        public void ExplicitInvalidationRebuildsLookupAfterControlledRecordArrayReplacement()
        {
            var first = ResourceDefinition.Create("First"); var second = ResourceDefinition.Create("Second");
            var database = ResourceDatabase.Create("Resources", first);
            try
            {
                Assert.That(database.TryGet(first.Id, out _), Is.True);
                var data = new UnityEditor.SerializedObject(database);
                data.FindProperty("resources").GetArrayElementAtIndex(0).objectReferenceValue = second;
                data.ApplyModifiedPropertiesWithoutUndo();
                database.InvalidateConfiguration();
                database.ValidateConfiguration();
                Assert.That(database.Contains(first), Is.False); Assert.That(database.Contains(second), Is.True);
                Assert.That(database.TryGet(first.Id, out _), Is.False);
                Assert.That(database.TryGet(second.Id, out var found), Is.True); Assert.That(found, Is.SameAs(second));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(database); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second);
            }
        }
    }
}
