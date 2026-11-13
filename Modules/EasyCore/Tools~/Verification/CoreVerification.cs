using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EasyCore.Samples;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace EasyCore.Verification
{
    /// <summary>Local standalone verification harness, excluded from the installed runtime.</summary>
    public sealed class CoreVerification : MonoBehaviour
    {
        [Serializable]
        public sealed class Report
        {
            public int schemaVersion = 2;
            public string executionMode = "Synchronous main-thread loops; no Jobs; frame clocks sampled without yielding";
            public string fixture = "Numeric boundaries v2: cycles of signed zero, signed epsilon, signed max, NaN, signed infinity, -1, 0.25, 42; double uses its own epsilon/max; Vector3 uses consecutive float cycle entries";
            public int warmupIterations = 4, timingRepetitions = 16;
            public double manualAdvanceSeconds = .001;
            public string unity, platform, cpu, backend, utc;
            public bool development, passed, firstRun, pausedScaled, advancingUnscaled, resumedScaled;
            public bool allocationMeterValid;
            public long positiveControlEvents, emptyControlEvents;
            public string allocationUnits = "main-thread GC.Alloc event count; not bytes";
            public List<Row> measurements = new List<Row>();
        }

        [Serializable]
        public sealed class Row
        {
            public string operation;
            public int operations, repetitions;
            public double checksum;
            public double totalMilliseconds, nanosecondsPerOperation;
            public long allocationEvents;
        }

        private const ProfilerRecorderOptions RecorderOptions = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
        private static double sink;
        private bool allocationMeterValid;

        private IEnumerator Start()
        {
            yield return null; // All sample Start callbacks have completed.
            var sample = GetComponent<CoreFirstRun>();
            var report = new Report
            {
                unity = Application.unityVersion, platform = Application.platform.ToString(),
                cpu = SystemInfo.processorType, development = Debug.isDebugBuild,
#if ENABLE_IL2CPP
                backend = "IL2CPP",
#else
                backend = "Mono",
#endif
                utc = DateTime.UtcNow.ToString("o"),
                firstRun = sample != null && sample.Completed && sample.Checks == 8 && sample.Failures == 0
            };
            float savedScale = Time.timeScale;
            var scaled = new ScaledClock();
            var unscaled = new UnscaledClock();
            try
            {
                Time.timeScale = 0;
                yield return null;
                double gameStart = scaled.NowSeconds, uiStart = unscaled.NowSeconds;
                yield return null;
                yield return null;
                report.pausedScaled = scaled.NowSeconds == gameStart;
                report.advancingUnscaled = unscaled.NowSeconds > uiStart;
                Time.timeScale = 1;
                yield return null;
                yield return null;
                report.resumedScaled = scaled.NowSeconds > gameStart;
            }
            finally { Time.timeScale = savedScale; }

            bool savedProfiler = Profiler.enabled;
            try
            {
                Profiler.enabled = true;
                CountAllocations(() => GC.KeepAlive(new byte[512])); // warm the measurement wrapper
                report.positiveControlEvents = CountAllocations(() => GC.KeepAlive(new byte[512]));
                report.emptyControlEvents = CountAllocations(() => { });
                allocationMeterValid = report.positiveControlEvents > 0 && report.emptyControlEvents == 0;
                report.allocationMeterValid = allocationMeterValid;

                foreach (int count in new[] { 32, 100000 })
                {
                    var floatCycle = new[] { 0f, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, -float.MaxValue,
                        float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, .25f, 42f };
                    var doubleCycle = new[] { 0d, -0d, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue,
                        double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1d, .25d, 42d };
                    var data = new float[count];
                    var doubles = new double[count];
                    var vectors = new Vector3[count];
                    for (int i = 0; i < count; i++)
                    {
                        data[i] = floatCycle[i % floatCycle.Length];
                        doubles[i] = doubleCycle[i % doubleCycle.Length];
                        vectors[i] = new Vector3(data[i], floatCycle[(i + 1) % floatCycle.Length], floatCycle[(i + 2) % floatCycle.Length]);
                    }
                    Action predicates = () =>
                    {
                        int accepted = 0;
                        for (int i = 0; i < data.Length; i++)
                        {
                            if (Numeric.IsFinite(data[i])) accepted++;
                            if (Numeric.IsPositive(data[i])) accepted++;
                            if (Numeric.IsNonNegative(data[i])) accepted++;
                            if (Numeric.IsFinite(doubles[i])) accepted++;
                            if (Numeric.IsPositive(doubles[i])) accepted++;
                            if (Numeric.IsNonNegative(doubles[i])) accepted++;
                            if (Numeric.IsFinite(vectors[i])) accepted++;
                        }
                        sink = accepted;
                    };
                    Action baseline = () =>
                    {
                        int accepted = 0;
                        for (int i = 0; i < data.Length; i++)
                        {
                            float value = data[i];
                            double twice = doubles[i];
                            var vector = vectors[i];
                            if (!float.IsNaN(value) && !float.IsInfinity(value)) accepted++;
                            if (!float.IsNaN(value) && !float.IsInfinity(value) && value > 0) accepted++;
                            if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0) accepted++;
                            if (!double.IsNaN(twice) && !double.IsInfinity(twice)) accepted++;
                            if (!double.IsNaN(twice) && !double.IsInfinity(twice) && twice > 0) accepted++;
                            if (!double.IsNaN(twice) && !double.IsInfinity(twice) && twice >= 0) accepted++;
                            if (!float.IsNaN(vector.x) && !float.IsInfinity(vector.x) &&
                                !float.IsNaN(vector.y) && !float.IsInfinity(vector.y) &&
                                !float.IsNaN(vector.z) && !float.IsInfinity(vector.z)) accepted++;
                        }
                        sink = accepted;
                    };
                    predicates(); double expected = sink; baseline();
                    if (sink != expected) throw new InvalidOperationException("Numeric baseline mismatch.");
                    report.measurements.Add(Measure("All 7 Numeric predicates per item", count, predicates));
                    report.measurements.Add(Measure("Equivalent 7-predicate inline baseline", count, baseline));
                    var manual = new ManualClock();
                    IClock manualInterface = manual;
                    report.measurements.Add(Measure("ManualClock Advance/read", count, () =>
                    {
                        double total = 0;
                        for (int i = 0; i < count; i++) { manual.Advance(report.manualAdvanceSeconds); total += manualInterface.NowSeconds; }
                        sink = total;
                    }));
                    report.measurements.Add(Measure("Unity clocks read", count, () =>
                    {
                        double total = 0;
                        for (int i = 0; i < count; i++) total += scaled.NowSeconds + unscaled.NowSeconds;
                        sink = total;
                    }));
                }
            }
            finally { Profiler.enabled = savedProfiler; }
            report.passed = report.firstRun && report.pausedScaled && report.advancingUnscaled && report.resumedScaled && report.allocationMeterValid;
            foreach (var row in report.measurements) report.passed &= row.allocationEvents == 0;
            string json = JsonUtility.ToJson(report, true);
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--easycore-report") File.WriteAllText(args[i + 1], json);
            Debug.Log("EASYCORE_VERIFICATION " + json);
            if (Array.IndexOf(args, "--easycore-auto-quit") >= 0) Application.Quit(report.passed ? 0 : 1);
        }

        private static long CountAllocations(Action action)
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, RecorderOptions))
            {
                if (!recorder.Valid || !Profiler.enabled) return -1;
                action();
                recorder.Stop();
                return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
        }

        private Row Measure(string operation, int operations, Action action)
        {
            const int repetitions = 16;
            for (int i = 0; i < 4; i++) action();
            long allocationEvents = allocationMeterValid ? CountAllocations(action) : -1;
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < repetitions; i++) action();
            double seconds = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            return new Row
            {
                operation = operation, operations = operations, repetitions = repetitions,
                checksum = sink,
                totalMilliseconds = seconds * 1000,
                nanosecondsPerOperation = seconds * 1e9 / (operations * repetitions),
                allocationEvents = allocationEvents
            };
        }
    }
}
