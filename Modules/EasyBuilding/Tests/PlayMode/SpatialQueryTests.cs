using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyBuilding.PlayModeTests
{
    public sealed class SpatialQueryTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private BuildingDefinition definition;
        private BuildingSystem world;
        private readonly Vector3 origin = new Vector3(40000, 0, 40000);
        private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        [SetUp] public void SetUp()
        {
            world = Go("World").AddComponent<BuildingSystem>();
            var prefab = Go("Prefab"); prefab.SetActive(false);
            definition = ScriptableObject.CreateInstance<BuildingDefinition>(); definition.Prefab = prefab; definition.RequireSurface = false;
            world.Select(definition);
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]);
            Object.Destroy(definition); objects.Clear(); yield return null;
        }
        [UnityTest] public IEnumerator IndexedSearchMatchesReferenceAfterMovementAndOverflow()
        {
            BuildingSocket wanted = null;
            for (int i = 0; i < 300; i++)
            {
                var go = Go("Socket"); go.transform.position = origin; var socket = go.AddComponent<BuildingSocket>(); socket.SocketType = "Other";
                if (i == 299) { socket.SocketType = "Block"; wanted = socket; }
            }
            yield return null;
            world.UseSpatialQueries = true; Assert.That(world.FindSocket(origin), Is.SameAs(wanted));
            world.UseSpatialQueries = false; Assert.That(world.FindSocket(origin), Is.SameAs(wanted));
            world.UseSpatialQueries = true; wanted.transform.position += Vector3.right * 10;
            Assert.That(world.FindSocket(origin), Is.Null);
            Assert.That(world.FindSocket(origin + Vector3.right * 10), Is.SameAs(wanted));
            wanted.enabled = false; Assert.That(world.FindSocket(origin + Vector3.right * 10), Is.Null);
        }
        [UnityTest] public IEnumerator QueryProxiesNeverProduceGameplayContacts()
        {
            var socket = Go("Socket"); socket.transform.position = origin; socket.AddComponent<BuildingSocket>();
            var body = Go("Body"); body.transform.position = origin; body.AddComponent<SphereCollider>();
            var rb = body.AddComponent<Rigidbody>(); rb.useGravity = false;
            var probe = body.AddComponent<QueryContactProbe>();
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Assert.That(world.FindSocket(origin), Is.Not.Null); Assert.That(probe.Contacts, Is.Zero);
        }
        [UnityTest] public IEnumerator InactiveBuildingsKeepTheirLogicalFootprint()
        {
            Assert.That(world.TryPlace(definition, new PlacementRequest(origin, Quaternion.identity), out var building, out var result), Is.True, result.Reason);
            building.gameObject.SetActive(false);
            Assert.That(world.Check(new PlacementRequest(origin, Quaternion.identity)).IsValid, Is.False);
            yield return null;
        }
    }
}
