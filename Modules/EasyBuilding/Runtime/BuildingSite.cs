using System;
using UnityEngine;

namespace EasyBuilding
{
    public enum BuildingConstructionPayment { Upfront, Delivery }
    [Serializable]
    public struct BuildingConstructionPhase
    {
        public string Name;
        [Range(0, 1)] public float FromProgress;
        public GameObject Visual;
    }
    /// <summary>Presentation for a construction placeholder. Finished prefab behaviours do not exist until completion.</summary>
    [DisallowMultipleComponent, AddComponentMenu("")]
    public sealed class BuildingSite : MonoBehaviour
    {
        public int PhaseIndex { get; private set; } = -1;
        private GameObject visual, displayedPrefab;
        private void OnEnable() => Refresh();
        internal void Refresh()
        {
            var building = GetComponent<PlacedBuilding>();
            if (building == null || building.Construction == null) return;
            var definition = building.Definition;
            GameObject prefab = definition.ConstructionVisual; float highest = -1; PhaseIndex = -1;
            if (definition.ConstructionPhases != null) for (int i = 0; i < definition.ConstructionPhases.Length; i++)
            {
                var phase = definition.ConstructionPhases[i];
                if (phase.FromProgress <= building.Construction.Progress && phase.FromProgress >= highest)
                { highest = phase.FromProgress; prefab = phase.Visual; PhaseIndex = i; }
            }
            if (displayedPrefab == prefab) return;
            if (visual != null) { visual.SetActive(false); BuildingMath.Destroy(visual); }
            displayedPrefab = prefab;
            visual = prefab != null ? Instantiate(prefab, transform, false) : null;
        }
        private void OnDrawGizmos()
        {
            var building = GetComponent<PlacedBuilding>();
            if (building == null || building.Definition == null) return;
            Gizmos.color = Color.yellow; Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < building.Definition.VolumeCount; i++)
            { var volume = building.Definition.GetVolume(i); Gizmos.DrawWireCube(volume.Center, volume.Size); }
        }
    }
}
