using System;
using UnityEngine;

namespace EasyBuilding
{
    public enum PlacementMode { Free, Grid, Socket }
    public enum BuildingPayment { Free, Paid }

    [Serializable]
    public struct BuildingCost
    {
        public string ResourceId;
        [Min(1)] public long Amount;
        public BuildingCost(string resourceId, long amount) { ResourceId = resourceId; Amount = amount; }
    }

    /// <summary>Reusable authoring data. Occupancy, IDs of instances and payments live outside this asset.</summary>
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Building Definition", fileName = "New Building")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string stableId = Guid.NewGuid().ToString("N");
        public string Id
        {
            get
            {
#if UNITY_EDITOR
                // Newly created assets already have an ID before their first import callback/save.
                var path = UnityEditor.AssetDatabase.GetAssetPath(this);
                if (!string.IsNullOrEmpty(path)) return UnityEditor.AssetDatabase.AssetPathToGUID(path);
#endif
                return stableId;
            }
        }
        [Tooltip("A prefab with its pivot on the bottom and root scale (1, 1, 1). No building component is required.")]
        public GameObject Prefab;
        public PlacementMode Placement = PlacementMode.Free;
        [Min(0.01f)] public float GridSize = 1f;
        public Vector3 GridOrigin;
        public string SocketType = "Block";
        [Tooltip("Optional restriction, combined with Free, Grid or Socket placement.")]
        public bool RequireZone;
        [Tooltip("Match a Building Zone's Zone Id. Empty accepts any enabled zone.")]
        public string ZoneId = "";
        [Tooltip("Local box enclosing the building. Bottom should meet the prefab's pivot.")]
        public Vector3 BoundsCenter = new Vector3(0, 0.5f, 0);
        public Vector3 BoundsSize = Vector3.one;
        public bool CheckObstacles = true;
        public LayerMask ObstacleLayers = ~0;
        [Tooltip("Checks real support again at confirmation. Socket placement does not require ground.")]
        public bool RequireSurface = true;
        public LayerMask SurfaceLayers = ~0;
        [Range(0, 89)] public float MaximumSlope = 45f;
        public BuildingPayment Payment;
        public BuildingCost[] Costs = Array.Empty<BuildingCost>();
        [Tooltip("Optional presentation. The asset name is used when Display Name is empty.")]
        public string DisplayName;
        public Sprite Icon;
        public string Category = "Buildings";
        public string[] Tags = Array.Empty<string>();
        [Min(1)] public int SaveVersion = 1;

