using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Unity.Profiling;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace EasyBuilding.Samples
{
    /// <summary>Standalone CPU benchmark. Its JSON is the evidence, not an assertion of universal frame rates.</summary>
    public sealed class BuildingBenchmark : MonoBehaviour
    {
        [Serializable] public sealed class Metric
        {
            public string Name;
            public int Iterations;
            public double MeanMicroseconds;
            public double P95Microseconds;
            public long AllocatedBytes;
            public long AllocationEvents;
            public long AllocationCalibrationBytes;
        }
        [Serializable] public sealed class Scenario
        {
            public string Name;
            public int Cells, Chunks;
            public int Buildings, Sockets, Zones;
            public long MonoUsedBytes, TotalAllocatedBytes, TotalReservedBytes;
            public List<Metric> Metrics = new List<Metric>();
        }
        [Serializable] public sealed class Report
        {
            public string Utc, Unity, Platform, Cpu, Graphics;
            public bool DevelopmentBuild;
            public string Backend;
            public List<Scenario> Scenarios = new List<Scenario>();
        }
        private readonly List<GameObject> sceneObjects = new List<GameObject>();
        private readonly List<ScriptableObject> assets = new List<ScriptableObject>();
        private readonly List<BuildingSocket> socketList = new List<BuildingSocket>();
        private BuildingSystem system;
        private BuildingPlacer placer;
        private BuildingDefinition definition;
        private GameObject prefab;
        private Collider[] hits = new Collider[32];
        private BuildingSocket baselineCandidate;
        private int sink;

        private IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            var report = new Report
            {
                Utc = DateTime.UtcNow.ToString("o"), Unity = Application.unityVersion, Platform = Application.platform.ToString(),
                Cpu = SystemInfo.processorType, Graphics = SystemInfo.graphicsDeviceType.ToString(), DevelopmentBuild = Debug.isDebugBuild,
#if ENABLE_IL2CPP
                Backend = "IL2CPP"
#else
                Backend = "Mono"
#endif
            };
            foreach (int count in new[] { 100, 5000 })
            {
                BuildScenario(count);
                yield return null;
                for (int i = 0; i < 50; i++) PreviewStep(i);
                var scenario = new Scenario { Buildings = count, Sockets = count, Zones = count / 5 };
                scenario.Metrics.Add(Measure("Preview (socket search + zone + overlap + mesh pose)", count > 100 ? 300 : 1000, PreviewStep));
                definition.Placement = PlacementMode.Free; definition.RequireZone = false;
                scenario.Metrics.Add(Measure("Preview (free + overlap + mesh pose)", 1000, SurfacePreviewStep));
                definition.Placement = PlacementMode.Grid; definition.RequireZone = true;
                scenario.Metrics.Add(Measure("Preview (grid + zone + overlap + mesh pose)", 1000, SurfacePreviewStep));
                definition.Placement = PlacementMode.Socket;
                scenario.Metrics.Add(Measure("Socket search: native broadphase", 500, i => baselineCandidate = system.FindSocket(Query(i), 2f)));
                system.UseSpatialQueries = false;
                scenario.Metrics.Add(Measure("Socket search: sequential reference", 500, i => baselineCandidate = system.FindSocket(Query(i), 2f)));
                system.UseSpatialQueries = true;
                scenario.Metrics.Add(Measure("Socket search baseline: uncached registry", 500, UncachedRegistrySearch));
                scenario.Metrics.Add(Measure("Socket search baseline: FindObjectsByType", 100, BaselineSocketSearch));
                // Both implementations return the same nearest socket, including after a transform moves.
                for (int i = 0; i < 40; i++)
                {
                    var current = socketList[i]; current.transform.position += Vector3.right * 0.2f;
                    BaselineSocketSearch(i);
                    if (baselineCandidate != system.FindSocket(Query(i), 2f)) throw new InvalidOperationException("Socket search results differ.");
                    UncachedRegistrySearch(i);
                    if (baselineCandidate != system.FindSocket(Query(i), 2f)) throw new InvalidOperationException("Cached handles changed socket search results.");
                }
                Physics.SyncTransforms();
                scenario.Metrics.Add(Measure("Obstacle query: CheckBox", 1000, i => sink = Physics.CheckBox(Query(i), Vector3.one, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore) ? 1 : 0));
                scenario.Metrics.Add(Measure("OverlapBoxNonAlloc", 1000, i => sink = Physics.OverlapBoxNonAlloc(Query(i), Vector3.one, hits, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)));
                scenario.Metrics.Add(Measure("OverlapBox allocating baseline", 1000, i => sink = Physics.OverlapBox(Query(i), Vector3.one, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Length));
                var saved = system.CaptureState();
                foreach (var record in saved.Buildings) { system.TryGet(record.InstanceId, out var item); system.Remove(item); }
                yield return null;
                var catalog = new[] { definition };
                scenario.Metrics.Add(Measure("Mass restore (one full batch)", 1, _ =>
                { if (!system.TryRestore(saved, catalog, out var reason)) throw new InvalidOperationException(reason); } , false));
                scenario.MonoUsedBytes = Profiler.GetMonoUsedSizeLong();
                scenario.TotalAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                scenario.TotalReservedBytes = Profiler.GetTotalReservedMemoryLong();
                report.Scenarios.Add(scenario);
                ClearScenario(); yield return null; yield return null;
            }
            string path = OutputPath(); Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("EASYBUILDING_BENCHMARK_COMPLETE " + path);
            if (!Application.isEditor) Application.Quit(0);
        }
        private void BuildScenario(int count)
        {
            var go = Make("Benchmark system"); system = go.AddComponent<BuildingSystem>(); placer = go.AddComponent<BuildingPlacer>(); placer.ShowHelp = false; placer.enabled = false;
            prefab = Make("Source prefab"); prefab.SetActive(false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(prefab.transform, false); cube.transform.localPosition = new Vector3(0, 0.5f, 0);
            var socket = new GameObject("Socket"); socket.transform.SetParent(prefab.transform, false); socket.transform.localPosition = Vector3.up; socket.AddComponent<BuildingSocket>();
            definition = ScriptableObject.CreateInstance<BuildingDefinition>(); assets.Add(definition); definition.Prefab = prefab;
            definition.RequireSurface = false;
            var state = new BuildingState();
            for (int i = 0; i < count; i++) state.Buildings.Add(new BuildingRecord
            { InstanceId = "bench-" + i, DefinitionId = definition.Id, Position = new Vector3((i % 100) * 3, 0, (i / 100) * 3), Rotation = Quaternion.identity });
            if (!system.TryRestore(state, new[] { definition }, out var reason)) throw new InvalidOperationException(reason);
            foreach (var record in state.Buildings) { system.TryGet(record.InstanceId, out var building); socketList.Add(building.GetComponentInChildren<BuildingSocket>()); }
            for (int i = 0; i < count / 5; i++)
            {
                var zone = Make("Zone").AddComponent<BuildingZone>(); zone.transform.position = new Vector3((i % 20) * 15 + 6, 0, (i / 20) * 3);
                zone.Size = new Vector3(15, 6, 3); zone.Center = new Vector3(0, 3, 0);
            }
            definition.Placement = PlacementMode.Socket; definition.RequireZone = true;
            system.Select(definition); Physics.SyncTransforms();
        }
        private Vector3 Query(int i) => socketList[(i * 47) % socketList.Count].transform.position;
        private void PreviewStep(int i)
        {
            var point = Query(i); var socket = system.FindSocket(point, 2);
            var result = placer.UpdatePreview(new PlacementRequest(point, Quaternion.identity, socket));
            if (!result.IsValid) throw new InvalidOperationException(result.Reason);
        }
        private void SurfacePreviewStep(int i)
        {
            var point = Query(i) + new Vector3(0, -1, 1);
            var result = placer.UpdatePreview(new PlacementRequest(point, Quaternion.identity));
            if (!result.IsValid) throw new InvalidOperationException(result.Reason);
        }
        private void BaselineSocketSearch(int index)
        {
            var point = Query(index); float best = 4; baselineCandidate = null;
            foreach (var socket in Object.FindObjectsByType<BuildingSocket>(FindObjectsSortMode.None))
            {
                if (socket.SocketType != definition.SocketType) continue;
                float distance = (socket.transform.position - point).sqrMagnitude;
                if (distance < best) { best = distance; baselineCandidate = socket; }
            }
        }
        private void UncachedRegistrySearch(int index)
        {
            var point = Query(index); float best = 4; baselineCandidate = null;
            for (int i = 0; i < socketList.Count; i++)
            {
                var socket = socketList[i];
                if (socket == null || socket.gameObject.scene != system.gameObject.scene || socket.SocketType != definition.SocketType) continue;
                float distance = (socket.transform.position - point).sqrMagnitude;
                if (distance < best) { best = distance; baselineCandidate = socket; }
            }
        }
        public static Metric Measure(string name, int count, Action<int> action, bool warmup = true)
        {
            if (warmup) for (int i = 0; i < 10; i++) action(i);
            var samples = new double[count];
            bool previous = Profiler.enabled;
            Profiler.enabled = true;
            try
            {
                var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.WrapAroundWhenCapacityReached;
                using var events = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1, options);
                using var bytes = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
                if (!events.Valid || !bytes.Valid) throw new InvalidOperationException("Unity allocation counters are unavailable. Use a Development Build.");
                events.Start();
                long calibrationStart = bytes.CurrentValue;
                var positiveControl = new byte[4096];
                long calibrationBytes = bytes.CurrentValue - calibrationStart;
                events.Stop(); GC.KeepAlive(positiveControl);
                if (events.Count == 0 || events.GetSample(0).Count != 1 || calibrationBytes < 4096)
                    throw new InvalidOperationException("Allocation counter calibration failed; do not report zero allocations.");
                events.Reset(); events.Start();
                long before = bytes.CurrentValue;
                double total = 0;
                for (int i = 0; i < count; i++)
                {
                    long start = Stopwatch.GetTimestamp(); action(i);
                    samples[i] = (Stopwatch.GetTimestamp() - start) * 1000000.0 / Stopwatch.Frequency; total += samples[i];
                }
                long allocated = bytes.CurrentValue - before;
                events.Stop();
                long eventCount = events.Count > 0 ? events.GetSample(0).Count : 0;
                Array.Sort(samples);
                return new Metric { Name = name, Iterations = count, MeanMicroseconds = total / count,
                    P95Microseconds = samples[Math.Min(count - 1, (int)(count * 0.95))], AllocatedBytes = allocated,
                    AllocationEvents = eventCount, AllocationCalibrationBytes = calibrationBytes };
            }
            finally { Profiler.enabled = previous; }
        }
        private GameObject Make(string name) { var go = new GameObject(name); sceneObjects.Add(go); return go; }
        private void ClearScenario()
        {
            var saved = system.CaptureState(); foreach (var record in saved.Buildings) { system.TryGet(record.InstanceId, out var item); system.Remove(item); }
            foreach (var go in sceneObjects) if (go != null) Destroy(go); sceneObjects.Clear();
            foreach (var asset in assets) Destroy(asset); assets.Clear(); socketList.Clear();
        }
        private static string OutputPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-easyBuildingReport") return Path.GetFullPath(args[i + 1]);
            return Path.Combine(Application.persistentDataPath, "EasyBuildingBenchmark.json");
        }
    }
}
