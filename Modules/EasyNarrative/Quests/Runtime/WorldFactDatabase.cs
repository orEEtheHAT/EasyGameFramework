using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EasyQuests
{
    public enum FactType { Flag, Integer, State }
    [Serializable] public sealed class FactState { public string name; public int value; }

    [Serializable]
    public sealed class WorldFact
    {
        public string name = "New fact";
        [Tooltip("Permanent key used by game code and saves. Renaming the display name is safe.")]
        public string id = Guid.NewGuid().ToString("N");
        public FactType type;
        public int initial;
        public FactState[] states = Array.Empty<FactState>();

        public bool Accepts(int value) => type == FactType.Integer ||
            (type == FactType.Flag ? value == 0 || value == 1 : states.Any(s => s.value == value));
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/World Fact Database")]
    public sealed class WorldFactDatabase : ScriptableObject
    {
        public WorldFact[] facts = Array.Empty<WorldFact>();

        public void ValidateDatabase() => BuildIndex();

        internal Dictionary<string, WorldFact> BuildIndex()
        {
            var result = new Dictionary<string, WorldFact>(StringComparer.Ordinal);
            if (facts == null) throw new ArgumentException("Missing fact definitions.");
            foreach (var fact in facts)
            {
                if (fact == null || string.IsNullOrWhiteSpace(fact.id) || !result.TryAdd(fact.id, fact))
                    throw new ArgumentException("Give every world fact a unique, permanent ID.");
                if (!Enum.IsDefined(typeof(FactType), fact.type)) throw new ArgumentException("Unknown fact type: " + fact.name);
                if (fact.type == FactType.State && (fact.states == null || fact.states.Length == 0 ||
                    fact.states.Any(s => s == null || string.IsNullOrWhiteSpace(s.name)) ||
                    fact.states.Select(s => s.value).Distinct().Count() != fact.states.Length))
                    throw new ArgumentException("Give each state a name and a unique integer code in " + fact.name);
                if (!fact.Accepts(fact.initial)) throw new ArgumentException("Invalid default for " + fact.name);
            }
            return result;
        }
    }
}
