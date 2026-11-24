using System;

namespace EasyQuests
{
    public enum FactScope { World, Character, Group, QuestInstance }

    /// <summary>Identifies whose value is being read from the shared fact schema.</summary>
    public readonly struct FactAddress : IEquatable<FactAddress>
    {
        public readonly FactScope Scope;
        public readonly string Subject;
        public readonly string Fact;

        public FactAddress(FactScope scope, string subject, string fact)
        {
            if (string.IsNullOrWhiteSpace(fact)) throw new ArgumentException("Choose a fact.", nameof(fact));
            if ((uint)scope > (uint)FactScope.QuestInstance) throw new ArgumentException("Unknown scope.", nameof(scope));
            if (scope != FactScope.World && string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("Choose a fact owner.", nameof(subject));
            Scope = scope; Subject = scope == FactScope.World ? null : subject; Fact = fact;
        }
        public bool Equals(FactAddress other) => Scope == other.Scope && Subject == other.Subject && Fact == other.Fact;
        public override bool Equals(object obj) => obj is FactAddress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Scope, Subject, Fact);
        public override string ToString() => Scope == FactScope.World ? Fact : Scope + "/" + Subject + "/" + Fact;
        public static implicit operator FactAddress(string worldFact) => new(FactScope.World, null, worldFact);
    }

    [Serializable]
    public sealed class QuestRoleBinding
    {
        public string role, target;
    }
}
