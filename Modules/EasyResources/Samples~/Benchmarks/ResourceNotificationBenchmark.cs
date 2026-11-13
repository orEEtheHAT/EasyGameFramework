using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using EasyResources;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EasyResources.IntegrationTests
{
    /// <summary>Local integration audit only. No gameplay input or dependency on a combat implementation.</summary>
    public sealed class ResourceNotificationBenchmark : MonoBehaviour
    {
        [Serializable] public sealed class Measurement
        {
            public string scenario;
            public int iterations, batches;
            public double medianMilliseconds, p95Milliseconds;
            public long allocationEvents;
            public bool invariantsPassed;
        }
        [Serializable] public sealed class Report
        {
            public string unity, utc, backend, cpu;
            public bool player, development;
            public long allocationPositiveControl;
            public List<Measurement> measurements = new List<Measurement>();
            public string error;
        }

        public bool Completed { get; private set; }
        public bool Passed { get; private set; }
        public string ReportPath { get; private set; }
        private long notifications;
        private void CountChange() => notifications++;

        private static ProfilerRecorder Recorder() => ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
            ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

        private IEnumerator Start()
        {
            if (!Application.isEditor) { Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
            var report = new Report { unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("o"),
                backend = "Mono", cpu = SystemInfo.processorType, player = !Application.isEditor, development = Debug.isDebugBuild };
#if ENABLE_IL2CPP
            report.backend = "IL2CPP";
#endif
            using (var recorder = Recorder())
            {
                GC.KeepAlive(new byte[1024]); recorder.Stop();
                report.allocationPositiveControl = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                if (!recorder.Valid || report.allocationPositiveControl == 0) report.error = "GC.Alloc positive control failed.";
            }
            yield return null;
            var ammo = ResourceDefinition.Create("Ammo"); var energy = ResourceDefinition.Create("Energy", decimalPlaces: 2);
            var initial = new ResourceCost(new ResourceAmount(ammo, 1000), new ResourceAmount(energy, 1000));
            var admission = new ResourceCost(new ResourceAmount(ammo, 3), new ResourceAmount(energy, 125));
            var pulse = new ResourceCost(new ResourceAmount(ammo, 1), new ResourceAmount(energy, 25));
            using (var store = new ResourceStore(initial))
            using (var peer = new ResourceStore(initial))
            {
                store.Changed += CountChange;
                var roundTrip = new ResourceTransaction().Transfer(store, peer, admission);
                var returnTrip = new ResourceTransaction().Transfer(peer, store, admission);
                try
                {
                    report.measurements.Add(Measure("legacy-immediate-spend-return", () =>
                    {
                        if (!store.TrySpend(admission)) throw new Exception("Legacy payment failed.");
                        store.Add(ammo, 3); store.Add(energy, 125);
                    }, () => store.GetTotal(ammo) == 1000 && store.GetTotal(energy) == 1000 && store.ActiveReservationCount == 0));
                    report.measurements.Add(Measure("nested-admission-pulse-and-refund", () =>
                    {
                        var parent = store.DeferNotifications(new ResourceOperationContext("ability"));
                        if (!store.TryReserve(admission, out var first)) throw new Exception("Admission failed.");
                        var child = store.DeferNotifications();
                        if (!store.TryReserve(pulse, out var second)) throw new Exception("Pulse failed.");
                        if (!second.TryCommit(out _)) throw new Exception("Pulse commit failed."); child.Dispose();
                        if (!first.TryCommit(out _)) throw new Exception("Admission commit failed."); parent.Dispose();
                        store.Add(ammo, 4); store.Add(energy, 150);
                    }, () => store.GetTotal(ammo) == 1000 && store.GetTotal(energy) == 1000 && store.ActiveReservationCount == 0));
                    report.measurements.Add(Measure("cancelled-admission-no-events", () =>
                    {
                        var before = notifications; var scope = store.DeferNotifications();
                        if (!store.TryReserve(admission, out var payment) || !payment.Cancel()) throw new Exception("Cancellation failed.");
                        scope.Dispose(); if (notifications != before) throw new Exception("Cancelled admission published an event.");
                    }, () => store.GetTotal(ammo) == 1000 && store.GetTotal(energy) == 1000 && store.ActiveReservationCount == 0));
                    report.measurements.Add(Measure("multi-store-transaction-roundtrip", () =>
                    {
                        if (!roundTrip.TryExecute() || !returnTrip.TryExecute()) throw new Exception("Transfer failed.");
                    }, () => store.GetTotal(ammo) == 1000 && peer.GetTotal(ammo) == 1000 && store.GetTotal(energy) == 1000 && peer.GetTotal(energy) == 1000));
                }
                catch (Exception exception) { report.error = exception.ToString(); Debug.LogException(exception); }
            }
            Destroy(ammo); Destroy(energy);
            ReportPath = OutputPath(); Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            Passed = string.IsNullOrEmpty(report.error) && report.measurements.Count == 4;
            foreach (var row in report.measurements) Passed &= row.invariantsPassed && row.allocationEvents == 0;
            Completed = true; Debug.Log("EasyResources integration benchmark: " + (Passed ? "PASS" : "FAIL") + " — " + ReportPath);
            if (!Application.isEditor) Application.Quit(Passed ? 0 : 1);
        }

        private static Measurement Measure(string name, Action iteration, Func<bool> invariant)
        {
            const int batches = 15, count = 1000;
            for (var i = 0; i < 128; i++) iteration();
            var times = new double[batches]; long allocations = 0;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            for (var batch = 0; batch < batches; batch++)
                using (var recorder = Recorder())
                {
                    var start = Stopwatch.GetTimestamp();
                    for (var i = 0; i < count; i++) iteration();
                    var elapsed = Stopwatch.GetTimestamp() - start; recorder.Stop();
                    allocations += recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
                    times[batch] = elapsed * 1000d / Stopwatch.Frequency;
                }
            if (!invariant()) throw new Exception("Balance invariant failed: " + name);
            Array.Sort(times);
            return new Measurement { scenario = name, iterations = count, batches = batches, medianMilliseconds = times[batches / 2],
                p95Milliseconds = times[batches - 1], allocationEvents = allocations, invariantsPassed = true };
        }

        private static string OutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++) if (args[i] == "-resourceNotificationReport") return Path.GetFullPath(args[i + 1]);
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(project, "Audits/EasyResources/IntegrationWave1/notifications-editor.json");
        }
    }
}
