using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [Serializable]
    public sealed class BuildingState
    {
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        public bool ReplicationOnly;
        public long Revision;
        public List<BuildingRecord> Buildings = new List<BuildingRecord>();
        public List<BuildingConnectionRecord> Connections = new List<BuildingConnectionRecord>();
        public List<string> RedeemedParcels = new List<string>();
    }
    [Serializable]
    public sealed class BuildingRecord
    {
        public string InstanceId;
        public string DefinitionId;
        public int DefinitionVersion = 1;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale = Vector3.one;
        public string SpaceId;
        public Vector3 LocalPosition;
        public Quaternion LocalRotation = Quaternion.identity;
        public string OwnerId;
        public string TeamId;
        public long Version;
        public BuildingCost[] PaidCosts = Array.Empty<BuildingCost>();
        public BuildingPartState[] Parts = Array.Empty<BuildingPartState>();
        public BuildingConstructionState Construction;
        public bool HasConstruction;
        public string SocketOwnerId;
        public string SocketId;
    }
    [Serializable]
    public sealed class BuildingConstructionState
    {
        public float Work;
        public float TotalWork;
        public bool Paused;
        public BuildingCost[] Required = Array.Empty<BuildingCost>();
        public BuildingCost[] Delivered = Array.Empty<BuildingCost>();
        public BuildingPartState[] CompletionParts = Array.Empty<BuildingPartState>();
        public float Progress => TotalWork > 0 ? Mathf.Clamp01(Work / TotalWork) : 1;
        public bool MaterialsReady
        {
            get
            {
                if (Required == null) return true;
                foreach (var cost in Required) if (Remaining(cost.ResourceId) > 0) return false;
                return true;
            }
        }
        public long Remaining(string resourceId)
        {
            long required = 0, delivered = 0;
            if (Required != null) foreach (var cost in Required) if (cost.ResourceId == resourceId) required = checked(required + cost.Amount);
            if (Delivered != null) foreach (var cost in Delivered) if (cost.ResourceId == resourceId) delivered = checked(delivered + cost.Amount);
            return Math.Max(0, required - delivered);
        }
    }
    [Serializable]
    public sealed class BuildingConnectionRecord
    {
        public string Id;
        public string SourceId;
        public string SourceSocketId;
        public string TargetId;
        public string TargetSocketId;
        public bool Primary;
    }
    [Serializable]
    public sealed class BuildingChangeSet
    {
        public long BeforeRevision;
        public long Revision;
        public BuildingAction Action;
        public string ActorId;
        public List<BuildingRecord> Upserts = new List<BuildingRecord>();
        public List<string> Removed = new List<string>();
        public List<BuildingConnectionRecord> Connections = new List<BuildingConnectionRecord>();
        public BuildingPaymentReceipt Payment = new BuildingPaymentReceipt();
        public List<string> RemovedConnections = new List<string>();
        // Local undo data. Replication strips these and private component state.
        public List<BuildingRecord> Previous = new List<BuildingRecord>();
        public List<BuildingConnectionRecord> PreviousConnections = new List<BuildingConnectionRecord>();
    }
}
