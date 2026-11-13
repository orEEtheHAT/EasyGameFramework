using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Authority-owned packed content. Keep it in your inventory/save; never trust a parcel body received from a client.</summary>
    [Serializable]
    public sealed class BuildingParcel
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string OwnerId;
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
        public List<BuildingConnectionRecord> Connections = new List<BuildingConnectionRecord>();
    }
    public sealed partial class BuildingSystem
    {
        private readonly HashSet<string> redeemedParcels = new HashSet<string>(StringComparer.Ordinal);
        public bool TryPack(IReadOnlyList<PlacedBuilding> selection, Pose pivot, out BuildingParcel parcel, out PlacementResult result, IBuildingActor actor = null)
        {
            parcel = null;
            if (selection == null || selection.Count == 0 || !BuildingMath.Finite(pivot.position) || !BuildingMath.ValidRotation(pivot.rotation))
            { result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Choose buildings and a finite packing pivot."); return false; }
            var edits = new BuildingEdit[selection.Count]; var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < edits.Length; i++)
            {
                if (selection[i] == null || !ids.Add(selection[i].InstanceId)) { result = PlacementResult.Fail(PlacementFailure.InvalidState, "Choose distinct existing buildings."); return false; }
                edits[i] = BuildingEdit.Pack(selection[i]);
            }
            if (!Execute(edits, out var operation, actor, true, null)) { result = operation.Validation; return false; }
            var packed = new BuildingParcel { OwnerId = actor?.Id ?? "" }; var inverse = Quaternion.Inverse(pivot.rotation.normalized);
            foreach (var record in operation.Changes.Previous) if (ids.Contains(record.InstanceId))
            {
                var copy = CloneRecord(record); copy.Position = inverse * (record.Position - pivot.position); copy.Rotation = inverse * record.Rotation;
                copy.SpaceId = ""; copy.LocalPosition = copy.Position; copy.LocalRotation = copy.Rotation; copy.SocketId = ""; copy.SocketOwnerId = "";
                packed.Buildings.Add(copy);
            }
            foreach (var link in operation.Changes.PreviousConnections)
                if (ids.Contains(link.SourceId) && ids.Contains(link.TargetId ?? "")) packed.Connections.Add(CloneLink(link));
            parcel = packed; result = operation.Validation; return true;
        }
        public bool TryUnpack(BuildingParcel parcel, Pose pose, IReadOnlyList<BuildingDefinition> definitions, out BuildingOperationResult result,
            IBuildingActor actor = null, BuildingSpace space = null)
        {
            result = new BuildingOperationResult();
            if (parcel == null || string.IsNullOrWhiteSpace(parcel.Id) || parcel.Buildings == null || parcel.Buildings.Count == 0 || definitions == null ||
                !BuildingMath.Finite(pose.position) || !BuildingMath.ValidRotation(pose.rotation)) return Reject(result, PlacementFailure.InvalidState, "Use a valid packed building, catalog and pose.");
            if (actor != null && !actor.IsAdministrator && !string.IsNullOrEmpty(parcel.OwnerId) && parcel.OwnerId != actor.Id)
                return Reject(result, PlacementFailure.PermissionDenied, "This parcel belongs to another actor.");
            if (redeemedParcels.Contains(parcel.Id)) return Reject(result, PlacementFailure.StaleRequest, "This packed building was already unpacked.");
            try
            {
                var catalog = CreateCatalog(definitions); var edits = new List<BuildingEdit>(parcel.Buildings.Count);
                foreach (var record in parcel.Buildings)
                {
                    if (record == null || !catalog.TryGetValue(record.DefinitionId ?? "", out var definition) || record.DefinitionVersion != definition.SaveVersion)
                        return Reject(result, PlacementFailure.MissingDefinition, "Load the parcel's definitions and migrate any older records first.");
                    var request = new PlacementRequest(pose.position + pose.rotation * record.Position, pose.rotation * record.Rotation, space: space, scale: record.Scale);
                    edits.Add(BuildingEdit.FromRecord(definition, request, CloneRecord(record), true, BuildingAction.Pack));
                }
                return Execute(edits, out result, actor, true, null, parcel.Connections, parcel.Id);
            }
            catch (Exception exception) { return Reject(result, PlacementFailure.InvalidState, exception.Message); }
        }
    }
}
