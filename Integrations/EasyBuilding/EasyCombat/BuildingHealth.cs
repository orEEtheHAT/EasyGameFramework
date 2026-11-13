using System;
using System.Collections.Generic;
using EasyCombat;
using UnityEngine;

namespace EasyBuilding.Integrations
{
    [RequireComponent(typeof(Health)), RequireComponent(typeof(PlacedBuilding))]
    [AddComponentMenu("EasyGameFramework/Building/Integrations/Building Health")]
    public sealed class BuildingHealth : MonoBehaviour, IBuildingStatePart, IBuildingRepairTarget, IBuildingUpgradeStatePart
    {
        public BuildingCost[] RepairCostPerHealth = Array.Empty<BuildingCost>();
        public bool AllowRepair = true;
        private Health health;
        private CombatDefinitionCatalog gateCatalog;
        private readonly CombatReferenceRegistry gateReferences = new CombatReferenceRegistry();
        private bool restoring, dirty;
        public string StateKey => "combat.health";
        public int StateVersion => 1;
        public bool ReplicateState => true;
        private Health Target => health != null ? health : health = GetComponent<Health>();
        private void OnEnable() { health = Target; health.Died += OnDied; health.Changed += OnChanged; }
        private void OnDisable() { if (health != null) { health.Died -= OnDied; health.Changed -= OnChanged; } }
        private void OnDied(Health _) => GetComponent<PlacedBuilding>().Remove();
        private void OnChanged(HealthChange change)
        {
            if (restoring) return;
            var building = GetComponent<PlacedBuilding>();
            if (building == null || building.System == null || building.System.IsReadOnly) return;
            dirty = !building.System.NotifyStateChanged(building, out _);
        }
        private void LateUpdate()
        {
            if (!dirty) return; var building = GetComponent<PlacedBuilding>();
            if (building != null && building.System != null && !building.System.IsBusy) dirty = !building.System.NotifyStateChanged(building, out _);
        }
        public string CaptureBuildingState() => JsonUtility.ToJson(Target.CaptureState());
        public bool CanRestoreBuildingState(int version, string json, out string reason)
        {
            reason = null;
            try
            {
                var state = JsonUtility.FromJson<HealthSnapshot>(json);
                if (version == 1 && (state.Version == 1 || state.Version == 2) && Finite(state.Maximum) && state.Maximum > 0 && Finite(state.Current) && state.Current >= 0 && state.Current <= state.Maximum &&
                    state.LifeState >= CombatLifeState.Alive && state.LifeState <= CombatLifeState.Dead &&
                    (state.Version == 1 || (state.LifeState == CombatLifeState.Alive ? state.Current > 0 : state.Current == 0)) &&
                    (state.LifeState != CombatLifeState.Downed || Target.LifeRules != null && Target.LifeRules.AllowDowned && state.Current == 0)) return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            reason = "Invalid or incompatible EasyCombat health state."; return false;
        }
        public void RestoreBuildingState(int version, string json)
        {
            restoring = true;
            try { if (!Target.RestoreState(JsonUtility.FromJson<HealthSnapshot>(json))) throw new InvalidOperationException("EasyCombat rejected health restore during an active combat transaction."); }
            finally { restoring = false; }
        }
        public bool TryPrepareUpgradeState(BuildingDefinition previousDefinition, in BuildingPartState previous, out BuildingPartState converted, out string reason)
        {
            converted = previous;
            if (!CanRestoreBuildingState(previous.Version, previous.Json, out reason)) return false;
            var state = JsonUtility.FromJson<HealthSnapshot>(previous.Json); float maximum = Target.Maximum;
            var upgraded = new HealthSnapshot(maximum, maximum * (state.Current / state.Maximum), state.LifeState);
            converted.Json = JsonUtility.ToJson(upgraded); return true;
        }
        public bool TryPrepareRepair(float maximumAmount, out IBuildingPreparedMutation repair, out string reason)
        {
            repair = null; reason = null;
            if (!AllowRepair || !Finite(maximumAmount) || maximumAmount <= 0 || !Target.isActiveAndEnabled || !Target.IsStanding)
            { reason = "Repair requires a living enabled building and a positive finite amount."; return false; }
            if (!BetweenCombatTransactions(out reason)) return false;
            var before = Target.CaptureState(); float amount = Mathf.Min(maximumAmount, before.Maximum - before.Current);
            if (amount <= 0) { reason = "This building is already fully repaired."; return false; }
            var after = new HealthSnapshot(before.Maximum, before.Current + amount, CombatLifeState.Alive);
            var costs = new BuildingCost[RepairCostPerHealth?.Length ?? 0];
            try
            {
                for (int i = 0; i < costs.Length; i++) costs[i] = new BuildingCost(RepairCostPerHealth[i].ResourceId,
                    checked((long)decimal.Ceiling(RepairCostPerHealth[i].Amount * (decimal)amount)));
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            repair = new Repair(this, before, after, costs); return true;
        }
        private bool BetweenCombatTransactions(out string reason)
        {
            if (gateCatalog == null) { gateCatalog = ScriptableObject.CreateInstance<CombatDefinitionCatalog>(); gateCatalog.hideFlags = HideFlags.HideAndDontSave; }
            // The public save API exposes a pure availability check. An empty registry captures no actor state.
            return CombatSaveSystem.TryCapture(gateCatalog, gateReferences, out _, out reason);
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private void OnDestroy()
        { if (gateCatalog != null) { if (Application.isPlaying) Destroy(gateCatalog); else DestroyImmediate(gateCatalog); } }
        private sealed class Repair : IBuildingPreparedMutation
        {
            private readonly BuildingHealth owner;
            private readonly HealthSnapshot before;
            public IBuildingStatePart Part => owner;
            public BuildingPartState After { get; }
            public BuildingAction Action => BuildingAction.Repair;
            public IReadOnlyList<BuildingCost> Costs { get; }
            public bool AddToPaidReceipt => false;
            internal Repair(BuildingHealth owner, HealthSnapshot before, HealthSnapshot after, BuildingCost[] costs)
            {
                this.owner = owner; this.before = before; Costs = costs;
                After = new BuildingPartState { Key = owner.StateKey, Version = owner.StateVersion, Json = JsonUtility.ToJson(after), Replicate = true };
            }
            public bool CanApply(out string reason)
            {
                reason = "The building's health changed while repair was prepared.";
                if (owner == null || !owner.AllowRepair || !owner.Target.isActiveAndEnabled) return false;
                var current = owner.Target.CaptureState();
                return current.Current == before.Current && current.Maximum == before.Maximum && current.LifeState == before.LifeState && owner.BetweenCombatTransactions(out reason);
            }
            public void Apply() => owner.RestoreBuildingState(After.Version, After.Json);
        }
    }
}
