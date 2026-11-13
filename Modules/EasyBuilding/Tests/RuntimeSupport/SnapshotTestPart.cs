using System;
using UnityEngine;

namespace EasyBuilding.Tests
{
    public sealed class SnapshotTestPart : MonoBehaviour, IBuildingStatePart
    {
        public int Value;
        public bool ThrowOnRestore;
        public static int Restores;
        public string StateKey => "private.test";
        public int StateVersion => 1;
        public bool ReplicateState => false;
        public string CaptureBuildingState() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public bool CanRestoreBuildingState(int version, string json, out string reason) { reason = "Invalid private payload."; return version == 1 && int.TryParse(json, out int value) && value >= 0; }
        public void RestoreBuildingState(int version, string json) { if (ThrowOnRestore) throw new InvalidOperationException("Test restore rejection."); Value = int.Parse(json); Restores++; }
    }
}
