using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public readonly struct BuildingUtilityStep
    {
        public readonly string ConnectionId, FromId, ToId;
        public readonly float Capacity;
        internal BuildingUtilityStep(BuildingConnection link, PlacedBuilding from, PlacedBuilding to, float capacity)
        { ConnectionId = link.Id; FromId = from.InstanceId; ToId = to.InstanceId; Capacity = capacity; }
    }
    /// <summary>Directed connectivity and shortest-hop paths. Energy/fluid production and consumption belong to your simulation.</summary>
    public sealed class BuildingUtilityNetwork
    {
        private readonly BuildingSystem system;
        private readonly List<PlacedBuilding> queue = new List<PlacedBuilding>();
        private readonly Dictionary<PlacedBuilding, BuildingUtilityStep> previous = new Dictionary<PlacedBuilding, BuildingUtilityStep>();
        private readonly Dictionary<string, PlacedBuilding> visited = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
        public BuildingUtilityNetwork(BuildingSystem system) => this.system = system != null ? system : throw new ArgumentNullException(nameof(system));
        public bool TryFindPath(PlacedBuilding start, PlacedBuilding end, string channel, List<BuildingUtilityStep> output, float minimumCapacity = 0)
        {
            if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear();
            if (!Valid(start, channel, minimumCapacity) || end == null || end.System != system || !end.IsComplete) return false;
            Search(start, end, channel, minimumCapacity);
            if (!visited.ContainsKey(end.InstanceId)) return false;
            var cursor = end;
            while (cursor != start) { var step = previous[cursor]; output.Add(step); cursor = visited[step.FromId]; }
            output.Reverse(); return true;
        }
        public int GetReachable(PlacedBuilding start, string channel, List<PlacedBuilding> output, float minimumCapacity = 0)
        {
            if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear();
            if (!Valid(start, channel, minimumCapacity)) return 0;
            Search(start, null, channel, minimumCapacity); output.AddRange(queue); return output.Count;
        }
        private bool Valid(PlacedBuilding start, string channel, float capacity)
            => start != null && start.System == system && start.IsComplete && !string.IsNullOrWhiteSpace(channel) && BuildingMath.Finite(capacity) && capacity >= 0;
        private void Search(PlacedBuilding start, PlacedBuilding end, string channel, float minimumCapacity)
        {
            queue.Clear(); previous.Clear(); visited.Clear(); queue.Add(start); visited.Add(start.InstanceId, start);
            for (int i = 0; i < queue.Count; i++)
            {
                var current = queue[i]; if (current == end) break;
                foreach (var link in current.links)
                {
                    var next = link.Other(current);
                    if (next == null || !next.IsComplete || visited.ContainsKey(next.InstanceId)) continue;
                    var fromSocket = link.Source == current ? link.SourceSocket : link.TargetSocket;
                    var toSocket = link.Source == current ? link.TargetSocket : link.SourceSocket;
                    if (fromSocket == null || toSocket == null || fromSocket.SocketType != channel || toSocket.SocketType != channel) continue;
                    var from = fromSocket.GetComponent<BuildingUtilityPort>(); var to = toSocket.GetComponent<BuildingUtilityPort>();
                    if (from == null || to == null || !from.Sends || !to.Receives) continue;
                    float capacity = Mathf.Min(from.Capacity, to.Capacity); if (capacity < minimumCapacity) continue;
                    previous.Add(next, new BuildingUtilityStep(link, current, next, capacity)); visited.Add(next.InstanceId, next); queue.Add(next);
                }
            }
        }
    }
}
