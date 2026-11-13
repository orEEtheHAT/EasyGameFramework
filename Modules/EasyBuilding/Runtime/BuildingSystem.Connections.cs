using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        public IReadOnlyCollection<BuildingConnection> Connections => connections.Values;

        public bool TryConnect(BuildingSocket source, BuildingSocket target, out string reason, IBuildingActor actor = null)
        {
            reason = null;
            if (IsReadOnly) { reason = "Read-only world: send the operation to the authority."; return false; }
            if (IsBusy) { reason = "A building operation is running."; return false; }
            if (source == null || target == null || source.Building == null || source.Building.System != this || !Accepts(target) || source.Building == target.Building)
            { reason = "Choose sockets on different buildings in this world (the target may be a scene socket)."; return false; }
            if (!CanEdit(source.Building, actor) || target.Building != null && !CanEdit(target.Building, actor))
            { reason = "You may not change both endpoints of this connection."; return false; }
            if (source.IsOccupied || target.IsOccupied) { reason = "One of these sockets is occupied."; return false; }
            if (!source.isActiveAndEnabled || !target.isActiveAndEnabled || !source.IsCompatible(target))
            { reason = "Both sockets must be enabled and compatible in type, role and size."; return false; }
            if ((source.transform.position - target.transform.position).sqrMagnitude > 0.0004f)
            { reason = "Move the sockets within 0.02 units of each other before connecting them."; return false; }
            busy = true;
            try
            {
                var sourceRecord = CaptureRecord(source.Building);
                var targetRecord = target.Building != null ? CaptureRecord(target.Building) : null;
                var link = AddConnection(source.Building, source, target, false, null);
                long before = Revision++; source.Building.Version = Revision;
                if (target.Building != null) target.Building.Version = Revision;
                var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.Connect, ActorId = actor?.Id ?? "" };
                change.Previous.Add(CloneRecord(sourceRecord));
                if (targetRecord != null) change.Previous.Add(CloneRecord(targetRecord));
                sourceRecord.Version = Revision; change.Upserts.Add(sourceRecord);
                if (targetRecord != null) { targetRecord.Version = Revision; change.Upserts.Add(targetRecord); }
                change.Connections.Add(link.Capture());
                busy = false; PublishChanged(change); return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
        public bool TryDisconnect(string connectionId, out string reason, IBuildingActor actor = null)
        {
            reason = null;
            if (IsReadOnly) { reason = "Read-only world: send the operation to the authority."; return false; }
            if (IsBusy) { reason = "A building operation is running."; return false; }
            if (connectionId == null || !connections.TryGetValue(connectionId, out var link)) { reason = "Connection not found."; return false; }
            if (!CanEdit(link.Source, actor) && (link.Target == null || !CanEdit(link.Target, actor)))
            { reason = "You may not change either endpoint of this connection."; return false; }
            busy = true;
            try
            {
                var a = link.Source; var b = link.Target;
                var ar = a != null ? CaptureRecord(a) : null; var br = b != null ? CaptureRecord(b) : null;
                var oldLink = link.Capture(); RemoveConnectionInternal(link);
                long before = Revision++;
                var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.Connect, ActorId = actor?.Id ?? "" };
                if (ar != null) change.Previous.Add(CloneRecord(ar)); if (br != null) change.Previous.Add(CloneRecord(br));
                if (a != null) { a.Version = Revision; UpdateRecordMetadata(ar, a, new Pose(a.transform.position, a.transform.rotation), a.transform.lossyScale); change.Upserts.Add(ar); }
                if (b != null) { b.Version = Revision; UpdateRecordMetadata(br, b, new Pose(b.transform.position, b.transform.rotation), b.transform.lossyScale); change.Upserts.Add(br); }
                change.PreviousConnections.Add(oldLink); change.RemovedConnections.Add(link.Id);
                busy = false; PublishChanged(change); return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { busy = false; FlushRequestedRemovals(); DrainPublications(); }
        }
        public void DisconnectPrimary(PlacedBuilding building)
        {
            if (IsBusy || building == null || building.System != this) return;
            for (int i = 0; i < building.links.Count; i++)
                if (building.links[i].Primary && building.links[i].Source == building)
                { TryDisconnect(building.links[i].Id, out _); return; }
        }
        private BuildingConnection AddConnection(PlacedBuilding source, BuildingSocket sourceSocket,
            BuildingSocket target, bool primary, string id)
        {
            if (source == null || target == null) throw new InvalidOperationException("A connection endpoint no longer exists.");
            target.Release(source);
            if (target.IsOccupied || sourceSocket != null && sourceSocket.IsOccupied) throw new InvalidOperationException("A connection endpoint is occupied.");
            var link = new BuildingConnection { Id = id ?? Guid.NewGuid().ToString("N"), System = this,
                Source = source, SourceSocket = sourceSocket, TargetSocket = target, Primary = primary };
            connections.Add(link.Id, link); source.links.Add(link);
            if (target.Building != null) target.Building.links.Add(link);
            target.links.Add(link); if (sourceSocket != null) sourceSocket.links.Add(link);
            if (primary) source.AttachedSocket = target;
            return link;
        }
        internal void RemoveConnectionInternal(BuildingConnection link)
        {
            if (link == null || !connections.Remove(link.Id)) return;
            if (link.Source != null)
            {
                link.Source.links.Remove(link);
                if (link.Primary && link.Source.AttachedSocket == link.TargetSocket) link.Source.AttachedSocket = null;
            }
            if (link.Target != null) link.Target.links.Remove(link);
            if (link.SourceSocket != null) link.SourceSocket.links.Remove(link);
            if (link.TargetSocket != null) link.TargetSocket.links.Remove(link);
        }
        internal void NotifySocketLost(BuildingConnection link)
        {
            if (link == null || !connections.ContainsKey(link.Id)) return;
            RemoveConnectionInternal(link);
            if (destroying) return;
            long before = Revision++;
            var change = new BuildingChangeSet { BeforeRevision = before, Revision = Revision, Action = BuildingAction.Connect | BuildingAction.State };
            change.RemovedConnections.Add(link.Id); PublishChanged(change);
        }
        private List<BuildingConnectionRecord> CaptureLinks(PlacedBuilding building)
        {
            var records = new List<BuildingConnectionRecord>(building.links.Count);
            foreach (var link in building.links) records.Add(link.Capture());
            return records;
        }
        internal static BuildingSocket GetSocket(PlacedBuilding building, string socketId)
        {
            if (building == null || string.IsNullOrEmpty(socketId)) return null;
            foreach (var socket in building.Sockets) if (socket != null && socket.Id == socketId) return socket;
            return null;
        }
        internal BuildingSocket FindSavedSocket(string ownerId, string socketId, Dictionary<string, PlacedBuilding> overrides = null)
        {
            if (string.IsNullOrEmpty(socketId)) return null;
            if (!string.IsNullOrEmpty(ownerId))
            {
                if (overrides != null && overrides.TryGetValue(ownerId, out var replacement)) return GetSocket(replacement, socketId);
                return buildings.TryGetValue(ownerId, out var building) ? GetSocket(building, socketId) : null;
            }
            BuildingSocket found = null;
            foreach (var socket in BuildingSocket.Active)
            {
                if (socket == null || socket.Building != null || socket.Id != socketId || !Accepts(socket)) continue;
                if (found != null) throw new InvalidOperationException("Scene socket IDs are ambiguous. Regenerate the duplicated ID.");
                found = socket;
            }
            return found;
        }
        private bool ValidateReplacementLinks(PreparedEdit entry, out string reason)
        {
            reason = null;
            var replacements = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            if (currentPlan != null) foreach (var prepared in currentPlan) if (prepared.Staged != null) replacements[prepared.Staged.InstanceId] = prepared.Staged;
            foreach (var link in entry.PreserveLinks)
            {
                if (currentPlan != null) foreach (var prepared in currentPlan)
                    if (prepared.Edit.Action == BuildingAction.Remove && (prepared.Edit.Existing.InstanceId == link.SourceId || prepared.Edit.Existing.InstanceId == link.TargetId))
                    { reason = "Detach a connection before removing its endpoint in an upgrade batch."; return false; }
                PlacedBuilding source = replacements.TryGetValue(link.SourceId, out var replacement) ? replacement : buildings[link.SourceId];
                var sourceSocket = GetSocket(source, link.SourceSocketId);
                var targetSocket = FindSavedSocket(link.TargetId, link.TargetSocketId, replacements);
                if (targetSocket == null || !string.IsNullOrEmpty(link.SourceSocketId) && sourceSocket == null)
                { reason = "The upgraded prefab must keep the IDs of connected sockets. Detach those connections first or copy the compatible socket layout."; return false; }
                if (sourceSocket != null ? !sourceSocket.IsCompatible(targetSocket) : source.Definition.SocketType != targetSocket.SocketType)
                { reason = "The upgraded socket layout is incompatible with an existing connection."; return false; }
                Vector3 sourcePoint = sourceSocket != null ? sourceSocket.transform.position : source.transform.position;
                if ((sourcePoint - targetSocket.transform.position).sqrMagnitude > 0.0004f)
                { reason = "The upgraded socket positions would break an existing connection. Use a compatible layout or detach first."; return false; }
            }
            return true;
        }
        private BuildingConnection RestoreConnection(BuildingConnectionRecord record)
        {
            if (!buildings.TryGetValue(record.SourceId, out var source)) throw new InvalidOperationException("Connection source is missing.");
            var sourceSocket = GetSocket(source, record.SourceSocketId);
            var target = FindSavedSocket(record.TargetId, record.TargetSocketId);
            return AddConnection(source, sourceSocket, target, record.Primary, record.Id);
        }
        private bool ValidateAdditionalLinks(List<PreparedEdit> plan, IReadOnlyList<BuildingConnectionRecord> links, out string reason)
        {
            reason = null; if (links == null || links.Count == 0) return true;
            var staged = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
            foreach (var entry in plan) if (entry.Staged != null) staged.Add(entry.Staged.InstanceId, entry.Staged);
            var ids = new HashSet<string>(StringComparer.Ordinal); var primaries = new HashSet<string>(StringComparer.Ordinal);
            var occupied = new HashSet<BuildingSocket>();
            foreach (var record in links)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Id) || !ids.Add(record.Id) || connections.ContainsKey(record.Id) ||
                    record.SourceId == null || record.TargetId == null || !staged.TryGetValue(record.SourceId, out var source) || !staged.TryGetValue(record.TargetId, out var target) || source == target)
                { reason = "Blueprint connections must have distinct IDs and endpoints inside the blueprint."; return false; }
                if (record.Primary && !primaries.Add(record.SourceId)) { reason = "A building may have only one primary attachment."; return false; }
                var a = GetSocket(source, record.SourceSocketId); var b = GetSocket(target, record.TargetSocketId);
                if (b == null || !string.IsNullOrEmpty(record.SourceSocketId) && a == null || a != null && !a.IsCompatible(b) || a == null && source.Definition.SocketType != b.SocketType)
                { reason = "Blueprint sockets are missing or incompatible."; return false; }
                if (!occupied.Add(b) || a != null && !occupied.Add(a)) { reason = "Blueprint connections share an occupied socket."; return false; }
                var position = a != null ? a.transform.position : source.transform.position;
                if ((position - b.transform.position).sqrMagnitude > 0.0004f) { reason = "Blueprint socket endpoints do not meet after placement."; return false; }
            }
            return true;
        }
    }
}
