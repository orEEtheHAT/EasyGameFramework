using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Catalog", fileName = "Building Catalog")]
    public sealed class BuildingCatalog : ScriptableObject
    {
        public BuildingDefinition[] Buildings = Array.Empty<BuildingDefinition>();
    }
    /// <summary>Per-game catalog with atomic mod/pack registration. It never mutates authored ScriptableObjects.</summary>
    public sealed class BuildingRegistry
    {
        private readonly Dictionary<string, BuildingDefinition> definitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> packs = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private BuildingDefinition[] snapshot = Array.Empty<BuildingDefinition>();
        private IReadOnlyList<BuildingDefinition> view = Array.Empty<BuildingDefinition>();
        public IReadOnlyList<BuildingDefinition> Definitions => view;
        public event Action Changed;
        public bool TryGet(string id, out BuildingDefinition definition) { definition = null; return id != null && definitions.TryGetValue(id, out definition); }
        public void Register(string packId, BuildingCatalog catalog) => Register(packId, catalog != null ? catalog.Buildings : throw new ArgumentNullException(nameof(catalog)));
        public void Register(string packId, IReadOnlyList<BuildingDefinition> content)
        {
            if (string.IsNullOrWhiteSpace(packId) || packs.ContainsKey(packId) || content == null) throw new ArgumentException("Choose a distinct nonempty pack ID and a catalog.");
            var ids = new string[content.Count]; var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < content.Count; i++)
            {
                var definition = content[i];
                if (definition == null || definition.ConfigurationError != null || definitions.ContainsKey(definition.Id) || !unique.Add(definition.Id))
                    throw new ArgumentException("Pack definitions must be valid and have unique IDs across all registered packs.");
                ids[i] = definition.Id;
            }
            for (int i = 0; i < content.Count; i++) definitions.Add(ids[i], content[i]);
            packs.Add(packId, ids); Rebuild();
        }
        public bool Unregister(string packId)
        {
            if (packId == null || !packs.TryGetValue(packId, out var ids)) return false;
            foreach (var id in ids) definitions.Remove(id); packs.Remove(packId); Rebuild(); return true;
        }
        public int Search(string text, string category, List<BuildingDefinition> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output)); output.Clear();
            foreach (var definition in snapshot)
            {
                if (!string.IsNullOrEmpty(category) && !string.Equals(definition.Category, category, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(text) && definition.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 &&
                    definition.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0 && !MatchesTags(definition.Tags, text)) continue;
                output.Add(definition);
            }
            return output.Count;
        }
        private static bool MatchesTags(string[] tags, string text)
        { if (tags != null) foreach (var tag in tags) if (tag != null && tag.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }
        private void Rebuild()
        {
            snapshot = new BuildingDefinition[definitions.Count]; definitions.Values.CopyTo(snapshot, 0);
            Array.Sort(snapshot, (a, b) => { int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); return byName != 0 ? byName : string.CompareOrdinal(a.Id, b.Id); });
            view = Array.AsReadOnly(snapshot);
            if (Changed != null) foreach (Action handler in Changed.GetInvocationList()) try { handler(); } catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
