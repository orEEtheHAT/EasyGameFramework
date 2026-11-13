using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyBuilding.Paths
{
    public enum BuildingPathShape { Road, Pipe }
    [Serializable] public sealed class BuildingPathSettings
    {
        public BuildingPathShape Shape;
        [Min(0.001f)] public float Width = 1;
        public bool Smooth;
        [Range(1, 64)] public int SamplesPerSegment = 8;
        [Range(3, 32)] public int PipeSides = 8;
        public Vector3 Up = Vector3.up;
        [Min(0.001f)] public float UvLength = 1;
    }
    /// <summary>Bounded polyline/Catmull-Rom road or pipe mesh. Points are local; pipe frames use parallel transport.</summary>
    public static class BuildingPathGeometry
    {
        public static bool TryCreate(IReadOnlyList<Vector3> points, BuildingPathSettings settings, out Mesh mesh, out float length, out string reason)
        {
            mesh = null; length = 0; reason = null;
            if (points == null || points.Count < 2 || points.Count > 1024 || settings == null || !float.IsFinite(settings.Width) || settings.Width <= 0 ||
                !float.IsFinite(settings.UvLength) || settings.UvLength <= 0 || !Finite(settings.Up) || settings.Up.sqrMagnitude < 0.0001f ||
                settings.SamplesPerSegment < 1 || settings.SamplesPerSegment > 64 || settings.PipeSides < 3 || settings.PipeSides > 32 ||
                settings.Shape < BuildingPathShape.Road || settings.Shape > BuildingPathShape.Pipe)
            { reason = "Use 2–1024 finite points, positive width/UV length, 1–64 samples, 3–32 pipe sides and a nonzero up vector."; return false; }
            int steps = settings.Smooth ? settings.SamplesPerSegment : 1, count = (points.Count - 1) * steps + 1;
            if (count > 4096) { reason = "Path exceeds 4096 samples. Split it into shorter segments."; return false; }
            for (int i = 0; i < points.Count; i++) if (!Finite(points[i]) || i > 0 && (points[i] - points[i - 1]).sqrMagnitude < 0.00000001f)
            { reason = "Path points must be finite and consecutive points must differ."; return false; }
            var samples = new Vector3[count]; var distances = new float[count];
            for (int i = 0; i < count; i++)
            {
                int segment = Mathf.Min(i / steps, points.Count - 2); float t = (i - segment * steps) / (float)steps;
                Vector3 b = points[segment], c = points[segment + 1];
                if (!settings.Smooth) samples[i] = Vector3.LerpUnclamped(b, c, t);
                else
                {
                    Vector3 a = segment > 0 ? points[segment - 1] : b * 2 - c;
                    Vector3 d = segment + 2 < points.Count ? points[segment + 2] : c * 2 - b;
                    samples[i] = 0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
                }
                if (i > 0) { length += Vector3.Distance(samples[i - 1], samples[i]); distances[i] = length; }
            }
            if (!float.IsFinite(length) || length <= 0) { reason = "Path length is invalid."; return false; }
            int ring = settings.Shape == BuildingPathShape.Road ? 2 : settings.PipeSides + 1;
            var vertices = new Vector3[count * ring]; var normals = new Vector3[vertices.Length]; var uv = new Vector2[vertices.Length];
            var triangles = new int[(count - 1) * (ring - 1) * 6];
            Vector3 previous = (samples[1] - samples[0]).normalized, up = settings.Up.normalized;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = i == count - 1 ? samples[i] - samples[i - 1] : i == 0 ? samples[1] - samples[0] : samples[i + 1] - samples[i - 1];
                if (tangent.sqrMagnitude < 0.00000001f) { reason = "The path doubles back through a singular tangent. Split that junction."; return false; }
                tangent.Normalize(); if (i != 0) up = Quaternion.FromToRotation(previous, tangent) * up;
                up = Vector3.ProjectOnPlane(up, tangent);
                if (up.sqrMagnitude < 0.0001f) up = Vector3.ProjectOnPlane(Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right, tangent);
                up.Normalize(); Vector3 right = Vector3.Cross(up, tangent).normalized; previous = tangent;
                for (int j = 0; j < ring; j++)
                {
                    int index = i * ring + j; float across = j / (float)(ring - 1);
                    Vector3 radial = settings.Shape == BuildingPathShape.Road ? right * (across * 2 - 1) : right * Mathf.Cos(across * Mathf.PI * 2) + up * Mathf.Sin(across * Mathf.PI * 2);
                    vertices[index] = samples[i] + radial * settings.Width * 0.5f;
                    normals[index] = settings.Shape == BuildingPathShape.Road ? up : radial;
                    uv[index] = new Vector2(across, distances[i] / settings.UvLength);
                    if (i == count - 1 || j == ring - 1) continue;
                    int k = (i * (ring - 1) + j) * 6, a = index, b = index + 1, c = index + ring, d = c + 1;
                    triangles[k] = a; triangles[k + 1] = c; triangles[k + 2] = b;
                    triangles[k + 3] = b; triangles[k + 4] = c; triangles[k + 5] = d;
                    if (settings.Shape == BuildingPathShape.Pipe)
                    { triangles[k + 1] = b; triangles[k + 2] = c; triangles[k + 4] = d; triangles[k + 5] = c; }
                }
            }
            mesh = new Mesh { name = "EasyBuilding path", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateBounds(); return true;
        }
        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
    [DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer)), AddComponentMenu("EasyGameFramework/Building/Paths/Path Mesh")]
    public sealed class BuildingPathMesh : MonoBehaviour, IBuildingStatePart
    {
        public BuildingPathSettings Settings = new BuildingPathSettings();
        [SerializeField] private Vector3[] points = { Vector3.zero, Vector3.forward * 4 };
        public bool CreateCollider = true;
        [Tooltip("Charged per unit of the new path's complete length. Empty allows free reshaping.")] public BuildingCost[] CostPerUnit = Array.Empty<BuildingCost>();
        private Mesh owned;
        private MeshFilter filter;
        private MeshCollider meshCollider;
        private long version;
        public float Length { get; private set; }
        public string StateKey => "path";
        public int StateVersion => 1;
        public bool ReplicateState => true;
        [Serializable] private sealed class Data { public Vector3[] points; }
        private void Awake() { if (owned == null && BuildingPathGeometry.TryCreate(points, Settings, out var mesh, out float length, out _)) Apply(points, mesh, length); }
        private void OnDestroy() => DestroyMesh(owned);
        private static void DestroyMesh(Mesh mesh) { if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
        public string CaptureBuildingState() => JsonUtility.ToJson(new Data { points = points });
        public bool CanRestoreBuildingState(int stateVersion, string json, out string reason)
        {
            reason = null;
            try
            {
                if (stateVersion != 1) { reason = "Unsupported path state version."; return false; }
                bool valid = BuildingPathGeometry.TryCreate(JsonUtility.FromJson<Data>(json).points, Settings, out var mesh, out _, out reason);
                if (valid) valid = FitsFootprint(mesh.bounds, out reason); DestroyMesh(mesh); return valid;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
        }
        public void RestoreBuildingState(int stateVersion, string json)
        {
            var data = JsonUtility.FromJson<Data>(json);
            if (!BuildingPathGeometry.TryCreate(data.points, Settings, out var mesh, out float length, out var reason)) throw new InvalidOperationException(reason);
            Apply(data.points, mesh, length);
        }
        public bool TrySetPoints(Vector3[] value, out string reason, IBuildingActor actor = null)
        {
            reason = null; if (!BuildingPathGeometry.TryCreate(value, Settings, out var mesh, out float length, out reason)) return false;
            bool applied = false;
            try
            {
                if (!FitsFootprint(mesh.bounds, out reason)) return false;
                var copied = (Vector3[])value.Clone(); var building = GetComponent<PlacedBuilding>();
                if (building == null || building.System == null) { Apply(copied, mesh, length); applied = true; return true; }
                var costs = new BuildingCost[CostPerUnit?.Length ?? 0];
                for (int i = 0; i < costs.Length; i++) costs[i] = new BuildingCost(CostPerUnit[i].ResourceId, checked((long)Math.Ceiling((decimal)CostPerUnit[i].Amount * (decimal)length)));
                var mutation = new Change(this, copied, mesh, length, costs);
                applied = building.System.TryApplyMutation(building, mutation, out var result, actor); reason = applied ? null : result.Reason; return applied;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
            finally { if (!applied) DestroyMesh(mesh); }
        }
        private bool FitsFootprint(Bounds bounds, out string reason)
        {
            reason = null; var building = GetComponent<PlacedBuilding>(); if (building == null || building.Definition == null) return true;
            // Keep the procedural geometry within the already validated prefab footprint.
            var definition = building.Definition;
            for (int v = 0; v < definition.VolumeCount; v++)
            {
                var box = BuildingGeometry.Transform(definition.GetVolume(v), new Pose(Vector3.zero, Quaternion.identity), Vector3.one);
                bool fits = true;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = BuildingGeometry.Abs(Quaternion.Inverse(box.Rotation) * (p - box.Center));
                    if (local.x > box.HalfSize.x + 0.0001f || local.y > box.HalfSize.y + 0.0001f || local.z > box.HalfSize.z + 0.0001f) { fits = false; break; }
                }
                if (fits) return true;
            }
            reason = "Path exceeds the definition's placement footprint. Use a larger authored footprint or another path segment."; return false;
        }
        private void Apply(Vector3[] value, Mesh mesh, float length)
        {
            if (filter == null) filter = GetComponent<MeshFilter>();
            if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
            if (CreateCollider && meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
            if (meshCollider != null) { meshCollider.sharedMesh = null; meshCollider.enabled = CreateCollider; if (CreateCollider) meshCollider.sharedMesh = mesh; }
            filter.sharedMesh = mesh; DestroyMesh(owned); owned = mesh; points = value; Length = length; version++;
        }
        private sealed class Change : IBuildingPreparedMutation
        {
            private readonly BuildingPathMesh owner; private readonly Vector3[] points; private readonly Mesh mesh; private readonly float length; private readonly long version;
            public IBuildingStatePart Part => owner;
            public BuildingPartState After { get; }
            public BuildingAction Action => BuildingAction.Customize;
            public IReadOnlyList<BuildingCost> Costs { get; }
            public bool AddToPaidReceipt => true;
            internal Change(BuildingPathMesh owner, Vector3[] points, Mesh mesh, float length, BuildingCost[] costs)
            {
                this.owner = owner; this.points = points; this.mesh = mesh; this.length = length; version = owner.version; Costs = costs;
                After = new BuildingPartState { Key = owner.StateKey, Version = 1, Json = JsonUtility.ToJson(new Data { points = points }), Replicate = true };
            }
            public bool CanApply(out string reason) { reason = "The path changed while preparing this edit."; return owner != null && owner.version == version && mesh != null && owner.FitsFootprint(mesh.bounds, out reason); }
            public void Apply() => owner.Apply(points, mesh, length);
        }
    }
}
