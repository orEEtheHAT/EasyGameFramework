using System;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Work contribution only. Your movement/AI decides how the worker reaches the site.</summary>
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Construction/Building Worker")]
    public sealed class BuildingWorker : MonoBehaviour
    {
        public BuildingWorkQueue Queue;
        [Tooltip("Optional component implementing IBuildingActor.")] public MonoBehaviour Actor;
        [Min(0)] public float WorkPerSecond = 1;
        [Min(0)] public float WorkRange = 3;
        [Min(0.02f)] public float TickInterval = 0.25f;
        public string TargetId { get; private set; }
        private float elapsed;
        public void SetTarget(PlacedBuilding site)
        {
            if (site != null && (Queue == null || site.System != Queue.System)) throw new ArgumentException("Assign a target in the worker's queue world.");
            TargetId = site != null ? site.InstanceId : null;
        }
        private void Update()
        {
            elapsed += Time.deltaTime;
            if (elapsed < Mathf.Max(0.02f, TickInterval)) return;
            float delta = elapsed; elapsed = 0; Tick(delta);
        }
        public bool Tick(float seconds)
        {
            if (Queue == null || Queue.System == null || !BuildingMath.Finite(seconds) || seconds <= 0 || !BuildingMath.Finite(WorkPerSecond) || WorkPerSecond <= 0 ||
                !BuildingMath.Finite(WorkRange) || WorkRange < 0 || Actor != null && !(Actor is IBuildingActor)) return false;
            var actor = Actor as IBuildingActor;
            PlacedBuilding site = null;
            if (!string.IsNullOrEmpty(TargetId)) Queue.System.TryGet(TargetId, out site);
            if (site == null || site.IsComplete || site.Construction.Paused || !site.Construction.MaterialsReady)
            {
                TargetId = null;
                if (!Queue.TryFindWork(transform.position, WorkRange, actor, out site)) return false;
                TargetId = site.InstanceId;
            }
            if ((site.transform.position - transform.position).sqrMagnitude > WorkRange * WorkRange) return false;
            if (!Queue.System.TryWork(site, seconds * WorkPerSecond, out var completed, out _, actor)) return false;
            if (completed.IsComplete) { Queue.Remove(TargetId); TargetId = null; }
            return true;
        }
    }
}
