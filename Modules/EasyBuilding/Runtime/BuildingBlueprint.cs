using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Portable local-space design. Costs and instance identities are recalculated when placed.</summary>
    [Serializable]
    public sealed class BuildingBlueprint
    {
        public string Name = "Blueprint";
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
        public List<BuildingConnectionRecord> Connections = new List<BuildingConnectionRecord>();
    }
    public sealed partial class BuildingSystem
    {
        public BuildingBlueprint CaptureBlueprint(IReadOnlyList<PlacedBuilding> selection, Pose pivot, bool includeComponentState = false)
        {
            RequireIdle();
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (!BuildingMath.Finite(pivot.position) || !BuildingMath.ValidRotation(pivot.rotation)) throw new ArgumentException("Use a finite blueprint pivot.");
            busy = true;
            try
            {
                var blueprint = new BuildingBlueprint(); var ids = new HashSet<string>(StringComparer.Ordinal);
                var inverse = Quaternion.Inverse(pivot.rotation.normalized);
                foreach (var item in selection)
                {
                    if (item == null || item.System != this || !ids.Add(item.InstanceId)) throw new ArgumentException("Select distinct buildings in this world.");
                    var record = CaptureRecord(item);
                    record.Position = inverse * (record.Position - pivot.position); record.Rotation = inverse * record.Rotation;
                    record.SpaceId = ""; record.LocalPosition = record.Position; record.LocalRotation = record.Rotation;
                    record.OwnerId = ""; record.TeamId = ""; record.Version = 0;
                    record.PaidCosts = Array.Empty<BuildingCost>(); record.Construction = null;
                    record.HasConstruction = false;
                    record.SocketOwnerId = ""; record.SocketId = "";
                    if (!includeComponentState) record.Parts = Array.Empty<BuildingPartState>();
                    blueprint.Buildings.Add(record);
                }
                foreach (var link in connections.Values)
                    if (link.Source != null && link.Target != null && ids.Contains(link.Source.InstanceId) && ids.Contains(link.Target.InstanceId))
                        blueprint.Connections.Add(link.Capture());
                return blueprint;
            }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
        public bool TryPlaceBlueprint(BuildingBlueprint blueprint, Pose pose, IReadOnlyList<BuildingDefinition> catalog,
            out BuildingOperationResult result, IBuildingActor actor = null, BuildingSpace space = null)
        {
            result = new BuildingOperationResult();
            if (IsBusy) return Reject(result, PlacementFailure.Busy, "A building operation is running.");
            if (blueprint == null || blueprint.Buildings == null || blueprint.Buildings.Count == 0 || catalog == null ||
                !BuildingMath.Finite(pose.position) || !BuildingMath.ValidRotation(pose.rotation))
                return Reject(result, PlacementFailure.InvalidState, "Use a nonempty blueprint, catalog and finite pose.");
            try
            {
                var definitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
                foreach (var definition in catalog) if (definition == null || !definitions.TryAdd(definition.Id, definition))
                    return Reject(result, PlacementFailure.InvalidConfiguration, "The catalog must contain distinct, assigned definitions.");
                var remap = new Dictionary<string, string>(StringComparer.Ordinal);
                var edits = new List<BuildingEdit>(blueprint.Buildings.Count);
                foreach (var source in blueprint.Buildings)
                {
                    if (source == null || string.IsNullOrWhiteSpace(source.InstanceId) || !remap.TryAdd(source.InstanceId, Guid.NewGuid().ToString("N")))
                        return Reject(result, PlacementFailure.InvalidState, "Blueprint instance IDs must be distinct and nonempty.");
                    if (!definitions.TryGetValue(source.DefinitionId ?? "", out var definition))
                        return Reject(result, PlacementFailure.MissingDefinition, "A blueprint definition is missing from the catalog.");
                    if (source.DefinitionVersion != definition.SaveVersion)
                        return Reject(result, PlacementFailure.InvalidState, "Migrate the blueprint to the current definition version first.");
                    var record = CloneRecord(source); record.InstanceId = remap[source.InstanceId];
                    var request = new PlacementRequest(pose.position + pose.rotation * source.Position, pose.rotation * source.Rotation, space: space, scale: source.Scale);
                    edits.Add(BuildingEdit.FromRecord(definition, request, record));
                }
                var links = new List<BuildingConnectionRecord>();
                if (blueprint.Connections != null) foreach (var source in blueprint.Connections)
                {
                    if (source == null || !remap.TryGetValue(source.SourceId ?? "", out var from) || !remap.TryGetValue(source.TargetId ?? "", out var to))
                        return Reject(result, PlacementFailure.InvalidConnection, "Blueprint links must refer to buildings inside the blueprint.");
                    var link = CloneLink(source); link.Id = Guid.NewGuid().ToString("N"); link.SourceId = from; link.TargetId = to; links.Add(link);
                }
                return Execute(edits, out result, actor, false, null, links);
            }
            catch (Exception exception) { return Reject(result, PlacementFailure.InvalidState, exception.Message); }
        }
        public bool TryCopy(PlacedBuilding building, PlacementRequest request, out PlacedBuilding copy, out PlacementResult result, IBuildingActor actor = null)
        {
            copy = null;
            if (building == null || building.System != this) { result = PlacementResult.Fail(PlacementFailure.NotFound, "Choose an existing building in this world."); return false; }
            return TryPlace(building.Definition, request, out copy, out result, actor);
        }
        internal static BuildingRecord CloneRecord(BuildingRecord value)
        {
            if (value == null) return null;
            var parts = value.Parts == null ? Array.Empty<BuildingPartState>() : (BuildingPartState[])value.Parts.Clone();
            return new BuildingRecord
            {
                InstanceId = value.InstanceId, DefinitionId = value.DefinitionId, DefinitionVersion = value.DefinitionVersion,
                Position = value.Position, Rotation = value.Rotation, Scale = value.Scale, SpaceId = value.SpaceId,
                LocalPosition = value.LocalPosition, LocalRotation = value.LocalRotation, OwnerId = value.OwnerId, TeamId = value.TeamId,
                Version = value.Version, PaidCosts = value.PaidCosts != null ? (BuildingCost[])value.PaidCosts.Clone() : Array.Empty<BuildingCost>(),
                Parts = parts, Construction = HasConstruction(value) ? CloneConstruction(value.Construction) : null, HasConstruction = HasConstruction(value), SocketId = value.SocketId, SocketOwnerId = value.SocketOwnerId
            };
        }
    }
}
