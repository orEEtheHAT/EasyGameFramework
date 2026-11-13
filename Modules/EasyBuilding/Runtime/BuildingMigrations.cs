using System;
using System.Collections.Generic;

namespace EasyBuilding
{
    /// <summary>Explicit, versioned definition migrations. Migration callbacks edit a copy; failure never changes the input snapshot.</summary>
    public sealed class BuildingMigrations
    {
        private readonly Dictionary<string, Dictionary<int, Step>> steps = new Dictionary<string, Dictionary<int, Step>>(StringComparer.Ordinal);
        private sealed class Step { internal int To; internal Action<BuildingRecord> Apply; }
        public void Register(string definitionId, int fromVersion, int toVersion, Action<BuildingRecord> migrate)
        {
            if (string.IsNullOrWhiteSpace(definitionId) || fromVersion < 1 || toVersion <= fromVersion || migrate == null) throw new ArgumentException("Provide an ID, an advancing version step and a migration callback.");
            if (!steps.TryGetValue(definitionId, out var versions)) { versions = new Dictionary<int, Step>(); steps.Add(definitionId, versions); }
            if (!versions.TryAdd(fromVersion, new Step { To = toVersion, Apply = migrate })) throw new ArgumentException("This migration step is already registered.");
        }
        public bool TryMigrate(BuildingState input, IReadOnlyList<BuildingDefinition> catalog, out BuildingState migrated, out string reason)
        {
            migrated = null; reason = null;
            if (input == null || input.Version < 1 || input.Version > BuildingState.CurrentVersion || input.Buildings == null || catalog == null)
            { reason = "Use a supported snapshot and current definition catalog."; return false; }
            try
            {
                var definitions = new Dictionary<string, BuildingDefinition>(StringComparer.Ordinal);
                foreach (var definition in catalog) if (definition == null || !definitions.TryAdd(definition.Id, definition)) throw new InvalidOperationException("The catalog contains a missing or duplicate definition.");
                var output = new BuildingState { Revision = input.Revision };
                foreach (var source in input.Buildings)
                {
                    if (source == null) throw new InvalidOperationException("A saved record is null.");
                    var record = BuildingSystem.CloneRecord(source);
                    if (input.Version == 1) { record.Scale = UnityEngine.Vector3.one; record.DefinitionVersion = Math.Max(1, record.DefinitionVersion); }
                    if (definitions.TryGetValue(record.DefinitionId ?? "", out var definition))
                    {
                        string instanceId = record.InstanceId, definitionId = record.DefinitionId; int count = 0;
                        if (record.DefinitionVersion > definition.SaveVersion) throw new InvalidOperationException("A saved definition is newer than the installed catalog.");
                        while (record.DefinitionVersion < definition.SaveVersion)
                        {
                            if (++count > 128 || !steps.TryGetValue(definitionId, out var versions) || !versions.TryGetValue(record.DefinitionVersion, out var step) || step.To > definition.SaveVersion)
                                throw new InvalidOperationException("Missing migration for " + definitionId + " version " + record.DefinitionVersion + ".");
                            step.Apply(record);
                            if (record.InstanceId != instanceId || record.DefinitionId != definitionId) throw new InvalidOperationException("Migrations must preserve instance and definition IDs; migrate links explicitly for content renames.");
                            record.DefinitionVersion = step.To;
                        }
                    }
                    output.Buildings.Add(record);
                }
                if (input.Connections != null) foreach (var link in input.Connections)
                { if (link == null) throw new InvalidOperationException("A saved connection is null."); output.Connections.Add(BuildingSystem.CloneLink(link)); }
                if (input.RedeemedParcels != null) output.RedeemedParcels.AddRange(input.RedeemedParcels);
                migrated = output; return true;
            }
            catch (Exception exception) { reason = exception.Message; return false; }
        }
    }
}
