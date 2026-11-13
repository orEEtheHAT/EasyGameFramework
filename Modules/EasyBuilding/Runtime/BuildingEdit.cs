using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public readonly struct BuildingPlacement
    {
        public readonly BuildingDefinition Definition;
        public readonly PlacementRequest Request;
        public BuildingPlacement(BuildingDefinition definition, PlacementRequest request) { Definition = definition; Request = request; }
    }

    public readonly struct BuildingEdit
    {
        public readonly BuildingAction Action;
        public readonly BuildingDefinition Definition;
        public readonly PlacedBuilding Existing;
        public readonly PlacementRequest Request;
        public readonly bool PreserveState;
        public readonly bool Refund;
        internal readonly BuildingRecord Seed;
        internal readonly bool KeepMetadata;
        internal readonly BuildingAction PolicyAction;
        private BuildingEdit(BuildingAction action, BuildingDefinition definition, PlacedBuilding existing, PlacementRequest request, bool preserveState, bool refund = false,
            BuildingRecord seed = null, bool keepMetadata = false, BuildingAction policyAction = 0)
        { Action = action; Definition = definition; Existing = existing; Request = request; PreserveState = preserveState; Refund = refund; Seed = seed; KeepMetadata = keepMetadata; PolicyAction = policyAction == 0 ? action : policyAction; }
        internal static BuildingEdit FromRecord(BuildingDefinition definition, PlacementRequest request, BuildingRecord seed, bool keepMetadata = false, BuildingAction policyAction = BuildingAction.Place)
            => new BuildingEdit(BuildingAction.Place, definition, null, request, true, seed: seed, keepMetadata: keepMetadata, policyAction: policyAction);
        internal static BuildingEdit Pack(PlacedBuilding building) => new BuildingEdit(BuildingAction.Remove, building != null ? building.Definition : null, building, default, false, policyAction: BuildingAction.Pack);
        internal static BuildingEdit Replay(PlacedBuilding existing, BuildingDefinition definition, PlacementRequest request)
            => new BuildingEdit(existing == null ? BuildingAction.Place : existing.Definition == definition ? BuildingAction.Move : BuildingAction.Upgrade, definition, existing, request, true);
        public static BuildingEdit Place(BuildingDefinition definition, PlacementRequest request) => new BuildingEdit(BuildingAction.Place, definition, null, request, false);
        public static BuildingEdit Move(PlacedBuilding building, PlacementRequest request) => new BuildingEdit(BuildingAction.Move, building != null ? building.Definition : null, building, request, true);
        public static BuildingEdit Upgrade(PlacedBuilding building, BuildingDefinition target, bool preserveState = true)
        {
            var pose = building != null ? new PlacementRequest(building.transform.position, building.transform.rotation, space: building.Space, scale: building.transform.lossyScale) : default;
            return new BuildingEdit(BuildingAction.Upgrade, target, building, pose, preserveState);
        }
        public static BuildingEdit Remove(PlacedBuilding building) => new BuildingEdit(BuildingAction.Remove, building != null ? building.Definition : null, building, default, false);
        public static BuildingEdit Demolish(PlacedBuilding building) => new BuildingEdit(BuildingAction.Remove, building != null ? building.Definition : null, building, default, false, true);
    }

    public sealed class BuildingOperationResult
    {
        public PlacementResult Validation { get; internal set; }
        public IReadOnlyList<PlacedBuilding> Buildings { get; internal set; } = Array.Empty<PlacedBuilding>();
        public BuildingChangeSet Changes { get; internal set; }
        public bool IsValid => Validation.IsValid;
    }

    /// <summary>Optional fluent batch. Every edit succeeds together or no edit/payment is committed.</summary>
    public sealed class BuildingBatch
    {
        private readonly BuildingSystem system;
        private readonly IBuildingActor actor;
        private readonly List<BuildingEdit> edits = new List<BuildingEdit>();
        private bool committed;
        internal BuildingBatch(BuildingSystem system, IBuildingActor actor) { this.system = system; this.actor = actor; }
        public BuildingBatch Place(BuildingDefinition definition, PlacementRequest request) { Add(BuildingEdit.Place(definition, request)); return this; }
        public BuildingBatch Move(PlacedBuilding building, PlacementRequest request) { Add(BuildingEdit.Move(building, request)); return this; }
        public BuildingBatch Upgrade(PlacedBuilding building, BuildingDefinition target, bool preserveState = true) { Add(BuildingEdit.Upgrade(building, target, preserveState)); return this; }
        public BuildingBatch Remove(PlacedBuilding building) { Add(BuildingEdit.Remove(building)); return this; }
        public BuildingBatch Demolish(PlacedBuilding building) { Add(BuildingEdit.Demolish(building)); return this; }
        private void Add(BuildingEdit edit) { if (committed) throw new InvalidOperationException("This batch was already committed."); edits.Add(edit); }
        public bool TryCommit(out BuildingOperationResult result)
        {
            if (committed) { result = new BuildingOperationResult { Validation = PlacementResult.Fail(PlacementFailure.StaleRequest, "This batch was already committed.") }; return false; }
            if (!system.TryExecute(edits, out result, actor)) return false;
            committed = true; return true;
        }
    }
}
