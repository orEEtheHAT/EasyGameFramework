using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Explicit completeness and commit boundary for local snapshot validation/restoration.</summary>
    public sealed class BuildingSnapshotReport
    {
        public bool Committed { get; internal set; }
        public int LoadedBuildings { get; internal set; }
        public int MissingDefinitions { get; internal set; }
        public int DeferredConnections { get; internal set; }
        public bool IsComplete => MissingDefinitions == 0 && DeferredConnections == 0;
    }

    public sealed partial class BuildingSystem
    {
        private sealed class SnapshotPlan
        {
            internal readonly Dictionary<string, BuildingDefinition> Catalog = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
            internal readonly Dictionary<string, BuildingRecord> Records = new Dictionary<string, BuildingRecord>(StringComparer.Ordinal);
            internal readonly List<BuildingConnectionRecord> Links = new List<BuildingConnectionRecord>();
            internal readonly HashSet<string> Deferred = new HashSet<string>(StringComparer.Ordinal);
            internal BuildingState State;
        }

        public bool ValidateSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> definitions, out string reason)
            => ValidateSnapshot(snapshot, definitions, null, out _, out reason);

        /// <summary>Read-only preflight: no instantiation, restoration, payments or world notifications.
        /// Custom state-part validators run on prefab configuration and must be pure.</summary>
        public bool ValidateSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> definitions,
            BuildingRestoreOptions options, out BuildingSnapshotReport report, out string reason)
        {
            report = new BuildingSnapshotReport(); reason = null;
            if (IsBusy || IsReadOnly) { reason = "Validate a local snapshot in an idle, writable building world."; return false; }
            busy = true;
            try { PrepareSnapshot(snapshot, definitions, options ?? new BuildingRestoreOptions(), report); return true; }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; }
        }

        public bool TryReplaceSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> definitions, out string reason)
            => TryReplaceSnapshot(snapshot, definitions, null, out _, out reason);

        /// <summary>Replaces the whole LOCAL world, including private parts, missing records and parcel claims.
        /// Strict by default. Opt in to preserving unresolved content using the options and inspect the report.
        /// No payment, Placed or Removed events; one Restored notification. A post-commit failure returns false with Committed=true.</summary>
        public bool TryReplaceSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> definitions,
            BuildingRestoreOptions options, out BuildingSnapshotReport report, out string reason)
        {
            report = new BuildingSnapshotReport(); reason = null;
            if (IsBusy || IsReadOnly) { reason = "Replace a local snapshot in an idle, writable building world."; return false; }
            options = options ?? new BuildingRestoreOptions(); busy = true;
            var staged = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            var links = new List<PreparedConnection>();
            var retired = new List<PlacedBuilding>();
            try
            {
                var plan = PrepareSnapshot(snapshot, definitions, options, report);
                foreach (var record in plan.State.Buildings)
                {
                    var item = StageRecord(record, plan.State.Version, plan.Catalog, options);
                    if (item != null) staged.Add(record.InstanceId, item);
                }
                foreach (var record in plan.Links)
                {
                    if (plan.Deferred.Contains(record.Id)) { links.Add(new PreparedConnection { Record = record }); continue; }
                    var source = staged[record.SourceId]; var a = GetSocket(source, record.SourceSocketId);
                    var target = string.IsNullOrEmpty(record.TargetId) ? FindSavedSocket("", record.TargetSocketId) : GetSocket(staged[record.TargetId], record.TargetSocketId);
                    if (target == null || (a != null ? !a.IsCompatible(target) : source.Definition.SocketType != target.SocketType))
                        throw new InvalidOperationException("Snapshot sockets changed during preparation.");
                    RejectForeignOccupancy(target);
                    links.Add(new PreparedConnection { Record = record, Source = source, SourceSocket = a, Target = target });
                }
                // Finish allocations and checks before detaching anything from the live world.
                retired.AddRange(buildings.Values);
                var oldLinks = new List<BuildingConnection>(connections.Values);
                buildings.EnsureCapacity(staged.Count); connections.EnsureCapacity(links.Count);
                missingBuildings.EnsureCapacity(report.MissingDefinitions);
                redeemedParcels.EnsureCapacity(plan.State.RedeemedParcels.Count);
                if (deferredConnections.Capacity < links.Count) deferredConnections.Capacity = links.Count;
                var activeSessions = new List<BuildingSession>(sessions);
                report.Committed = true;
                foreach (var link in oldLinks) RemoveConnectionInternal(link);
                foreach (var old in retired) { old.ForgetSystem(); old.gameObject.SetActive(false); }
                buildings.Clear(); inactiveBuildings.Clear(); missingBuildings.Clear(); deferredConnections.Clear();
                unloadedIds.Clear(); redeemedParcels.Clear(); knownDefinitions.Clear(); prefabInfo.Clear();
                foreach (var pair in plan.Catalog) knownDefinitions.Add(pair.Key, pair.Value);
                foreach (var pair in staged) buildings.Add(pair.Key, pair.Value);
                foreach (var record in plan.State.Buildings) if (!staged.ContainsKey(record.InstanceId)) missingBuildings.Add(record.InstanceId, record);
                foreach (var link in links)
                    if (link.Target == null) deferredConnections.Add(link.Record);
                    else AddConnection(link.Source, link.SourceSocket, link.Target, link.Record.Primary, link.Record.Id);
                foreach (var id in plan.State.RedeemedParcels) redeemedParcels.Add(id);
                Revision = plan.State.Revision;
                foreach (var session in activeSessions) session.Cancel();
                foreach (var item in staged.Values) ApplyPose(item, new Pose(item.transform.position, item.transform.rotation), item.transform.lossyScale, true);
                foreach (var old in retired) if (old != null) BuildingMath.Destroy(old.gameObject);
                retired.Clear();
                busy = false;
                Exception notificationFailure = null;
                if (Restored != null) foreach (Action handler in Restored.GetInvocationList())
                    try { handler(); } catch (Exception exception) { if (notificationFailure == null) notificationFailure = exception; }
                if (notificationFailure != null) throw new InvalidOperationException("A Restored observer failed: " + notificationFailure.Message, notificationFailure);
                return true;
            }
            catch (Exception exception)
            {
                reason = report.Committed ? "Snapshot committed, but completion failed: " + exception.Message : exception.Message;
                return false;
            }
            finally
            {
                if (!report.Committed) { foreach (var item in staged.Values) if (item != null) Rollback(item); }
                else { foreach (var old in retired) if (old != null) BuildingMath.Destroy(old.gameObject); }
                busy = false; FlushRequestedRemovals(); DrainPublications();
            }
        }

        private SnapshotPlan PrepareSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> definitions,
            BuildingRestoreOptions options, BuildingSnapshotReport report)
        {
            if (snapshot == null || snapshot.ReplicationOnly || snapshot.Version != BuildingState.CurrentVersion || snapshot.Revision < 0 || snapshot.Buildings == null || snapshot.Connections == null || snapshot.RedeemedParcels == null || definitions == null)
                throw new InvalidOperationException("Supply a current full local BuildingState, its lists, a nonnegative revision and a definition catalog.");
            if (options.SkipUnknownDefinitions || options.IgnoreMissingStateParts || options.UseWorldPoseWhenSpaceMissing)
                throw new InvalidOperationException("Full snapshot replacement cannot discard definitions, state parts or missing spaces. Preserve unknown definitions/defer connections explicitly, or load required content first.");
            var plan = new SnapshotPlan { State = new BuildingState { Version = snapshot.Version, Revision = snapshot.Revision } };
            foreach (var definition in definitions)
            {
                if (definition == null || definition.ConfigurationError != null || !plan.Catalog.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException("The definition catalog contains missing, invalid or duplicate definitions.");
                ValidateSnapshotPrefab(definition);
            }
            var claims = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in snapshot.RedeemedParcels)
            { if (string.IsNullOrWhiteSpace(id) || !claims.Add(id)) throw new InvalidOperationException("Parcel claim IDs must be distinct and nonempty."); plan.State.RedeemedParcels.Add(id); }
            foreach (var value in snapshot.Buildings)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.InstanceId) || string.IsNullOrWhiteSpace(value.DefinitionId) || value.DefinitionVersion < 1 || value.Version < 0 || plan.Records.ContainsKey(value.InstanceId))
                    throw new InvalidOperationException("Snapshot building/definition IDs and versions must be valid, with unique instance IDs.");
                var record = CloneRecord(value); plan.Records.Add(record.InstanceId, record); plan.State.Buildings.Add(record);
                if (!BuildingMath.Finite(record.Position) || !BuildingMath.ValidRotation(record.Rotation) || !ValidScale(record.Scale)) throw new InvalidOperationException("A snapshot pose or scale is invalid.");
                var space = ResolveSavedSpace(record.SpaceId);
                if (space != null && (!space.IsValid || !BuildingMath.Finite(record.LocalPosition) || !BuildingMath.ValidRotation(record.LocalRotation))) throw new InvalidOperationException("A snapshot local pose or space is invalid.");
                ValidateSavedCosts(record.PaidCosts);
                if (HasConstruction(record)) { if (record.Construction == null) throw new InvalidOperationException("Construction state is missing."); ValidateConstruction(record.Construction); }
                ValidateSnapshotPartKeys(record.Parts);
                if (record.Construction != null) ValidateSnapshotPartKeys(record.Construction.CompletionParts);
                if (!plan.Catalog.TryGetValue(record.DefinitionId, out var definition))
                { if (!options.PreserveUnknownDefinitions) throw new InvalidOperationException("Missing definition: " + record.DefinitionId); report.MissingDefinitions++; continue; }
                if (record.DefinitionVersion != definition.SaveVersion) throw new InvalidOperationException("Definition version differs: " + record.DefinitionId);
                if (HasConstruction(record))
                {
                    if (record.Parts != null && record.Parts.Length != 0) throw new InvalidOperationException("A construction site cannot carry finished-building state parts.");
                    ValidateSnapshotParts(definition, record.Construction.CompletionParts);
                }
                else ValidateSnapshotParts(definition, record.Parts, true);
                report.LoadedBuildings++;
            }
            var primary = new Dictionary<string, BuildingConnectionRecord>(StringComparer.Ordinal); var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in snapshot.Connections)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.Id) || !linkIds.Add(value.Id)) throw new InvalidOperationException("Snapshot connection IDs must be distinct and nonempty.");
                var link = CloneLink(value); plan.Links.Add(link);
                if (link.Primary && (string.IsNullOrWhiteSpace(link.SourceId) || !primary.TryAdd(link.SourceId, link))) throw new InvalidOperationException("A building has multiple primary connections.");
            }
            foreach (var record in plan.State.Buildings)
            {
                if (string.IsNullOrEmpty(record.SocketId)) { if (!string.IsNullOrEmpty(record.SocketOwnerId)) throw new InvalidOperationException("Socket owner requires a socket ID."); continue; }
                if (primary.TryGetValue(record.InstanceId, out var link))
                { if (link.TargetSocketId != record.SocketId || (link.TargetId ?? "") != (record.SocketOwnerId ?? "")) throw new InvalidOperationException("Primary connection and building attachment disagree."); }
                else
                {
                    link = new BuildingConnectionRecord { Id = record.InstanceId + "/primary", SourceId = record.InstanceId, TargetId = record.SocketOwnerId, TargetSocketId = record.SocketId, Primary = true };
                    if (!linkIds.Add(link.Id)) throw new InvalidOperationException("Duplicate generated primary connection ID.");
                    primary.Add(record.InstanceId, link); plan.Links.Add(link);
                }
            }
            var occupied = new HashSet<(string owner, string socket)>();
            foreach (var link in plan.Links)
            {
                if (string.IsNullOrWhiteSpace(link.SourceId) || string.IsNullOrWhiteSpace(link.TargetSocketId) || link.SourceId == link.TargetId)
                    throw new InvalidOperationException("A connection requires distinct endpoints and a target socket ID.");
                if (!occupied.Add((link.TargetId ?? "", link.TargetSocketId)) || !string.IsNullOrEmpty(link.SourceSocketId) && !occupied.Add((link.SourceId, link.SourceSocketId)))
                    throw new InvalidOperationException("Snapshot connections occupy the same socket.");
                bool sourceKnown = plan.Records.TryGetValue(link.SourceId, out var sourceRecord) && plan.Catalog.TryGetValue(sourceRecord.DefinitionId, out _);
                var source = sourceKnown ? SnapshotSocket(plan.Catalog[sourceRecord.DefinitionId], link.SourceSocketId) : null;
                if (sourceKnown && !string.IsNullOrEmpty(link.SourceSocketId) && source == null) throw new InvalidOperationException("A saved source socket is missing.");
                BuildingSocket target = null; bool targetKnown;
                if (string.IsNullOrEmpty(link.TargetId)) { target = FindSavedSocket("", link.TargetSocketId); targetKnown = target != null; if (target != null) RejectForeignOccupancy(target); }
                else { targetKnown = plan.Records.TryGetValue(link.TargetId, out var targetRecord) && plan.Catalog.TryGetValue(targetRecord.DefinitionId, out _); if (targetKnown) target = SnapshotSocket(plan.Catalog[targetRecord.DefinitionId], link.TargetSocketId); }
                if (targetKnown && target == null) throw new InvalidOperationException("A saved target socket is missing.");
                if (!sourceKnown || !targetKnown)
                {
                    if (!options.DeferMissingConnections) throw new InvalidOperationException("A connection has an unresolved endpoint. Load it or explicitly defer connections.");
                    plan.Deferred.Add(link.Id); report.DeferredConnections++; continue;
                }
                if (source != null ? !SnapshotSocketsCompatible(source, target) : plan.Catalog[sourceRecord.DefinitionId].SocketType != target.SocketType)
                    throw new InvalidOperationException("Snapshot socket types, roles or sizes are incompatible.");
            }
            return plan;
        }

        private void RejectForeignOccupancy(BuildingSocket socket)
        {
            foreach (var link in socket.links) if (link.System != this) throw new InvalidOperationException("A scene socket is occupied by another world.");
            if (socket.Occupant != null && socket.Occupant.System != this) throw new InvalidOperationException("A scene socket is reserved by another world.");
        }
        private static bool SnapshotSocketsCompatible(BuildingSocket a, BuildingSocket b)
            => a.SocketType == b.SocketType && BuildingMath.Finite(a.Size) && BuildingMath.Finite(b.Size) && a.Size > 0 && b.Size > 0 && Mathf.Abs(a.Size - b.Size) <= 0.001f &&
                (a.Role == SocketRole.Any || b.Role == SocketRole.Any || a.Role != b.Role);
        private static BuildingSocket SnapshotSocket(BuildingDefinition definition, string id)
        { if (string.IsNullOrEmpty(id)) return null; foreach (var socket in definition.Prefab.GetComponentsInChildren<BuildingSocket>(true)) if (socket.Id == id) return socket; return null; }
        private static void ValidateSnapshotPrefab(BuildingDefinition definition)
        {
            if (definition.Prefab.GetComponentInChildren<BuildingSystem>(true) != null || definition.Prefab.GetComponentInChildren<BuildingSpace>(true) != null) throw new InvalidOperationException("Keep building worlds/spaces outside building prefabs.");
            var instances = definition.Prefab.GetComponentsInChildren<PlacedBuilding>(true);
            if (instances.Length > 1 || instances.Length == 1 && instances[0].gameObject != definition.Prefab) throw new InvalidOperationException("Only the prefab root may contain PlacedBuilding.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var socket in definition.Prefab.GetComponentsInChildren<BuildingSocket>(true))
                if (string.IsNullOrWhiteSpace(socket.Id) || !ids.Add(socket.Id) || !BuildingMath.Finite(socket.Size) || socket.Size <= 0) throw new InvalidOperationException("Prefab sockets require distinct IDs and positive finite size.");
        }
        private static void ValidateSnapshotPartKeys(BuildingPartState[] states)
        {
            if (states == null) return; var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in states) if (string.IsNullOrWhiteSpace(state.Key) || !keys.Add(state.Key) || state.Version < 1 || state.Json == null) throw new InvalidOperationException("Saved state-part keys, versions and payloads are invalid.");
        }
        private static void ValidateSnapshotParts(BuildingDefinition definition, BuildingPartState[] states, bool requireAll = false)
        {
            var parts = new Dictionary<string, IBuildingStatePart>(StringComparer.Ordinal);
            foreach (var behaviour in definition.Prefab.GetComponentsInChildren<MonoBehaviour>(true)) if (behaviour is IBuildingStatePart part)
                if (string.IsNullOrWhiteSpace(part.StateKey) || !parts.TryAdd(part.StateKey, part)) throw new InvalidOperationException("Prefab state-part keys must be distinct and nonempty.");
            if (requireAll && (states?.Length ?? 0) != parts.Count) throw new InvalidOperationException("A full local snapshot must contain every state part, including private parts.");
            if (states == null) return;
            foreach (var state in states)
            {
                if (!parts.TryGetValue(state.Key, out var part)) throw new InvalidOperationException("Missing building state adapter: " + state.Key);
                if (!part.CanRestoreBuildingState(state.Version, state.Json, out var reason)) throw new InvalidOperationException(reason ?? "A state adapter rejected its saved payload.");
            }
        }
    }
}
