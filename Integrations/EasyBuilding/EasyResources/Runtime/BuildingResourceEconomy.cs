using System;
using System.Collections.Generic;
using EasyResources;

namespace EasyBuilding.Integrations
{
    /// <summary>Optional authority-side resolver. Return only stores this actor may spend from, in spending order.</summary>
    public interface IBuildingResourceAccounts
    {
        void GetStores(IBuildingActor actor, List<ResourceStore> output, out ResourceStore refundStore);
    }

    /// <summary>Atomic multi-store payment and refunds using EasyResources' public ResourceTransaction API.</summary>
    public sealed class BuildingResourceEconomy : IBuildingEconomy, IBuildingCostProvider
    {
        private readonly Dictionary<string, ResourceDefinition> resources = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<ResourceDefinition, long> totals = new Dictionary<ResourceDefinition, long>();
        private readonly List<ResourceStore> accounts = new List<ResourceStore>();
        private readonly List<Line> plan = new List<Line>();
        private readonly ResourceTransaction preview = new ResourceTransaction();
        private readonly ResourceStore[] fixedStores;
        private readonly ResourceStore fixedRefund;
        private readonly IBuildingResourceAccounts resolver;
        private bool planning;
        private struct Line { internal ResourceStore Store; internal ResourceDefinition Resource; internal long Amount; }
        public BuildingResourceEconomy(IReadOnlyList<ResourceStore> stores, IReadOnlyList<ResourceDefinition> definitions, ResourceStore refundStore = null)
        {
            if (stores == null) throw new ArgumentNullException(nameof(stores));
            fixedStores = new ResourceStore[stores.Count]; for (int i = 0; i < stores.Count; i++) fixedStores[i] = stores[i];
            fixedRefund = refundStore ?? (fixedStores.Length != 0 ? fixedStores[0] : null); Register(definitions);
        }
        public BuildingResourceEconomy(IBuildingResourceAccounts accounts, IReadOnlyList<ResourceDefinition> definitions)
        { resolver = accounts ?? throw new ArgumentNullException(nameof(accounts)); Register(definitions); }
        private void Register(IReadOnlyList<ResourceDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            foreach (var resource in definitions)
                if (resource == null || !resources.TryAdd(resource.Id, resource)) throw new ArgumentException("Resources must have distinct nonempty IDs.");
        }
        public void AddAlias(string id, ResourceDefinition resource)
        {
            if (planning || string.IsNullOrWhiteSpace(id) || resource == null || !resources.TryGetValue(resource.Id, out var known) || known != resource)
                throw new ArgumentException("Register the resource first and choose a distinct alias while idle.");
            if (!resources.TryAdd(id, resource)) throw new ArgumentException("The resource ID or alias is already registered.");
        }
        public bool CanAfford(BuildingDefinition definition, out string reason) => CanApply(new BuildingBill(definition.Costs, Array.Empty<BuildingCost>(), null, BuildingAction.Place), out reason);
        public bool TryReserve(BuildingDefinition definition, out IBuildingCostReservation reservation, out string reason)
            => TryReserve(new BuildingBill(definition.Costs, Array.Empty<BuildingCost>(), null, BuildingAction.Place), out reservation, out reason);
        public bool CanApply(in BuildingBill bill, out string reason)
        {
            reason = null;
            if (planning) { reason = "This resource provider is already preparing a payment."; return false; }
            planning = true;
            try { return Prepare(bill, out reason); }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { planning = false; }
        }
        public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
        {
            reservation = null; reason = null;
            if (planning) { reason = "This resource provider is already preparing a payment."; return false; }
            planning = true; Hold hold = null;
            try
            {
                if (!Prepare(bill, out reason)) return false;
                hold = new Hold(bill.Actor?.Id);
                var rows = new List<ResourceAmount>();
                foreach (var store in accounts)
                {
                    rows.Clear();
                    foreach (var line in plan) if (ReferenceEquals(line.Store, store) && line.Amount > 0) rows.Add(new ResourceAmount(line.Resource, line.Amount));
                    if (rows.Count == 0) continue;
                    if (!store.TryReserve(new ResourceCost(rows.ToArray()), out var handle, out var failure, new ResourceReservationOptions(bill.Actor?.Id)))
                    { reason = failure.ToString(); return false; }
                    hold.Reservations.Add(handle); hold.Transaction.Commit(handle);
                }
                foreach (var line in plan) if (line.Amount < 0) hold.Transaction.Add(line.Store, line.Resource, checked(-line.Amount));
                if (!hold.Transaction.CanExecute(out var blocked)) { reason = blocked.ToString(); return false; }
                reservation = hold; return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { if (reservation == null) hold?.Cancel(); planning = false; }
        }
        private bool Prepare(in BuildingBill bill, out string reason)
        {
            reason = null; accounts.Clear(); plan.Clear(); totals.Clear(); preview.Clear();
            ResourceStore refund;
            if (resolver != null) resolver.GetStores(bill.Actor, accounts, out refund);
            else { accounts.AddRange(fixedStores); refund = fixedRefund; }
            // Duplicate shared/account references must never make the same available funds count twice.
            for (int i = 0; i < accounts.Count; i++)
            {
                if (accounts[i] == null) { reason = "Assign every resource store."; return false; }
                for (int j = accounts.Count - 1; j > i; j--) if (ReferenceEquals(accounts[i], accounts[j])) accounts.RemoveAt(j);
            }
            Accumulate(bill.Charges, false); Accumulate(bill.Credits, true);
            foreach (var total in totals)
            {
                if (total.Value < 0)
                {
                    if (refund == null) { reason = "Choose a refund store for this actor."; return false; }
                    plan.Add(new Line { Store = refund, Resource = total.Key, Amount = total.Value });
                    preview.Add(refund, total.Key, checked(-total.Value));
                    continue;
                }
                long remaining = total.Value;
                foreach (var store in accounts)
                {
                    long amount = Math.Min(remaining, store.GetAvailable(total.Key));
                    if (amount > 0) { plan.Add(new Line { Store = store, Resource = total.Key, Amount = amount }); preview.Spend(store, total.Key, amount); remaining -= amount; }
                    if (remaining == 0) break;
                }
                if (remaining != 0) { reason = "Not enough " + total.Key.DisplayName + " across the permitted stores."; return false; }
            }
            if (!preview.CanExecute(out var failure)) { reason = failure.ToString(); return false; }
            return true;
        }
        private void Accumulate(IReadOnlyList<BuildingCost> costs, bool credit)
        {
            if (costs == null) return;
            for (int i = 0; i < costs.Count; i++)
            {
                var cost = costs[i];
                if (cost.Amount < 0 || cost.ResourceId == null || !resources.TryGetValue(cost.ResourceId, out var resource))
                    throw new ArgumentException("Unknown resource ID or invalid amount in the building bill: " + cost.ResourceId);
                totals.TryGetValue(resource, out long current);
                totals[resource] = credit ? checked(current - cost.Amount) : checked(current + cost.Amount);
            }
        }
        private sealed class Hold : IBuildingCostReservation
        {
            internal readonly List<ResourceReservation> Reservations = new List<ResourceReservation>();
            internal readonly ResourceTransaction Transaction = new ResourceTransaction();
            private readonly string actorId;
            private bool done;
            internal Hold(string actorId) => this.actorId = actorId;
            public bool TryCommit(out string reason)
            {
                reason = null;
                if (done) { reason = "This building payment was already completed."; return false; }
                if (!Transaction.TryExecute(out var failure, new ResourceOperationContext("EasyBuilding", actorId))) { reason = failure.ToString(); return false; }
                done = true; return true;
            }
            public void Cancel()
            {
                if (done) return;
                done = true;
                foreach (var handle in Reservations) try { handle.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }
}
