namespace EasyBuilding
{
    public sealed class BuildingConnection
    {
        public string Id { get; internal set; }
        public BuildingSystem System { get; internal set; }
        public PlacedBuilding Source { get; internal set; }
        public BuildingSocket SourceSocket { get; internal set; }
        public BuildingSocket TargetSocket { get; internal set; }
        public PlacedBuilding Target => TargetSocket != null ? TargetSocket.Building : null;
        public bool Primary { get; internal set; }
        public bool IsStructural => TargetSocket != null && TargetSocket.Structural && (SourceSocket == null || SourceSocket.Structural);
        public PlacedBuilding Other(PlacedBuilding building) => building == Source ? Target : Source;
        public BuildingConnectionRecord Capture() => new BuildingConnectionRecord
        {
            Id = Id, SourceId = Source.InstanceId, SourceSocketId = SourceSocket != null ? SourceSocket.Id : "",
            TargetId = Target != null ? Target.InstanceId : "", TargetSocketId = TargetSocket != null ? TargetSocket.Id : "", Primary = Primary
        };
    }
}
