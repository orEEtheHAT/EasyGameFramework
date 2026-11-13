using EasyBuilding.TerrainEditing;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyBuilding.Tests.TerrainEditing
{
    public sealed class TerrainTests
    {
        private TerrainData original;
        private GameObject root;
        private BuildingTerrainBrush brush;
        private readonly Vector3 origin = new Vector3(70000, 0, 70000);
        [SetUp] public void Setup()
        {
            original = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 16, 32) };
            root = Terrain.CreateTerrainGameObject(original); root.transform.position = origin; root.layer = 31;
            brush = root.AddComponent<BuildingTerrainBrush>();
        }
        [UnityTearDown] public IEnumerator Teardown() { root.SetActive(false); Object.Destroy(root); Object.Destroy(original); yield return null; }
        [UnityTest] public IEnumerator TerrainEditsAreUndoableAndNeverChangeTheSourceData()
        {
            Assert.That(brush.TryPrepareCircle(origin + new Vector3(16, 4, 16), 4, BuildingTerrainMode.Flatten, 1, out var patch, out var reason), Is.True, reason);
            Assert.That(original.GetHeight(16, 16), Is.Zero);
            Assert.That(patch.TryApply(out reason), Is.True, reason); var terrain = root.GetComponent<Terrain>();
            Assert.That(terrain.terrainData.GetHeight(16, 16), Is.EqualTo(4).Within(0.002f)); Assert.That(original.GetHeight(16, 16), Is.Zero);
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(origin + new Vector3(16, 10, 16), Vector3.down, out var hit, 20, 1 << 31), Is.True);
            Assert.That(hit.point.y, Is.EqualTo(4).Within(0.02f));
            var saved = brush.CaptureState(); Assert.That(patch.TryUndo(out reason), Is.True, reason); Assert.That(terrain.terrainData.GetHeight(16, 16), Is.Zero);
            Assert.That(brush.TryRestore(saved, out reason), Is.True, reason); Assert.That(terrain.terrainData.GetHeight(16, 16), Is.EqualTo(4).Within(0.002f));
            brush.enabled = false; Assert.That(terrain.terrainData, Is.SameAs(original));
            yield return null;
        }
        [UnityTest] public IEnumerator StaleTerrainStrokeCannotOverwriteAnotherEdit()
        {
            var center = origin + new Vector3(16, 0, 16);
            brush.TryPrepareCircle(center, 3, BuildingTerrainMode.RaiseLower, 2, out var first, out _);
            brush.TryPrepareCircle(center, 3, BuildingTerrainMode.RaiseLower, 5, out var stale, out _);
            Assert.That(first.TryApply(out _), Is.True); Assert.That(stale.TryApply(out _), Is.False);
            Assert.That(root.GetComponent<Terrain>().terrainData.GetHeight(16, 16), Is.EqualTo(2).Within(0.002f));
            brush.MaximumBrushSamples = 4; Assert.That(brush.TryPrepareCircle(center, 3, BuildingTerrainMode.Smooth, 1, out _, out _), Is.False);
            yield return null;
        }
    }
}
