using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Deterministic discrete support tree. Each member transfers its full load to its nearest foundation through structural sockets.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Structure/Building Structure")]
    public sealed class BuildingStructure : MonoBehaviour
    {
        public BuildingSystem System;
        public bool AutoEvaluate = true;
        public bool RemoveUnsupported;
        [Min(0)] public float CollapseDelay = 1;
        [Min(1)] public int CollapseBudget = 16;
        private sealed class Node
        {
            internal PlacedBuilding Building;
            internal BuildingStructuralMember Member;
            internal Node Parent;
            internal int Depth;
            internal float Load;
            internal BuildingSupportStatus Status;
        }
        private readonly Dictionary<PlacedBuilding, Node> nodes = new Dictionary<PlacedBuilding, Node>();
        private readonly List<PlacedBuilding> buildings = new List<PlacedBuilding>();
        private readonly List<Node> order = new List<Node>(), queue = new List<Node>();
        private readonly Stack<Node> pool = new Stack<Node>();
        private readonly HashSet<string> liveIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> deadlines = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<string> cleanup = new List<string>();
        private BuildingSystem subscribed;
        private bool dirty = true;
        public event Action Evaluated;
        private void OnEnable() { Subscribe(); dirty = true; }
        private void OnDisable() { if (subscribed != null) { subscribed.Changed -= Changed; subscribed.Restored -= MarkDirty; } subscribed = null; }
        private void Subscribe()
        {
            if (subscribed == System) return;
            if (subscribed != null) { subscribed.Changed -= Changed; subscribed.Restored -= MarkDirty; }
            subscribed = System; if (subscribed != null) { subscribed.Changed += Changed; subscribed.Restored += MarkDirty; }
        }
        private void Changed(BuildingChangeSet change) => dirty = true;
        public void MarkDirty() => dirty = true;
        private void LateUpdate()
        {
            Subscribe();
            if (System == null || System.IsBusy) return;
            if (AutoEvaluate && dirty) Evaluate();
            if (Application.isPlaying && RemoveUnsupported && !System.IsReadOnly) ProcessCollapses(Time.time, CollapseBudget);
        }
        public bool TryGetSupport(PlacedBuilding building, out BuildingSupport support)
        {
            support = default;
            if (building == null || !nodes.TryGetValue(building, out var node)) return false;
            support = new BuildingSupport(node.Status, node.Load, node.Parent != null ? node.Parent.Building.InstanceId : null); return true;
        }
        public void Evaluate()
        {
            if (System == null || System.IsBusy) return;
            Subscribe(); System.GetBuildings(buildings); nodes.Clear();
            foreach (var old in order) { old.Building = null; old.Member = null; old.Parent = null; pool.Push(old); }
            order.Clear(); queue.Clear(); liveIds.Clear();
            buildings.Sort((a, b) => string.CompareOrdinal(a.InstanceId, b.InstanceId));
            foreach (var building in buildings)
            {
                if (!building.IsComplete) continue;
                var member = building.GetComponent<BuildingStructuralMember>(); if (member == null || !member.enabled) continue;
                var node = pool.Count != 0 ? pool.Pop() : new Node();
                node.Building = building; node.Member = member; node.Depth = -1; node.Load = member.Mass + member.ExtraLoad; node.Status = BuildingSupportStatus.Unsupported;
                if (!BuildingMath.Finite(node.Load) || node.Load < 0 || !BuildingMath.Finite(member.Capacity) || member.Capacity < 0)
                    node.Status = BuildingSupportStatus.InvalidConfiguration;
                nodes.Add(building, node); order.Add(node); liveIds.Add(building.InstanceId);
                bool anchored = member.Foundation;
                foreach (var link in building.links) if (link.Source == building && link.Target == null && Transfers(link)) anchored = true;
                if (anchored && node.Status != BuildingSupportStatus.InvalidConfiguration) { node.Depth = 0; queue.Add(node); }
            }
            for (int i = 0; i < queue.Count; i++)
            {
                var current = queue[i];
                foreach (var link in current.Building.links)
                {
                    var other = link.Other(current.Building);
                    if (other == null || !Transfers(link) || !nodes.TryGetValue(other, out var child) || child.Status == BuildingSupportStatus.InvalidConfiguration) continue;
                    if (child.Depth < 0) { child.Depth = current.Depth + 1; child.Parent = current; queue.Add(child); }
                    else if (child.Depth == current.Depth + 1 && string.CompareOrdinal(current.Building.InstanceId, child.Parent.Building.InstanceId) < 0) child.Parent = current;
                }
            }
            for (int i = queue.Count - 1; i >= 0; i--) if (queue[i].Parent != null) queue[i].Parent.Load += queue[i].Load;
            foreach (var node in queue)
                node.Status = node.Parent != null && node.Parent.Status != BuildingSupportStatus.Supported ? BuildingSupportStatus.Unsupported :
                    !BuildingMath.Finite(node.Load) || node.Load > node.Member.Capacity ? BuildingSupportStatus.Overloaded : BuildingSupportStatus.Supported;
            cleanup.Clear(); foreach (var id in deadlines.Keys) if (!liveIds.Contains(id)) cleanup.Add(id);
            foreach (var node in order)
            {
                string id = node.Building.InstanceId;
                if (node.Status == BuildingSupportStatus.Supported || node.Status == BuildingSupportStatus.InvalidConfiguration) deadlines.Remove(id);
                else if (!deadlines.ContainsKey(id)) deadlines.Add(id, Time.time + Mathf.Max(0, CollapseDelay));
            }
            foreach (var id in cleanup) deadlines.Remove(id);
            dirty = false;
            if (Evaluated != null) foreach (Action handler in Evaluated.GetInvocationList()) try { handler(); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        public int ProcessCollapses(float now, int budget)
        {
            // Partial region loads may temporarily hide a foundation. Wait until their links resolve.
            if (System == null || System.IsReadOnly || System.IsBusy || System.PendingConnectionCount != 0 || !BuildingMath.Finite(now) || budget <= 0) return 0;
            if (dirty) Evaluate();
            cleanup.Clear(); foreach (var pair in deadlines) if (pair.Value <= now) { cleanup.Add(pair.Key); if (cleanup.Count >= budget) break; }
            int removed = 0;
            foreach (var id in cleanup)
                if (System.TryGet(id, out var building) && System.Remove(building)) { deadlines.Remove(id); removed++; }
            return removed;
        }
        private static bool Transfers(BuildingConnection link) => link.TargetSocket != null && link.TargetSocket.Structural && (link.SourceSocket == null || link.SourceSocket.Structural);
    }
}
