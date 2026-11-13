namespace EasyBuilding
{
    /// <summary>Main-thread contract. Read-only affordability must not reserve or spend anything.</summary>
    public interface IBuildingCostProvider
    {
        bool CanAfford(BuildingDefinition definition, out string reason);
        bool TryReserve(BuildingDefinition definition, out IBuildingCostReservation reservation, out string reason);
    }

    /// <summary>
    /// A reservation is exclusive. TryCommit atomically spends once; false leaves funds unchanged.
    /// Cancel releases uncommitted funds and is idempotent, including after commit.
    /// Implementations must not throw after spending. All calls run on Unity's main thread.
    /// </summary>
    public interface IBuildingCostReservation
    {
        bool TryCommit(out string reason);
        void Cancel();
    }
}
