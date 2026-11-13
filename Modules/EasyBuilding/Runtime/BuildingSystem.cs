using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyBuilding
{
    /// <summary>Main-thread world service. Input selections live in independent BuildingSession instances.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building System")]
    public sealed partial class BuildingSystem : MonoBehaviour
    {
        [SerializeField, Tooltip("A component implementing IBuildingEconomy, or the legacy single-placement cost provider.")]
        private MonoBehaviour costProvider = null;
        [SerializeField, Tooltip("Optional custom physics backend. Empty uses this scene's 3D physics world.")]
        private MonoBehaviour physicsProvider = null;
        public BuildingRule[] Rules = Array.Empty<BuildingRule>();
        public bool UseSpatialQueries = true;
        public bool DestroyBuildingsWithSystem = true;
        private IBuildingCostProvider runtimeProvider;
        private IBuildingEconomy runtimeEconomy;
        private IBuildingPhysics physics;
        private BuildingSpatialQueries spatial;
        private BuildingSession legacySession;
        private readonly List<BuildingSession> sessions = new List<BuildingSession>();
        private readonly List<IBuildingRule> runtimeRules = new List<IBuildingRule>();
        private readonly Dictionary<string, PlacedBuilding> buildings = new Dictionary<string, PlacedBuilding>(StringComparer.Ordinal);
        private readonly Dictionary<string, BuildingConnection> connections = new Dictionary<string, BuildingConnection>(StringComparer.Ordinal);
        private readonly Dictionary<string, BuildingDefinition> knownDefinitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<BuildingDefinition, PrefabInfo> prefabInfo = new Dictionary<BuildingDefinition, PrefabInfo>();
        private readonly HashSet<Transform> ignoredRoots = new HashSet<Transform>();
        private readonly HashSet<PlacedBuilding> inactiveBuildings = new HashSet<PlacedBuilding>();
        private readonly List<PlacedBuilding> nearbyBuildings = new List<PlacedBuilding>(64);
        private readonly List<BuildingSocket> nearbySockets = new List<BuildingSocket>(64);
        private readonly List<BuildingZone> nearbyZones = new List<BuildingZone>(32);
        private readonly List<BuildingCost> quoteCharges = new List<BuildingCost>(8);
        private readonly List<BuildingCost> emptyCosts = new List<BuildingCost>(0);
        private Transform stagingRoot;
        private bool busy, checking, destroying;
        private static readonly ProfilerMarker CheckMarker = new ProfilerMarker("EasyBuilding.Check");
        private static readonly ProfilerMarker SocketMarker = new ProfilerMarker("EasyBuilding.FindSocket");
        public bool IsBusy => busy || checking || destroying;
        public long Revision { get; private set; }
        public int Count => buildings.Count;
        public BuildingDefinition Selected => DefaultSession.Selected;
        public BuildingSession DefaultSession => legacySession != null && !legacySession.IsDisposed ? legacySession : legacySession = CreateSession();
        public IBuildingPriceModifier PriceModifier { get; set; }
        public event Action<PlacedBuilding> Placed;
        public event Action<PlacedBuilding> Removed;
        public event Action<BuildingChangeSet> Changed;
        public event Action Restored;

        public IBuildingCostProvider CostProvider
        {
            get => Alive(runtimeProvider) ? runtimeProvider : costProvider != null ? costProvider as IBuildingCostProvider : null;
            set { RequireIdle(); runtimeProvider = value; runtimeEconomy = value as IBuildingEconomy; costProvider = value as MonoBehaviour; }
        }
        public IBuildingEconomy Economy
        {
            get => Alive(runtimeEconomy) ? runtimeEconomy : costProvider != null ? costProvider as IBuildingEconomy : runtimeProvider as IBuildingEconomy;
            set { RequireIdle(); runtimeEconomy = value; runtimeProvider = value as IBuildingCostProvider; costProvider = value as MonoBehaviour; }
        }
        public IBuildingPhysics PhysicsBackend
        {
            get { EnsureServices(); return physics; }
            set { RequireIdle(); physics = value ?? new BuildingPhysics3D(this); }
        }
        private static bool Alive(object value) => value != null && (!(value is UnityEngine.Object unityObject) || unityObject != null);
        private void RequireIdle() { if (IsBusy) throw new InvalidOperationException("A building operation is running."); }
        private void EnsureServices()
        {
            if (!Alive(physics)) physics = physicsProvider != null ? physicsProvider as IBuildingPhysics : new BuildingPhysics3D(this);
            if (physics == null) throw new InvalidOperationException("Physics Provider must implement IBuildingPhysics.");
            if (spatial == null) spatial = new BuildingSpatialQueries(this);
        }
        public BuildingSession CreateSession(IBuildingActor actor = null)
        {
            if (destroying || this == null) throw new ObjectDisposedException(nameof(BuildingSystem));
            var session = new BuildingSession(this, actor); sessions.Add(session); return session;
        }
        internal void ReleaseSession(BuildingSession session) => sessions.Remove(session);
        public bool Select(BuildingDefinition definition) => DefaultSession.Select(definition);
        public void Cancel() => DefaultSession.Cancel();
        public bool TryConfirm(PlacementRequest request, out PlacedBuilding building, out PlacementResult result) => DefaultSession.TryConfirm(request, out building, out result);
        public void AddRule(IBuildingRule rule) { RequireIdle(); if (rule == null) throw new ArgumentNullException(nameof(rule)); if (!runtimeRules.Contains(rule)) runtimeRules.Add(rule); }
        public bool RemoveRule(IBuildingRule rule) { RequireIdle(); return runtimeRules.Remove(rule); }
        public void InvalidateDefinition(BuildingDefinition definition) { RequireIdle(); if (definition != null) prefabInfo.Remove(definition); }

        public PlacementResult Check(PlacementRequest request) => Check(Selected, request);
        public PlacementResult Check(BuildingDefinition definition, PlacementRequest request, IBuildingActor actor = null)
            => CheckOperation(definition, request, null, actor, BuildingAction.Place, true);
        public PlacementResult CheckMove(PlacedBuilding building, PlacementRequest request, IBuildingActor actor = null)
            => building == null ? PlacementResult.Fail(PlacementFailure.NotFound, "Select an existing building.")
                : CheckOperation(building.Definition, request, building, actor, BuildingAction.Move, false);

        private PlacementResult CheckOperation(BuildingDefinition definition, PlacementRequest request, PlacedBuilding existing,
            IBuildingActor actor, BuildingAction action, bool checkCost)
        {
            if (IsBusy) return PlacementResult.Fail(PlacementFailure.Busy, "A building operation is running.");
            checking = true;
            try
            {
                EnsureServices(); ignoredRoots.Clear();
                if (existing != null) ignoredRoots.Add(existing.transform);
                Synchronize();
                return CheckCore(definition, request, existing, actor, action, checkCost && !IsReadOnly);
            }
            catch (Exception exception) { return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, exception.Message); }
            finally { ignoredRoots.Clear(); checking = false; }
        }
        private void Synchronize()
        {
            BuildingZone.SynchronizeQueries();
            physics.SyncTransforms();
            if (physics.Is2D) Physics.SyncTransforms(); // The query-only broadphase is shared by both backends.
            if (physics.Is2D) UnityEngine.Physics.SyncTransforms();
        }

        private PlacementResult CheckCore(BuildingDefinition definition, PlacementRequest request, PlacedBuilding existing,
            IBuildingActor actor, BuildingAction action, bool checkCost, bool embeddedConnection = false)
        {
            using var marker = CheckMarker.Auto();
            if (definition == null) return PlacementResult.Fail(PlacementFailure.NoSelection, "Select a building first.");
            string error = definition.ConfigurationError;
            if (error != null) return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, error);
            if (!BuildingMath.Finite(request.Position) || !BuildingMath.ValidRotation(request.Rotation) || !ValidScale(request.Scale))
                return PlacementResult.Fail(PlacementFailure.InvalidPose, "Use a finite position/rotation and positive scale.");
            var info = GetPrefabInfo(definition);
            if (info.Error != null) return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, info.Error);
            if (existing != null && !CanEdit(existing, actor)) return PlacementResult.Fail(PlacementFailure.PermissionDenied, "You do not have permission to change this building.");
            var space = ResolveSpace(request, existing);
            if (request.Socket != null && request.Space != null && request.Space != request.Socket.Space)
                return PlacementResult.Fail(PlacementFailure.InvalidConnection, "A socket placement must use its target's Building Space.");
            if (space != null && (!space.IsValid || space.System != null && space.System != this || space.gameObject.scene.GetPhysicsScene() != gameObject.scene.GetPhysicsScene()))
                return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Use a Building Space with positive uniform scale in this world's physics scene.");
            bool uniform = Mathf.Abs(request.Scale.x - request.Scale.y) < 0.0001f && Mathf.Abs(request.Scale.x - request.Scale.z) < 0.0001f;
            if (!uniform)
            {
                for (int i = 0; i < definition.VolumeCount; i++) if (definition.GetVolume(i).Rotation.sqrMagnitude > 0.00001f)
                    return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Rotated footprint volumes require uniform instance scale.");
                if (definition.Clearances != null) foreach (var volume in definition.Clearances) if (volume.Rotation.sqrMagnitude > 0.00001f)
                    return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Rotated clearance volumes require uniform instance scale.");
            }
            var pose = new Pose(request.Position, request.Rotation.normalized);
            BuildingSocket target = null;
            if (definition.Placement == PlacementMode.Grid)
            {
                pose.position = space != null ? space.Snap(definition, pose.position) :
                    BuildingGeometry.Snap(pose.position, definition.GridSize, definition.GridOrigin, definition.GridLayout, definition.GridPlane);
                if (!BuildingMath.Finite(pose.position)) return PlacementResult.Fail(PlacementFailure.InvalidPose, "Grid coordinates are outside the supported numeric range.");
            }
            if (definition.Placement == PlacementMode.Socket && !embeddedConnection || request.Socket != null)
            {
                target = request.Socket;
                if (target == null || !target.isActiveAndEnabled || !Accepts(target)) return PlacementResult.Fail(PlacementFailure.MissingSocket, "Point at an enabled socket in this building world.", pose);
                if (target.Building != null && !target.Building.IsComplete) return PlacementResult.Fail(PlacementFailure.InvalidConnection, "Finish the supporting building before attaching another.", pose);
                if (target.IsOccupied && (existing == null || target.Occupant != existing)) return PlacementResult.Fail(PlacementFailure.OccupiedSocket, "This socket is occupied. Remove or detach its building first.", pose);
                if (!string.Equals(target.SocketType, definition.SocketType, StringComparison.Ordinal)) return PlacementResult.Fail(PlacementFailure.IncompatibleSocket, "Socket Type does not match this building.", pose);
                if (existing != null && target.Building == existing) return PlacementResult.Fail(PlacementFailure.InvalidConnection, "A building cannot attach to its own socket.", pose);
                string sourceId = request.SourceSocketId ?? definition.AttachmentSocketId;
                Quaternion twist = request.Rotation.normalized;
                if (definition.SocketRotationStep > 0)
                    twist = Quaternion.AngleAxis(Mathf.Round((physics.Is2D ? twist.eulerAngles.z : twist.eulerAngles.y) / definition.SocketRotationStep) * definition.SocketRotationStep, physics.Is2D ? Vector3.forward : Vector3.up);
                var rotation = target.transform.rotation * twist;
                if (!string.IsNullOrEmpty(sourceId))
                {
                    BuildingSocket source = existing != null && existing.Definition == definition ? GetSocket(existing, sourceId) : null;
                    if (source == null && !info.Sockets.TryGetValue(sourceId, out source)) return PlacementResult.Fail(PlacementFailure.InvalidConfiguration, "Attachment Socket Id does not exist in the prefab.", pose);
                    if (!source.IsCompatible(target)) return PlacementResult.Fail(PlacementFailure.IncompatibleSocket, "Socket type, role or size is incompatible.", pose);
                    if (target.FaceToFace) rotation *= physics.Is2D ? Quaternion.Euler(0, 0, 180) : Quaternion.Euler(0, 180, 0);
                    var sourceRoot = source.Building == existing && existing != null ? existing.transform : definition.Prefab.transform;
                    var localRotation = Quaternion.Inverse(sourceRoot.rotation) * source.transform.rotation;
                    rotation *= Quaternion.Inverse(localRotation);
                    var localPosition = sourceRoot.InverseTransformPoint(source.transform.position);
                    pose = new Pose(target.transform.position - rotation * Vector3.Scale(localPosition, request.Scale), rotation);
                }
                else pose = new Pose(target.transform.position, rotation);
            }
            else if (!embeddedConnection)
            {
                var up = space != null ? space.UpAt(pose.position) : Vector3.up;
                var normal = request.HasSurfaceNormal ? request.SurfaceNormal : up;
                if (!BuildingMath.Finite(normal) || normal.sqrMagnitude < 0.000001f) return PlacementResult.Fail(PlacementFailure.InvalidPose, "Use a nonzero finite surface normal.", pose);
                normal.Normalize();
                if (definition.RequireSurface)
                {
                    var filter = new BuildingCollisionFilter(ignoredRoots);
                    float probe = definition.SurfaceProbeDistance;
                    if (!physics.TryGetSurface(pose.position + normal * probe, -normal, probe * 2 + 0.01f, definition.SurfaceLayers, filter, out var surface))
                        return PlacementResult.Fail(PlacementFailure.MissingSurface, "Place the pivot on a surface in Surface Layers.", pose);
                    float dot = Vector3.Dot(surface.Normal, up);
                    float threshold = Mathf.Cos(definition.MaximumSlope * Mathf.Deg2Rad);
                    bool floor = dot >= threshold && (definition.Surfaces & BuildingSurface.Floor) != 0;
                    bool ceiling = -dot >= threshold && (definition.Surfaces & BuildingSurface.Ceiling) != 0;
                    bool wall = Mathf.Abs(dot) < threshold && (definition.Surfaces & BuildingSurface.Wall) != 0;
                    if (!floor && !ceiling && !wall) return PlacementResult.Fail(PlacementFailure.Slope, "This surface direction or slope is not allowed by the definition.", pose);
                    normal = surface.Normal;
                    if (definition.AlignToSurface) { pose.rotation = Quaternion.FromToRotation(pose.rotation * Vector3.up, normal) * pose.rotation; pose.position = surface.Point; }
                    if (definition.SupportPoints != null)
                        for (int i = 0; i < definition.SupportPoints.Length; i++)
                        {
                            var point = pose.position + pose.rotation * Vector3.Scale(definition.SupportPoints[i], request.Scale);
                            if (!physics.TryGetSurface(point + normal * probe, -normal, probe * 2 + 0.01f, definition.SurfaceLayers, filter, out _))
                                return PlacementResult.Fail(PlacementFailure.MissingSurface, "All support points must rest on a surface.", pose);
                        }
                }
                else if (definition.AlignToSurface) pose.rotation = Quaternion.FromToRotation(pose.rotation * Vector3.up, normal) * pose.rotation;
            }
            var first = BuildingGeometry.Transform(definition.GetVolume(0), pose, request.Scale);
            var footprint = first.Bounds;
            for (int i = 1; i < definition.VolumeCount; i++) footprint.Encapsulate(BuildingGeometry.Transform(definition.GetVolume(i), pose, request.Scale).Bounds);
            GetZones(footprint.center, footprint.extents.magnitude, nearbyZones);
            bool contained = !definition.RequireZone;
            for (int z = 0; z < nearbyZones.Count; z++)
            {
                var zone = nearbyZones[z];
                if (zone == null || !zone.isActiveAndEnabled || !Accepts(zone)) continue;
                if (!string.IsNullOrEmpty(zone.RequiredTag) && !definition.HasTag(zone.RequiredTag)) continue;
                if (zone.ForbidBuilding)
                {
                    // Forbidden volumes are checked for intersection, not only pivot containment.
                    if (IntersectsZone(definition, pose, request.Scale, zone)) return PlacementResult.Fail(PlacementFailure.OutsideZone, "This area forbids building.", pose);
                    continue;
                }
                if (!string.IsNullOrEmpty(definition.ZoneId) && zone.ZoneId != definition.ZoneId) continue;
                bool fits = true;
                for (int i = 0; i < definition.VolumeCount; i++)
                    if (!zone.Contains(BuildingGeometry.Transform(definition.GetVolume(i), pose, request.Scale))) { fits = false; break; }
                if (fits && zone.Allows(actor)) contained = true;
            }
            if (!contained) return PlacementResult.Fail(PlacementFailure.OutsideZone, "The whole building must fit inside a matching zone that you may use.", pose);
            if (definition.CheckObstacles)
            {
                var filter = new BuildingCollisionFilter(ignoredRoots);
                for (int i = 0; i < definition.VolumeCount; i++)
                {
                    var box = BuildingGeometry.Transform(definition.GetVolume(i), pose, request.Scale);
                    if (physics.HasObstacle(box, definition.ObstacleLayers, filter) || OverlapsBuilding(box, existing))
                        return PlacementResult.Fail(PlacementFailure.Obstructed, "Another object or reserved building footprint overlaps this position.", pose);
                }
                if (definition.Clearances != null) foreach (var volume in definition.Clearances)
                {
                    var box = BuildingGeometry.Transform(volume, pose, request.Scale);
                    if (physics.HasObstacle(box, definition.ObstacleLayers, filter) || OverlapsBuilding(box, existing, true))
                        return PlacementResult.Fail(PlacementFailure.Obstructed, "Keep the building's service space or doorway clear.", pose);
                }
            }
            var context = new BuildingContext(this, definition, request, pose, existing, actor, action);
            var ruleResult = CheckRules(context);
            if (ruleResult != null) return PlacementResult.Fail(PlacementFailure.RuleRejected, ruleResult, pose);
            if (checkCost && definition.Payment == BuildingPayment.Paid && !(definition.WorkRequired > 0 && definition.ConstructionPayment == BuildingConstructionPayment.Delivery))
            {
                quoteCharges.Clear(); AddPrice(quoteCharges, definition, request.Scale, actor, action);
                if (!CanPay(new BuildingBill(quoteCharges, emptyCosts, actor, action), definition, out var failure, out error)) return PlacementResult.Fail(failure, error, pose);
            }
            return new PlacementResult(PlacementFailure.None, "Ready to build", pose, target);
        }

        private string CheckRules(in BuildingContext context)
        {
            if (Rules != null) foreach (var rule in Rules)
            {
                if (rule == null) return "Assign every world rule or remove its empty entry.";
                if ((rule.Actions & context.Action) != 0 && !rule.Allows(context, out var reason)) return Useful(reason, "A world rule rejected this operation.");
            }
            if (context.Definition.Rules != null) foreach (var rule in context.Definition.Rules)
                if ((rule.Actions & context.Action) != 0 && !rule.Allows(context, out var reason)) return Useful(reason, "A building rule rejected this operation.");
            foreach (var rule in runtimeRules) if (!rule.Allows(context, out var reason)) return Useful(reason, "A custom rule rejected this operation.");
            return null;
        }
        private static string Useful(string reason, string fallback) => string.IsNullOrWhiteSpace(reason) ? fallback : reason;
        internal bool CanEdit(PlacedBuilding building, IBuildingActor actor)
        {
            if (building == null || building.System != this) return false;
            if (actor == null || actor.IsAdministrator || building.Definition.Access == BuildingAccess.Everyone) return true;
            return building.Definition.Access == BuildingAccess.Owner ? building.OwnerId == actor.Id : !string.IsNullOrEmpty(building.TeamId) && building.TeamId == actor.TeamId;
        }
        private static bool ValidScale(Vector3 scale) => BuildingMath.Finite(scale) && scale.x > 0.00001f && scale.y > 0.00001f && scale.z > 0.00001f;
        private static BuildingSpace ResolveSpace(PlacementRequest request, PlacedBuilding existing = null)
            => request.Space != null ? request.Space : request.Socket != null ? request.Socket.Space : existing != null ? existing.Space : null;
        internal bool Accepts(BuildingSocket socket) => socket.Building != null ? socket.Building.System == this : Accepts(socket.gameObject, socket.Space);
        internal bool Accepts(BuildingZone zone) => Accepts(zone.gameObject, zone.Space);
        private bool Accepts(GameObject item, BuildingSpace space) => space != null && space.System != null ? space.System == this : item.scene == gameObject.scene;
        private bool NativeQueries => Application.isPlaying && UseSpatialQueries;
        private void GetZones(Vector3 point, float radius, List<BuildingZone> output)
        {
            if (NativeQueries) spatial.Around(point, radius, output);
            else { output.Clear(); output.AddRange(BuildingZone.Active); }
        }
        private bool IntersectsZone(BuildingDefinition definition, Pose pose, Vector3 scale, BuildingZone zone)
        {
            var zoneBox = new BuildingBox(zone.transform.TransformPoint(zone.Center), Vector3.Scale(zone.Size, BuildingGeometry.Abs(zone.transform.lossyScale)) * 0.5f, zone.transform.rotation);
            for (int i = 0; i < definition.VolumeCount; i++)
                if (BuildingGeometry.Intersects(BuildingGeometry.Transform(definition.GetVolume(i), pose, scale), zoneBox)) return true;
            return false;
        }
        private bool OverlapsBuilding(in BuildingBox box, PlacedBuilding existing, bool clearance = false)
        {
            GetNearbyBuildings(box.Center, box.HalfSize.magnitude, nearbyBuildings);
            for (int i = 0; i < nearbyBuildings.Count; i++)
            {
                var building = nearbyBuildings[i];
                if (building == null || building == existing || ignoredRoots.Contains(building.transform)) continue;
                for (int v = 0; v < building.Volumes.Length; v++)
                    if (BuildingGeometry.Intersects(box, building.GetVolume(v), 0.01f)) return true;
                if (!clearance) foreach (var reservedSpace in building.Clearances)
                    if (BuildingGeometry.Intersects(box, BuildingGeometry.Transform(reservedSpace, new Pose(building.transform.position, building.transform.rotation), building.transform.lossyScale), 0.01f)) return true;
            }
            return false;
        }
        private void GetNearbyBuildings(Vector3 point, float radius, List<PlacedBuilding> output)
        {
            if (NativeQueries)
            {
                spatial.Around(point, radius, output);
                for (int i = output.Count - 1; i >= 0; i--) if (output[i].System != this) output.RemoveAt(i);
                foreach (var item in inactiveBuildings) if (item != null && !output.Contains(item)) output.Add(item);
            }
            else { output.Clear(); foreach (var item in buildings.Values) if (item != null) output.Add(item); }
        }
        public int QueryBuildings(Vector3 point, float radius, List<PlacedBuilding> output, string tag = null)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear(); if (!BuildingMath.Finite(point) || !BuildingMath.Finite(radius) || radius < 0) return 0;
            EnsureServices(); Synchronize(); GetNearbyBuildings(point, radius, output);
            for (int i = output.Count - 1; i >= 0; i--)
                if ((output[i].WorldBounds.ClosestPoint(point) - point).sqrMagnitude > radius * radius || !string.IsNullOrEmpty(tag) && !output[i].Definition.HasTag(tag)) output.RemoveAt(i);
            return output.Count;
        }
        public void GetBuildings(List<PlacedBuilding> output)
        { if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear(); foreach (var item in buildings.Values) if (item != null) output.Add(item); }
        public bool TryGet(string instanceId, out PlacedBuilding building)
        { building = null; return instanceId != null && buildings.TryGetValue(instanceId, out building) && building != null; }
        public BuildingSocket FindSocket(Vector3 point, float radius = 2) => FindSocket(Selected, point, radius);
        public BuildingSocket FindSocket(BuildingDefinition definition, Vector3 point, float radius = 2)
        {
            using var marker = SocketMarker.Auto();
            if (definition == null || !BuildingMath.Finite(point) || !BuildingMath.Finite(radius) || radius <= 0) return null;
            EnsureServices(); Synchronize();
            if (NativeQueries) spatial.Around(point, radius, nearbySockets);
            else { nearbySockets.Clear(); nearbySockets.AddRange(BuildingSocket.Active); }
            BuildingSocket candidate = null; float best = radius * radius;
            for (int i = 0; i < nearbySockets.Count; i++)
            {
                var socket = nearbySockets[i];
                if (socket == null || !socket.isActiveAndEnabled || !Accepts(socket) || socket.SocketType != definition.SocketType) continue;
                float distance = (socket.Anchor.position - point).sqrMagnitude;
                if (distance < best || distance == best && (candidate == null || string.CompareOrdinal(socket.Id, candidate.Id) < 0))
                { best = distance; candidate = socket; }
            }
            return candidate;
        }

        private sealed class PrefabInfo
        {
            internal GameObject Prefab;
            internal string Error;
            internal readonly Dictionary<string, BuildingSocket> Sockets = new Dictionary<string, BuildingSocket>(StringComparer.Ordinal);
        }
        private PrefabInfo GetPrefabInfo(BuildingDefinition definition)
        {
            if (prefabInfo.TryGetValue(definition, out var cached) && cached.Prefab == definition.Prefab) return cached;
            var info = new PrefabInfo { Prefab = definition.Prefab };
            if (definition.Prefab.GetComponentInChildren<BuildingSpace>(true) != null || definition.Prefab.GetComponentInChildren<BuildingSystem>(true) != null)
                info.Error = "Keep Building System and Building Space on persistent scene/platform roots outside building prefabs. Buildings may reference those spaces.";
            var authoredInstances = definition.Prefab.GetComponentsInChildren<PlacedBuilding>(true);
            if (authoredInstances.Length > 1 || authoredInstances.Length == 1 && authoredInstances[0].gameObject != definition.Prefab)
                info.Error = "A prefab may contain one Placed Building on its root. Compose multiple buildings with a blueprint instead of nested Placed Building components.";
            foreach (var socket in definition.Prefab.GetComponentsInChildren<BuildingSocket>(true))
                if (string.IsNullOrWhiteSpace(socket.Id) || !info.Sockets.TryAdd(socket.Id, socket))
                    info.Error = "Prefab sockets need distinct nonempty IDs. Use Regenerate Socket ID on the duplicate.";
            prefabInfo[definition] = info;
            return info;
        }
        internal void NotifyVisibility(PlacedBuilding building, bool enabled)
        {
            if (enabled) inactiveBuildings.Remove(building);
            else if (building.InstanceId != null && buildings.TryGetValue(building.InstanceId, out var stored) && stored == building) inactiveBuildings.Add(building);
        }
    }
}
