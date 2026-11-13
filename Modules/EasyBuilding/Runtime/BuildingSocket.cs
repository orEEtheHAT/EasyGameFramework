using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Socket")]
    public sealed class BuildingSocket : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable local ID within a prefab, or unique ID for a socket in the scene.")]
        private string socketId = Guid.NewGuid().ToString("N");
        public string SocketType = "Block";
        [Min(0.001f)] public float Size = 1;
        public SocketRole Role;
        [Tooltip("Oppose the source socket's forward direction when mating two authored sockets.")]
        public bool FaceToFace;
        [Tooltip("Connections through this socket may transfer structural support.")]
        public bool Structural = true;
        public BuildingSpace Space => Building != null ? Building.Space : GetComponentInParent<BuildingSpace>();
        public string Id => socketId;
        private PlacedBuilding reservedBy;
        internal readonly List<BuildingConnection> links = new List<BuildingConnection>();
        public PlacedBuilding Occupant => reservedBy != null ? reservedBy : links.Count != 0 ? links[0].Other(Building) : null;
        public PlacedBuilding Building { get; internal set; }
        public bool IsOccupied => reservedBy != null || links.Count != 0;
        internal static readonly List<BuildingSocket> Active = new List<BuildingSocket>();
        private int registryIndex = -1;
        internal Transform Anchor { get; private set; }
        internal GameObject SceneObject { get; private set; }
        private BuildingQueryHandle query;

        private void OnEnable()
        {
            if (registryIndex >= 0) return;
            Anchor = transform;
            SceneObject = gameObject;
            registryIndex = Active.Count; Active.Add(this);
            query = new BuildingQueryHandle(this, Vector3.zero, 0.01f);
        }
        private void OnDisable() => Unregister();
        private void Unregister()
        {
            if (registryIndex < 0) return;
            int last = Active.Count - 1;
            var moved = Active[last]; Active[registryIndex] = moved; moved.registryIndex = registryIndex;
            Active.RemoveAt(last); registryIndex = -1;
            query?.Dispose(); query = null;
        }
        private void OnDestroy()
        {
            Unregister();
            while (links.Count != 0) links[links.Count - 1].System.NotifySocketLost(links[links.Count - 1]);
            reservedBy = null;
        }

        internal bool TryClaim(PlacedBuilding building)
        {
            if (IsOccupied && Occupant != building) return false;
            reservedBy = building;
            return true;
        }
        internal void Release(PlacedBuilding building) { if (reservedBy == building) reservedBy = null; }
        internal void CopyConfiguration(BuildingSocket source)
        { socketId = source.socketId; SocketType = source.SocketType; Size = source.Size; Role = source.Role; FaceToFace = source.FaceToFace; Structural = source.Structural; }

        public bool IsCompatible(BuildingSocket other)
        {
            if (other == null || other == this || !string.Equals(SocketType, other.SocketType, StringComparison.Ordinal)) return false;
            if (!BuildingMath.Finite(Size) || !BuildingMath.Finite(other.Size) || Size <= 0 || other.Size <= 0 || Mathf.Abs(Size - other.Size) > 0.001f) return false;
            return Role == SocketRole.Any || other.Role == SocketRole.Any || Role != other.Role;
        }

        /// <summary>Regenerate only when authoring a duplicated socket, never while a save uses its ID.</summary>
        [ContextMenu("Regenerate Socket ID")]
        public void RegenerateId() => socketId = Guid.NewGuid().ToString("N");

        private void OnDrawGizmos()
        {
            Gizmos.color = IsOccupied ? Color.red : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.15f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.5f);
        }
    }
    public enum SocketRole { Any, Plug, Socket }
}
