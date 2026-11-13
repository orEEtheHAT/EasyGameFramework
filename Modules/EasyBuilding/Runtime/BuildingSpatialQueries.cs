using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyBuilding
{
    /// <summary>Runtime-only broadphase handle. All contacts are excluded; trigger queries can still discover it.</summary>
    internal sealed class BuildingQueryHandle : IDisposable
    {
        internal static readonly Dictionary<Collider, Component> Owners = new Dictionary<Collider, Component>();
        private GameObject root;
        private SphereCollider collider;
        internal BuildingQueryHandle(Component owner, Vector3 localCenter, float localRadius)
        {
            if (!Application.isPlaying) return;
            root = new GameObject("EasyBuilding query") { layer = 2, hideFlags = HideFlags.HideAndDontSave };
            root.transform.SetParent(owner.transform, false);
            collider = root.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.includeLayers = 0;
            collider.excludeLayers = ~0;
            collider.layerOverridePriority = int.MaxValue;
            Update(localCenter, localRadius);
            Owners.Add(collider, owner);
        }
        internal void Update(Vector3 localCenter, float localRadius)
        {
            if (collider == null) return;
            bool valid = BuildingMath.Finite(localCenter) && BuildingMath.Finite(localRadius) && localRadius > 0;
            collider.enabled = valid;
            if (!valid) return;
            collider.center = localCenter;
            collider.radius = Mathf.Max(0.001f, localRadius);
        }
        public void Dispose()
        {
            if (collider != null) { Owners.Remove(collider); collider.enabled = false; }
            if (root != null) { root.SetActive(false); BuildingMath.Destroy(root); }
            collider = null; root = null;
        }
    }

    internal sealed class BuildingSpatialQueries
    {
        private readonly BuildingSystem system;
        private Collider[] candidates = new Collider[64];
        private readonly HashSet<int> seen = new HashSet<int>();
        internal BuildingSpatialQueries(BuildingSystem system) => this.system = system;

        internal void Around<T>(Vector3 point, float radius, List<T> output) where T : Component
        {
            output.Clear(); seen.Clear();
            var scene = system.gameObject.scene.GetPhysicsScene();
            int count;
            while (true)
            {
                count = scene.OverlapSphere(point, Mathf.Max(0.001f, radius), candidates, 1 << 2, QueryTriggerInteraction.Collide);
                if (count < candidates.Length) break;
                Array.Resize(ref candidates, checked(candidates.Length * 2));
            }
            for (int i = 0; i < count; i++)
            {
                var collider = candidates[i];
                if (collider == null || !BuildingQueryHandle.Owners.TryGetValue(collider, out var owner) || !(owner is T candidate)) continue;
                if (candidate != null && seen.Add(candidate.GetInstanceID())) output.Add(candidate);
            }
        }
    }
}
