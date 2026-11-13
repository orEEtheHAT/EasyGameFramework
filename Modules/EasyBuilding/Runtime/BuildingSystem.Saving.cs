using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        private static readonly ProfilerMarker RestoreMarker = new ProfilerMarker("EasyBuilding.Restore");
        internal BuildingRecord CaptureRecord(PlacedBuilding building, bool replicatedOnly = false)
        {
            var pose = new Pose(building.transform.position, building.transform.rotation);
            var local = building.Space != null ? building.Space.ToLocal(pose) : pose;
            var socket = building.AttachedSocket;
            return new BuildingRecord
            {
                InstanceId = building.InstanceId, DefinitionId = building.Definition.Id, DefinitionVersion = building.Definition.SaveVersion,
                Position = pose.position, Rotation = pose.rotation, Scale = building.transform.lossyScale,
                SpaceId = building.Space != null ? building.Space.Id : "", LocalPosition = local.position, LocalRotation = local.rotation,
                OwnerId = building.OwnerId, TeamId = building.TeamId, Version = building.Version,
                PaidCosts = replicatedOnly ? Array.Empty<BuildingCost>() : (BuildingCost[])building.PaidCosts.Clone(),
                Parts = building.CaptureParts(replicatedOnly), Construction = CloneConstruction(building.Construction, replicatedOnly), HasConstruction = building.Construction != null,
                SocketOwnerId = socket != null && socket.Building != null ? socket.Building.InstanceId : "", SocketId = socket != null ? socket.Id : ""
            };
        }
        private static BuildingConstructionState CloneConstruction(BuildingConstructionState value, bool replicatedOnly = false) => value == null ? null : new BuildingConstructionState
        {
            Work = value.Work, TotalWork = value.TotalWork, Paused = value.Paused,
            Required = value.Required != null ? (BuildingCost[])value.Required.Clone() : Array.Empty<BuildingCost>(),
            Delivered = value.Delivered != null ? (BuildingCost[])value.Delivered.Clone() : Array.Empty<BuildingCost>(),
            CompletionParts = replicatedOnly ? BuildingChanges.PublicParts(value.CompletionParts) : value.CompletionParts != null ? (BuildingPartState[])value.CompletionParts.Clone() : Array.Empty<BuildingPartState>()
        };
        private static bool HasConstruction(BuildingRecord record)
        {
            var state = record.Construction;
            return record.HasConstruction || state != null && (state.TotalWork != 0 || state.Work != 0 || state.Paused ||
                (state.Required?.Length ?? 0) != 0 || (state.Delivered?.Length ?? 0) != 0 || (state.CompletionParts?.Length ?? 0) != 0);
        }
        private static void UpdateRecordMetadata(BuildingRecord record, PlacedBuilding building, Pose pose, Vector3 scale)
        {
            record.Position = pose.position; record.Rotation = pose.rotation; record.Scale = scale;
            record.SpaceId = building.Space != null ? building.Space.Id : "";
            var local = building.Space != null ? building.Space.ToLocal(pose) : pose;
            record.LocalPosition = local.position; record.LocalRotation = local.rotation;
            record.Version = building.Version; record.OwnerId = building.OwnerId; record.TeamId = building.TeamId;
            var socket = building.AttachedSocket;
            record.SocketId = socket != null ? socket.Id : "";
            record.SocketOwnerId = socket != null && socket.Building != null ? socket.Building.InstanceId : "";
        }
        public BuildingState CaptureState() => CaptureState(false);
        public BuildingState CaptureState(bool replicatedOnly)
        {
            RequireIdle(); busy = true;
            try
            {
                var state = new BuildingState { Revision = Revision, ReplicationOnly = replicatedOnly };
                var snapshot = new List<PlacedBuilding>(buildings.Values);
                foreach (var building in snapshot) if (building != null) state.Buildings.Add(CaptureRecord(building, replicatedOnly));
                foreach (var record in missingBuildings.Values) state.Buildings.Add(BuildingChanges.CopyRecord(record, replicatedOnly));
                foreach (var link in connections.Values) state.Connections.Add(link.Capture());
                foreach (var link in deferredConnections) state.Connections.Add(CloneLink(link));
                if (!replicatedOnly) state.RedeemedParcels.AddRange(redeemedParcels);
                return state;
            }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
        public bool TryRestore(BuildingState state, IReadOnlyList<BuildingDefinition> definitions, out string reason)
            => TryRestore(state, definitions, new BuildingRestoreOptions(), out reason);

        public bool TryRestore(BuildingState state, IReadOnlyList<BuildingDefinition> definitions, BuildingRestoreOptions options, out string reason)
        {
            using var marker = RestoreMarker.Auto();
            reason = null;
            if (IsBusy) { reason = "A building operation is running."; return false; }
            if (state == null || state.Version < 1 || state.Version > BuildingState.CurrentVersion || state.Buildings == null || definitions == null)
            { reason = "Use a supported BuildingState (v1 or v2) and a definition catalog."; return false; }
            options = options ?? new BuildingRestoreOptions();
            busy = true;
            var staged = new List<PlacedBuilding>(state.Buildings.Count);
            var allStaged = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            var unknown = new Dictionary<string, BuildingRecord>(StringComparer.Ordinal);
            var preparedLinks = new List<PreparedConnection>();
            bool committed = false;
            try
            {
                var catalog = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
                if (state.RedeemedParcels != null) foreach (var id in state.RedeemedParcels)
                    if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("A redeemed parcel ID is empty.");
                foreach (var definition in definitions)
                {
                    if (definition == null || definition.ConfigurationError != null) throw new InvalidOperationException("The catalog contains a missing or invalid definition.");
                    if (!catalog.TryAdd(definition.Id, definition)) throw new InvalidOperationException("Duplicate definition ID in the catalog.");
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in state.Buildings)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.InstanceId) || !ids.Add(record.InstanceId) || buildings.ContainsKey(record.InstanceId) || missingBuildings.ContainsKey(record.InstanceId))
                        throw new InvalidOperationException("Snapshot instance IDs must be unique and not already loaded.");
                    var building = StageRecord(record, state.Version, catalog, options);
                    if (building == null)
                    {
                        if (options.PreserveUnknownDefinitions)
                        {
                            var preserved = CloneRecord(record); if (state.Version == 1) preserved.Scale = Vector3.one;
                            unknown.Add(record.InstanceId, preserved);
                        }
                        continue;
                    }
                    staged.Add(building); allStaged.Add(record.InstanceId, building);
                }
                var linkRecords = new List<BuildingConnectionRecord>();
                var primaries = new HashSet<string>(StringComparer.Ordinal);
                if (state.Connections != null) foreach (var link in state.Connections)
                {
                    if (link == null) throw new InvalidOperationException("A saved connection is null.");
                    linkRecords.Add(link);
                    if (link.Primary && !primaries.Add(link.SourceId)) throw new InvalidOperationException("A building may have only one primary connection.");
                }
                foreach (var record in state.Buildings)
                {
                    if (string.IsNullOrEmpty(record.SocketId))
                    {
                        if (!string.IsNullOrEmpty(record.SocketOwnerId)) throw new InvalidOperationException("Socket owner without a socket ID.");
                        continue;
                    }
                    if (primaries.Add(record.InstanceId)) linkRecords.Add(new BuildingConnectionRecord
                    { Id = record.InstanceId + "/primary", SourceId = record.InstanceId, TargetId = record.SocketOwnerId, TargetSocketId = record.SocketId, Primary = true });
                }
                var linkIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pending in deferredConnections) linkIds.Add(pending.Id);
                var occupied = new HashSet<BuildingSocket>();
                foreach (var record in linkRecords)
                {
                    if (string.IsNullOrWhiteSpace(record.Id) || !linkIds.Add(record.Id) || connections.ContainsKey(record.Id)) throw new InvalidOperationException("Saved connection IDs must be unique.");
                    if (string.IsNullOrWhiteSpace(record.SourceId) || string.IsNullOrWhiteSpace(record.TargetSocketId) || record.SourceId == record.TargetId)
                        throw new InvalidOperationException("A saved connection needs distinct endpoints and a target socket ID.");
                    if (!allStaged.TryGetValue(record.SourceId, out var source) && !buildings.TryGetValue(record.SourceId, out source))
                    {
                        if (options.DeferMissingConnections || unknown.ContainsKey(record.SourceId)) { preparedLinks.Add(new PreparedConnection { Record = CloneLink(record) }); continue; }
                        if (options.SkipUnknownDefinitions) continue;
                        throw new InvalidOperationException("A saved connection source is missing from the snapshot.");
                    }
                    var sourceSocket = GetSocket(source, record.SourceSocketId);
                    if (record.Primary && source.AttachedSocket != null) throw new InvalidOperationException("A loaded building already has a primary connection.");
                    if (!string.IsNullOrEmpty(record.SourceSocketId) && sourceSocket == null)
                        throw new InvalidOperationException("A saved source socket does not exist.");
                    var target = FindSavedSocket(record.TargetId, record.TargetSocketId, allStaged);
                    if (target == null)
                    {
                        if (options.DeferMissingConnections || unknown.ContainsKey(record.TargetId ?? ""))
                        {
                            if (string.IsNullOrWhiteSpace(record.TargetSocketId) || record.SourceId == record.TargetId || sourceSocket != null && (sourceSocket.IsOccupied || !occupied.Add(sourceSocket)))
                                throw new InvalidOperationException("A deferred connection has an invalid or duplicate endpoint.");
                            preparedLinks.Add(new PreparedConnection { Record = CloneLink(record) }); continue;
                        }
                        throw new InvalidOperationException("A saved socket is missing. Restore its owner in this batch or load its scene first.");
                    }
                    if (source == target.Building || !string.IsNullOrEmpty(record.SourceSocketId) && sourceSocket == null)
                        throw new InvalidOperationException("A saved socket is missing or belongs to the same building.");
                    if (sourceSocket != null ? !sourceSocket.IsCompatible(target) : source.Definition.SocketType != target.SocketType)
                        throw new InvalidOperationException("Saved socket types, roles or sizes are incompatible.");
                    if (target.IsOccupied || !occupied.Add(target) || sourceSocket != null && (sourceSocket.IsOccupied || !occupied.Add(sourceSocket)))
                        throw new InvalidOperationException("Two connections cannot occupy the same socket.");
                    preparedLinks.Add(new PreparedConnection { Record = record, Source = source, SourceSocket = sourceSocket, Target = target });
                }
                buildings.EnsureCapacity(buildings.Count + staged.Count);
                connections.EnsureCapacity(connections.Count + preparedLinks.Count);
                foreach (var building in staged) { buildings.Add(building.InstanceId, building); unloadedIds.Remove(building.InstanceId); }
                foreach (var prepared in preparedLinks)
                    if (prepared.Target != null) AddConnection(prepared.Source, prepared.SourceSocket, prepared.Target, prepared.Record.Primary, prepared.Record.Id);
                    else deferredConnections.Add(prepared.Record);
                foreach (var pair in catalog) knownDefinitions[pair.Key] = pair.Value;
                foreach (var pair in unknown) { missingBuildings.Add(pair.Key, pair.Value); unloadedIds.Remove(pair.Key); }
                committed = true; Revision = Math.Max(Revision + 1, state.Revision);
                if (state.RedeemedParcels != null) foreach (var id in state.RedeemedParcels) redeemedParcels.Add(id);
                foreach (var building in staged) ApplyPose(building, new Pose(building.transform.position, building.transform.rotation), building.transform.lossyScale, true);
                busy = false; FlushRequestedRemovals();
                ResolveDeferredConnections();
                if (Restored != null) foreach (Action handler in Restored.GetInvocationList()) try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
                return true;
            }
            catch (Exception exception)
            {
                if (committed) { Debug.LogException(exception, this); return true; }
                reason = exception.Message; return false;
            }
            finally
            {
                if (!committed) foreach (var building in staged) if (building != null) Rollback(building);
                busy = false;
                FlushRequestedRemovals(); DrainPublications();
            }
        }
        private sealed class PreparedConnection
        {
            internal BuildingConnectionRecord Record;
            internal PlacedBuilding Source;
            internal BuildingSocket SourceSocket, Target;
        }
        private PlacedBuilding StageRecord(BuildingRecord record, int formatVersion, Dictionary<string, BuildingDefinition> catalog, BuildingRestoreOptions options)
        {
            if (record.DefinitionId == null || !catalog.TryGetValue(record.DefinitionId, out var definition))
            {
                if (options.SkipUnknownDefinitions || options.PreserveUnknownDefinitions) return null;
                throw new InvalidOperationException("A snapshot definition is missing from the catalog.");
            }
            if (record.DefinitionVersion > 0 && record.DefinitionVersion != definition.SaveVersion)
                throw new InvalidOperationException("Definition version differs. Migrate the saved record before restoring it.");
            var scale = formatVersion == 1 ? Vector3.one : record.Scale;
            if (!BuildingMath.Finite(record.Position) || !BuildingMath.ValidRotation(record.Rotation) || !ValidScale(scale))
                throw new InvalidOperationException("Snapshot contains an invalid position, rotation or scale.");
            var space = ResolveSavedSpace(record.SpaceId, options.UseWorldPoseWhenSpaceMissing);
            if (space != null && (!space.IsValid || !BuildingMath.Finite(record.LocalPosition) || !BuildingMath.ValidRotation(record.LocalRotation)))
                throw new InvalidOperationException("Saved local pose or Building Space scale is invalid.");
            var pose = space != null ? space.ToWorld(new Pose(record.LocalPosition, record.LocalRotation.normalized)) : new Pose(record.Position, record.Rotation.normalized);
            bool construction = HasConstruction(record);
            if (construction && record.Construction == null) throw new InvalidOperationException("A construction record is missing its construction state.");
            var building = CreateStaged(definition, record.InstanceId, pose, scale, construction);
            try
            {
                building.Space = space; building.OwnerId = record.OwnerId ?? ""; building.TeamId = record.TeamId ?? "";
                building.Version = record.Version; building.PaidCosts = ValidateSavedCosts(record.PaidCosts);
                building.Construction = construction ? CloneConstruction(record.Construction) : null; ValidateConstruction(building.Construction);
                building.RestoreParts(record.Parts, options.IgnoreMissingStateParts);
                return building;
            }
            catch { Rollback(building); throw; }
        }
        internal BuildingSpace ResolveSavedSpace(string id, bool allowMissing = false)
        {
            if (string.IsNullOrEmpty(id)) return null;
            BuildingSpace found = null;
            foreach (var candidate in BuildingSpace.Active)
                if (candidate != null && candidate.Id == id && Accepts(candidate.gameObject, candidate))
                {
                    if (found != null) throw new InvalidOperationException("Saved space ID is ambiguous.");
                    found = candidate;
                }
            if (found == null && !allowMissing) throw new InvalidOperationException("Load the saved Building Space before its buildings.");
            return found;
        }
        internal static BuildingConnectionRecord CloneLink(BuildingConnectionRecord value) => new BuildingConnectionRecord
        { Id = value.Id, SourceId = value.SourceId, SourceSocketId = value.SourceSocketId, TargetId = value.TargetId, TargetSocketId = value.TargetSocketId, Primary = value.Primary };
        private readonly List<BuildingConnectionRecord> deferredConnections = new List<BuildingConnectionRecord>();
        private readonly Dictionary<string, BuildingRecord> missingBuildings = new Dictionary<string, BuildingRecord>(StringComparer.Ordinal);
        public int MissingBuildingCount => missingBuildings.Count;
        public void GetMissingBuildings(List<BuildingRecord> output)
        { if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear(); foreach (var record in missingBuildings.Values) output.Add(CloneRecord(record)); }
        public bool TryResolveMissingBuildings(IReadOnlyList<BuildingDefinition> catalog, out int restored, out string reason)
        {
            restored = 0; reason = null;
            if (IsBusy || catalog == null) { reason = "Use a catalog while the world is idle."; return false; }
            var definitions = new HashSet<string>(StringComparer.Ordinal); foreach (var definition in catalog) if (definition != null) definitions.Add(definition.Id);
            var state = new BuildingState(); var originals = new List<BuildingRecord>();
            foreach (var record in missingBuildings.Values) if (definitions.Contains(record.DefinitionId))
            {
                var copy = CloneRecord(record); copy.SocketId = ""; copy.SocketOwnerId = "";
                state.Buildings.Add(copy); originals.Add(record);
            }
            if (state.Buildings.Count == 0) return true;
            foreach (var record in originals) missingBuildings.Remove(record.InstanceId);
            bool success = false;
            try
            {
                success = TryRestore(state, catalog, new BuildingRestoreOptions { DeferMissingConnections = true }, out reason);
                if (success) restored = originals.Count; return success;
            }
            finally { if (!success) foreach (var record in originals) missingBuildings[record.InstanceId] = record; }
        }
        public int PendingConnectionCount => deferredConnections.Count;
        public int ResolveDeferredConnections()
        {
            if (IsBusy) return 0;
            int resolved = 0; busy = true;
            var change = new BuildingChangeSet { BeforeRevision = Revision, Action = BuildingAction.Connect | BuildingAction.State };
            try
            {
                for (int i = deferredConnections.Count - 1; i >= 0; i--)
                {
                    var record = deferredConnections[i];
                    if (!buildings.TryGetValue(record.SourceId, out var source) || record.Primary && source.AttachedSocket != null) continue;
                    var target = FindSavedSocket(record.TargetId, record.TargetSocketId);
                    var sourceSocket = GetSocket(source, record.SourceSocketId);
                    if (target == null || target.Building == source || target.IsOccupied || !string.IsNullOrEmpty(record.SourceSocketId) && sourceSocket == null || sourceSocket != null && sourceSocket.IsOccupied) continue;
                    if (sourceSocket != null ? !sourceSocket.IsCompatible(target) : source.Definition.SocketType != target.SocketType) continue;
                    AddConnection(source, sourceSocket, target, record.Primary, record.Id);
                    deferredConnections.RemoveAt(i); change.Connections.Add(CloneLink(record)); resolved++;
                }
                if (resolved != 0) { change.Revision = ++Revision; busy = false; PublishChanged(change); }
                return resolved;
            }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
        private static BuildingCost[] ValidateSavedCosts(BuildingCost[] costs)
        {
            if (costs == null) return Array.Empty<BuildingCost>();
            var validated = new List<BuildingCost>(); AddCosts(validated, costs, 1); return validated.ToArray();
        }
    }

    public sealed class BuildingRestoreOptions
    {
        public bool SkipUnknownDefinitions;
        public bool PreserveUnknownDefinitions;
        public bool IgnoreMissingStateParts;
        public bool UseWorldPoseWhenSpaceMissing;
        public bool DeferMissingConnections;
    }
}
