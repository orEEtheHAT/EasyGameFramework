using System.Collections;
using System.Collections.Generic;
using EasyBuilding.TwoD;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyBuilding.Tests.TwoD
{
    public sealed class LocalPhysics2DTests
    {
        [UnityTest] public IEnumerator NativeBroadphaseMatchesReferenceInsideLocal2DPhysicsScene()
        {
            var scene = SceneManager.CreateScene("EasyBuilding local 2D", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var go = new GameObject("2D world"); SceneManager.MoveGameObjectToScene(go, scene); var world = go.AddComponent<BuildingSystem>(); go.AddComponent<BuildingPhysics2D>();
            var prefab = new GameObject("2D prefab"); prefab.SetActive(false); prefab.AddComponent<BoxCollider2D>().offset = Vector2.up * 0.5f;
            var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = prefab; definition.RequireSurface = false; definition.ObstacleLayers = 0;
            var request = new PlacementRequest(new Vector3(1000, 0, 0), Quaternion.identity);
            Assert.That(world.TryPlace(definition, request, out var first, out var result), Is.True, result.Reason);
            yield return null;
            world.UseSpatialQueries = true; Assert.That(world.Check(definition, request).Failure, Is.EqualTo(PlacementFailure.Obstructed));
            world.UseSpatialQueries = false; Assert.That(world.Check(definition, request).Failure, Is.EqualTo(PlacementFailure.Obstructed));
            world.UseSpatialQueries = true; first.transform.position += Vector3.right * 4;
            Assert.That(world.Check(definition, request).IsValid, Is.True);
            var output = new List<PlacedBuilding>(); Assert.That(world.QueryBuildings(first.transform.position, 1, output), Is.EqualTo(1));
            Object.Destroy(go); Object.Destroy(prefab); Object.Destroy(definition); yield return null; yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
