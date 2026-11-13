using System;
using System.Collections.Generic;
using System.Diagnostics;
using EasyBuilding.Cells;
using UnityEngine;
using UnityEngine.Rendering;

namespace EasyBuilding.Voxels
{
    /// <summary>One visible-face mesh per occupied chunk; no GameObject per voxel. Mesh and collider rebuilding share a frame budget.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BuildingCellWorld)), AddComponentMenu("EasyGameFramework/Building/Voxels/Voxel Renderer")]
    public sealed class BuildingVoxelRenderer : MonoBehaviour
    {
        public BuildingCellWorld World;
        [Tooltip("Use a material/shader that reads vertex colors for palette colors.")] public Material Material;
        [Min(0.001f)] public float CellSize = 1;
        public bool CreateColliders = true;
        [Min(1)] public int ChunksPerFrame = 2;
        [Min(0.1f)] public float MillisecondsPerFrame = 2;
        private sealed class View { internal GameObject Root; internal Mesh Mesh; internal MeshCollider Collider; internal MeshRenderer Renderer; }
        private readonly Dictionary<Vector3Int, View> views = new Dictionary<Vector3Int, View>();
        private readonly HashSet<Vector3Int> dirty = new HashSet<Vector3Int>();
        private readonly Queue<Vector3Int> queue = new Queue<Vector3Int>();
        private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Vector3Int> keys = new List<Vector3Int>();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly ushort[] cells = new ushort[BuildingCellWorld.ChunkVolume];
        private readonly ushort[][] neighbours = {
            new ushort[BuildingCellWorld.ChunkVolume], new ushort[BuildingCellWorld.ChunkVolume], new ushort[BuildingCellWorld.ChunkVolume],
            new ushort[BuildingCellWorld.ChunkVolume], new ushort[BuildingCellWorld.ChunkVolume], new ushort[BuildingCellWorld.ChunkVolume]
        };
        private BuildingCellWorld subscribed;
        private Material defaultMaterial;
        public int PendingChunks => queue.Count;
        public int MeshCount => views.Count;
        private static readonly Vector3Int[] Directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
        private static readonly Vector3[] Tangents = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
        private static readonly Vector3[] Bitangents = { Vector3.down, Vector3.down, Vector3.back, Vector3.forward, Vector3.down, Vector3.down };
        private void OnEnable()
        {
            if (World == null) World = GetComponent<BuildingCellWorld>();
            subscribed = World; if (subscribed == null) return;
            if (Material == null) defaultMaterial = new Material(Resources.Load<Shader>("EasyBuildingCellColors")) { hideFlags = HideFlags.HideAndDontSave };
            subscribed.Changed += Changed; subscribed.Restored += RebuildAll; RebuildAll();
        }
        private void OnDisable()
        {
            if (subscribed != null) { subscribed.Changed -= Changed; subscribed.Restored -= RebuildAll; } subscribed = null;
            foreach (var view in views.Values) Dispose(view); views.Clear(); dirty.Clear(); queue.Clear();
            DestroyOwned(defaultMaterial); defaultMaterial = null;
        }
        private static void DestroyOwned(UnityEngine.Object value) { if (value != null) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); } }
        private static void Dispose(View view) { DestroyOwned(view.Root); DestroyOwned(view.Mesh); }
        private void Mark(Vector3Int key) { if (dirty.Add(key)) queue.Enqueue(key); }
        private void Changed(IReadOnlyList<BuildingCellEdit> edits)
        {
            foreach (var edit in edits)
            {
                Mark(BuildingCellWorld.ChunkOf(edit.Position));
                foreach (var direction in Directions) Mark(BuildingCellWorld.ChunkOf(edit.Position + direction));
            }
        }
        public void RebuildAll()
        {
            foreach (var key in views.Keys) Mark(key);
            if (World != null) { World.GetChunks(keys); foreach (var key in keys) Mark(key); }
        }
        private void LateUpdate() => Tick(ChunksPerFrame, MillisecondsPerFrame);
        public int Tick(int maximumChunks = 2, double milliseconds = 2)
        {
            if (World == null || World.Palette == null || !float.IsFinite(CellSize) || CellSize <= 0 || maximumChunks < 1 || milliseconds <= 0 || double.IsNaN(milliseconds)) return 0;
            int built = 0; clock.Restart();
            while (queue.Count != 0 && built < maximumChunks && clock.Elapsed.TotalMilliseconds < milliseconds)
            { var key = queue.Dequeue(); dirty.Remove(key); Build(key); built++; }
            clock.Stop(); return built;
        }
        private void Build(Vector3Int key)
        {
            if (!World.CopyChunk(key, cells)) { if (views.Remove(key, out var missing)) Dispose(missing); return; }
            for (int face = 0; face < 6; face++) World.CopyChunk(key + Directions[face], neighbours[face]);
            vertices.Clear(); normals.Clear(); colors.Clear(); triangles.Clear(); uvs.Clear();
            for (int i = 0; i < BuildingCellWorld.ChunkVolume; i++)
            {
                int value = cells[i]; if (value == 0) continue;
                var cell = new Vector3(i & 15, (i >> 4) & 15, i >> 8);
                Color color = World.Palette.Types[value - 1].Color;
                for (int face = 0; face < 6; face++)
                {
                    if (Neighbour(i, face) != 0) continue;
                    Vector3 normal = Directions[face], u = Tangents[face], v = Bitangents[face];
                    Vector3 center = (cell + Vector3.one * 0.5f + normal * 0.5f) * CellSize;
                    int start = vertices.Count;
                    vertices.Add(center + (-u - v) * (CellSize * 0.5f)); vertices.Add(center + (u - v) * (CellSize * 0.5f));
                    vertices.Add(center + (u + v) * (CellSize * 0.5f)); vertices.Add(center + (-u + v) * (CellSize * 0.5f));
                    for (int c = 0; c < 4; c++) { normals.Add(normal); colors.Add(color); }
                    uvs.Add(Vector2.zero); uvs.Add(Vector2.right); uvs.Add(Vector2.one); uvs.Add(Vector2.up);
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
                }
            }
            if (vertices.Count == 0) { if (views.Remove(key, out var empty)) Dispose(empty); return; }
            if (!views.TryGetValue(key, out var view))
            {
                var root = new GameObject("Voxel chunk " + key) { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(transform, false); root.layer = gameObject.layer;
                view = new View { Root = root, Mesh = new Mesh { name = "Voxel chunk mesh", indexFormat = IndexFormat.UInt32 }, Renderer = root.AddComponent<MeshRenderer>() };
                root.AddComponent<MeshFilter>().sharedMesh = view.Mesh; views.Add(key, view);
            }
            view.Root.transform.localPosition = (Vector3)(key * 16) * CellSize; view.Renderer.sharedMaterial = Material != null ? Material : defaultMaterial;
            if (view.Collider != null) view.Collider.sharedMesh = null;
            var mesh = view.Mesh; mesh.Clear(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            if (CreateColliders)
            { if (view.Collider == null) view.Collider = view.Root.AddComponent<MeshCollider>(); view.Collider.enabled = true; view.Collider.sharedMesh = mesh; }
            else if (view.Collider != null) view.Collider.enabled = false;
        }
        private int Neighbour(int index, int face)
        {
            switch (face)
            {
                case 0: return (index & 15) != 15 ? cells[index + 1] : neighbours[0][index - 15];
                case 1: return (index & 15) != 0 ? cells[index - 1] : neighbours[1][index + 15];
                case 2: return ((index >> 4) & 15) != 15 ? cells[index + 16] : neighbours[2][index - 240];
                case 3: return ((index >> 4) & 15) != 0 ? cells[index - 16] : neighbours[3][index + 240];
                case 4: return (index >> 8) != 15 ? cells[index + 256] : neighbours[4][index - 3840];
                default: return (index >> 8) != 0 ? cells[index - 256] : neighbours[5][index + 3840];
            }
        }
        public Vector3Int WorldToCell(Vector3 point) => Vector3Int.FloorToInt(transform.InverseTransformPoint(point) / CellSize);
    }
}
