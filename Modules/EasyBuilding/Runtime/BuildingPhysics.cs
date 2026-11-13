using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyBuilding
{
    public readonly struct BuildingSurfaceHit
    {
        public readonly Vector3 Point, Normal;
        public readonly PlacedBuilding Building;
        public BuildingSurfaceHit(Vector3 point, Vector3 normal, PlacedBuilding building = null)
        { Point = point; Normal = normal; Building = building; }
    }

    public readonly struct BuildingCollisionFilter
    {
        private readonly HashSet<Transform> ignoredRoots;
        internal BuildingCollisionFilter(HashSet<Transform> ignoredRoots) => this.ignoredRoots = ignoredRoots;
        public bool HasIgnoredRoots => ignoredRoots != null && ignoredRoots.Count != 0;
        public bool Ignores(Transform candidate)
        {
            if (!HasIgnoredRoots) return false;
            while (candidate != null)
            {
                if (ignoredRoots.Contains(candidate)) return true;
                candidate = candidate.parent;
            }
            return false;
        }
    }

    /// <summary>Optional geometry backend. The built-in implementation uses the world's 3D PhysicsScene.</summary>
    public interface IBuildingPhysics
    {
        bool Is2D { get; }
        void SyncTransforms();
        bool HasObstacle(in BuildingBox box, int layers, in BuildingCollisionFilter filter);
        bool TryGetSurface(Vector3 origin, Vector3 direction, float distance, int layers,
            in BuildingCollisionFilter filter, out BuildingSurfaceHit hit);
    }

    internal sealed class BuildingPhysics3D : IBuildingPhysics
    {
        private readonly BuildingSystem system;
        private Collider[] overlaps = new Collider[32];
        private RaycastHit[] rayHits = new RaycastHit[16];
        internal BuildingPhysics3D(BuildingSystem system) => this.system = system;
        public bool Is2D => false;
        public void SyncTransforms() => Physics.SyncTransforms();
        public bool HasObstacle(in BuildingBox box, int layers, in BuildingCollisionFilter filter)
        {
            var half = box.HalfSize - Vector3.Min(box.HalfSize * 0.02f, Vector3.one * 0.01f);
            var scene = system.gameObject.scene.GetPhysicsScene();
            if (!filter.HasIgnoredRoots && scene == Physics.defaultPhysicsScene)
                return Physics.CheckBox(box.Center, half, box.Rotation, layers, QueryTriggerInteraction.Ignore);
            int count;
            while (true)
            {
                count = scene.OverlapBox(box.Center, half, overlaps, box.Rotation, layers, QueryTriggerInteraction.Ignore);
                if (count < overlaps.Length) break;
                Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            }
            for (int i = 0; i < count; i++)
                if (overlaps[i] != null && !filter.Ignores(overlaps[i].transform)) return true;
            return false;
        }
        public bool TryGetSurface(Vector3 origin, Vector3 direction, float distance, int layers,
            in BuildingCollisionFilter filter, out BuildingSurfaceHit hit)
        {
            hit = default;
            var scene = system.gameObject.scene.GetPhysicsScene();
            if (!filter.HasIgnoredRoots)
            {
                if (!scene.Raycast(origin, direction, out var result, distance, layers, QueryTriggerInteraction.Ignore)) return false;
                hit = new BuildingSurfaceHit(result.point, result.normal, result.collider.GetComponentInParent<PlacedBuilding>());
                return true;
            }
            int count;
            while (true)
            {
                count = scene.Raycast(origin, direction, rayHits, distance, layers, QueryTriggerInteraction.Ignore);
                if (count < rayHits.Length) break;
                Array.Resize(ref rayHits, checked(rayHits.Length * 2));
            }
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var candidate = rayHits[i];
                if (candidate.collider == null || candidate.distance >= nearest || filter.Ignores(candidate.collider.transform)) continue;
                nearest = candidate.distance;
                hit = new BuildingSurfaceHit(candidate.point, candidate.normal, candidate.collider.GetComponentInParent<PlacedBuilding>());
            }
            return !float.IsPositiveInfinity(nearest);
        }
    }
}
