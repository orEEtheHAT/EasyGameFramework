using System;
using System.Collections.Generic;

namespace EasyBuilding
{
    public enum BuildingCostBasis { Fixed, Length, Area, Volume }

    /// <summary>Borrowed lists, valid during the provider call. A reservation must retain its own immutable copy.</summary>
    public readonly struct BuildingBill
    {
        public readonly IReadOnlyList<BuildingCost> Charges;
        public readonly IReadOnlyList<BuildingCost> Credits;
        public readonly IBuildingActor Actor;
        public readonly BuildingAction Action;
        public BuildingBill(IReadOnlyList<BuildingCost> charges, IReadOnlyList<BuildingCost> credits,
            IBuildingActor actor = null, BuildingAction action = BuildingAction.Place)
        { Charges = charges; Credits = credits; Actor = actor; Action = action; }
        public bool IsEmpty => (Charges == null || Charges.Count == 0) && (Credits == null || Credits.Count == 0);
    }

    /// <summary>Atomic aggregate payment for groups, upgrades, refunds and history. No scene mutation is allowed in these methods.</summary>
    public interface IBuildingEconomy
    {
        bool CanApply(in BuildingBill bill, out string reason);
        bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason);
    }

    public interface IBuildingPriceModifier
    {
        float GetMultiplier(BuildingDefinition definition, IBuildingActor actor, BuildingAction action);
    }

    [Serializable]
    public sealed class BuildingPaymentReceipt
    {
        public BuildingCost[] Charges = Array.Empty<BuildingCost>();
        public BuildingCost[] Credits = Array.Empty<BuildingCost>();
    }
}
