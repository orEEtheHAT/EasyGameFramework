using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        public bool IsReadOnly { get; private set; }
        /// <summary>Replica worlds reject local gameplay mutations. Only trusted snapshots/deltas may update them.</summary>
        public void SetReadOnly(bool readOnly) { RequireIdle(); IsReadOnly = readOnly; }
        public bool TryApplyReplicaDelta(BuildingChangeSet delta, IReadOnlyList<BuildingDefinition> catalog, out string reason)
        {
            reason = null;
            if (!IsReadOnly) { reason = "SetReadOnly(true) before applying authoritative data to a replica world."; return false; }
            if (delta == null || delta.Revision <= delta.BeforeRevision) { reason = "Use a delta with an advancing revision."; return false; }
            return ApplyPatch(delta.Upserts, delta.Removed, delta.Connections, delta.RemovedConnections, catalog,
                delta.BeforeRevision, delta.Revision, delta.Action, null, null, false, out _, out reason);
        }
        public bool TryApplyReplicaSnapshot(BuildingState snapshot, IReadOnlyList<BuildingDefinition> catalog, out string reason)
        {
            reason = null;
            if (!IsReadOnly) { reason = "SetReadOnly(true) before applying authoritative data to a replica world."; return false; }
            if (snapshot == null || snapshot.Version != BuildingState.CurrentVersion || snapshot.Buildings == null || snapshot.Revision < 0)
            { reason = "Use a current, complete authoritative snapshot."; return false; }
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in snapshot.Buildings) if (record != null) present.Add(record.InstanceId);
            var remove = new List<string>(); foreach (var id in buildings.Keys) if (!present.Contains(id)) remove.Add(id);
            var links = new List<string>(connections.Keys);
            return ApplyPatch(snapshot.Buildings, remove, snapshot.Connections, links, catalog, Revision, snapshot.Revision,
                BuildingAction.All, null, null, false, out _, out reason);
        }
        internal bool ApplyPatch(IReadOnlyList<BuildingRecord> records, IReadOnlyList<string> removals,
            IReadOnlyList<BuildingConnectionRecord> linkRecords, IReadOnlyList<string> linkRemovals, IReadOnlyList<BuildingDefinition> definitions,
            long expectedRevision, long targetRevision, BuildingAction action, IBuildingActor actor, BuildingPaymentReceipt payment,
            bool validatePlacement, out BuildingChangeSet applied, out string reason)
        {
            applied = null; reason = null;
            if (IsBusy) { reason = "A building operation is running."; return false; }
            if (Revision != expectedRevision) { reason = "The world changed. Request a fresh snapshot or start a new history branch."; return false; }
            if (records == null || definitions == null || targetRevision < 0) { reason = "Supply records, catalog and a valid revision."; return false; }
            busy = true; bool committed = false;
            var staged = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            var old = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            var deleted = new HashSet<string>(StringComparer.Ordinal);
            var affectedLinks = new Dictionary<string, BuildingConnectionRecord>(StringComparer.Ordinal);
            var desiredLinks = new Dictionary<string, BuildingConnectionRecord>(StringComparer.Ordinal);
            var removedLinks = new HashSet<string>(StringComparer.Ordinal);
            IBuildingCostReservation reservation = null;
            try
            {
                var catalog = CreateCatalog(definitions);
                if (removals != null) foreach (var id in removals)
                {
                    if (string.IsNullOrWhiteSpace(id) || !deleted.Add(id) || !buildings.TryGetValue(id, out var item)) throw new InvalidOperationException("A removed building is missing or listed twice.");
                    old.Add(id, item);
                }
                var recordIds = new HashSet<string>(StringComparer.Ordinal); var options = new BuildingRestoreOptions();
                foreach (var record in records)
                {
                    if (record == null || string.IsNullOrWhiteSpace(record.InstanceId) || !recordIds.Add(record.InstanceId) || deleted.Contains(record.InstanceId))
                        throw new InvalidOperationException("Patch building IDs must be distinct and cannot be both removed and replaced.");
                    if (buildings.TryGetValue(record.InstanceId, out var prior)) old[record.InstanceId] = prior;
                    var copy = StageRecord(record, BuildingState.CurrentVersion, catalog, options); staged.Add(record.InstanceId, copy);
                    if (validatePlacement && prior == null && actor != null && !actor.IsAdministrator && !string.IsNullOrEmpty(record.OwnerId) && actor.Id != record.OwnerId)
                        throw new InvalidOperationException("You may not restore another owner's building through history.");
                }
                foreach (var pair in old)
                {
                    if (validatePlacement && !CanEdit(pair.Value, actor)) throw new InvalidOperationException("You may not change one of these buildings.");
                    ignoredRoots.Add(pair.Value.transform);
                    foreach (var link in pair.Value.links) affectedLinks[link.Id] = link.Capture();
                }
                if (linkRemovals != null) foreach (var id in linkRemovals)
                {
                    if (string.IsNullOrWhiteSpace(id) || !removedLinks.Add(id)) throw new InvalidOperationException("Removed connection IDs must be distinct.");
                    if (connections.TryGetValue(id, out var link)) affectedLinks[id] = link.Capture();
                }
                foreach (var pair in affectedLinks)
                    if (!removedLinks.Contains(pair.Key) && !deleted.Contains(pair.Value.SourceId) && !deleted.Contains(pair.Value.TargetId ?? "")) desiredLinks.Add(pair.Key, pair.Value);
                if (linkRecords != null)
                {
                    var unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var record in linkRecords)
                    {
                        if (record == null || string.IsNullOrWhiteSpace(record.Id) || !unique.Add(record.Id)) throw new InvalidOperationException("Patch connection IDs must be distinct and nonempty.");
                        if (connections.TryGetValue(record.Id, out var existing)) affectedLinks[record.Id] = existing.Capture();
                        desiredLinks[record.Id] = CloneLink(record);
                    }
                }
                var occupied = new HashSet<BuildingSocket>(); var primaries = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in desiredLinks.Values)
                {
                    if (record.SourceId == null || deleted.Contains(record.SourceId) || !staged.TryGetValue(record.SourceId, out var source) && !buildings.TryGetValue(record.SourceId, out source))
                        throw new InvalidOperationException("A patched connection source is missing.");
                    if (record.TargetId != null && deleted.Contains(record.TargetId)) throw new InvalidOperationException("A connection points to a removed building.");
                    var a = GetSocket(source, record.SourceSocketId); var b = FindSavedSocket(record.TargetId, record.TargetSocketId, staged);
                    if (b == null || b.Building == source || !string.IsNullOrEmpty(record.SourceSocketId) && a == null ||
                        (a != null ? !a.IsCompatible(b) : source.Definition.SocketType != b.SocketType)) throw new InvalidOperationException("Patched sockets are missing or incompatible.");
                    if (!occupied.Add(b) || a != null && !occupied.Add(a)) throw new InvalidOperationException("Two patch links occupy the same socket.");
                    foreach (var existing in b.links) if (!affectedLinks.ContainsKey(existing.Id)) throw new InvalidOperationException("A patched target socket is occupied.");
                    if (a != null) foreach (var existing in a.links) if (!affectedLinks.ContainsKey(existing.Id)) throw new InvalidOperationException("A patched source socket is occupied.");
                    if (record.Primary)
                    {
                        if (!primaries.Add(record.SourceId)) throw new InvalidOperationException("A patched building has two primary attachments.");
                        foreach (var existing in source.links) if (existing.Primary && existing.Source == source && !affectedLinks.ContainsKey(existing.Id))
                            throw new InvalidOperationException("The connection source already has a primary attachment.");
                    }
                    if (validatePlacement && ((a != null ? a.transform.position : source.transform.position) - b.transform.position).sqrMagnitude > 0.0004f)
                        throw new InvalidOperationException("Connected sockets no longer meet at the restored pose.");
                }
                if (validatePlacement)
                {
                    EnsureServices(); Synchronize(); currentPlan = new List<PreparedEdit>();
                    foreach (var pair in staged)
                    {
                        var item = pair.Value; old.TryGetValue(pair.Key, out var prior);
                        var request = new PlacementRequest(item.transform.position, item.transform.rotation, space: item.Space, scale: item.transform.lossyScale);
                        var edit = BuildingEdit.Replay(prior, item.Definition, request);
                        currentPlan.Add(new PreparedEdit { Edit = edit, Staged = item });
                    }
                    foreach (var id in deleted) currentPlan.Add(new PreparedEdit { Edit = BuildingEdit.Remove(old[id]) });
                    ValidatePatchPlan(currentPlan, primaries, actor);
                }
                var change = new BuildingChangeSet { BeforeRevision = Revision, Revision = targetRevision, Action = action, ActorId = actor?.Id ?? "" };
                foreach (var item in old.Values) change.Previous.Add(CaptureRecord(item));
                foreach (var link in affectedLinks.Values) change.PreviousConnections.Add(CloneLink(link));
                foreach (var item in staged.Values) change.Upserts.Add(CaptureRecord(item));
                foreach (var id in deleted) change.Removed.Add(id);
                foreach (var link in desiredLinks.Values) change.Connections.Add(CloneLink(link));
                foreach (var id in affectedLinks.Keys) if (!desiredLinks.ContainsKey(id)) change.RemovedConnections.Add(id);
                var bill = new BuildingBill(payment?.Charges ?? Array.Empty<BuildingCost>(), payment?.Credits ?? Array.Empty<BuildingCost>(), actor, action);
                if (!CanPay(bill, null, out _, out reason)) return false;
                buildings.EnsureCapacity(buildings.Count + staged.Count); connections.EnsureCapacity(connections.Count + desiredLinks.Count);
                if (!bill.IsEmpty)
                {
                    if (Economy == null) { reason = "Paid undo/redo requires an IBuildingEconomy provider."; return false; }
                    if (!Economy.TryReserve(bill, out reservation, out reason) || reservation == null) return false;
                    if (validatePlacement) { Synchronize(); ValidatePatchPlan(currentPlan, primaries, actor); }
                    if (!reservation.TryCommit(out reason)) return false;
                    change.Payment = new BuildingPaymentReceipt { Charges = CopyCosts(bill.Charges), Credits = CopyCosts(bill.Credits) };
                }
                committed = true;
                foreach (var id in affectedLinks.Keys) if (connections.TryGetValue(id, out var link)) RemoveConnectionInternal(link);
                var removedObjects = new List<PlacedBuilding>(old.Values);
                foreach (var item in removedObjects)
                { buildings.Remove(item.InstanceId); inactiveBuildings.Remove(item); item.ForgetSystem(); item.gameObject.SetActive(false); }
                foreach (var item in staged.Values) { buildings.Add(item.InstanceId, item); item.Version = targetRevision; knownDefinitions[item.Definition.Id] = item.Definition; }
                foreach (var link in desiredLinks.Values) RestoreConnection(link);
                Revision = targetRevision;
                foreach (var record in change.Upserts)
                {
                    var item = staged[record.InstanceId]; UpdateRecordMetadata(record, item, new Pose(item.transform.position, item.transform.rotation), item.transform.lossyScale);
                }
                applied = change;
                publications.Enqueue(new Publication { Change = change, Removed = removedObjects, Placed = new List<PlacedBuilding>(staged.Values) });
                foreach (var item in staged.Values) ApplyPose(item, new Pose(item.transform.position, item.transform.rotation), item.transform.lossyScale, true);
                return true;
            }
            catch (Exception exception)
            {
                if (committed) { Debug.LogException(exception, this); return true; }
                reason = exception.Message; return false;
            }
            finally
            {
                if (!committed) foreach (var item in staged.Values) if (item != null) Rollback(item);
                if (reservation != null) try { reservation.Cancel(); } catch (Exception exception) { Debug.LogException(exception, this); }
                currentPlan = null; ignoredRoots.Clear(); busy = false; FlushRequestedRemovals(); DrainPublications();
            }
        }
        private Dictionary<string, BuildingDefinition> CreateCatalog(IReadOnlyList<BuildingDefinition> definitions)
        {
            var catalog = new Dictionary<string, BuildingDefinition>(knownDefinitions, StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (definition == null || definition.ConfigurationError != null) throw new InvalidOperationException("A catalog definition is missing or invalid.");
                if (catalog.TryGetValue(definition.Id, out var existing) && existing != definition) throw new InvalidOperationException("Two definitions use the same stable ID.");
                catalog[definition.Id] = definition;
            }
            return catalog;
        }
        private void ValidatePatchPlan(List<PreparedEdit> plan, HashSet<string> primaries, IBuildingActor actor)
        {
            foreach (var entry in plan)
            {
                if (entry.Edit.Action == BuildingAction.Remove)
                {
                    var item = entry.Edit.Existing;
                    var reason = CheckRules(new BuildingContext(this, item.Definition, default, new Pose(item.transform.position, item.transform.rotation), item, actor, BuildingAction.Remove));
                    if (reason != null) throw new InvalidOperationException(reason);
                    continue;
                }
                var stagedItem = entry.Staged; var request = entry.Edit.Request;
                var check = CheckCore(stagedItem.Definition, request, entry.Edit.Existing, actor, entry.Edit.Action, false, primaries.Contains(stagedItem.InstanceId));
                if (!check.IsValid || (check.Pose.position - request.Position).sqrMagnitude > 0.0001f || Quaternion.Angle(check.Pose.rotation, request.Rotation) > 0.1f)
                    throw new InvalidOperationException(check.IsValid ? "The restored pose no longer matches placement rules." : check.Reason);
                entry.Check = check;
            }
            var sorted = new List<PreparedEdit>(); var bounds = new Bounds(); bool first = true;
            foreach (var entry in plan) if (entry.Staged != null)
            { entry.Bounds = PlanBounds(entry); sorted.Add(entry); if (first) { bounds = entry.Bounds; first = false; } else bounds.Encapsulate(entry.Bounds); }
            var size = bounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            sorted.Sort((a, b) => a.Bounds.min[axis].CompareTo(b.Bounds.min[axis]));
            for (int i = 0; i < sorted.Count; i++) for (int j = i + 1; j < sorted.Count && sorted[j].Bounds.min[axis] < sorted[i].Bounds.max[axis]; j++)
                if (sorted[i].Bounds.Intersects(sorted[j].Bounds) && PlanOverlaps(sorted[i], sorted[j])) throw new InvalidOperationException("Restored buildings overlap.");
        }
    }
}
