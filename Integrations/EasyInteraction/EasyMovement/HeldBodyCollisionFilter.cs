using System;
using System.Collections.Generic;
using ECM2;
using UnityEngine;

namespace EasyInteraction.Integrations.Movement
{
    // Do not toggle ECM2's IgnoreCollision sets: they have no public ownership/snapshot API.
    internal static class HeldBodyCollisionFilter
    {
        private sealed class Filter
        {
            public CharacterMovement Movement;
            public CharacterMovement.ColliderFilterCallback Previous, Callback;
            public readonly Dictionary<Rigidbody, int> Bodies = new Dictionary<Rigidbody, int>();
            public bool Excludes(Collider collider) => collider && collider.attachedRigidbody && Bodies.ContainsKey(collider.attachedRigidbody) ||
                (Previous != null && Previous(collider));
        }

        private sealed class Lease : IDisposable
        {
            private Filter filter;
            private readonly Rigidbody body;
            public Lease(Filter filter, Rigidbody body) { this.filter = filter; this.body = body; }
            public void Dispose()
            {
                var current = filter;
                if (current == null)
                    return;
                filter = null;
                int count = current.Bodies[body];
                if (count > 1) { current.Bodies[body] = count - 1; return; }
                current.Bodies.Remove(body);
                if (current.Bodies.Count != 0)
                    return;
                if (current.Movement && current.Movement.colliderFilterCallback == current.Callback)
                    current.Movement.colliderFilterCallback = current.Previous;
                filters.Remove(current);
                // Keep the previous delegate if another owner's wrapper captured ours; this now passes through.
            }
        }

        private static readonly List<Filter> filters = new List<Filter>();
        internal static IDisposable Acquire(CharacterMovement movement, Rigidbody body)
        {
            if (!movement || !body)
                throw new InvalidOperationException("Assign an initialized EasyMovement Character before grabbing a body.");
            Filter filter = null;
            for (int i = 0; i < filters.Count; i++)
                if (filters[i].Movement == movement && movement.colliderFilterCallback == filters[i].Callback)
                { filter = filters[i]; break; }
            if (filter == null)
            {
                filter = new Filter { Movement = movement, Previous = movement.colliderFilterCallback };
                filter.Callback = filter.Excludes;
                movement.colliderFilterCallback = filter.Callback;
                filters.Add(filter);
            }
            filter.Bodies.TryGetValue(body, out int count);
            filter.Bodies[body] = count + 1;
            return new Lease(filter, body);
        }
    }
}
