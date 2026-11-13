using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public readonly struct BuildingInterest
    {
        public readonly Vector3 Position;
        public readonly float Radius;
        public readonly string SpaceId;
        public BuildingInterest(Vector3 position, float radius, string spaceId = "")
        { Position = position; Radius = radius; SpaceId = spaceId ?? ""; }
    }
    /// <summary>In-memory region store and interest loader. Persist CaptureAll with your save backend.
    /// Region membership uses world coordinates or a Building Space's local coordinates.</summary>
    public sealed class BuildingRegions : IDisposable, IBuildingRule
    {
        private readonly BuildingSystem system;
        private IReadOnlyList<BuildingDefinition> catalog;
        private readonly Dictionary<string, BuildingRecord> archived = new Dictionary<string, BuildingRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, BuildingConnectionRecord> links = new Dictionary<string, BuildingConnectionRecord>(StringComparer.Ordinal);
        private readonly HashSet<string> redeemed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, BuildingRecord> live = new Dictionary<string, BuildingRecord>(StringComparer.Ordinal);
        private readonly List<string> unload = new List<string>();
        private readonly HashSet<(string, Vector3Int)> blockedCells = new HashSet<(string, Vector3Int)>();
        private readonly HashSet<string> blockedSpaces = new HashSet<string>(StringComparer.Ordinal);
        private BuildingLoadQueue queue;
        private BuildingLoadJob job;
        private bool dirty = true, disposed, changingInterest;
        public float RegionSize { get; }
        public string Failure { get; private set; }
        public int ArchivedCount => archived.Count;
        public bool IsLoading => job != null && !job.IsDone;

        public BuildingRegions(BuildingSystem system, IReadOnlyList<BuildingDefinition> catalog, float regionSize = 64)
        {
            this.system = system != null ? system : throw new ArgumentNullException(nameof(system));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (!BuildingMath.Finite(regionSize) || regionSize <= 0) throw new ArgumentOutOfRangeException(nameof(regionSize));
            RegionSize = regionSize; queue = new BuildingLoadQueue(system); system.Changed += Changed; system.Restored += Restored; system.AddRule(this);
        }
        private void Changed(BuildingChangeSet change) => dirty = true;
        private void Restored() => dirty = true;
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(BuildingRegions)); }
        public void RefreshCatalog(IReadOnlyList<BuildingDefinition> definitions)
        {
            Check(); if (system.IsBusy || IsLoading || changingInterest) throw new InvalidOperationException("Finish loading before changing the region catalog.");
            catalog = definitions ?? throw new ArgumentNullException(nameof(definitions)); dirty = true; Refresh();
        }
        private void Refresh()
        {
            if (!dirty) return;
            var snapshot = system.CaptureState(); live.Clear();
            foreach (var record in snapshot.Buildings) { live[record.InstanceId] = record; archived.Remove(record.InstanceId); }
            // Links with both endpoints live are authoritative; forget disconnected links.
            var remove = new List<string>();
            foreach (var pair in links)
            {
                var link = pair.Value;
                if (!live.ContainsKey(link.SourceId) && !archived.ContainsKey(link.SourceId) ||
                    !string.IsNullOrEmpty(link.TargetId) && !live.ContainsKey(link.TargetId) && !archived.ContainsKey(link.TargetId) ||
                    live.ContainsKey(link.SourceId) && (string.IsNullOrEmpty(link.TargetId) || live.ContainsKey(link.TargetId))) remove.Add(pair.Key);
            }
            foreach (var id in remove) links.Remove(id);
            foreach (var link in snapshot.Connections) links[link.Id] = link;
            foreach (var id in snapshot.RedeemedParcels) redeemed.Add(id);
            RebuildBlocked(); dirty = false;
        }
        /// <summary>Add saved, currently unloaded records. Existing IDs are rejected. Does not instantiate prefabs.</summary>
        public void Add(BuildingState snapshot)
        {
            Check(); Refresh();
            if (snapshot == null || snapshot.Version != BuildingState.CurrentVersion || snapshot.Buildings == null || snapshot.Connections == null || snapshot.RedeemedParcels == null)
                throw new ArgumentException("Use a complete snapshot in the current format before adding regions.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in snapshot.Buildings)
                if (record == null || string.IsNullOrEmpty(record.InstanceId) || string.IsNullOrEmpty(record.DefinitionId) || !ids.Add(record.InstanceId) || live.ContainsKey(record.InstanceId) || archived.ContainsKey(record.InstanceId) ||
                    !BuildingMath.Finite(record.Position) || !BuildingMath.Finite(record.LocalPosition) || !BuildingMath.ValidRotation(record.Rotation) || !BuildingMath.ValidRotation(record.LocalRotation) ||
                    !BuildingMath.Finite(record.Scale) || record.Scale.x <= 0 || record.Scale.y <= 0 || record.Scale.z <= 0) throw new ArgumentException("Region records need distinct IDs and valid finite poses/scales.");
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in snapshot.Connections)
                if (link == null || string.IsNullOrEmpty(link.Id) || string.IsNullOrEmpty(link.SourceId) || string.IsNullOrEmpty(link.TargetSocketId) || link.SourceId == link.TargetId ||
                    !linkIds.Add(link.Id) || links.ContainsKey(link.Id)) throw new ArgumentException("Region connections need valid endpoints and distinct IDs.");
            foreach (var id in snapshot.RedeemedParcels) if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Redeemed parcel IDs must be nonempty.");
            foreach (var record in snapshot.Buildings) archived.Add(record.InstanceId, BuildingSystem.CloneRecord(record));
            foreach (var link in snapshot.Connections) links.Add(link.Id, BuildingSystem.CloneLink(link));
            foreach (var id in snapshot.RedeemedParcels) redeemed.Add(id);
            RebuildBlocked();
        }
        private void RebuildBlocked()
        {
            blockedCells.Clear(); blockedSpaces.Clear();
            var definitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal); foreach (var definition in catalog) if (definition != null) definitions[definition.Id] = definition;
            foreach (var record in live.Values) if (!definitions.ContainsKey(record.DefinitionId)) blockedSpaces.Add(record.SpaceId ?? "");
            foreach (var record in archived.Values)
            {
                string spaceId = record.SpaceId ?? "";
                if (!definitions.TryGetValue(record.DefinitionId, out var definition)) { blockedSpaces.Add(spaceId); continue; }
                var space = system.ResolveSavedSpace(spaceId, true);
                var pose = spaceId.Length == 0 ? new Pose(record.Position, record.Rotation) : new Pose(record.LocalPosition, record.LocalRotation);
                Vector3 scale = record.Scale / (space != null ? space.transform.lossyScale.x : 1);
                for (int i = 0; i < definition.VolumeCount + (definition.Clearances?.Length ?? 0); i++)
                {
                    var volume = i < definition.VolumeCount ? definition.GetVolume(i) : definition.Clearances[i - definition.VolumeCount];
                    var bounds = BuildingGeometry.Transform(volume, pose, scale).Bounds;
                    var min = Cell(bounds.min); var max = Cell(bounds.max);
                    if (CellCount(min, max) > 4096) { blockedSpaces.Add(spaceId); break; }
                    for (long z = min.z; z <= max.z; z++) for (long y = min.y; y <= max.y; y++) for (long x = min.x; x <= max.x; x++) blockedCells.Add((spaceId, new Vector3Int((int)x, (int)y, (int)z)));
                }
            }
        }
        private static double CellCount(Vector3Int min, Vector3Int max) => ((double)max.x - min.x + 1) * ((double)max.y - min.y + 1) * ((double)max.z - min.z + 1);
        public bool Allows(in BuildingContext context, out string reason)
        {
            reason = null;
            if ((context.Action & (BuildingAction.Place | BuildingAction.Move | BuildingAction.Upgrade)) == 0) return true;
            if (changingInterest || IsLoading) { reason = "Finish loading the requested building regions before editing them."; return false; }
            var space = context.Request.Space != null ? context.Request.Space : context.Request.Socket != null ? context.Request.Socket.Space : context.Existing != null ? context.Existing.Space : null;
            string spaceId = space != null ? space.Id : "";
            if (blockedSpaces.Contains(spaceId)) { reason = "Load this space's archived buildings and missing definitions before placement."; return false; }
            var pose = space != null ? space.ToLocal(context.Pose) : context.Pose;
            Vector3 scale = context.Request.Scale / (space != null ? space.transform.lossyScale.x : 1);
            for (int i = 0; i < context.Definition.VolumeCount + (context.Definition.Clearances?.Length ?? 0); i++)
            {
                var volume = i < context.Definition.VolumeCount ? context.Definition.GetVolume(i) : context.Definition.Clearances[i - context.Definition.VolumeCount];
                var bounds = BuildingGeometry.Transform(volume, pose, scale).Bounds; var min = Cell(bounds.min); var max = Cell(bounds.max);
                if (CellCount(min, max) > 4096) { reason = "The footprint spans too many regions. Use smaller building segments."; return false; }
                for (long z = min.z; z <= max.z; z++) for (long y = min.y; y <= max.y; y++) for (long x = min.x; x <= max.x; x++)
                    if (blockedCells.Contains((spaceId, new Vector3Int((int)x, (int)y, (int)z)))) { reason = "Load this building region before placing into its saved footprint."; return false; }
            }
            return true;
        }
        private Vector3Int Cell(BuildingRecord record) => Cell(string.IsNullOrEmpty(record.SpaceId) ? record.Position : record.LocalPosition);
        private Vector3Int Cell(Vector3 point) => new Vector3Int(Mathf.FloorToInt(point.x / RegionSize), Mathf.FloorToInt(point.y / RegionSize), Mathf.FloorToInt(point.z / RegionSize));

        /// <summary>Sets spherical region interest. Connected dependencies stay loaded, including dependencies in other spaces.
        /// Call again when interest or the world changes, then Tick every frame. Positions are local to spaceId.</summary>
        public void SetInterest(Vector3 position, float radius, string spaceId = "")
            => SetInterest(new[] { new BuildingInterest(position, radius, spaceId) });

        /// <summary>Union of interest regions for several players/cameras. Empty interest unloads all complete connected groups.</summary>
        public void SetInterest(IReadOnlyList<BuildingInterest> interests)
        {
            Check(); if (system.IsBusy || IsLoading || changingInterest) throw new InvalidOperationException("Finish the current load before changing region interest.");
            if (interests == null || interests.Count > 1024) throw new ArgumentException("Use a list of at most 1024 interest points.");
            foreach (var interest in interests) if (!BuildingMath.Finite(interest.Position) || !BuildingMath.Finite(interest.Radius) || interest.Radius < 0)
                throw new ArgumentException("Use finite positions and nonnegative radii.");
            changingInterest = true;
            try
            {
            Refresh(); wanted.Clear();
            void Select(Dictionary<string, BuildingRecord> records)
            {
                foreach (var pair in records)
                {
                    var cell = Cell(pair.Value);
                    foreach (var interest in interests)
                    {
                        if ((pair.Value.SpaceId ?? "") != (interest.SpaceId ?? "")) continue;
                        var center = Cell(interest.Position); double cells = Math.Ceiling(interest.Radius / (double)RegionSize);
                        long dx = (long)cell.x - center.x, dy = (long)cell.y - center.y, dz = (long)cell.z - center.z;
                        if ((double)dx * dx + (double)dy * dy + (double)dz * dz <= cells * cells) { wanted.Add(pair.Key); break; }
                    }
                }
            }
            Select(live); Select(archived);
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            void Edge(string from, string to)
            {
                if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return;
                if (!adjacency.TryGetValue(from, out var list)) adjacency[from] = list = new List<string>(); list.Add(to);
            }
            foreach (var link in links.Values) { Edge(link.SourceId, link.TargetId); Edge(link.TargetId, link.SourceId); }
            var pending = new List<string>(wanted);
            for (int i = 0; i < pending.Count; i++) if (adjacency.TryGetValue(pending[i], out var neighbours))
                foreach (var id in neighbours) if (wanted.Add(id)) pending.Add(id);
            unload.Clear(); foreach (var id in live.Keys) if (!wanted.Contains(id)) unload.Add(id);
            Failure = null;
            // A connected group is unloaded atomically. This cold operation can exceed the loading time budget.
            if (unload.Count != 0)
            {
                if (!system.TryUnload(unload, out var saved, out var reason)) { Failure = reason; return; }
                foreach (var record in saved.Buildings) archived[record.InstanceId] = record;
                foreach (var link in saved.Connections) links[link.Id] = link;
                Refresh();
            }
            var load = new BuildingState();
            foreach (var id in wanted) if (archived.TryGetValue(id, out var record)) load.Buildings.Add(record);
            foreach (var link in links.Values) if (archived.ContainsKey(link.SourceId) && wanted.Contains(link.SourceId)) load.Connections.Add(link);
            foreach (var id in redeemed) load.RedeemedParcels.Add(id);
            job = load.Buildings.Count == 0 ? null : queue.Enqueue(load, catalog);
            }
            finally { changingInterest = false; }
        }
        public int Tick(int maximumBuildings = 64, double milliseconds = 2, int batchSize = 8)
        {
            Check(); int count = queue.Tick(maximumBuildings, milliseconds, batchSize);
            if (job != null && job.IsDone) { Failure = job.Failure; Refresh(); }
            return count;
        }
        public BuildingState CaptureAll()
        {
            Check(); Refresh(); var output = new BuildingState { Revision = system.Revision };
            foreach (var record in live.Values) output.Buildings.Add(BuildingSystem.CloneRecord(record));
            foreach (var record in archived.Values) output.Buildings.Add(BuildingSystem.CloneRecord(record));
            foreach (var link in links.Values) output.Connections.Add(BuildingSystem.CloneLink(link));
            output.RedeemedParcels.AddRange(redeemed); return output;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; job?.Cancel();
            if (system != null) { system.Changed -= Changed; system.Restored -= Restored; system.RemoveRule(this); }
        }
    }
}
