using System;
using System.Collections.Generic;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        private readonly HashSet<string> unloadedIds = new HashSet<string>(StringComparer.Ordinal);
        public bool IsUnloaded(string instanceId) => instanceId != null && unloadedIds.Contains(instanceId);

        /// <summary>Unloads a complete connected group without demolition, refunds or gameplay placement/removal events.
        /// Save the returned snapshot before discarding it. No active connection may cross the group boundary.</summary>
        public bool TryUnload(IReadOnlyList<string> instanceIds, out BuildingState state, out string reason)
        {
            state = null; reason = null;
            if (IsBusy || instanceIds == null) { reason = "Unload while the world is idle, using a list of instance IDs."; return false; }
            busy = true;
            try
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var snapshot = new BuildingState { Revision = Revision };
                foreach (var id in instanceIds)
                {
                    if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new InvalidOperationException("Unload IDs must be distinct and nonempty.");
                    if (buildings.TryGetValue(id, out var item)) snapshot.Buildings.Add(CaptureRecord(item));
                    else if (missingBuildings.TryGetValue(id, out var missing)) snapshot.Buildings.Add(CloneRecord(missing));
                    else throw new InvalidOperationException("An unload target is not loaded: " + id);
                }
                foreach (var link in connections.Values)
                {
                    bool source = ids.Contains(link.Source.InstanceId), target = link.Target != null && ids.Contains(link.Target.InstanceId);
                    if (source != target && link.Target != null) throw new InvalidOperationException("Unload connected buildings together; an active connection crosses this group boundary.");
                    if (source) snapshot.Connections.Add(link.Capture());
                }
                foreach (var link in deferredConnections)
                {
                    bool source = ids.Contains(link.SourceId), target = !string.IsNullOrEmpty(link.TargetId) && ids.Contains(link.TargetId);
                    if (source != target && !string.IsNullOrEmpty(link.TargetId)) throw new InvalidOperationException("Resolve or include both endpoints of pending connections before unloading.");
                    if (source) snapshot.Connections.Add(CloneLink(link));
                }
                if (ids.Count == 0) { state = snapshot; return true; }
                var change = new BuildingChangeSet { BeforeRevision = Revision, Revision = Revision + 1, Action = BuildingAction.State };
                foreach (var link in snapshot.Connections)
                {
                    if (connections.TryGetValue(link.Id, out var active)) RemoveConnectionInternal(active);
                    deferredConnections.RemoveAll(value => value.Id == link.Id);
                    change.RemovedConnections.Add(link.Id);
                }
                foreach (var id in ids)
                {
                    if (buildings.TryGetValue(id, out var item)) Rollback(item);
                    missingBuildings.Remove(id); unloadedIds.Add(id); change.Removed.Add(id);
                }
                Revision++; state = snapshot; busy = false; PublishChanged(change); return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
    }
}
