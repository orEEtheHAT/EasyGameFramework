using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EasyBuilding
{
    /// <summary>Budgeted append loading. Each small batch is atomic; cancellation preserves already loaded buildings.</summary>
    public sealed class BuildingLoadQueue
    {
        private readonly BuildingSystem system;
        private readonly Queue<BuildingLoadJob> jobs = new Queue<BuildingLoadJob>();
        private readonly Stopwatch clock = new Stopwatch();
        private bool ticking;
        public int Count => jobs.Count;
        public BuildingLoadQueue(BuildingSystem system) => this.system = system != null ? system : throw new ArgumentNullException(nameof(system));
        public BuildingLoadJob Enqueue(BuildingState state, IReadOnlyList<BuildingDefinition> catalog, bool preserveUnknown = true)
        {
            var job = new BuildingLoadJob(state, catalog, preserveUnknown); jobs.Enqueue(job); return job;
        }
        /// <summary>Time is checked between batches. One prefab or callback can exceed the time budget; reduce batchSize for tighter limits.</summary>
        public int Tick(int maximumBuildings = 64, double milliseconds = 2, int batchSize = 8)
        {
            if (maximumBuildings < 1 || milliseconds <= 0 || double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || batchSize < 1)
                throw new ArgumentException("Use positive finite loading budgets.");
            if (ticking || system == null || system.IsBusy) return 0;
            ticking = true; clock.Restart(); int processed = 0;
            try
            {
                while (jobs.Count != 0 && processed < maximumBuildings && clock.Elapsed.TotalMilliseconds < milliseconds)
                {
                    var job = jobs.Peek();
                    if (job.CancelRequested) { job.Cancelled = true; job.IsDone = true; jobs.Dequeue(); continue; }
                    int count = Math.Min(Math.Min(batchSize, maximumBuildings - processed), job.RemainingCount);
                    if (count == 0) { job.IsDone = true; jobs.Dequeue(); continue; }
                    var batch = job.PrepareBatch(count);
                    if (!system.TryRestore(batch, job.Catalog, new BuildingRestoreOptions { DeferMissingConnections = true, PreserveUnknownDefinitions = job.PreserveUnknown }, out var reason))
                    { job.Failure = reason; job.IsDone = true; jobs.Dequeue(); continue; }
                    job.ProcessedCount += count; processed += count;
                    if (job.RemainingCount == 0) { job.IsDone = true; jobs.Dequeue(); }
                }
                return processed;
            }
            finally { clock.Stop(); ticking = false; }
        }
    }
    public sealed class BuildingLoadJob
    {
        private readonly BuildingRecord[] records;
        private readonly Dictionary<string, List<BuildingConnectionRecord>> links = new Dictionary<string, List<BuildingConnectionRecord>>(StringComparer.Ordinal);
        private readonly BuildingState batch;
        private readonly int version;
        internal readonly BuildingDefinition[] Catalog;
        internal readonly bool PreserveUnknown;
        internal bool CancelRequested;
        public int ProcessedCount { get; internal set; }
        public int RemainingCount => records.Length - ProcessedCount;
        public bool IsDone { get; internal set; }
        public bool Cancelled { get; internal set; }
        public string Failure { get; internal set; }
        internal BuildingLoadJob(BuildingState state, IReadOnlyList<BuildingDefinition> catalog, bool preserveUnknown)
        {
            if (state == null || state.Buildings == null || state.Version < 1 || state.Version > BuildingState.CurrentVersion || catalog == null)
                throw new ArgumentException("Use a supported snapshot and definition catalog.");
            version = state.Version; PreserveUnknown = preserveUnknown;
            Catalog = new BuildingDefinition[catalog.Count]; for (int i = 0; i < catalog.Count; i++) Catalog[i] = catalog[i];
            records = new BuildingRecord[state.Buildings.Count]; var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < records.Length; i++)
            {
                var record = state.Buildings[i];
                if (record == null || string.IsNullOrWhiteSpace(record.InstanceId) || !ids.Add(record.InstanceId)) throw new ArgumentException("Snapshot instance IDs must be distinct and nonempty.");
                records[i] = BuildingSystem.CloneRecord(record);
            }
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            if (state.Connections != null) foreach (var link in state.Connections)
            {
                if (link == null || string.IsNullOrWhiteSpace(link.Id) || !linkIds.Add(link.Id) || link.SourceId == null || !ids.Contains(link.SourceId))
                    throw new ArgumentException("Queued connection sources must belong to this snapshot and connection IDs must be distinct.");
                if (!links.TryGetValue(link.SourceId, out var list)) { list = new List<BuildingConnectionRecord>(); links.Add(link.SourceId, list); }
                list.Add(BuildingSystem.CloneLink(link));
            }
            batch = new BuildingState { Version = version, Revision = state.Revision };
            if (state.RedeemedParcels != null) batch.RedeemedParcels.AddRange(state.RedeemedParcels);
        }
        public void Cancel() { if (!IsDone) CancelRequested = true; }
        internal BuildingState PrepareBatch(int count)
        {
            batch.Buildings.Clear(); batch.Connections.Clear();
            for (int i = ProcessedCount; i < ProcessedCount + count; i++)
            { batch.Buildings.Add(records[i]); if (links.TryGetValue(records[i].InstanceId, out var list)) batch.Connections.AddRange(list); }
            return batch;
        }
        public BuildingState CaptureRemaining()
        {
            var output = new BuildingState { Version = version, Revision = batch.Revision };
            output.RedeemedParcels.AddRange(batch.RedeemedParcels);
            for (int i = ProcessedCount; i < records.Length; i++)
            {
                output.Buildings.Add(BuildingSystem.CloneRecord(records[i]));
                if (links.TryGetValue(records[i].InstanceId, out var list)) foreach (var link in list) output.Connections.Add(BuildingSystem.CloneLink(link));
            }
            return output;
        }
    }
}
