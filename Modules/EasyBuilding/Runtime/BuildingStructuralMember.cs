using System;
using UnityEngine;

namespace EasyBuilding
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Structure/Structural Member")]
    public sealed class BuildingStructuralMember : MonoBehaviour, IBuildingStatePart
    {
        [Min(0)] public float Mass = 1;
        [Min(0)] public float Capacity = 100;
        [Tooltip("A fixed support, such as a foundation. Scene sockets marked Structural also provide support.")]
        public bool Foundation;
        public float ExtraLoad { get; private set; }
        public string StateKey => "structure.load";
        public int StateVersion => 1;
        public bool ReplicateState => true;
        [Serializable] private struct Data { public float load; }
        public bool SetExtraLoad(float load, out string reason)
        {
            reason = null; var building = GetComponent<PlacedBuilding>();
            if (!BuildingMath.Finite(load) || load < 0 || building == null || building.System == null || building.System.IsReadOnly || building.System.IsBusy)
            { reason = "Set a finite nonnegative load on an idle authoritative building."; return false; }
            if (load == ExtraLoad) return true;
            float previous = ExtraLoad; ExtraLoad = load;
            if (building.System.NotifyStateChanged(building, out reason)) return true;
            ExtraLoad = previous; return false;
        }
        public string CaptureBuildingState() => JsonUtility.ToJson(new Data { load = ExtraLoad });
        public bool CanRestoreBuildingState(int version, string json, out string reason)
        {
            reason = null;
            try { var data = JsonUtility.FromJson<Data>(json); if (version == 1 && BuildingMath.Finite(data.load) && data.load >= 0) return true; }
            catch (Exception) { }
            reason = "Invalid structural load state."; return false;
        }
        public void RestoreBuildingState(int version, string json) => ExtraLoad = JsonUtility.FromJson<Data>(json).load;
    }
    public enum BuildingSupportStatus { Supported, Unsupported, Overloaded, InvalidConfiguration }
    public readonly struct BuildingSupport
    {
        public readonly BuildingSupportStatus Status;
        public readonly float Load;
        public readonly string SupportId;
        internal BuildingSupport(BuildingSupportStatus status, float load, string supportId) { Status = status; Load = load; SupportId = supportId; }
    }
}
