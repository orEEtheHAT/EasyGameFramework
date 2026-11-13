using System.Collections.Generic;
using EasyBuilding.TwoD;
using NUnit.Framework;
using UnityEngine;

namespace EasyBuilding.Tests.TwoD
{
    public sealed class Physics2DTests
    {
        private readonly List<Object> objects = new List<Object>();
        private BuildingSystem world;
        private BuildingDefinition definition;
        private readonly Vector3 origin = new Vector3(50000, 0, 0);
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void Setup()
        {
            world = Go("2D world").AddComponent<BuildingSystem>(); var backend = world.gameObject.AddComponent<BuildingPhysics2D>();
            world.PhysicsBackend = backend; world.AddRule(backend);
            var prefab = Go("2D prefab"); prefab.layer = 31; prefab.SetActive(false);
            prefab.AddComponent<BoxCollider2D>().offset = Vector2.up * 0.5f;
            definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab;
            definition.RequireSurface = false; definition.ObstacleLayers = 1 << 31; definition.SurfaceLayers = 1 << 31;
        }
        [TearDown] public void TearDown()
        {
            if (world != null) Object.DestroyImmediate(world.gameObject);
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Physics2D.SyncTransforms();
        }
        [Test] public void Moving2DBuildingIgnoresItsOwnColliderAndRejectsOtherCollider()
        {
            Assert.That(world.TryPlace(definition, new PlacementRequest(origin, Quaternion.identity), out var building, out var result), Is.True, result.Reason);
            Assert.That(world.TryMove(building, new PlacementRequest(origin + Vector3.right * 0.1f, Quaternion.identity), out result), Is.True, result.Reason);
            var obstacle = Go("Obstacle"); obstacle.layer = 31; obstacle.transform.position = origin + new Vector3(3, 0.5f, 0); obstacle.AddComponent<BoxCollider2D>();
            Assert.That(world.TryMove(building, new PlacementRequest(origin + Vector3.right * 3, Quaternion.identity), out _), Is.False);
            Assert.That(building.transform.position.x, Is.EqualTo(origin.x + 0.1f).Within(0.01f));
        }
        [Test] public void TwoDSurfaceGridAndTriggerFilteringUseXYPlane()
        {
            var ground = Go("Ground"); ground.layer = 31; ground.transform.position = origin + Vector3.down * 0.5f; ground.AddComponent<BoxCollider2D>().size = new Vector2(10, 1);
            definition.RequireSurface = true; definition.Placement = PlacementMode.Grid; definition.GridPlane = BuildingGridPlane.XY;
            var trigger = Go("Trigger"); trigger.layer = 31; trigger.transform.position = origin + Vector3.up * 0.5f; trigger.AddComponent<BoxCollider2D>().isTrigger = true;
            var result = world.Check(definition, new PlacementRequest(origin + Vector3.right * 0.2f, Quaternion.identity));
            Assert.That(result.IsValid, Is.True, result.Reason); Assert.That(result.Pose.position, Is.EqualTo(origin));
            Assert.That(world.Check(definition, new PlacementRequest(origin, Quaternion.Euler(90, 0, 0))).IsValid, Is.False);
        }
    }
}
