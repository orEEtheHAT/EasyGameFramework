using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Prepared state edit. CanApply is pure; after it succeeds Apply must not fail. Economy providers and their callbacks must not mutate scene state during payment.</summary>
    public interface IBuildingPreparedMutation
    {
        IBuildingStatePart Part { get; }
        BuildingPartState After { get; }
        BuildingAction Action { get; }
        IReadOnlyList<BuildingCost> Costs { get; }
        bool AddToPaidReceipt { get; }
        bool CanApply(out string reason);
        void Apply();
    }
    public interface IBuildingRepairTarget
    {
        bool TryPrepareRepair(float maximumAmount, out IBuildingPreparedMutation repair, out string reason);
    }
    public sealed partial class BuildingSystem
    {
        public bool TryApplyMutation(PlacedBuilding building, IBuildingPreparedMutation mutation, out PlacementResult result, IBuildingActor actor = null)
        {
            result = PlacementResult.Fail(PlacementFailure.InvalidState, "Choose a prepared component change on a building.");
            if (IsBusy || IsReadOnly) { result = PlacementResult.Fail(IsReadOnly ? PlacementFailure.PermissionDenied : PlacementFailure.Busy, "Use an idle authoritative world."); return false; }
            if (building == null || building.System != this || !building.IsComplete || mutation == null || mutation.Part == null || !building.OwnsStatePart(mutation.Part) ||
                mutation.Action != BuildingAction.Customize && mutation.Action != BuildingAction.Repair) return false;
            if (!CanEdit(building, actor)) { result = PlacementResult.Fail(PlacementFailure.PermissionDenied, "You may not change this building."); return false; }
            busy = true; bool committed = false; IBuildingCostReservation reservation = null;
            try
            {
                var pose = new Pose(building.transform.position, building.transform.rotation);
                var request = new PlacementRequest(pose.position, pose.rotation, space: building.Space, scale: building.transform.lossyScale);
                string reason = CheckRules(new BuildingContext(this, building.Definition, request, pose, building, actor, mutation.Action));
                if (reason != null) { result = PlacementResult.Fail(PlacementFailure.RuleRejected, reason); return false; }
                var next = mutation.After;
                if (next.Key != mutation.Part.StateKey || !mutation.CanApply(out reason) || !mutation.Part.CanRestoreBuildingState(next.Version, next.Json, out reason))
                { result = PlacementResult.Fail(PlacementFailure.InvalidState, Useful(reason, "The prepared state edit is no longer valid.")); return false; }
                next.Replicate = mutation.Part.ReplicateState;
                var previous = CaptureRecord(building); var after = CloneRecord(previous); bool found = false;
                for (int i = 0; i < after.Parts.Length; i++) if (after.Parts[i].Key == next.Key) { after.Parts[i] = next; found = true; break; }
                if (!found) throw new InvalidOperationException("The target state adapter is missing from the captured building.");
                var costs = new List<BuildingCost>(); AddCosts(costs, mutation.Costs, 1);
                if (mutation.AddToPaidReceipt) after.PaidCosts = CombineCosts(after.PaidCosts, costs);
                var bill = new BuildingBill(costs, emptyCosts, actor, mutation.Action);
                if (!CanPay(bill, null, out var failure, out reason)) { result = PlacementResult.Fail(failure, reason); return false; }
                if (!bill.IsEmpty)
                {
                    if (Economy == null || !Economy.TryReserve(bill, out reservation, out reason) || reservation == null)
                    { result = PlacementResult.Fail(PlacementFailure.PaymentFailed, Useful(reason, "This change needs an aggregate economy provider.")); return false; }
                    if (!mutation.CanApply(out reason)) { result = PlacementResult.Fail(PlacementFailure.StaleRequest, reason); return false; }
                    if (!reservation.TryCommit(out reason)) { result = PlacementResult.Fail(PlacementFailure.PaymentFailed, reason); return false; }
                }
                committed = true; long before = Revision++; building.Version = Revision; building.PaidCosts = (BuildingCost[])after.PaidCosts.Clone(); after.Version = Revision;
                var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = mutation.Action, ActorId = actor?.Id ?? "",
                    Payment = new BuildingPaymentReceipt { Charges = costs.ToArray() } };
                change.Previous.Add(previous); change.Upserts.Add(after); publications.Enqueue(new Publication { Change = change });
                mutation.Apply(); result = new PlacementResult(PlacementFailure.None, "Building updated", pose); return true;
            }
            catch (Exception exception)
            {
                if (committed) { Debug.LogError("A prepared mutation broke its non-failing Apply contract: " + exception, this); result = new PlacementResult(PlacementFailure.None, "Payment committed; mutation adapter reported an error", default); return true; }
                result = PlacementResult.Fail(PlacementFailure.InvalidState, exception.Message); return false;
            }
            finally
            {
                if (reservation != null) try { reservation.Cancel(); } catch (Exception exception) { Debug.LogException(exception, this); }
                busy = false; FlushRequestedRemovals(); DrainPublications();
            }
        }
        public bool TrySetVariant(PlacedBuilding building, string variantId, out PlacementResult result, IBuildingActor actor = null)
        {
            result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Add Building Appearance to the building prefab.");
            var appearance = building != null ? building.GetComponent<BuildingAppearance>() : null;
            if (appearance == null) return false;
            if (!appearance.TryPrepareVariant(variantId, out var mutation, out var reason)) { result = PlacementResult.Fail(PlacementFailure.InvalidState, reason); return false; }
            return TryApplyMutation(building, mutation, out result, actor);
        }
        public bool TryRepair(PlacedBuilding building, float amount, out PlacementResult result, IBuildingActor actor = null)
        {
            result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Add a repair adapter, such as the EasyCombat Building Health integration.");
            if (building == null) return false;
            foreach (var component in building.GetComponents<MonoBehaviour>()) if (component is IBuildingRepairTarget target)
            {
                if (!target.TryPrepareRepair(amount, out var mutation, out var reason)) { result = PlacementResult.Fail(PlacementFailure.InvalidState, reason); return false; }
                return TryApplyMutation(building, mutation, out result, actor);
            }
            return false;
        }
        /// <summary>Call after external component state changes (health/inventory/load). Invalidates undo and emits a replication record.</summary>
        public bool NotifyStateChanged(PlacedBuilding building, out string reason)
        {
            reason = null;
            if (IsReadOnly || IsBusy) { reason = "State notifications require an idle authoritative world."; return false; }
            if (building == null || building.System != this) { reason = "Building is not registered in this world."; return false; }
            busy = true;
            try
            {
                var record = CaptureRecord(building);
                long before = Revision++; building.Version = Revision; record.Version = Revision;
                var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.State };
                change.Upserts.Add(record); publications.Enqueue(new Publication { Change = change }); return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
    }
}
