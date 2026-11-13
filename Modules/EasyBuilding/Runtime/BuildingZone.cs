using System.Collections.Generic;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Serialization;

namespace EasyBuilding
{
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Zone")]
    public sealed class BuildingZone : MonoBehaviour
    {
        public string ZoneId = "Plot";
        [FormerlySerializedAs("Center"), SerializeField] private Vector3 center = new Vector3(0, 2, 0);
        [FormerlySerializedAs("Size"), SerializeField] private Vector3 size = new Vector3(10, 4, 10);
        public Vector3 Center { get => center; set { center = value; UpdateQuery(); } }
        public Vector3 Size { get => size; set { size = value; UpdateQuery(); } }
        public bool ForbidBuilding;
        public BuildingAccess Access;
        public string OwnerId;
        public string TeamId;
        public string RequiredTag;
        public BuildingSpace Space => GetComponentInParent<BuildingSpace>();
        internal static readonly List<BuildingZone> Active = new List<BuildingZone>();
        private int registryIndex = -1;
        private BuildingQueryHandle query;
        private static readonly ConcurrentQueue<BuildingZone> DirtyQueries = new ConcurrentQueue<BuildingZone>();
        private void OnEnable()
        {
            if (registryIndex >= 0) return;
            registryIndex = Active.Count; Active.Add(this);
            query = new BuildingQueryHandle(this, Center, (Mathf.Abs(Size.x) + Mathf.Abs(Size.y) + Mathf.Abs(Size.z)) * 0.5f);
        }
        private void OnDisable()
        {
            if (registryIndex < 0) return;
            int last = Active.Count - 1;
            var moved = Active[last]; Active[registryIndex] = moved; moved.registryIndex = registryIndex;
            Active.RemoveAt(last); registryIndex = -1;
            query?.Dispose(); query = null;
        }
        private void OnValidate() => DirtyQueries.Enqueue(this);
        internal void UpdateQuery()
        {
            query?.Update(Center, (Mathf.Abs(Size.x) + Mathf.Abs(Size.y) + Mathf.Abs(Size.z)) * 0.5f);
        }
        internal static void SynchronizeQueries()
        { while (DirtyQueries.TryDequeue(out var zone)) if (zone != null) zone.UpdateQuery(); }

        public bool Contains(BuildingDefinition definition, Pose pose)
        {
            for (int i = 0; i < definition.VolumeCount; i++)
                if (!Contains(BuildingGeometry.Transform(definition.GetVolume(i), pose, Vector3.one))) return false;
            return true;
        }
        public bool Contains(in BuildingBox box)
        {
            if (!isActiveAndEnabled || !BuildingMath.Finite(Center) || !BuildingMath.Finite(Size) || Size.x <= 0 || Size.y <= 0 || Size.z <= 0) return false;
            var scale = transform.lossyScale;
            if (!BuildingMath.Finite(scale) || Mathf.Abs(scale.x) < 0.000001f || Mathf.Abs(scale.y) < 0.000001f || Mathf.Abs(scale.z) < 0.000001f) return false;
            var worldToLocal = transform.worldToLocalMatrix;
            var limit = Size * 0.5f + Vector3.one * 0.0001f;
            for (int i = 0; i < 8; i++)
            {
                var local = worldToLocal.MultiplyPoint3x4(box.Corner(i)) - Center;
                if (Mathf.Abs(local.x) > limit.x || Mathf.Abs(local.y) > limit.y || Mathf.Abs(local.z) > limit.z) return false;
            }
            return true;
        }
        public bool Allows(IBuildingActor actor)
        {
            if (Access == BuildingAccess.Everyone || actor == null || actor.IsAdministrator) return true;
            return Access == BuildingAccess.Owner ? actor.Id == OwnerId : !string.IsNullOrEmpty(TeamId) && actor.TeamId == TeamId;
        }
        private void OnDrawGizmos()
        {
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.8f, 1, 0.7f);
            Gizmos.DrawWireCube(Center, Size);
            Gizmos.matrix = previous;
        }
    }
}
