using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyBuilding.TwoD
{
    [DisallowMultipleComponent, RequireComponent(typeof(BuildingSystem)), AddComponentMenu("EasyGameFramework/Building/2D/Building Physics 2D")]
    public sealed class BuildingPhysics2D : MonoBehaviour, IBuildingPhysics, IBuildingRule
    {
        public float PlaneZ;
        [Min(0), Tooltip("Inset on each edge of the overlap query. The default compensates Box2D polygon contact skin so touching a floor is allowed.")]
        public float OverlapInset = 0.025f;
        private BuildingSystem world;
        private Collider2D[] overlaps = new Collider2D[32];
        private RaycastHit2D[] hits = new RaycastHit2D[16];
        public bool Is2D => true;
        private void OnEnable() { world = GetComponent<BuildingSystem>(); world.PhysicsBackend = this; world.AddRule(this); }
        private void OnDisable()
        {
            if (world == null || world.IsBusy) return;
            world.RemoveRule(this); if (ReferenceEquals(world.PhysicsBackend, this)) world.PhysicsBackend = null;
        }
        public void SyncTransforms() => Physics2D.SyncTransforms();
        public bool Allows(in BuildingContext context, out string reason)
        {
            reason = null;
            if (context.Action == BuildingAction.Remove || context.Action == BuildingAction.Pack && context.Existing != null) return true;
            if (float.IsNaN(PlaneZ) || float.IsInfinity(PlaneZ) || Mathf.Abs(context.Pose.position.z - PlaneZ) > 0.001f ||
                Vector3.Dot(context.Pose.rotation * Vector3.forward, Vector3.forward) < 0.99999f)
            { reason = "2D buildings must stay on Plane Z and rotate around the Z axis."; return false; }
            if (context.Definition.Placement == PlacementMode.Grid && context.Definition.GridPlane != BuildingGridPlane.XY)
            { reason = "Choose XY as the grid plane for a 2D building."; return false; }
            if (context.Request.Space != null && context.Request.Space.gameObject.scene != gameObject.scene)
            { reason = "A 2D Building Space must belong to the same scene as its Building System."; return false; }
            return true;
        }
        public bool HasObstacle(in BuildingBox box, int layers, in BuildingCollisionFilter filter)
        {
            if (Vector3.Dot(box.Rotation * Vector3.forward, Vector3.forward) < 0.99999f) throw new InvalidOperationException("2D footprint volumes must rotate only around Z.");
            var contact = new ContactFilter2D { useLayerMask = true, layerMask = layers, useTriggers = false };
            if (float.IsNaN(OverlapInset) || float.IsInfinity(OverlapInset) || OverlapInset < 0) throw new InvalidOperationException("Set Overlap Inset to a finite nonnegative value.");
            var size = new Vector2(Mathf.Max(0.0001f, (box.HalfSize.x - OverlapInset) * 2), Mathf.Max(0.0001f, (box.HalfSize.y - OverlapInset) * 2));
            int count; var scene = gameObject.scene.GetPhysicsScene2D();
            while (true)
            {
                count = scene.OverlapBox(box.Center, size, box.Rotation.eulerAngles.z, contact, overlaps);
                if (count < overlaps.Length) break; Array.Resize(ref overlaps, checked(overlaps.Length * 2));
            }
            for (int i = 0; i < count; i++) if (overlaps[i] != null && !filter.Ignores(overlaps[i].transform)) return true;
            return false;
        }
        public bool TryGetSurface(Vector3 origin, Vector3 direction, float distance, int layers, in BuildingCollisionFilter filter, out BuildingSurfaceHit hit)
        {
            hit = default;
            var ray = new Vector2(direction.x, direction.y); if (ray.sqrMagnitude < 0.000001f) return false;
            var contact = new ContactFilter2D { useLayerMask = true, layerMask = layers, useTriggers = false };
            int count; var scene = gameObject.scene.GetPhysicsScene2D();
            while (true)
            {
                count = scene.Raycast(origin, ray.normalized, distance, contact, hits);
                if (count < hits.Length) break; Array.Resize(ref hits, checked(hits.Length * 2));
            }
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null || candidate.fraction == 0 || candidate.distance >= nearest || filter.Ignores(candidate.transform)) continue;
                nearest = candidate.distance;
                hit = new BuildingSurfaceHit(new Vector3(candidate.point.x, candidate.point.y, PlaneZ), candidate.normal, candidate.collider.GetComponentInParent<PlacedBuilding>());
            }
            return !float.IsPositiveInfinity(nearest);
        }
    }
}
