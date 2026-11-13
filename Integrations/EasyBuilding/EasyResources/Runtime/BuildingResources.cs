using System;
using System.Collections.Generic;
using EasyResources;
using UnityEngine;

namespace EasyBuilding.Integrations
{
    [DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Integrations/EasyResources Payment")]
    public sealed class BuildingResources : MonoBehaviour, IBuildingEconomy, IBuildingCostProvider, IBuildingResourceAccounts
    {
        public ResourceDefinition[] Resources = Array.Empty<ResourceDefinition>();
        [Tooltip("Spent in this order. These are shared by all actors unless an Accounts Provider is assigned.")]
        public ResourceContainer[] Containers = Array.Empty<ResourceContainer>();
        [Tooltip("Empty uses the first container.")] public ResourceContainer RefundContainer;
        [Tooltip("Optional component implementing IBuildingResourceAccounts for actor/team inventories.")]
        public MonoBehaviour AccountsProvider;
        public ResourceAlias[] Aliases = Array.Empty<ResourceAlias>();
        private BuildingResourceEconomy economy;
        [Serializable] public struct ResourceAlias { public string Id; public ResourceDefinition Resource; }
        public void RefreshConfiguration() { economy = null; }
        private BuildingResourceEconomy Service
        {
            get
            {
                if (economy != null) return economy;
                if (AccountsProvider != null && !(AccountsProvider is IBuildingResourceAccounts)) throw new InvalidOperationException("Accounts Provider must implement IBuildingResourceAccounts.");
                var created = new BuildingResourceEconomy(AccountsProvider != null ? (IBuildingResourceAccounts)AccountsProvider : this, Resources);
                foreach (var alias in Aliases) created.AddAlias(alias.Id, alias.Resource);
                economy = created; return created;
            }
        }
        public void GetStores(IBuildingActor actor, List<ResourceStore> output, out ResourceStore refundStore)
        {
            output.Clear();
            foreach (var container in Containers)
            { if (container == null) throw new InvalidOperationException("Assign every resource container or remove its empty row."); output.Add(container.Store); }
            refundStore = RefundContainer != null ? RefundContainer.Store : output.Count > 0 ? output[0] : null;
        }
        public bool CanApply(in BuildingBill bill, out string reason) => Service.CanApply(bill, out reason);
        public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason) => Service.TryReserve(bill, out reservation, out reason);
        public bool CanAfford(BuildingDefinition definition, out string reason) => Service.CanAfford(definition, out reason);
        public bool TryReserve(BuildingDefinition definition, out IBuildingCostReservation reservation, out string reason) => Service.TryReserve(definition, out reservation, out reason);
        private void OnValidate() => RefreshConfiguration();
    }
}
