using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Placed Building")]
    public sealed class PlacedBuilding : MonoBehaviour
    {
        public string InstanceId { get; private set; }
        public BuildingDefinition Definition { get; private set; }
        public BuildingSystem System { get; private set; }
        public BuildingSpace Space { get; internal set; }
        public string OwnerId { get; internal set; }
        public string TeamId { get; internal set; }
        public long Version { get; internal set; }
        public BuildingCost[] PaidCosts { get; internal set; } = Array.Empty<BuildingCost>();
        public BuildingConstructionState Construction { get; internal set; }
        public bool IsComplete => Construction == null;
        public BuildingSocket AttachedSocket { get; internal set; }
        public IReadOnlyList<BuildingConnection> Connections => links;
        internal readonly List<BuildingConnection> links = new List<BuildingConnection>();
        internal BuildingSocket[] Sockets { get; private set; }
        internal BuildingVolume[] Volumes { get; private set; }
        internal BuildingVolume[] Clearances { get; private set; }
        private IBuildingStatePart[] stateParts = Array.Empty<IBuildingStatePart>();
        private BuildingQueryHandle query;
        private Bounds localBounds;
        private bool initialized;

        internal void Initialize(BuildingSystem system, BuildingDefinition definition, string id)
        {
            System = system; Definition = definition; InstanceId = id;
            Sockets = GetComponentsInChildren<BuildingSocket>(true);
            foreach (var socket in Sockets) socket.Building = this;
            Volumes = new BuildingVolume[definition.VolumeCount];
            for (int i = 0; i < Volumes.Length; i++) Volumes[i] = definition.GetVolume(i);
            Clearances = definition.Clearances != null ? (BuildingVolume[])definition.Clearances.Clone() : Array.Empty<BuildingVolume>();
            localBounds = BuildingGeometry.Transform(Volumes[0], new Pose(Vector3.zero, Quaternion.identity), Vector3.one).Bounds;
            for (int i = 1; i < Volumes.Length; i++) localBounds.Encapsulate(BuildingGeometry.Transform(Volumes[i], new Pose(Vector3.zero, Quaternion.identity), Vector3.one).Bounds);
            for (int i = 0; i < Clearances.Length; i++) localBounds.Encapsulate(BuildingGeometry.Transform(Clearances[i], new Pose(Vector3.zero, Quaternion.identity), Vector3.one).Bounds);
            var parts = new List<IBuildingStatePart>(); var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(behaviour is IBuildingStatePart part)) continue;
                if (string.IsNullOrWhiteSpace(part.StateKey) || !keys.Add(part.StateKey))
                    throw new InvalidOperationException("State adapter keys must be nonempty and unique within a building.");
                parts.Add(part);
            }
            stateParts = parts.ToArray();
            GetComponentsInChildren<Collider>(true); // Warm collider wrappers before hot queries.
            initialized = true;
            if (isActiveAndEnabled) OnEnable();
        }
        private void OnEnable()
        {
            if (!initialized || query != null) return;
            query = new BuildingQueryHandle(this, localBounds.center, localBounds.extents.x + localBounds.extents.y + localBounds.extents.z);
            if (System != null) System.NotifyVisibility(this, true);
        }
        private void OnDisable() { query?.Dispose(); query = null; if (System != null) System.NotifyVisibility(this, false); }
        public BuildingBox GetVolume(int index) => BuildingGeometry.Transform(Volumes[index], new Pose(transform.position, transform.rotation), transform.lossyScale);
        public Bounds WorldBounds
        {
            get
            {
                if (Volumes == null || Volumes.Length == 0) return new Bounds(transform.position, Vector3.zero);
                var bounds = GetVolume(0).Bounds;
                for (int i = 1; i < Volumes.Length; i++) bounds.Encapsulate(GetVolume(i).Bounds);
                return bounds;
            }
        }
        internal BuildingPartState[] CaptureParts(bool replicatedOnly = false)
        {
            var parts = new List<BuildingPartState>(stateParts.Length);
            foreach (var part in stateParts)
            {
                if (replicatedOnly && !part.ReplicateState) continue;
                parts.Add(new BuildingPartState { Key = part.StateKey, Version = part.StateVersion, Json = part.CaptureBuildingState(), Replicate = part.ReplicateState });
            }
            return parts.ToArray();
        }
        internal bool OwnsStatePart(IBuildingStatePart target)
        { foreach (var part in stateParts) if (ReferenceEquals(part, target)) return true; return false; }
        internal void RestoreParts(BuildingPartState[] states, bool ignoreMissing = false)
        {
            if (states == null) return;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in states)
            {
                if (string.IsNullOrWhiteSpace(state.Key) || !keys.Add(state.Key)) throw new InvalidOperationException("Saved component keys must be unique.");
                IBuildingStatePart target = null;
                foreach (var part in stateParts) if (part.StateKey == state.Key) { target = part; break; }
                if (target == null)
                {
                    if (ignoreMissing) continue;
                    throw new InvalidOperationException("Missing building state adapter: " + state.Key);
                }
                if (!target.CanRestoreBuildingState(state.Version, state.Json, out var reason)) throw new InvalidOperationException(reason ?? "Component state version is unsupported.");
            }
            foreach (var state in states)
                foreach (var part in stateParts)
                    if (part.StateKey == state.Key) { part.RestoreBuildingState(state.Version, state.Json); break; }
        }
        internal void RestoreUpgradeParts(BuildingPartState[] states, BuildingDefinition previousDefinition)
        {
            if (states == null) return;
            var converted = (BuildingPartState[])states.Clone();
            for (int i = 0; i < converted.Length; i++)
                foreach (var part in stateParts)
                    if (part.StateKey == states[i].Key && part is IBuildingUpgradeStatePart upgrade)
                    {
                        if (!upgrade.TryPrepareUpgradeState(previousDefinition, states[i], out converted[i], out var reason) || converted[i].Key != states[i].Key)
                            throw new InvalidOperationException(reason ?? "The target prefab cannot preserve this component state during upgrade.");
                        break;
                    }
            RestoreParts(converted);
        }
        public void Detach() { if (System != null) System.DisconnectPrimary(this); else AttachedSocket = null; }
        internal void ReleaseLinks()
        {
            while (links.Count != 0) links[links.Count - 1].System.RemoveConnectionInternal(links[links.Count - 1]);
            AttachedSocket = null;
        }
        internal void ForgetSystem() => System = null;
        private void OnDestroy()
        {
            query?.Dispose(); query = null;
            if (System != null) System.NotifyDestroyed(this);
            else ReleaseLinks();
        }
        public bool Remove() => System != null && System.Remove(this);
    }
}
