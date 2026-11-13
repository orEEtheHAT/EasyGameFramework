using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        private static readonly ProfilerMarker OperationMarker = new ProfilerMarker("EasyBuilding.Operation");
        private sealed class PreparedEdit
        {
            internal BuildingEdit Edit;
            internal PlacementResult Check;
            internal PlacedBuilding Staged;
            internal BuildingRecord Before;
            internal BuildingRecord After;
            internal BuildingCost[] Price = Array.Empty<BuildingCost>();
            internal List<BuildingConnectionRecord> PreserveLinks;
            internal Bounds Bounds;
        }
        private List<PreparedEdit> currentPlan;
        private IReadOnlyList<BuildingConnectionRecord> currentPlanLinks;
        public BuildingBatch BeginBatch(IBuildingActor actor = null) => new BuildingBatch(this, actor);

        public bool TryPlace(BuildingDefinition definition, PlacementRequest request, out PlacedBuilding building,
            out PlacementResult result, IBuildingActor actor = null)
        {
            bool success = TryExecute(new[] { BuildingEdit.Place(definition, request) }, out var operation, actor);
            result = operation.Validation; building = success && operation.Buildings.Count != 0 ? operation.Buildings[0] : null;
            return success;
        }
        public bool TryMove(PlacedBuilding building, PlacementRequest request, out PlacementResult result, IBuildingActor actor = null)
        {
            bool success = TryExecute(new[] { BuildingEdit.Move(building, request) }, out var operation, actor);
            result = operation.Validation; return success;
        }
        public bool TryUpgrade(PlacedBuilding building, BuildingDefinition target, out PlacedBuilding replacement,
            out PlacementResult result, IBuildingActor actor = null, bool preserveState = true)
        {
            bool success = TryExecute(new[] { BuildingEdit.Upgrade(building, target, preserveState) }, out var operation, actor);
            result = operation.Validation; replacement = success && operation.Buildings.Count != 0 ? operation.Buildings[0] : null;
            return success;
        }
        public bool TryRemove(PlacedBuilding building, out PlacementResult result, IBuildingActor actor = null)
        {
            bool success = TryExecute(new[] { BuildingEdit.Remove(building) }, out var operation, actor);
            result = operation.Validation; return success;
        }
        public bool Remove(PlacedBuilding building)
        {
            if (building == null || building.System != this) return false;
            if (busy && !destroying) { requestedRemovals.Add(building); return true; }
            return TryRemove(building, out _);
        }
        public bool TryDemolish(PlacedBuilding building, out PlacementResult result, IBuildingActor actor = null)
        {
            bool success = TryExecute(new[] { BuildingEdit.Demolish(building) }, out var operation, actor);
            result = operation.Validation; return success;
        }
        public bool TryPlaceBatch(IReadOnlyList<BuildingPlacement> placements, List<PlacedBuilding> output,
            out PlacementResult result, IBuildingActor actor = null)
        {
            if (placements == null || output == null) throw new ArgumentNullException(placements == null ? nameof(placements) : nameof(output));
            var edits = new BuildingEdit[placements.Count];
            for (int i = 0; i < edits.Length; i++) edits[i] = BuildingEdit.Place(placements[i].Definition, placements[i].Request);
            bool success = TryExecute(edits, out var operation, actor); result = operation.Validation;
            if (success) { output.Clear(); for (int i = 0; i < operation.Buildings.Count; i++) output.Add(operation.Buildings[i]); }
            return success;
        }

        public bool TryExecute(IReadOnlyList<BuildingEdit> edits, out BuildingOperationResult result, IBuildingActor actor = null)
            => Execute(edits, out result, actor, false, null);

        private bool Execute(IReadOnlyList<BuildingEdit> edits, out BuildingOperationResult result, IBuildingActor actor,
            bool noPayment, BuildingPaymentReceipt explicitPayment, IReadOnlyList<BuildingConnectionRecord> additionalLinks = null, string redeemParcel = null)
        {
            using var marker = OperationMarker.Auto();
            result = new BuildingOperationResult();
            if (IsReadOnly) return Reject(result, PlacementFailure.PermissionDenied, "Read-only world: send the operation to the authority.");
            if (redeemParcel != null && redeemedParcels.Contains(redeemParcel)) return Reject(result, PlacementFailure.StaleRequest, "This packed building was already unpacked.");
            if (IsBusy) { result.Validation = PlacementResult.Fail(PlacementFailure.Busy, "A building operation is running."); return false; }
            if (edits == null || edits.Count == 0) { result.Validation = PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Add at least one edit to the batch."); return false; }
            busy = true;
            var plan = new List<PreparedEdit>(edits.Count);
            var charges = new List<BuildingCost>(); var credits = new List<BuildingCost>();
            var touched = new HashSet<PlacedBuilding>(); var claimed = new List<KeyValuePair<BuildingSocket, PlacedBuilding>>();
            var changedNeighbours = new HashSet<PlacedBuilding>();
            var neighbourRecords = new Dictionary<PlacedBuilding, BuildingRecord>();
            var previousLinks = new Dictionary<string, BuildingConnectionRecord>(StringComparer.Ordinal);
            var outputs = new List<PlacedBuilding>(edits.Count);
            var oldObjects = new List<PlacedBuilding>();
            var seedIds = new HashSet<string>(StringComparer.Ordinal);
            IBuildingCostReservation payment = null;
            bool committed = false;
            BuildingAction actions = 0;
            try
            {
                EnsureServices(); ignoredRoots.Clear();
                for (int i = 0; i < edits.Count; i++)
                {
                    var edit = edits[i]; actions |= edit.PolicyAction;
                    if (edit.Action != BuildingAction.Place && edit.Action != BuildingAction.Move && edit.Action != BuildingAction.Upgrade && edit.Action != BuildingAction.Remove)
                        return Reject(result, PlacementFailure.UnsupportedOperation, "Use Place, Move, Upgrade or Remove in this batch.");
                    if (edit.Definition == null) return Reject(result, PlacementFailure.NoSelection, "Select a valid building definition.");
                    if (edit.Seed != null && (string.IsNullOrWhiteSpace(edit.Seed.InstanceId) || buildings.ContainsKey(edit.Seed.InstanceId) || !seedIds.Add(edit.Seed.InstanceId)))
                        return Reject(result, PlacementFailure.InvalidState, "Every copied or restored building needs a new unique instance ID.");
                    if (edit.Action != BuildingAction.Place)
                    {
                        if (edit.Existing == null || edit.Existing.System != this || !buildings.TryGetValue(edit.Existing.InstanceId, out var stored) || stored != edit.Existing)
                            return Reject(result, PlacementFailure.NotFound, "The building is no longer registered in this world.");
                        if (!touched.Add(edit.Existing)) return Reject(result, PlacementFailure.InvalidConfiguration, "A batch may edit each existing building only once.");
                        if (!CanEdit(edit.Existing, actor)) return Reject(result, PlacementFailure.PermissionDenied, "You do not have permission to change this building.");
                        ignoredRoots.Add(edit.Existing.transform);
                    }
                    if (edit.Action == BuildingAction.Upgrade && !edit.Existing.Definition.CanUpgradeTo(edit.Definition))
                        return Reject(result, PlacementFailure.RuleRejected, "Add the target definition to this building's Upgrades list.");
                    if (edit.Action == BuildingAction.Upgrade && !edit.Existing.IsComplete)
                        return Reject(result, PlacementFailure.RuleRejected, "Finish or cancel the current construction before upgrading again.");
                    var entry = new PreparedEdit { Edit = edit };
                    if (edit.Existing != null)
                    {
                        entry.Before = CaptureRecord(edit.Existing);
                        foreach (var link in edit.Existing.links) previousLinks[link.Id] = link.Capture();
                    }
                    if (edit.Action == BuildingAction.Upgrade) entry.PreserveLinks = CaptureLinks(edit.Existing);
                    plan.Add(entry);
                }
                foreach (var entry in plan) if (entry.Edit.Existing != null)
                    foreach (var link in entry.Edit.Existing.links)
                    {
                        var other = link.Other(entry.Edit.Existing);
                        if (other != null && !touched.Contains(other)) changedNeighbours.Add(other);
                    }
                foreach (var neighbour in changedNeighbours) neighbourRecords.Add(neighbour, CaptureRecord(neighbour));
                currentPlan = plan;
                currentPlanLinks = additionalLinks;
                Synchronize();
                if (!ValidatePlan(plan, actor, out var validation)) { result.Validation = validation; return false; }
                BuildPlanBill(plan, actor, charges, credits);
                if (explicitPayment != null)
                { charges.Clear(); credits.Clear(); AddCosts(charges, explicitPayment.Charges, 1); AddCosts(credits, explicitPayment.Credits, 1); }
                var bill = new BuildingBill(charges, credits, actor, actions);
                var legacy = plan.Count == 1 && plan[0].Edit.Action == BuildingAction.Place && CostsMatch(charges, plan[0].Edit.Definition.Costs)
                    ? plan[0].Edit.Definition : null;
                if (!noPayment && !CanPay(bill, legacy, out var failure, out var reason)) return Reject(result, failure, reason);
                if (!noPayment && !bill.IsEmpty)
                {
                    bool reserved = Economy != null ? Economy.TryReserve(bill, out payment, out reason) : CostProvider.TryReserve(legacy, out payment, out reason);
                    if (!reserved || payment == null) return Reject(result, PlacementFailure.PaymentFailed, Useful(reason, "Could not reserve the full cost."));
                    Synchronize();
                    if (!ValidatePlan(plan, actor, out validation)) { result.Validation = validation; return false; }
                    var newCharges = new List<BuildingCost>(); var newCredits = new List<BuildingCost>();
                    if (explicitPayment == null)
                    {
                        BuildPlanBill(plan, actor, newCharges, newCredits);
                        if (!CostsMatch(charges, newCharges) || !CostsMatch(credits, newCredits))
                            return Reject(result, PlacementFailure.StaleRequest, "The price changed while reserving resources. Request a fresh quote.");
                    }
                }
                // All construction happens under an inactive root. Gameplay callbacks start only after commit.
                foreach (var entry in plan)
                {
                    var edit = entry.Edit;
                    if (edit.Action != BuildingAction.Place && edit.Action != BuildingAction.Upgrade) continue;
                    string id = edit.Existing != null ? edit.Existing.InstanceId : edit.Seed != null ? edit.Seed.InstanceId : Guid.NewGuid().ToString("N");
                    bool site = edit.KeepMetadata ? HasConstruction(edit.Seed) : edit.Definition.WorkRequired > 0;
                    entry.Staged = CreateStaged(edit.Definition, id, entry.Check.Pose, edit.Request.Scale, site);
                    entry.Staged.Space = ResolveSpace(edit.Request, edit.Existing);
                    entry.Staged.OwnerId = edit.Existing != null ? edit.Existing.OwnerId : actor?.Id ?? "";
                    entry.Staged.TeamId = edit.Existing != null ? edit.Existing.TeamId : actor?.TeamId ?? "";
                    var initialParts = edit.Existing != null && edit.PreserveState ? entry.Before.Parts : edit.Seed?.Parts;
                    bool upfront = !site || edit.Definition.ConstructionPayment == BuildingConstructionPayment.Upfront;
                    entry.Staged.PaidCosts = CombineCosts(edit.Existing != null ? edit.Existing.PaidCosts : null, noPayment || !upfront ? null : entry.Price);
                    if (site)
                    {
                        entry.Staged.Construction = new BuildingConstructionState
                        {
                            TotalWork = edit.Definition.WorkRequired, Required = (BuildingCost[])entry.Price.Clone(),
                            Delivered = upfront ? (BuildingCost[])entry.Price.Clone() : Array.Empty<BuildingCost>(),
                            CompletionParts = initialParts != null ? (BuildingPartState[])initialParts.Clone() : Array.Empty<BuildingPartState>()
                        };
                    }
                    else if (edit.Action == BuildingAction.Upgrade && edit.PreserveState) entry.Staged.RestoreUpgradeParts(initialParts, edit.Existing.Definition);
                    else entry.Staged.RestoreParts(initialParts);
                    if (edit.Seed != null)
                    {
                        if (edit.KeepMetadata)
                        {
                            entry.Staged.OwnerId = edit.Seed.OwnerId ?? ""; entry.Staged.TeamId = edit.Seed.TeamId ?? "";
                            entry.Staged.PaidCosts = ValidateSavedCosts(edit.Seed.PaidCosts);
                            entry.Staged.Construction = HasConstruction(edit.Seed) ? CloneConstruction(edit.Seed.Construction) : null;
                        }
                    }
                    if (entry.Staged.Construction != null)
                    {
                        ValidateConstruction(entry.Staged.Construction);
                        var completionParts = entry.Staged.Construction.CompletionParts;
                        if (completionParts != null && completionParts.Length != 0)
                        {
                            var validationCopy = CreateStaged(edit.Definition, Guid.NewGuid().ToString("N"), entry.Check.Pose, edit.Request.Scale);
                            try
                            {
                                if (edit.Action == BuildingAction.Upgrade && edit.PreserveState) validationCopy.RestoreUpgradeParts(completionParts, edit.Existing.Definition);
                                else validationCopy.RestoreParts(completionParts);
                                entry.Staged.Construction.CompletionParts = validationCopy.CaptureParts();
                            }
                            finally { Rollback(validationCopy); }
                        }
                    }
                }
                if (!ValidateAdditionalLinks(plan, additionalLinks, out reason)) return Reject(result, PlacementFailure.InvalidConnection, reason);
                foreach (var entry in plan) if (entry.PreserveLinks != null && !ValidateReplacementLinks(entry, out reason))
                    return Reject(result, PlacementFailure.InvalidConnection, reason);
                foreach (var entry in plan)
                    if (entry.Edit.Action != BuildingAction.Remove)
                    {
                        // Custom state adapters must succeed before any resources are committed.
                        entry.After = CaptureRecord(entry.Staged != null ? entry.Staged : entry.Edit.Existing);
                    }
                foreach (var entry in plan)
                {
                    var target = entry.Check.Socket;
                    if (target == null || entry.Edit.Action == BuildingAction.Upgrade) continue;
                    if (target.Building != null && touched.Contains(target.Building)) return Reject(result, PlacementFailure.InvalidConnection, "This target socket is changing in the same batch.");
                    var claimant = entry.Staged != null ? entry.Staged : entry.Edit.Existing;
                    if (!target.TryClaim(claimant)) return Reject(result, PlacementFailure.OccupiedSocket, "This socket became occupied.");
                    claimed.Add(new KeyValuePair<BuildingSocket, PlacedBuilding>(target, claimant));
                }
                buildings.EnsureCapacity(buildings.Count + plan.Count);
                connections.EnsureCapacity(connections.Count + plan.Count * 2);
                if (redeemParcel != null) redeemedParcels.EnsureCapacity(redeemedParcels.Count + 1);
                if (payment != null && !payment.TryCommit(out reason)) return Reject(result, PlacementFailure.PaymentFailed, Useful(reason, "Payment was declined."));

                committed = true;
                if (redeemParcel != null) redeemedParcels.Add(redeemParcel);
                long previousRevision = Revision;
                Revision++;
                foreach (var entry in plan)
                {
                    var old = entry.Edit.Existing;
                    if (old == null) continue;
                    foreach (var link in old.links) { var other = link.Other(old); if (other != null && !touched.Contains(other)) changedNeighbours.Add(other); }
                    old.ReleaseLinks();
                    if (entry.Edit.Action == BuildingAction.Remove || entry.Edit.Action == BuildingAction.Upgrade)
                    {
                        buildings.Remove(old.InstanceId); inactiveBuildings.Remove(old); old.ForgetSystem();
                        old.gameObject.SetActive(false); oldObjects.Add(old);
                    }
                }
                foreach (var entry in plan)
                {
                    var edit = entry.Edit;
                    var building = entry.Staged != null ? entry.Staged : edit.Existing;
                    if (edit.Action == BuildingAction.Remove) continue;
                    if (entry.Staged != null) buildings.Add(building.InstanceId, building);
                    building.Space = ResolveSpace(edit.Request, edit.Existing);
                    building.Version = Revision;
                    if (edit.Action == BuildingAction.Move)
                        ApplyPose(building, entry.Check.Pose, edit.Request.Scale, false);
                    knownDefinitions[building.Definition.Id] = building.Definition;
                    outputs.Add(building);
                }
                foreach (var entry in plan)
                {
                    var building = entry.Staged != null ? entry.Staged : entry.Edit.Existing;
                    if (entry.Edit.Action == BuildingAction.Remove) continue;
                    if (entry.PreserveLinks != null)
                    {
                        foreach (var record in entry.PreserveLinks)
                            if (!connections.ContainsKey(record.Id)) RestoreConnection(record);
                    }
                    else if (entry.Check.Socket != null)
                    {
                        string sourceId = entry.Edit.Request.SourceSocketId ?? entry.Edit.Definition.AttachmentSocketId;
                        AddConnection(building, GetSocket(building, sourceId), entry.Check.Socket, true, null);
                    }
                }
                foreach (var pair in claimed) pair.Key.Release(pair.Value);
                if (additionalLinks != null) foreach (var link in additionalLinks) RestoreConnection(link);
                foreach (var neighbour in changedNeighbours) if (neighbour != null && neighbour.System == this) neighbour.Version = Revision;
                var changes = new BuildingChangeSet { BeforeRevision = previousRevision, Revision = Revision, Action = actions, ActorId = actor?.Id ?? "",
                    Payment = new BuildingPaymentReceipt { Charges = noPayment ? Array.Empty<BuildingCost>() : charges.ToArray(), Credits = noPayment ? Array.Empty<BuildingCost>() : credits.ToArray() } };
                foreach (var entry in plan)
                {
                    if (entry.Before != null) changes.Previous.Add(entry.Before);
                    if (entry.Edit.Action == BuildingAction.Remove) changes.Removed.Add(entry.Before.InstanceId);
                    else
                    {
                        UpdateRecordMetadata(entry.After, entry.Staged != null ? entry.Staged : entry.Edit.Existing,
                            entry.Check.Pose, entry.Edit.Request.Scale);
                        changes.Upserts.Add(entry.After);
                    }
                }
                foreach (var pair in neighbourRecords) if (pair.Key != null && pair.Key.System == this)
                {
                    var record = pair.Value;
                    changes.Previous.Add(CloneRecord(record));
                    UpdateRecordMetadata(record, pair.Key, new Pose(pair.Key.transform.position, pair.Key.transform.rotation), pair.Key.transform.lossyScale);
                    changes.Upserts.Add(record);
                }
                var emittedLinks = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in previousLinks)
                {
                    changes.PreviousConnections.Add(pair.Value);
                    if (!connections.ContainsKey(pair.Key)) changes.RemovedConnections.Add(pair.Key);
                }
                foreach (var item in outputs) foreach (var link in item.links)
                    if (emittedLinks.Add(link.Id)) changes.Connections.Add(link.Capture());
                result.Changes = changes; result.Buildings = outputs;
                result.Validation = new PlacementResult(PlacementFailure.None, "Operation completed", plan[0].Check.Pose);
                currentPlan = null; currentPlanLinks = null; ignoredRoots.Clear();
                var placedEvents = new List<PlacedBuilding>();
                foreach (var entry in plan) if (entry.Staged != null) placedEvents.Add(entry.Staged);
                publications.Enqueue(new Publication { Change = changes, Removed = oldObjects, Placed = placedEvents });
                foreach (var entry in plan) if (entry.Staged != null) ApplyPose(entry.Staged, entry.Check.Pose, entry.Edit.Request.Scale, true);
                busy = false;
                FlushRequestedRemovals();
                DrainPublications();
                return true;
            }
            catch (Exception exception)
            {
                if (committed)
                {
                    Debug.LogException(exception, this);
                    result.Validation = new PlacementResult(PlacementFailure.None, "Operation committed; a gameplay callback reported an error", default);
                    result.Buildings = outputs; return true;
                }
                return Reject(result, PlacementFailure.CreationFailed, "Building operation failed: " + exception.Message);
            }
            finally
            {
                foreach (var pair in claimed) if (pair.Key != null) pair.Key.Release(pair.Value);
                if (!committed) foreach (var entry in plan) if (entry.Staged != null) Rollback(entry.Staged);
                if (payment != null) try { payment.Cancel(); } catch (Exception exception) { Debug.LogException(exception, this); }
                currentPlan = null; currentPlanLinks = null; ignoredRoots.Clear(); busy = false;
                FlushRequestedRemovals(); DrainPublications();
            }
        }

        private bool ValidatePlan(List<PreparedEdit> plan, IBuildingActor actor, out PlacementResult result)
        {
            var targets = new HashSet<BuildingSocket>();
            foreach (var entry in plan)
            {
                var edit = entry.Edit;
                if (edit.Action == BuildingAction.Remove)
                {
                    var context = new BuildingContext(this, edit.Definition, default,
                        new Pose(edit.Existing.transform.position, edit.Existing.transform.rotation), edit.Existing, actor, edit.PolicyAction);
                    var reason = CheckRules(context);
                    if (reason != null) { result = PlacementResult.Fail(PlacementFailure.RuleRejected, reason); return false; }
                    continue;
                }
                var request = edit.Request;
                // Upgrade preserves the old primary connection while validating the replacement footprint.
                if (edit.Action == BuildingAction.Upgrade && edit.Existing.AttachedSocket != null)
                {
                    var target = edit.Existing.AttachedSocket;
                    request = new PlacementRequest(request.Position, Quaternion.Inverse(target.transform.rotation) * request.Rotation,
                        target, request.Space, request.Scale, sourceSocketId: edit.Definition.AttachmentSocketId);
                }
                bool embedded = false;
                if (edit.Seed != null && currentPlanLinks != null) foreach (var link in currentPlanLinks)
                    if (link.Primary && link.SourceId == edit.Seed.InstanceId) { embedded = true; break; }
                entry.Check = CheckCore(edit.Definition, request, edit.Existing, actor, edit.PolicyAction, false, embedded);
                if (!entry.Check.IsValid) { result = entry.Check; return false; }
                if (entry.Check.Socket != null && !targets.Add(entry.Check.Socket))
                { result = PlacementResult.Fail(PlacementFailure.OccupiedSocket, "Two edits cannot claim the same socket."); return false; }
            }
            // Sweep the longest world axis. Sparse rows and large grids avoid an all-pairs SAT pass.
            var ordered = new List<PreparedEdit>(plan.Count);
            var totalBounds = new Bounds(); bool first = true;
            foreach (var entry in plan)
            {
                if (entry.Edit.Action == BuildingAction.Remove) continue;
                entry.Bounds = PlanBounds(entry); ordered.Add(entry);
                if (first) { totalBounds = entry.Bounds; first = false; } else totalBounds.Encapsulate(entry.Bounds);
            }
            var size = totalBounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            ordered.Sort((a, b) => a.Bounds.min[axis].CompareTo(b.Bounds.min[axis]));
            for (int a = 0; a < ordered.Count; a++)
            {
                float end = ordered[a].Bounds.max[axis];
                for (int b = a + 1; b < ordered.Count; b++)
                {
                    if (ordered[b].Bounds.min[axis] >= end) break;
                    if (ordered[a].Bounds.Intersects(ordered[b].Bounds) && PlanOverlaps(ordered[a], ordered[b]))
                    { result = PlacementResult.Fail(PlacementFailure.Obstructed, "Buildings inside this batch overlap or block each other's clearance."); return false; }
                }
            }
            result = new PlacementResult(PlacementFailure.None, "Ready to build", default); return true;
        }
        private static Bounds PlanBounds(PreparedEdit entry)
        {
            var definition = entry.Edit.Definition;
            var result = BuildingGeometry.Transform(definition.GetVolume(0), entry.Check.Pose, entry.Edit.Request.Scale).Bounds;
            for (int i = 1; i < definition.VolumeCount; i++) result.Encapsulate(BuildingGeometry.Transform(definition.GetVolume(i), entry.Check.Pose, entry.Edit.Request.Scale).Bounds);
            if (definition.Clearances != null) foreach (var volume in definition.Clearances)
                result.Encapsulate(BuildingGeometry.Transform(volume, entry.Check.Pose, entry.Edit.Request.Scale).Bounds);
            return result;
        }
        private static bool PlanOverlaps(PreparedEdit a, PreparedEdit b)
        {
            var da = a.Edit.Definition; var db = b.Edit.Definition;
            if (!da.CheckObstacles && !db.CheckObstacles) return false;
            int ac = da.VolumeCount + (da.Clearances?.Length ?? 0), bc = db.VolumeCount + (db.Clearances?.Length ?? 0);
            for (int i = 0; i < ac; i++)
                for (int j = 0; j < bc; j++)
                {
                    if (i >= da.VolumeCount && j >= db.VolumeCount) continue; // Shared clearances may overlap.
                    var va = i < da.VolumeCount ? da.GetVolume(i) : da.Clearances[i - da.VolumeCount];
                    var vb = j < db.VolumeCount ? db.GetVolume(j) : db.Clearances[j - db.VolumeCount];
                    if (BuildingGeometry.Intersects(BuildingGeometry.Transform(va, a.Check.Pose, a.Edit.Request.Scale),
                        BuildingGeometry.Transform(vb, b.Check.Pose, b.Edit.Request.Scale), 0.01f)) return true;
                }
            return false;
        }
        private static bool Reject(BuildingOperationResult result, PlacementFailure failure, string reason)
        { result.Validation = PlacementResult.Fail(failure, reason); return false; }

        private void BuildPlanBill(List<PreparedEdit> plan, IBuildingActor actor, List<BuildingCost> charges, List<BuildingCost> credits)
        {
            foreach (var entry in plan)
            {
                if (entry.Edit.Action == BuildingAction.Place || entry.Edit.Action == BuildingAction.Upgrade)
                {
                    var price = new List<BuildingCost>(); AddPrice(price, entry.Edit.Definition, entry.Edit.Request.Scale, actor, entry.Edit.Action);
                    entry.Price = price.ToArray();
                    if (entry.Edit.Definition.WorkRequired <= 0 || entry.Edit.Definition.ConstructionPayment == BuildingConstructionPayment.Upfront)
                        AddCosts(charges, price, 1);
                }
                else if (entry.Edit.Action == BuildingAction.Remove && entry.Edit.Refund) AddCosts(credits, entry.Edit.Existing.PaidCosts, entry.Edit.Definition.DemolitionRefund);
            }
        }
        private void AddPrice(List<BuildingCost> output, BuildingDefinition definition, Vector3 scale, IBuildingActor actor, BuildingAction action)
        {
            if (definition.Payment == BuildingPayment.Free) return;
            double units = 1;
            var size = Vector3.Scale(definition.BoundsSize, scale);
            if (definition.CostBasis == BuildingCostBasis.Length) units = size.z;
            else if (definition.CostBasis == BuildingCostBasis.Area) units = definition.GridPlane == BuildingGridPlane.XY ? (double)size.x * size.y : (double)size.x * size.z;
            else if (definition.CostBasis == BuildingCostBasis.Volume) units = (double)size.x * size.y * size.z;
            float modifier = PriceModifier != null ? PriceModifier.GetMultiplier(definition, actor, action) : 1;
            if (!BuildingMath.Finite(modifier) || modifier < 0 || double.IsInfinity(units) || double.IsNaN(units) || units < 0) throw new InvalidOperationException("Cost multipliers must be finite and nonnegative.");
            foreach (var cost in definition.Costs)
            {
                if (units == 1 && modifier == 1) { AddCost(output, cost.ResourceId, cost.Amount); continue; }
                decimal amount = decimal.Ceiling(cost.Amount * (decimal)units * (decimal)modifier);
                AddCost(output, cost.ResourceId, checked((long)amount));
            }
        }
        private static void AddCosts(List<BuildingCost> output, IReadOnlyList<BuildingCost> costs, float factor)
        {
            if (costs == null) return;
            for (int i = 0; i < costs.Count; i++) AddCost(output, costs[i].ResourceId, factor == 1 ? costs[i].Amount : checked((long)decimal.Floor(costs[i].Amount * (decimal)factor)));
        }
        private static void AddCost(List<BuildingCost> output, string id, long amount)
        {
            if (amount == 0) return;
            if (amount < 0 || string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Costs require an ID and a nonnegative amount.");
            for (int i = 0; i < output.Count; i++)
                if (output[i].ResourceId == id) { output[i] = new BuildingCost(id, checked(output[i].Amount + amount)); return; }
            output.Add(new BuildingCost(id, amount));
        }
        private static BuildingCost[] CombineCosts(IReadOnlyList<BuildingCost> a, IReadOnlyList<BuildingCost> b)
        { var result = new List<BuildingCost>(); AddCosts(result, a, 1); AddCosts(result, b, 1); return result.ToArray(); }
        private static bool CostsMatch(IReadOnlyList<BuildingCost> a, IReadOnlyList<BuildingCost> b)
        {
            if ((a?.Count ?? 0) != (b?.Count ?? 0)) return false;
            for (int i = 0; i < (a?.Count ?? 0); i++) if (a[i].ResourceId != b[i].ResourceId || a[i].Amount != b[i].Amount) return false;
            return true;
        }
        private bool CanPay(in BuildingBill bill, BuildingDefinition legacy, out PlacementFailure failure, out string reason)
        {
            failure = PlacementFailure.None; reason = null;
            if (bill.IsEmpty && (legacy == null || legacy.Payment != BuildingPayment.Paid)) return true;
            if (Economy == null && CostProvider == null)
            { failure = PlacementFailure.MissingCostProvider; reason = "Assign a Cost Provider on Building System, or choose Free payment."; return false; }
            try
            {
                bool valid;
                if (Economy != null) valid = Economy.CanApply(bill, out reason);
                else
                {
                    if (legacy == null || bill.Credits.Count != 0)
                    { failure = PlacementFailure.UnsupportedOperation; reason = "This operation needs an IBuildingEconomy provider for aggregate costs and refunds."; return false; }
                    valid = CostProvider.CanAfford(legacy, out reason);
                }
                if (valid) return true;
                failure = PlacementFailure.CannotAfford; reason = Useful(reason, "Not enough resources or storage capacity."); return false;
            }
            catch (Exception exception) { failure = PlacementFailure.PaymentFailed; reason = "Cost Provider failed: " + exception.Message; return false; }
        }

        private PlacedBuilding CreateStaged(BuildingDefinition definition, string id, Pose pose, Vector3? scale = null, bool constructionSite = false)
        {
            if (stagingRoot == null)
            {
                var root = new GameObject("EasyBuilding staging") { hideFlags = HideFlags.HideInHierarchy };
                root.SetActive(false); SceneManager.MoveGameObjectToScene(root, gameObject.scene); stagingRoot = root.transform;
            }
            GameObject clone;
            if (constructionSite)
            {
                clone = new GameObject(definition.Name + " (construction)"); clone.transform.SetParent(stagingRoot, false);
                clone.AddComponent<BuildingSite>();
                // Only socket configuration is copied. No finished-building gameplay script is instantiated yet.
                foreach (var socket in definition.Prefab.GetComponentsInChildren<BuildingSocket>(true))
                {
                    var port = new GameObject(socket.name); port.transform.SetParent(clone.transform, false);
                    port.transform.localPosition = definition.Prefab.transform.InverseTransformPoint(socket.transform.position);
                    port.transform.localRotation = Quaternion.Inverse(definition.Prefab.transform.rotation) * socket.transform.rotation;
                    port.AddComponent<BuildingSocket>().CopyConfiguration(socket);
                }
            }
            else clone = Instantiate(definition.Prefab, stagingRoot);
            try
            {
                clone.transform.SetPositionAndRotation(pose.position, pose.rotation); clone.transform.localScale = scale ?? Vector3.one;
                var building = clone.GetComponent<PlacedBuilding>(); if (building == null) building = clone.AddComponent<PlacedBuilding>();
                building.Initialize(this, definition, id);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var socket in building.Sockets)
                    if (string.IsNullOrWhiteSpace(socket.Id) || !ids.Add(socket.Id)) throw new InvalidOperationException("Prefab socket IDs must be nonempty and unique.");
                return building;
            }
            catch { BuildingMath.Destroy(clone); throw; }
        }
        private void ApplyPose(PlacedBuilding building, Pose pose, Vector3 worldScale, bool activate)
        {
            building.transform.SetParent(building.Space != null ? building.Space.transform : null, true);
            building.transform.SetPositionAndRotation(pose.position, pose.rotation);
            float parentScale = building.Space != null ? building.Space.transform.lossyScale.x : 1;
            building.transform.localScale = worldScale / parentScale;
            if (activate) building.gameObject.SetActive(true);
        }
        private void Rollback(PlacedBuilding building)
        {
            if (buildings.TryGetValue(building.InstanceId, out var registered) && registered == building) buildings.Remove(building.InstanceId);
            inactiveBuildings.Remove(building); building.ReleaseLinks(); building.ForgetSystem();
            building.gameObject.SetActive(false); BuildingMath.Destroy(building.gameObject);
        }
        internal void NotifyDestroyed(PlacedBuilding building)
        {
            inactiveBuildings.Remove(building);
            if (building.InstanceId == null || !buildings.TryGetValue(building.InstanceId, out var stored) || stored != building) return;
            var links = CaptureLinks(building);
            building.ReleaseLinks();
            buildings.Remove(building.InstanceId);
            if (destroying) return;
            long before = Revision++; var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.Remove | BuildingAction.State };
            change.Removed.Add(building.InstanceId);
            foreach (var link in links) { change.RemovedConnections.Add(link.Id); change.PreviousConnections.Add(link); }
            publications.Enqueue(new Publication { Change = change, Removed = new List<PlacedBuilding> { building }, KeepRemovedObjects = true });
            DrainPublications();
        }
        private static void Publish(Action<PlacedBuilding> handlers, PlacedBuilding building)
        {
            if (handlers == null) return;
            foreach (Action<PlacedBuilding> handler in handlers.GetInvocationList())
                try { handler(building); } catch (Exception exception) { Debug.LogException(exception); }
        }
        private void PublishChanged(BuildingChangeSet change)
        {
            publications.Enqueue(new Publication { Change = change });
            DrainPublications();
        }
        private void DispatchChanged(BuildingChangeSet change)
        {
            if (Changed == null) return;
            foreach (Action<BuildingChangeSet> handler in Changed.GetInvocationList())
                try { handler(change); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        private sealed class Publication
        {
            internal BuildingChangeSet Change;
            internal List<PlacedBuilding> Removed, Placed;
            internal bool KeepRemovedObjects;
        }
        private readonly Queue<Publication> publications = new Queue<Publication>();
        private readonly HashSet<PlacedBuilding> requestedRemovals = new HashSet<PlacedBuilding>();
        private bool publishing;
        private int publicationHold;
        private void FlushRequestedRemovals()
        {
            if (requestedRemovals.Count == 0) return;
            var pending = new List<PlacedBuilding>(requestedRemovals); requestedRemovals.Clear();
            publicationHold++;
            try { foreach (var item in pending) if (item != null && item.System == this) Remove(item); }
            finally { publicationHold--; }
        }
        private void DrainPublications()
        {
            if (busy || publishing || publicationHold != 0 || destroying) return;
            publishing = true;
            try
            {
                while (publications.Count != 0)
                {
                    var publication = publications.Dequeue();
                    if (publication.Change != null) DispatchChanged(publication.Change);
                    if (publication.Removed != null) foreach (var old in publication.Removed)
                        if (old != null) { Publish(Removed, old); if (!publication.KeepRemovedObjects) BuildingMath.Destroy(old.gameObject); }
                    if (publication.Placed != null) foreach (var building in publication.Placed)
                        if (building != null) Publish(Placed, building);
                }
            }
            finally { publishing = false; }
        }
        private void OnDestroy()
        {
            destroying = true;
            while (sessions.Count != 0) { int index = sessions.Count - 1; var session = sessions[index]; sessions.RemoveAt(index); session.Dispose(); }
            var owned = new List<PlacedBuilding>(buildings.Values);
            foreach (var building in owned)
            {
                if (building == null) continue;
                building.ReleaseLinks(); building.ForgetSystem();
                if (DestroyBuildingsWithSystem) { building.gameObject.SetActive(false); BuildingMath.Destroy(building.gameObject); }
            }
            buildings.Clear(); connections.Clear(); inactiveBuildings.Clear();
            publications.Clear(); requestedRemovals.Clear();
            if (stagingRoot != null) BuildingMath.Destroy(stagingRoot.gameObject);
        }
    }
}
