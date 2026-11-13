using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyBuilding.Networking.Sample.Tests
{
    public sealed class BuildingTcpTests
    {
        [UnityTest] public IEnumerator SocketTransportReplaysOnceAndSynchronizesLateJoinAndOtherClient()
        {
            var objects = new List<Object>(); var scenes = new List<Scene>();
            BuildingTcpServer server = null; BuildingAuthority authority = null; BuildingTcpClient first = null, late = null;
            try
            {
                BuildingSystem World(string name)
                {
                    var scene = SceneManager.CreateScene(name, new CreateSceneParameters(LocalPhysicsMode.Physics3D)); scenes.Add(scene);
                    var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); objects.Add(go); return go.AddComponent<BuildingSystem>();
                }
                var world = World("TCP authority"); var replica = World("TCP first replica");
                var prefab = new GameObject("TCP prefab"); objects.Add(prefab); prefab.SetActive(false);
                var definition = ScriptableObject.CreateInstance<BuildingDefinition>(); objects.Add(definition); definition.Prefab = prefab; definition.RequireSurface = false;
                definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost("wood", 3) }; var wallet = new Wallet(); world.Economy = wallet;
                var registry = new BuildingRegistry(); registry.Register("test", new[] { definition });
                authority = new BuildingAuthority(world, registry); server = new BuildingTcpServer(authority, i => new Actor("player" + i));
                first = new BuildingTcpClient(replica, registry.Definitions, server.Port);
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!first.Ready && Time.realtimeSinceStartupAsDouble < deadline) { server.Tick(); first.Tick(); yield return null; }
                Assert.That(first.Ready, Is.True, first.LastError);
                int responses = 0; first.Completed += result => { Assert.That(result.Success, Is.True, result.Reason); responses++; };
                var command = new BuildingCommand { Edits = new[] { new BuildingCommandEdit { DefinitionId = definition.Id, Position = Vector3.zero } } };
                first.Send(command); first.Send(command);
                while ((responses < 2 || replica.Count != 1) && Time.realtimeSinceStartupAsDouble < deadline) { server.Tick(); first.Tick(); yield return null; }
                Assert.That(responses, Is.EqualTo(2)); Assert.That(world.Count, Is.EqualTo(1)); Assert.That(replica.Count, Is.EqualTo(1)); Assert.That(wallet.Wood, Is.EqualTo(97));
                var lateWorld = World("TCP late replica"); late = new BuildingTcpClient(lateWorld, registry.Definitions, server.Port);
                while ((!late.Ready || lateWorld.Count != 1) && Time.realtimeSinceStartupAsDouble < deadline) { server.Tick(); first.Tick(); late.Tick(); yield return null; }
                Assert.That(late.Ready, Is.True, late.LastError); Assert.That(lateWorld.Count, Is.EqualTo(1));
                late.Send(new BuildingCommand { Edits = new[] { new BuildingCommandEdit { DefinitionId = definition.Id, Position = Vector3.right * 3 } } });
                while ((replica.Count != 2 || lateWorld.Count != 2) && Time.realtimeSinceStartupAsDouble < deadline) { server.Tick(); first.Tick(); late.Tick(); yield return null; }
                Assert.That(world.Count, Is.EqualTo(2)); Assert.That(replica.Count, Is.EqualTo(2)); Assert.That(lateWorld.Count, Is.EqualTo(2)); Assert.That(wallet.Wood, Is.EqualTo(94));
                Assert.That(replica.Revision, Is.EqualTo(world.Revision)); Assert.That(lateWorld.Revision, Is.EqualTo(world.Revision));
                Assert.That(replica.TryPlace(definition, new PlacementRequest(Vector3.right * 6, Quaternion.identity), out _, out _), Is.False);
            }
            finally
            {
                first?.Dispose(); late?.Dispose(); server?.Dispose(); authority?.Dispose();
                for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.Destroy(objects[i]);
            }
            yield return null;
            foreach (var scene in scenes) if (scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
        private sealed class Actor : IBuildingActor
        {
            public Actor(string id) => Id = id;
            public string Id { get; } public string TeamId => ""; public Vector3 Position => Vector3.zero;
            public bool IsAdministrator => false; public bool HasCapability(string capability) => true;
        }
        private sealed class Wallet : IBuildingEconomy
        {
            public long Wood = 100;
            public bool CanApply(in BuildingBill bill, out string reason) { reason = null; return true; }
            public bool TryReserve(in BuildingBill bill, out IBuildingCostReservation reservation, out string reason)
            {
                long amount = 0; if (bill.Charges != null) foreach (var cost in bill.Charges) amount += cost.Amount;
                reason = null; reservation = new Reservation(this, amount); return true;
            }
            private sealed class Reservation : IBuildingCostReservation
            {
                private Wallet owner; private readonly long amount;
                public Reservation(Wallet owner, long amount) { this.owner = owner; this.amount = amount; }
                public bool TryCommit(out string reason) { reason = null; if (owner == null || owner.Wood < amount) return false; owner.Wood -= amount; owner = null; return true; }
                public void Cancel() => owner = null;
            }
        }
    }
}
