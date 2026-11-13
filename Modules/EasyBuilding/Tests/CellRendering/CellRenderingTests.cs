using System.Collections;
using EasyBuilding.Cells;
using EasyBuilding.Voxels;
using EasyBuilding.Tilemaps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests
{
    public sealed class CellRenderingTests
    {
        [UnityTest] public IEnumerator BufferedVoxelFacesMatchIndependentReferenceAcrossAllChunkBoundaries()
        {
            var root = new GameObject("Voxel reference"); var palette = ScriptableObject.CreateInstance<BuildingCellPalette>(); palette.Types = new[] { new BuildingCellType { Id = "stone" } };
            var world = root.AddComponent<BuildingCellWorld>(); world.Palette = palette;
            var renderer = root.AddComponent<BuildingVoxelRenderer>(); renderer.CreateColliders = false;
            var cells = new System.Collections.Generic.HashSet<Vector3Int>(); var random = new System.Random(724);
            for (int i = 0; i < 600; i++) cells.Add(new Vector3Int(random.Next(-18,19), random.Next(-18,19), random.Next(-18,19)));
            var directions = new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
            foreach (var direction in directions) { cells.Add(direction * 15); cells.Add(direction * 16); cells.Add(direction * 17); }
            var edits = new System.Collections.Generic.List<BuildingCellEdit>(); foreach (var cell in cells) edits.Add(new BuildingCellEdit(cell, 1));
            Assert.That(world.TryApply(edits, out _, out var reason), Is.True, reason);
            var expected = new System.Collections.Generic.HashSet<(Vector3Int,Vector3Int)>();
            foreach (var cell in cells) foreach (var direction in directions) if (!cells.Contains(cell + direction)) expected.Add((cell,direction));
            while (renderer.PendingChunks != 0) renderer.Tick(64, 1000);
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var vertices = filter.sharedMesh.vertices; var normals = filter.sharedMesh.normals;
                for (int i = 0; i < vertices.Length; i += 4)
                {
                    Vector3 center = filter.transform.TransformPoint((vertices[i] + vertices[i+1] + vertices[i+2] + vertices[i+3]) * 0.25f);
                    var normal = Vector3Int.RoundToInt(normals[i]); var cell = Vector3Int.FloorToInt(center - (Vector3)normal * 0.01f);
                    Assert.That(expected.Remove((cell,normal)), Is.True, "Unexpected or duplicate voxel face at " + cell + " normal " + normal);
                    Assert.That(Vector3.Dot(Vector3.Cross(vertices[i+1]-vertices[i], vertices[i+2]-vertices[i]), normals[i]), Is.GreaterThan(0));
                }
            }
            Assert.That(expected.Count, Is.Zero); Object.Destroy(root); Object.Destroy(palette); yield return null;
        }
        [UnityTest] public IEnumerator VoxelBoundaryCullsFacesAndRebuildsBothColliderChunks()
        {
            var root = new GameObject("Voxel test"); var palette = ScriptableObject.CreateInstance<BuildingCellPalette>();
            palette.Types = new[] { new BuildingCellType { Id = "stone" } };
            var world = root.AddComponent<BuildingCellWorld>(); world.Palette = palette;
            var renderer = root.AddComponent<BuildingVoxelRenderer>(); renderer.enabled = false; renderer.World = world; renderer.enabled = true;
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(new Vector3Int(15, 0, 0), 1), new BuildingCellEdit(new Vector3Int(16, 0, 0), 1) }, out _, out var reason), Is.True, reason);
            while (renderer.PendingChunks > 0) renderer.Tick(32, 1000);
            Assert.That(renderer.MeshCount, Is.EqualTo(2)); int vertices = 0;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>()) vertices += filter.sharedMesh.vertexCount;
            Assert.That(vertices, Is.EqualTo(40)); Assert.That(root.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(2));
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(new Vector3Int(16, 0, 0), 0) }, out _, out reason), Is.True, reason);
            while (renderer.PendingChunks > 0) renderer.Tick(32, 1000);
            yield return null;
            Assert.That(renderer.MeshCount, Is.EqualTo(1)); Assert.That(root.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(24));
            Physics.SyncTransforms(); Assert.That(Physics.Raycast(new Vector3(15.5f, 3, 0.5f), Vector3.down, out var hit, 5), Is.True); Assert.That(hit.point.y, Is.EqualTo(1).Within(0.01f));
            Object.Destroy(root); Object.Destroy(palette); yield return null;
        }
        [UnityTest] public IEnumerator TilemapSharesCellTransactionsAndUndo()
        {
            var root = new GameObject("Grid", typeof(Grid)); var child = new GameObject("Tiles", typeof(Tilemap)); child.transform.SetParent(root.transform);
            var palette = ScriptableObject.CreateInstance<BuildingCellPalette>(); palette.Types = new[] { new BuildingCellType { Id = "floor" } };
            var tile = ScriptableObject.CreateInstance<Tile>(); var world = root.AddComponent<BuildingCellWorld>(); world.Palette = palette;
            var renderer = child.AddComponent<BuildingTilemapRenderer>(); renderer.enabled = false; renderer.World = world; renderer.Tiles = new[] { new BuildingTileBinding { CellId = "floor", Tile = tile } }; renderer.enabled = true;
            var map = child.GetComponent<Tilemap>(); var cell = new Vector3Int(-2, 4, 0);
            Assert.That(world.TryApply(new[] { new BuildingCellEdit(cell, 1) }, out var stroke, out var reason), Is.True, reason); Assert.That(map.GetTile(cell), Is.SameAs(tile));
            Assert.That(world.TryUndo(stroke, out reason), Is.True, reason); Assert.That(map.GetTile(cell), Is.Null);
            Object.Destroy(root); Object.Destroy(palette); Object.Destroy(tile); yield return null;
        }
    }
}
