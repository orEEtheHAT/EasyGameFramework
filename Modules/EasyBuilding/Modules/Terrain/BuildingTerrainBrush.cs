using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding.TerrainEditing
{
    public enum BuildingTerrainMode { RaiseLower, Flatten, Smooth }
    [Serializable]
    public sealed class BuildingTerrainState
    {
        public int Version = 1;
        public int Resolution;
        public Vector3 Size;
        public float[] Heights;
    }
    /// <summary>Optional height editing on an owned runtime TerrainData copy. Terrain transforms must use identity rotation/scale.</summary>
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.Terrain)), AddComponentMenu("EasyGameFramework/Building/Terrain/Terrain Brush")]
    public sealed class BuildingTerrainBrush : MonoBehaviour
    {
        public UnityEngine.Terrain Target;
        [Min(1)] public int MaximumBrushSamples = 262144;
        [Min(1)] public int MaximumSaveSamples = 4198401;
        private TerrainData original, owned, originalColliderData;
        private UnityEngine.Terrain ownedTarget;
        private TerrainCollider targetCollider;
        private static readonly Dictionary<UnityEngine.Terrain, BuildingTerrainBrush> Owners = new Dictionary<UnityEngine.Terrain, BuildingTerrainBrush>();
        public long Revision { get; private set; }
        public event Action<RectInt> Changed;
        internal TerrainData Data => owned;
        public bool TryPrepareCircle(Vector3 center, float radius, BuildingTerrainMode mode, float amount, out BuildingTerrainPatch patch, out string reason, float? targetWorldHeight = null)
        {
            patch = null; reason = null;
            if (!Finite(center) || !Finite(radius) || radius <= 0 || !Finite(amount) || mode < BuildingTerrainMode.RaiseLower || mode > BuildingTerrainMode.Smooth ||
                mode != BuildingTerrainMode.RaiseLower && (amount < 0 || amount > 1) || targetWorldHeight.HasValue && !Finite(targetWorldHeight.Value))
            { reason = "Use finite brush values; radius must be positive and flatten/smooth strength must be between 0 and 1."; return false; }
            if (!EnsureData(out reason)) return false;
            var local = center - Target.transform.position; var size = owned.size; int resolution = owned.heightmapResolution;
            if (local.x + radius < 0 || local.z + radius < 0 || local.x - radius > size.x || local.z - radius > size.z)
            { reason = "The brush does not intersect this terrain."; return false; }
            int x0 = Mathf.FloorToInt(Mathf.Clamp01((local.x - radius) / size.x) * (resolution - 1));
            int z0 = Mathf.FloorToInt(Mathf.Clamp01((local.z - radius) / size.z) * (resolution - 1));
            int x1 = Mathf.CeilToInt(Mathf.Clamp01((local.x + radius) / size.x) * (resolution - 1));
            int z1 = Mathf.CeilToInt(Mathf.Clamp01((local.z + radius) / size.z) * (resolution - 1));
            int width = x1 - x0 + 1, height = z1 - z0 + 1;
            if ((long)width * height > MaximumBrushSamples) { reason = "Brush exceeds Maximum Brush Samples. Use smaller strokes."; return false; }
            int readX = Mathf.Max(0, x0 - 1), readZ = Mathf.Max(0, z0 - 1);
            int readWidth = Mathf.Min(resolution - 1, x1 + 1) - readX + 1, readHeight = Mathf.Min(resolution - 1, z1 + 1) - readZ + 1;
            var source = owned.GetHeights(readX, readZ, readWidth, readHeight); var before = new float[height, width]; var after = new float[height, width];
            float target = Mathf.Clamp01(((targetWorldHeight ?? center.y) - Target.transform.position.y) / size.y);
            for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
            {
                int sx = x0 + x - readX, sz = z0 + z - readZ; float value = source[sz, sx]; before[z, x] = after[z, x] = value;
                float dx = (x0 + x) * size.x / (resolution - 1) - local.x, dz = (z0 + z) * size.z / (resolution - 1) - local.z;
                float distance = Mathf.Sqrt(dx * dx + dz * dz); if (distance > radius) continue;
                float falloff = 1 - Mathf.SmoothStep(0, 1, distance / radius);
                if (mode == BuildingTerrainMode.RaiseLower) value += amount / size.y * falloff;
                else if (mode == BuildingTerrainMode.Flatten) value = Mathf.Lerp(value, target, amount * falloff);
                else
                {
                    float sum = 0;
                    for (int nz = -1; nz <= 1; nz++) for (int nx = -1; nx <= 1; nx++) sum += source[Mathf.Clamp(sz + nz, 0, readHeight - 1), Mathf.Clamp(sx + nx, 0, readWidth - 1)];
                    value = Mathf.Lerp(value, sum / 9, amount * falloff);
                }
                after[z, x] = Mathf.Clamp01(value);
            }
            patch = new BuildingTerrainPatch(this, owned, new RectInt(x0, z0, width, height), before, after); return true;
        }
        public BuildingTerrainState CaptureState()
        {
            if (!EnsureData(out var reason)) throw new InvalidOperationException(reason);
            int resolution = owned.heightmapResolution; if ((long)resolution * resolution > MaximumSaveSamples) throw new InvalidOperationException("Terrain exceeds Maximum Save Samples. Save smaller terrain tiles.");
            var source = owned.GetHeights(0, 0, resolution, resolution); var flat = new float[resolution * resolution];
            Buffer.BlockCopy(source, 0, flat, 0, flat.Length * sizeof(float));
            return new BuildingTerrainState { Resolution = resolution, Size = owned.size, Heights = flat };
        }
        public bool TryRestore(BuildingTerrainState state, out string reason)
        {
            reason = null;
            if (!EnsureData(out reason)) return false;
            int resolution = owned.heightmapResolution;
            if (state == null || state.Version != 1 || state.Resolution != resolution || state.Size != owned.size || state.Heights == null ||
                (long)resolution * resolution > MaximumSaveSamples || state.Heights.Length != (long)resolution * resolution)
            { reason = "Saved terrain resolution, size or sample budget does not match this tile."; return false; }
            foreach (var value in state.Heights) if (!Finite(value) || value < 0 || value > 1) { reason = "Saved terrain heights must be finite and normalized."; return false; }
            var heights = new float[resolution, resolution]; Buffer.BlockCopy(state.Heights, 0, heights, 0, state.Heights.Length * sizeof(float));
            owned.SetHeightsDelayLOD(0, 0, heights); owned.SyncHeightmap(); Publish(new RectInt(0, 0, resolution, resolution)); return true;
        }
        public void Flush() { if (owned != null) owned.SyncHeightmap(); }
        private bool EnsureData(out string reason)
        {
            reason = null;
            if (!Application.isPlaying) { reason = "Run terrain brush operations in Play Mode."; return false; }
            if (Target == null) Target = GetComponent<UnityEngine.Terrain>();
            if (Target == null || Target.terrainData == null || !Finite(Target.terrainData.size) || !Finite(Target.transform.position) ||
                Target.terrainData.size.x <= 0 || Target.terrainData.size.y <= 0 || Target.terrainData.size.z <= 0) { reason = "Assign a Terrain with valid TerrainData."; return false; }
            if (Quaternion.Angle(Target.transform.rotation, Quaternion.identity) > 0.001f || (Target.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            { reason = "Use identity terrain rotation/scale; translate the terrain or its parent for origin shifts."; return false; }
            if (owned != null) { if (Target == ownedTarget && Target.terrainData == owned) return true; reason = "Terrain or TerrainData was replaced while this brush owned its runtime copy. Disable the brush before rebinding it."; return false; }
            if (Owners.TryGetValue(Target, out var existing) && existing != null && existing != this) { reason = "Use one terrain brush owner per Terrain."; return false; }
            ownedTarget = Target; original = Target.terrainData; owned = Instantiate(original); owned.name = original.name + " (EasyBuilding runtime)"; owned.hideFlags = HideFlags.DontSave;
            targetCollider = Target.GetComponent<TerrainCollider>(); originalColliderData = targetCollider != null ? targetCollider.terrainData : null;
            Target.terrainData = owned; if (targetCollider != null) targetCollider.terrainData = owned; Owners[Target] = this; return true;
        }
        internal void Publish(RectInt area)
        {
            Revision++;
            if (Changed != null) foreach (Action<RectInt> handler in Changed.GetInvocationList()) try { handler(area); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
        private void OnDisable()
        {
            if (ownedTarget != null && Owners.TryGetValue(ownedTarget, out var owner) && owner == this) Owners.Remove(ownedTarget);
            if (ownedTarget != null && ownedTarget.terrainData == owned) ownedTarget.terrainData = original;
            if (targetCollider != null && targetCollider.terrainData == owned) targetCollider.terrainData = originalColliderData;
            if (owned != null) { if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned); }
            owned = null; original = null; originalColliderData = null; targetCollider = null; ownedTarget = null;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
    public sealed class BuildingTerrainPatch
    {
        private readonly BuildingTerrainBrush owner;
        private readonly TerrainData data;
        private readonly float[,] before;
        private float[,] after;
        public RectInt Area { get; }
        public bool IsApplied { get; private set; }
        internal BuildingTerrainPatch(BuildingTerrainBrush owner, TerrainData data, RectInt area, float[,] before, float[,] after)
        { this.owner = owner; this.data = data; Area = area; this.before = before; this.after = after; }
        public bool TryApply(out string reason, bool synchronize = true) => Change(true, synchronize, out reason);
        public bool TryUndo(out string reason, bool synchronize = true) => Change(false, synchronize, out reason);
        private bool Change(bool apply, bool synchronize, out string reason)
        {
            reason = null;
            if (owner == null || data == null || owner.Data != data || IsApplied == apply) { reason = "This terrain edit is already applied/undone or its runtime terrain no longer exists."; return false; }
            var expected = apply ? before : after; var current = data.GetHeights(Area.x, Area.y, Area.width, Area.height);
            for (int z = 0; z < Area.height; z++) for (int x = 0; x < Area.width; x++)
                if (current[z, x] != expected[z, x]) { reason = "This terrain region changed after the edit was prepared. Prepare a new stroke."; return false; }
            data.SetHeightsDelayLOD(Area.x, Area.y, apply ? after : before); if (synchronize) data.SyncHeightmap();
            if (apply) after = data.GetHeights(Area.x, Area.y, Area.width, Area.height); // Retain Unity's quantized samples for conflict-safe undo.
            IsApplied = apply; owner.Publish(Area); return true;
        }
    }
}