        public BuildingGridLayout GridLayout;
        public BuildingGridPlane GridPlane;
        [Tooltip("Optional compound footprint. Empty uses Bounds Center / Bounds Size.")]
        public BuildingVolume[] Volumes = Array.Empty<BuildingVolume>();
        [Tooltip("Optional occupied cells, centered at Cell * Cell Size plus Bounds Center. Use cells or volumes, not both.")]
        public Vector3Int[] Cells = Array.Empty<Vector3Int>();
        public Vector3 CellSize = Vector3.one;
        [Tooltip("Space that must remain clear, such as a door swing or service aisle.")]
        public BuildingVolume[] Clearances = Array.Empty<BuildingVolume>();
        public BuildingSurface Surfaces = BuildingSurface.Floor;
        public bool AlignToSurface;
        [Min(0.001f)] public float SurfaceProbeDistance = 0.1f;
        [Tooltip("Local contact points. Empty checks the root pivot. Every point must have support.")]
        public Vector3[] SupportPoints = Array.Empty<Vector3>();
        [Tooltip("Optional source socket on this prefab. Empty uses the prefab root as the attachment point.")]
        public string AttachmentSocketId;
        [Range(0, 180)] public float SocketRotationStep;
        public BuildingRule[] Rules = Array.Empty<BuildingRule>();
        public BuildingAccess Access;
        [Range(0, 1)] public float DemolitionRefund;
        [Min(0)] public float WorkRequired;
        public GameObject ConstructionVisual;
        public BuildingConstructionPayment ConstructionPayment;
        public BuildingConstructionPhase[] ConstructionPhases = Array.Empty<BuildingConstructionPhase>();
        [Range(0, 1)] public float CancellationRefund = 1;
        public BuildingDefinition[] Upgrades = Array.Empty<BuildingDefinition>();
        public BuildingCostBasis CostBasis;
        public string Name => string.IsNullOrWhiteSpace(DisplayName) ? name : DisplayName;
        public int VolumeCount => Cells != null && Cells.Length != 0 ? Cells.Length : Volumes != null && Volumes.Length != 0 ? Volumes.Length : 1;
        public BuildingVolume GetVolume(int index) => Cells != null && Cells.Length != 0 ? new BuildingVolume(BoundsCenter + Vector3.Scale(Cells[index], CellSize), CellSize)
            : Volumes != null && Volumes.Length != 0 ? Volumes[index] : new BuildingVolume(BoundsCenter, BoundsSize);
        public bool HasTag(string tag)
        {
            if (Tags == null) return false;
            for (int i = 0; i < Tags.Length; i++) if (string.Equals(Tags[i], tag, StringComparison.Ordinal)) return true;
            return false;
        }
        public bool CanUpgradeTo(BuildingDefinition target)
        {
            if (Upgrades == null) return false;
            for (int i = 0; i < Upgrades.Length; i++) if (Upgrades[i] == target) return true;
            return false;
        }
        public static BuildingDefinition Create(string id, GameObject prefab)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A definition ID cannot be empty.", nameof(id));
            var definition = CreateInstance<BuildingDefinition>(); definition.stableId = id; definition.Prefab = prefab;
            return definition;
        }

        public string ConfigurationError
        {
            get
            {
                if (string.IsNullOrWhiteSpace(stableId)) return "Definition ID is missing. Save this Building Definition asset.";
                if (Prefab == null) return "Assign a prefab to Prefab.";
                if ((Prefab.transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                    return "Set the prefab root Scale to (1, 1, 1); resize its children instead.";
                if (!BuildingMath.Finite(BoundsCenter) || !BuildingMath.Finite(BoundsSize) ||
                    BoundsSize.x <= 0 || BoundsSize.y <= 0 || BoundsSize.z <= 0)
                    return "Set Bounds Size to positive finite values enclosing the prefab.";
                if (Placement < PlacementMode.Free || Placement > PlacementMode.Socket) return "Choose Free, Grid or Socket placement.";
                if (Payment < BuildingPayment.Free || Payment > BuildingPayment.Paid) return "Choose Free or Paid payment.";
                if (SaveVersion < 1) return "Set Save Version to at least 1.";
                if (!BuildingMath.Finite(SurfaceProbeDistance) || SurfaceProbeDistance <= 0) return "Set Surface Probe Distance to a positive finite value.";
                if (Surfaces == 0 || (Surfaces & ~BuildingSurface.Any) != 0) return "Choose at least one allowed surface.";
                if (GridLayout < BuildingGridLayout.Rectangular || GridLayout > BuildingGridLayout.Isometric ||
                    GridPlane < BuildingGridPlane.XZ || GridPlane > BuildingGridPlane.XYZ) return "Choose a supported grid layout and plane.";
                if (Volumes != null) for (int i = 0; i < Volumes.Length; i++) if (!Volumes[i].IsValid) return "Every footprint volume needs a finite center/rotation and a positive size.";
                if (Cells != null && Cells.Length != 0 && (Volumes != null && Volumes.Length != 0 || !BuildingMath.Finite(CellSize) || CellSize.x <= 0 || CellSize.y <= 0 || CellSize.z <= 0))
                    return "Use either Cells or Volumes; Cell Size must be positive and finite.";
                if (Clearances != null) for (int i = 0; i < Clearances.Length; i++) if (!Clearances[i].IsValid) return "Every clearance volume needs a finite center/rotation and a positive size.";
                if (SupportPoints != null) for (int i = 0; i < SupportPoints.Length; i++) if (!BuildingMath.Finite(SupportPoints[i])) return "Support points must be finite.";
                if (!BuildingMath.Finite(WorkRequired) || WorkRequired < 0 || !BuildingMath.Finite(DemolitionRefund) || DemolitionRefund < 0 || DemolitionRefund > 1) return "Work Required must be nonnegative; Demolition Refund must be between 0 and 1.";
                if (!BuildingMath.Finite(CancellationRefund) || CancellationRefund < 0 || CancellationRefund > 1 || ConstructionPayment < BuildingConstructionPayment.Upfront || ConstructionPayment > BuildingConstructionPayment.Delivery)
                    return "Choose a construction payment mode and a cancellation refund between 0 and 1.";
                if (ConstructionPhases != null) for (int i = 0; i < ConstructionPhases.Length; i++)
                    if (!BuildingMath.Finite(ConstructionPhases[i].FromProgress) || ConstructionPhases[i].FromProgress < 0 || ConstructionPhases[i].FromProgress > 1)
                        return "Construction phase progress must be between 0 and 1.";
                if (Rules != null) for (int i = 0; i < Rules.Length; i++) if (Rules[i] == null) return "Assign each rule or remove the empty Rules entry.";
                if (!BuildingMath.Finite(MaximumSlope) || MaximumSlope < 0 || MaximumSlope > 89) return "Set Maximum Slope between 0 and 89 degrees.";
                if (Placement == PlacementMode.Grid && (!BuildingMath.Finite(GridSize) || GridSize < 0.01f || !BuildingMath.Finite(GridOrigin)))
                    return "Set Grid Size to at least 0.01 and use a finite Grid Origin.";
                if (Placement == PlacementMode.Socket && string.IsNullOrWhiteSpace(SocketType)) return "Enter a Socket Type matching a Building Socket.";
                if (Payment == BuildingPayment.Paid)
                {
                    if (Costs == null || Costs.Length == 0) return "Add at least one positive cost, or choose Free payment.";
                    for (int i = 0; i < Costs.Length; i++)
                        if (string.IsNullOrWhiteSpace(Costs[i].ResourceId) || Costs[i].Amount <= 0)
                            return "Each cost needs a Resource Id and an Amount greater than zero.";
                }
                return null;
            }
        }

    }

    internal static class BuildingMath
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        internal static bool ValidRotation(Quaternion q)
        {
            float lengthSquared = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            return Finite(lengthSquared) && lengthSquared > 0.00001f;
        }
        internal static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
