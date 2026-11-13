using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("EasyBuilding.Physics2D")]

namespace EasyBuilding
{
    // Two placement readers share exactly this ownership policy; this is not a global input manager.
    internal sealed class BuildingInputBlocks
    {
        private sealed class Identity : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
        private readonly HashSet<object> owners = new HashSet<object>(new Identity());
        private static readonly Predicate<object> Inactive = value => !IsActive(value);
        public bool IsBlocked { get { if (owners.Count != 0) owners.RemoveWhere(Inactive); return owners.Count != 0; } }
        public void Set(object owner, bool blocked)
        {
            if (ReferenceEquals(owner, null)) throw new ArgumentNullException(nameof(owner));
            if (blocked && IsActive(owner)) owners.Add(owner); else owners.Remove(owner);
        }
        private static bool IsActive(object owner)
        {
            if (!(owner is UnityEngine.Object value)) return true; // Plain token: explicit release by its owner.
            if (value == null) return false;
            if (value is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (value is Component component) return component.gameObject.activeInHierarchy;
            return !(value is GameObject go) || go.activeInHierarchy;
        }
    }
}
