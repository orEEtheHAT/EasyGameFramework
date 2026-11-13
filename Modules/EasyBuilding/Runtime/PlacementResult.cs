using UnityEngine;

namespace EasyBuilding
{
    public enum PlacementFailure
    {
        None, NoSelection, Busy, InvalidConfiguration, InvalidPose, MissingSurface, Slope,
        OutsideZone, MissingSocket, IncompatibleSocket, OccupiedSocket, Obstructed,
        MissingCostProvider, CannotAfford, PaymentFailed, CreationFailed,
        PermissionDenied, RuleRejected, InvalidConnection, StaleRequest, UnsupportedOperation,
        MissingDefinition, InvalidState, Cancelled, LimitExceeded, NotFound
    }

    /// <summary>A requested world pose. Socket placement resolves the pose from Socket at each check.</summary>
    public readonly struct PlacementRequest
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly BuildingSocket Socket;
        public readonly BuildingSpace Space;
        public readonly Vector3 Scale;
        public readonly Vector3 SurfaceNormal;
        public readonly bool HasSurfaceNormal;
        public readonly string SourceSocketId;
        public PlacementRequest(Vector3 position, Quaternion rotation, BuildingSocket socket = null,
            BuildingSpace space = null, Vector3? scale = null, Vector3? surfaceNormal = null, string sourceSocketId = null)
        {
            Position = position; Rotation = rotation; Socket = socket; Space = space;
            Scale = scale ?? Vector3.one; SurfaceNormal = surfaceNormal ?? Vector3.zero;
            HasSurfaceNormal = surfaceNormal.HasValue; SourceSocketId = sourceSocketId;
        }
    }

    public readonly struct PlacementResult
    {
        public readonly PlacementFailure Failure;
        public readonly string Reason;
        public readonly Pose Pose;
        public readonly BuildingSocket Socket;
        public bool IsValid => Failure == PlacementFailure.None && Reason != null;
        public string MessageKey => Failure.ToString();
        public PlacementResult(PlacementFailure failure, string reason, Pose pose, BuildingSocket socket = null)
        { Failure = failure; Reason = reason; Pose = pose; Socket = socket; }
        public static PlacementResult Fail(PlacementFailure failure, string reason, Pose pose = default) => new PlacementResult(failure, reason, pose);
    }
}
