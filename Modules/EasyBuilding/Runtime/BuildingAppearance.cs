using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [Serializable]
    public sealed class BuildingMaterialVariant
    {
        public string Id;
        public string Name;
        public BuildingMaterialBinding[] Materials = Array.Empty<BuildingMaterialBinding>();
        public BuildingCost[] Costs = Array.Empty<BuildingCost>();
    }
    [Serializable]
    public struct BuildingMaterialBinding
    {
        public Renderer Renderer;
        public Material[] Materials;
    }
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Appearance")]
    public sealed class BuildingAppearance : MonoBehaviour, IBuildingStatePart
    {
        public BuildingMaterialVariant[] Variants = Array.Empty<BuildingMaterialVariant>();
        public string CurrentVariant { get; private set; } = "";
        private readonly Dictionary<Renderer, Material[]> baseline = new Dictionary<Renderer, Material[]>();
        private bool initialized;
        public string StateKey => "appearance";
        public int StateVersion => 1;
        public bool ReplicateState => true;
        [Serializable] private struct Data { public string id; }
        public string CaptureBuildingState() => JsonUtility.ToJson(new Data { id = CurrentVariant });
        public bool CanRestoreBuildingState(int version, string json, out string reason)
        {
            reason = null;
            try { if (version == 1) return Validate(JsonUtility.FromJson<Data>(json).id ?? "", out _, out reason); }
            catch (Exception exception) { reason = exception.Message; return false; }
            reason = "Unsupported appearance state version."; return false;
        }
        public void RestoreBuildingState(int version, string json) => ApplyVariant(JsonUtility.FromJson<Data>(json).id ?? "");
        public bool TryPrepareVariant(string id, out IBuildingPreparedMutation mutation, out string reason)
        {
            mutation = null; id = id ?? "";
            if (!Validate(id, out var variant, out reason)) return false;
            if (id == CurrentVariant) { reason = "This material variant is already applied."; return false; }
            EnsureBaseline(); mutation = new Change(this, id, variant?.Costs ?? Array.Empty<BuildingCost>()); return true;
        }
        private bool Validate(string id, out BuildingMaterialVariant selected, out string reason)
        {
            selected = null; reason = null;
            if (Variants != null) for (int i = 0; i < Variants.Length; i++)
            {
                var variant = Variants[i];
                if (variant == null || string.IsNullOrWhiteSpace(variant.Id)) { reason = "Assign nonempty variant IDs."; return false; }
                for (int j = 0; j < i; j++) if (Variants[j].Id == variant.Id) { reason = "Material variant IDs must be unique."; return false; }
                if (variant.Id == id) selected = variant;
                if (variant.Materials != null) foreach (var binding in variant.Materials)
                {
                    if (binding.Renderer == null || binding.Materials == null || binding.Materials.Length == 0) { reason = "Assign each material binding's renderer and materials."; return false; }
                    if (binding.Renderer.transform != transform && !binding.Renderer.transform.IsChildOf(transform)) { reason = "Material bindings must reference renderers inside this building."; return false; }
                    foreach (var material in binding.Materials) if (material == null) { reason = "Assign every material or remove its empty slot."; return false; }
                }
            }
            if (id.Length == 0 || selected != null) return true;
            reason = "This material variant is not defined on the prefab."; return false;
        }
        private void EnsureBaseline()
        {
            if (initialized) return; initialized = true;
            if (Variants != null) foreach (var variant in Variants) if (variant?.Materials != null) foreach (var binding in variant.Materials)
                if (binding.Renderer != null && !baseline.ContainsKey(binding.Renderer)) baseline.Add(binding.Renderer, binding.Renderer.sharedMaterials);
        }
        private void ApplyVariant(string id)
        {
            EnsureBaseline();
            foreach (var pair in baseline) if (pair.Key != null) pair.Key.sharedMaterials = pair.Value;
            if (Variants != null) foreach (var variant in Variants) if (variant.Id == id && variant.Materials != null)
                foreach (var binding in variant.Materials) if (binding.Renderer != null) binding.Renderer.sharedMaterials = binding.Materials;
            CurrentVariant = id;
        }
        private sealed class Change : IBuildingPreparedMutation
        {
            private readonly BuildingAppearance owner;
            private readonly string id, previous;
            public IBuildingStatePart Part => owner;
            public BuildingPartState After { get; }
            public BuildingAction Action => BuildingAction.Customize;
            public IReadOnlyList<BuildingCost> Costs { get; }
            public bool AddToPaidReceipt => true;
            internal Change(BuildingAppearance owner, string id, BuildingCost[] costs)
            {
                this.owner = owner; this.id = id; previous = owner.CurrentVariant; Costs = (BuildingCost[])costs.Clone();
                After = new BuildingPartState { Key = owner.StateKey, Version = 1, Json = JsonUtility.ToJson(new Data { id = id }), Replicate = true };
            }
            public bool CanApply(out string reason)
            {
                reason = "Appearance changed while preparing the operation.";
                return owner != null && owner.CurrentVariant == previous && owner.Validate(id, out _, out reason);
            }
            public void Apply() => owner.ApplyVariant(id);
        }
    }
}
