using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public enum BuildingPortDirection { Bidirectional, Input, Output }
    [DisallowMultipleComponent, RequireComponent(typeof(BuildingSocket)), AddComponentMenu("EasyGameFramework/Building/Networks/Utility Port")]
    public sealed class BuildingUtilityPort : MonoBehaviour, IBuildingStatePart
    {
        public BuildingPortDirection Direction;
        [Min(0)] public float Capacity = 1;
        public bool Open = true;
        public string Channel => GetComponent<BuildingSocket>().SocketType;
        public string StateKey => "utility." + GetComponent<BuildingSocket>().Id;
        public int StateVersion => 1;
        public bool ReplicateState => true;
        [Serializable] private struct Data { public bool open; public float capacity; public BuildingPortDirection direction; }
        public string CaptureBuildingState() => JsonUtility.ToJson(new Data { open = Open, capacity = Capacity, direction = Direction });
        public bool CanRestoreBuildingState(int version, string json, out string reason)
        {
            reason = null;
            try { var data = JsonUtility.FromJson<Data>(json); if (version == 1 && BuildingMath.Finite(data.capacity) && data.capacity >= 0 && data.direction >= BuildingPortDirection.Bidirectional && data.direction <= BuildingPortDirection.Output) return true; }
            catch (Exception) { }
            reason = "Invalid utility port state."; return false;
        }
        public void RestoreBuildingState(int version, string json)
        { var data = JsonUtility.FromJson<Data>(json); Open = data.open; Capacity = data.capacity; Direction = data.direction; }
        public bool SetOpen(bool value, out string reason)
        {
            reason = null; var building = GetComponentInParent<PlacedBuilding>();
            if (building == null || building.System == null || building.System.IsReadOnly || building.System.IsBusy) { reason = "Change ports on an idle authoritative building."; return false; }
            if (Open == value) return true; bool previous = Open; Open = value;
            if (building.System.NotifyStateChanged(building, out reason)) return true;
            Open = previous; return false;
        }
        internal bool Sends => Open && isActiveAndEnabled && Direction != BuildingPortDirection.Input && BuildingMath.Finite(Capacity) && Capacity > 0;
        internal bool Receives => Open && isActiveAndEnabled && Direction != BuildingPortDirection.Output && BuildingMath.Finite(Capacity) && Capacity > 0;
    }
}
