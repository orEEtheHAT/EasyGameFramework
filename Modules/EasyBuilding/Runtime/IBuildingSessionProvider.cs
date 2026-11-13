namespace EasyBuilding
{
    /// <summary>Owns a shared selection session for a player and world. Consumers borrow it and must not dispose it.</summary>
    public interface IBuildingSessionProvider
    {
        BuildingSession GetSession(BuildingSystem system);
    }
}
