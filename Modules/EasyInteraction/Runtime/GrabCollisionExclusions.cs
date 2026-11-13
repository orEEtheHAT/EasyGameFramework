using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyInteraction
{
    // Narrow runtime cleanup for grab-owned pairs: it must outlive a disabled/destroyed GrabAction.
    [AddComponentMenu(""), DefaultExecutionOrder(-31000)]
    internal sealed class GrabCollisionExclusions : MonoBehaviour
    {
        private sealed class Pair
        {
            public Collider Item, Player;
            public bool WasIgnored;
            public int Users;
        }

        private sealed class Hold
        {
            public GrabAction Grab;
            public InteractionActor Actor;
            public Rigidbody Body;
            public Transform Root;
            public bool Released;
            public IDisposable Queries;
            public readonly List<Collider> Items = new List<Collider>(8);
            public readonly List<Collider> Players = new List<Collider>(8);
            public readonly List<Pair> Pairs = new List<Pair>(8);
        }

        private static GrabCollisionExclusions instance;
        private readonly List<Hold> holds = new List<Hold>(4);
        private readonly List<Pair> pairs = new List<Pair>(16);

        internal static void Acquire(GrabAction grab, InteractionActor actor)
        {
            if (!grab.IgnoreOwnerCollisions || !actor || !grab.Body)
                return;
            if (!instance)
            {
                var go = new GameObject("EasyInteraction grab collision cleanup") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(go);
                instance = go.AddComponent<GrabCollisionExclusions>();
            }
            for (int i = 0; i < instance.holds.Count; i++)
            {
                var existing = instance.holds[i];
                if (!existing.Released && existing.Grab == grab && existing.Actor == actor && existing.Root == actor.PlayerRoot)
                    return;
            }
            var hold = new Hold { Grab = grab, Actor = actor, Body = grab.Body, Root = actor.PlayerRoot };
            try
            {
                if (actor.Motor is IInteractionCollisionFilter filter)
                    hold.Queries = filter.AcquireCollisionExclusion(hold.Body);
                instance.Refresh(hold);
                instance.holds.Add(hold);
                instance.enabled = true;
            }
            catch
            {
                instance.End(hold);
                throw;
            }
        }

        internal static void Release(GrabAction grab, InteractionActor actor = null)
        {
            if (!instance)
                return;
            for (int i = 0; i < instance.holds.Count; i++)
            {
                var hold = instance.holds[i];
                if (hold.Grab == grab && (!actor || hold.Actor == actor))
                    hold.Released = true;
            }
            instance.Cleanup();
        }

        private static bool Active(Collider collider) => collider && collider.enabled && collider.gameObject.activeInHierarchy;

        private static bool Overlaps(Pair pair)
        {
            if (!Active(pair.Item) || !Active(pair.Player) || pair.Item.isTrigger || pair.Player.isTrigger)
                return false;
            // Dynamic held bodies use convex shapes. Preserve a conservative AABB fallback for unsupported shapes.
            bool supported = pair.Item is BoxCollider || pair.Item is SphereCollider || pair.Item is CapsuleCollider ||
                pair.Item is MeshCollider itemMesh && itemMesh.convex || pair.Player is BoxCollider ||
                pair.Player is SphereCollider || pair.Player is CapsuleCollider || pair.Player is MeshCollider playerMesh && playerMesh.convex;
            if (!supported)
                return pair.Item.bounds.Intersects(pair.Player.bounds);
            PhysicsPose(pair.Item, out var itemPosition, out var itemRotation);
            PhysicsPose(pair.Player, out var playerPosition, out var playerRotation);
            return Physics.ComputePenetration(pair.Item, itemPosition, itemRotation, pair.Player, playerPosition, playerRotation, out _, out _);
        }

        private static void PhysicsPose(Collider collider, out Vector3 position, out Quaternion rotation)
        {
            var transform = collider.transform;
            position = transform.position;
            rotation = transform.rotation;
            var body = collider.attachedRigidbody;
            if (!body)
                return;
            // Transform can still contain an interpolated render pose after a physics move/drop.
            var correction = body.rotation * Quaternion.Inverse(body.transform.rotation);
            position = body.position + correction * (position - body.transform.position);
            rotation = correction * rotation;
        }

        private Pair AcquirePair(Collider item, Collider player)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair.Item == item && pair.Player == player || pair.Item == player && pair.Player == item)
                {
                    pair.Users++;
                    return pair;
                }
            }
            var added = new Pair { Item = item, Player = player, Users = 1, WasIgnored = Physics.GetIgnoreCollision(item, player) };
            pairs.Add(added);
            Apply(added);
            return added;
        }

        private static void Apply(Pair pair)
        {
            if (Active(pair.Item) && Active(pair.Player) && !Physics.GetIgnoreCollision(pair.Item, pair.Player))
                Physics.IgnoreCollision(pair.Item, pair.Player, true);
        }

        private void Refresh(Hold hold)
        {
            hold.Body.GetComponentsInChildren(true, hold.Items);
            hold.Root.GetComponentsInChildren(true, hold.Players);
            for (int i = hold.Pairs.Count - 1; i >= 0; i--)
            {
                var pair = hold.Pairs[i];
                bool belongs = pair.Item && pair.Player && !pair.Item.isTrigger && !pair.Player.isTrigger &&
                    (pair.Item.attachedRigidbody == hold.Body && hold.Items.Contains(pair.Item) && hold.Players.Contains(pair.Player) ||
                    pair.Player.attachedRigidbody == hold.Body && hold.Items.Contains(pair.Player) && hold.Players.Contains(pair.Item));
                if (!belongs)
                {
                    pair.Users--;
                    hold.Pairs.RemoveAt(i);
                }
            }
            for (int i = 0; i < hold.Items.Count; i++)
            {
                var item = hold.Items[i];
                if (!item || item.isTrigger || item.attachedRigidbody != hold.Body)
                    continue;
                for (int p = 0; p < hold.Players.Count; p++)
                {
                    var player = hold.Players[p];
                    if (!player || player.isTrigger || player == item || player.attachedRigidbody == hold.Body)
                        continue;
                    bool found = false;
                    for (int j = 0; j < hold.Pairs.Count; j++)
                        if (hold.Pairs[j].Item == item && hold.Pairs[j].Player == player || hold.Pairs[j].Item == player && hold.Pairs[j].Player == item) { found = true; break; }
                    if (!found)
                        hold.Pairs.Add(AcquirePair(item, player));
                }
            }
        }

        private void End(Hold hold)
        {
            for (int i = 0; i < hold.Pairs.Count; i++)
                hold.Pairs[i].Users--;
            hold.Pairs.Clear();
            var queries = hold.Queries;
            hold.Queries = null;
            queries?.Dispose();
        }

        private void FixedUpdate()
        {
            for (int i = 0; i < holds.Count; i++)
            {
                var hold = holds[i];
                if (!hold.Released && hold.Grab && hold.Actor && hold.Body && hold.Root)
                    Refresh(hold);
            }
            Cleanup();
        }

        // Cleanup still runs while Time.timeScale == 0 (pooling, disable and destruction).
        private void Update() => Cleanup();

        private void Cleanup()
        {
            for (int i = holds.Count - 1; i >= 0; i--)
            {
                var hold = holds[i];
                if (!hold.Grab || !hold.Actor || !hold.Grab.isActiveAndEnabled || !hold.Actor.isActiveAndEnabled ||
                    !hold.Grab.IgnoreOwnerCollisions || !hold.Grab.Owns(hold.Actor) || hold.Root != hold.Actor.PlayerRoot)
                    hold.Released = true;
                if (!hold.Body || !hold.Root || !hold.Body.gameObject.activeInHierarchy || !hold.Root.gameObject.activeInHierarchy)
                    hold.Released = true;
                if (!hold.Released)
                    continue;
                bool overlapping = false;
                if (hold.Body && hold.Root && hold.Body.gameObject.activeInHierarchy && hold.Root.gameObject.activeInHierarchy)
                    for (int p = 0; p < hold.Pairs.Count; p++)
                        overlapping |= Overlaps(hold.Pairs[p]);
                if (overlapping)
                    continue;
                End(hold);
                holds.RemoveAt(i);
            }
            for (int i = pairs.Count - 1; i >= 0; i--)
            {
                var pair = pairs[i];
                if (pair.Users > 0 || Overlaps(pair))
                {
                    Apply(pair);
                    continue;
                }
                if (pair.Item && pair.Player)
                    Physics.IgnoreCollision(pair.Item, pair.Player, pair.WasIgnored);
                pairs.RemoveAt(i);
            }
            enabled = holds.Count > 0 || pairs.Count > 0;
        }

        private void OnDestroy()
        {
            for (int i = 0; i < holds.Count; i++)
                End(holds[i]);
            for (int i = 0; i < pairs.Count; i++)
                if (pairs[i].Item && pairs[i].Player)
                    Physics.IgnoreCollision(pairs[i].Item, pairs[i].Player, pairs[i].WasIgnored);
            holds.Clear();
            pairs.Clear();
            if (instance == this)
                instance = null;
        }
    }
}
