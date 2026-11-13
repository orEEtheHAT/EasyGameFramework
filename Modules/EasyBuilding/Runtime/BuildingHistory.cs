using System;
using System.Collections.Generic;

namespace EasyBuilding
{
    /// <summary>Bounded, transactional undo/redo. Component adapters must report external changes via NotifyStateChanged.</summary>
    public sealed class BuildingHistory : IDisposable
    {
        private readonly BuildingSystem system;
        private readonly IBuildingActor actor;
        private readonly BuildingDefinition[] catalog;
        private readonly List<BuildingChangeSet> undo = new List<BuildingChangeSet>(), redo = new List<BuildingChangeSet>();
        private readonly int capacity;
        private long expectedRevision, replayRevision;
        private bool replaying, interrupted, disposed;
        public int UndoCount => undo.Count;
        public int RedoCount => redo.Count;
        public BuildingHistory(BuildingSystem system, IReadOnlyList<BuildingDefinition> catalog, IBuildingActor actor = null, int capacity = 128)
        {
            this.system = system != null ? system : throw new ArgumentNullException(nameof(system));
            if (catalog == null || capacity < 1) throw new ArgumentException("Supply a catalog and positive history capacity.");
            this.catalog = new BuildingDefinition[catalog.Count]; for (int i = 0; i < catalog.Count; i++) this.catalog[i] = catalog[i];
            this.actor = actor; this.capacity = capacity; expectedRevision = system.Revision; system.Changed += Record;
        }
        private void Record(BuildingChangeSet change)
        {
            if (replaying) { if (change.Revision != replayRevision) interrupted = true; return; }
            if (actor != null && change.ActorId != actor.Id || (change.Action & (BuildingAction.Pack | BuildingAction.State)) != 0) { Clear(); return; }
            undo.Add(BuildingChanges.Copy(change)); if (undo.Count > capacity) undo.RemoveAt(0);
            redo.Clear(); expectedRevision = change.Revision;
        }
        public bool TryUndo(out string reason) => Replay(true, out reason);
        public bool TryRedo(out string reason) => Replay(false, out reason);
        private bool Replay(bool reverse, out string reason)
        {
            reason = null;
            if (disposed || system == null) { reason = "This history is disposed."; return false; }
            if (system.IsReadOnly || replaying) { reason = "History is unavailable on a replica or during another history operation."; return false; }
            var source = reverse ? undo : redo; var destination = reverse ? redo : undo;
            if (source.Count == 0) { reason = reverse ? "Nothing to undo." : "Nothing to redo."; return false; }
            if (system.Revision != expectedRevision) { reason = "The world changed outside this history. Clear it before editing again."; return false; }
            var entry = source[source.Count - 1];
            var restore = reverse ? entry.Previous : entry.Upserts;
            var removals = reverse ? new List<string>() : entry.Removed;
            if (reverse)
            {
                var restoredIds = new HashSet<string>(StringComparer.Ordinal); foreach (var record in restore) restoredIds.Add(record.InstanceId);
                foreach (var record in entry.Upserts) if (!restoredIds.Contains(record.InstanceId)) removals.Add(record.InstanceId);
            }
            var removeLinks = reverse ? new List<string>() : entry.RemovedConnections;
            if (reverse) foreach (var link in entry.Connections) removeLinks.Add(link.Id);
            var payment = new BuildingPaymentReceipt
            {
                Charges = reverse ? entry.Payment.Credits : entry.Payment.Charges,
                Credits = reverse ? entry.Payment.Charges : entry.Payment.Credits
            };
            replaying = true; interrupted = false; replayRevision = checked(expectedRevision + 1);
            try
            {
                if (!system.ApplyPatch(restore, removals, reverse ? entry.PreviousConnections : entry.Connections, removeLinks, catalog,
                    expectedRevision, replayRevision, entry.Action, actor, payment, true, out _, out reason)) return false;
                if (interrupted) { Clear(); return true; }
                source.RemoveAt(source.Count - 1); destination.Add(entry); expectedRevision = replayRevision; return true;
            }
            finally { replaying = false; }
        }
        public void Clear() { undo.Clear(); redo.Clear(); expectedRevision = system != null ? system.Revision : 0; }
        public void Dispose() { if (disposed) return; disposed = true; if (system != null) system.Changed -= Record; Clear(); }
    }
    public static class BuildingChanges
    {
        public static BuildingChangeSet Copy(BuildingChangeSet source, bool replicatedOnly = false)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var result = new BuildingChangeSet { BeforeRevision = source.BeforeRevision, Revision = source.Revision, Action = source.Action, ActorId = source.ActorId };
            foreach (var record in source.Upserts) result.Upserts.Add(CopyRecord(record, replicatedOnly));
            result.Removed.AddRange(source.Removed); result.RemovedConnections.AddRange(source.RemovedConnections);
            foreach (var link in source.Connections) result.Connections.Add(BuildingSystem.CloneLink(link));
            if (!replicatedOnly)
            {
                foreach (var record in source.Previous) result.Previous.Add(BuildingSystem.CloneRecord(record));
                foreach (var link in source.PreviousConnections) result.PreviousConnections.Add(BuildingSystem.CloneLink(link));
                result.Payment = new BuildingPaymentReceipt { Charges = (BuildingCost[])source.Payment.Charges.Clone(), Credits = (BuildingCost[])source.Payment.Credits.Clone() };
            }
            return result;
        }
        internal static BuildingRecord CopyRecord(BuildingRecord source, bool replicatedOnly)
        {
            var record = BuildingSystem.CloneRecord(source);
            if (!replicatedOnly) return record;
            record.PaidCosts = Array.Empty<BuildingCost>(); record.Parts = PublicParts(record.Parts);
            if (record.Construction != null) record.Construction.CompletionParts = PublicParts(record.Construction.CompletionParts);
            return record;
        }
        internal static BuildingPartState[] PublicParts(BuildingPartState[] parts)
        {
            if (parts == null) return Array.Empty<BuildingPartState>();
            var output = new List<BuildingPartState>(); foreach (var part in parts) if (part.Replicate) output.Add(part); return output.ToArray();
        }
    }
}
