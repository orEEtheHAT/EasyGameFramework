using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Ordered work list, independent of navigation. Entries use stable IDs so completion/reloading is safe.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Construction/Work Queue")]
    public sealed class BuildingWorkQueue : MonoBehaviour
    {
        public BuildingSystem System;
        [Min(1)] public int SearchBudget = 64;
        private readonly List<string> order = new List<string>();
        private readonly HashSet<string> membership = new HashSet<string>(StringComparer.Ordinal);
        private int cursor;
        public int Count => order.Count;
        public void Enqueue(PlacedBuilding site)
        {
            if (site == null || site.Construction == null || System == null || site.System != System) throw new ArgumentException("Choose a site in this queue's Building System.");
            if (membership.Add(site.InstanceId)) order.Add(site.InstanceId);
        }
        public bool Remove(string instanceId)
        { if (instanceId == null || !membership.Remove(instanceId)) return false; order.Remove(instanceId); if (cursor >= order.Count) cursor = 0; return true; }
        public void Clear() { order.Clear(); membership.Clear(); cursor = 0; }
        public bool TryFindWork(Vector3 workerPosition, float range, IBuildingActor actor, out PlacedBuilding site)
        {
            site = null;
            if (System == null || range < 0 || !BuildingMath.Finite(range) || !BuildingMath.Finite(workerPosition)) return false;
            int budget = Mathf.Min(order.Count, Mathf.Max(1, SearchBudget));
            for (int i = 0; i < budget && order.Count != 0; i++)
            {
                if (cursor >= order.Count) cursor = 0;
                string id = order[cursor];
                if (!System.TryGet(id, out var candidate))
                {
                    if (!System.IsUnloaded(id)) Remove(id); else cursor = (cursor + 1) % order.Count;
                    continue;
                }
                if (candidate.IsComplete) { Remove(id); continue; }
                cursor = (cursor + 1) % order.Count;
                if (candidate.Construction.Paused || !candidate.Construction.MaterialsReady || !System.CanEdit(candidate, actor) ||
                    (candidate.transform.position - workerPosition).sqrMagnitude > range * range) continue;
                site = candidate; return true;
            }
            return false;
        }
    }
}
