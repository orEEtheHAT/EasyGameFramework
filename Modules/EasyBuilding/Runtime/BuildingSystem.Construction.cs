using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        public bool TryDeliver(PlacedBuilding site, IReadOnlyList<BuildingCost> materials, out PlacementResult result, IBuildingActor actor = null)
        {
            result = ValidateSite(site, actor);
            if (!result.IsValid) return false;
            if (materials == null || materials.Count == 0) { result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Choose materials to deliver."); return false; }
            try
            {
                var charges = new List<BuildingCost>(); AddCosts(charges, materials, 1);
                if (charges.Count == 0) { result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Delivery must contain a positive amount."); return false; }
                foreach (var cost in charges) if (cost.Amount > site.Construction.Remaining(cost.ResourceId))
                { result = PlacementResult.Fail(PlacementFailure.InvalidState, "Deliver only materials still required by this site."); return false; }
                var next = CloneConstruction(site.Construction); next.Delivered = CombineCosts(next.Delivered, charges);
                return ChangeConstruction(site, next, charges, actor, out _, out result);
            }
            catch (Exception exception) { result = PlacementResult.Fail(PlacementFailure.InvalidState, exception.Message); return false; }
        }
        public bool TryWork(PlacedBuilding site, float work, out PlacedBuilding building, out PlacementResult result, IBuildingActor actor = null)
        {
            building = site; result = ValidateSite(site, actor);
            if (!result.IsValid) return false;
            if (!BuildingMath.Finite(work) || work <= 0) { result = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Work must be positive and finite."); return false; }
            if (site.Construction.Paused) { result = PlacementResult.Fail(PlacementFailure.RuleRejected, "Construction is paused."); return false; }
            if (!site.Construction.MaterialsReady) { result = PlacementResult.Fail(PlacementFailure.CannotAfford, "Deliver the remaining materials before adding work."); return false; }
            var next = CloneConstruction(site.Construction); next.Work = (float)Math.Min(next.TotalWork, (double)next.Work + work);
            return ChangeConstruction(site, next, emptyCosts, actor, out building, out result);
        }
        public bool TryPauseConstruction(PlacedBuilding site, bool paused, out PlacementResult result, IBuildingActor actor = null)
        {
            result = ValidateSite(site, actor); if (!result.IsValid) return false;
            if (site.Construction.Paused == paused) return true;
            var next = CloneConstruction(site.Construction); next.Paused = paused;
            return ChangeConstruction(site, next, emptyCosts, actor, out _, out result);
        }
        public bool TryCancelConstruction(PlacedBuilding site, out PlacementResult result, IBuildingActor actor = null)
        {
            result = ValidateSite(site, actor); if (!result.IsValid) return false;
            var refund = new List<BuildingCost>(); AddCosts(refund, site.PaidCosts, site.Definition.CancellationRefund);
            bool success = Execute(new[] { BuildingEdit.Remove(site) }, out var operation, actor, false,
                new BuildingPaymentReceipt { Credits = refund.ToArray() });
            result = operation.Validation; return success;
        }
        private PlacementResult ValidateSite(PlacedBuilding site, IBuildingActor actor)
        {
            if (IsReadOnly) return PlacementResult.Fail(PlacementFailure.PermissionDenied, "Read-only world: send the operation to the authority.");
            if (IsBusy) return PlacementResult.Fail(PlacementFailure.Busy, "A building operation is running.");
            if (site == null || site.System != this || site.Construction == null) return PlacementResult.Fail(PlacementFailure.NotFound, "Choose an unfinished building in this world.");
            if (!CanEdit(site, actor)) return PlacementResult.Fail(PlacementFailure.PermissionDenied, "You may not work on this building.");
            return new PlacementResult(PlacementFailure.None, "Ready", new Pose(site.transform.position, site.transform.rotation));
        }
        private bool ChangeConstruction(PlacedBuilding site, BuildingConstructionState next, IReadOnlyList<BuildingCost> charges,
            IBuildingActor actor, out PlacedBuilding building, out PlacementResult result)
        {
            building = site; result = ValidateSite(site, actor); if (!result.IsValid) return false;
            busy = true; IBuildingCostReservation reservation = null; PlacedBuilding replacement = null; bool committed = false;
            try
            {
                ValidateConstruction(next);
                var pose = new Pose(site.transform.position, site.transform.rotation); var scale = site.transform.lossyScale;
                string reason = CheckRules(new BuildingContext(this, site.Definition, new PlacementRequest(pose.position, pose.rotation, space: site.Space, scale: scale), pose, site, actor, BuildingAction.Work));
                if (reason != null) { result = PlacementResult.Fail(PlacementFailure.RuleRejected, reason); return false; }
                var previous = CaptureRecord(site); var after = CloneRecord(previous); after.Construction = next;
                after.PaidCosts = CombineCosts(site.PaidCosts, charges);
                var links = CaptureLinks(site);
                bool complete = next.Work >= next.TotalWork && next.MaterialsReady;
                if (complete)
                {
                    replacement = CreateStaged(site.Definition, site.InstanceId, pose, scale);
                    replacement.Space = site.Space; replacement.OwnerId = site.OwnerId; replacement.TeamId = site.TeamId;
                    replacement.PaidCosts = after.PaidCosts; replacement.RestoreParts(next.CompletionParts);
                    var entry = new PreparedEdit { Edit = BuildingEdit.Upgrade(site, site.Definition), Staged = replacement, PreserveLinks = links };
                    currentPlan = new List<PreparedEdit> { entry };
                    if (!ValidateReplacementLinks(entry, out reason)) { result = PlacementResult.Fail(PlacementFailure.InvalidConnection, reason); return false; }
                    after = CaptureRecord(replacement); after.Construction = null;
                }
                var bill = new BuildingBill(charges, emptyCosts, actor, BuildingAction.Work);
                if (!CanPay(bill, null, out var failure, out reason)) { result = PlacementResult.Fail(failure, reason); return false; }
                if (!bill.IsEmpty)
                {
                    if (Economy == null) { result = PlacementResult.Fail(PlacementFailure.UnsupportedOperation, "Deliveries need an IBuildingEconomy provider."); return false; }
                    if (!Economy.TryReserve(bill, out reservation, out reason) || reservation == null)
                    { result = PlacementResult.Fail(PlacementFailure.PaymentFailed, Useful(reason, "Delivery could not reserve resources.")); return false; }
                    if (!reservation.TryCommit(out reason)) { result = PlacementResult.Fail(PlacementFailure.PaymentFailed, Useful(reason, "Delivery payment was declined.")); return false; }
                }
                committed = true; long before = Revision++;
                var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.Work, ActorId = actor?.Id ?? "",
                    Payment = new BuildingPaymentReceipt { Charges = CopyCosts(charges) } };
                change.Previous.Add(previous); change.Upserts.Add(after); after.Version = Revision;
                if (complete)
                {
                    site.ReleaseLinks(); inactiveBuildings.Remove(site); site.ForgetSystem(); site.gameObject.SetActive(false);
                    buildings[site.InstanceId] = replacement; replacement.Version = Revision;
                    foreach (var link in links) { RestoreConnection(link); change.Connections.Add(CloneLink(link)); change.PreviousConnections.Add(CloneLink(link)); }
                    building = replacement;
                    publications.Enqueue(new Publication { Change = change, Removed = new List<PlacedBuilding> { site }, Placed = new List<PlacedBuilding> { replacement } });
                    ApplyPose(replacement, pose, scale, true);
                }
                else
                {
                    site.Construction = CloneConstruction(next); site.PaidCosts = (BuildingCost[])after.PaidCosts.Clone(); site.Version = Revision;
                    publications.Enqueue(new Publication { Change = change });
                    site.GetComponent<BuildingSite>()?.Refresh();
                }
                result = new PlacementResult(PlacementFailure.None, complete ? "Construction completed" : "Construction updated", pose);
                return true;
            }
            catch (Exception exception)
            {
                if (committed) { Debug.LogException(exception, this); result = new PlacementResult(PlacementFailure.None, "Construction committed", default); return true; }
                result = PlacementResult.Fail(PlacementFailure.InvalidState, exception.Message); return false;
            }
            finally
            {
                if (!committed && replacement != null) Rollback(replacement);
                if (reservation != null) try { reservation.Cancel(); } catch (Exception exception) { Debug.LogException(exception, this); }
                currentPlan = null; busy = false; FlushRequestedRemovals(); DrainPublications();
            }
        }
        private static BuildingCost[] CopyCosts(IReadOnlyList<BuildingCost> costs)
        { var copy = new BuildingCost[costs.Count]; for (int i = 0; i < copy.Length; i++) copy[i] = costs[i]; return copy; }
        private static void ValidateConstruction(BuildingConstructionState value)
        {
            if (value == null) return;
            if (!BuildingMath.Finite(value.Work) || !BuildingMath.Finite(value.TotalWork) || value.TotalWork <= 0 || value.Work < 0 || value.Work > value.TotalWork)
                throw new InvalidOperationException("Saved construction work must be within its positive total work amount.");
            var required = ValidateSavedCosts(value.Required); var delivered = ValidateSavedCosts(value.Delivered);
            foreach (var row in delivered)
            {
                long maximum = 0; foreach (var cost in required) if (row.ResourceId == cost.ResourceId) maximum = checked(maximum + cost.Amount);
                long actual = 0; foreach (var cost in delivered) if (row.ResourceId == cost.ResourceId) actual = checked(actual + cost.Amount);
                if (actual > maximum) throw new InvalidOperationException("Delivered materials exceed the construction requirement.");
            }
        }
    }
}
