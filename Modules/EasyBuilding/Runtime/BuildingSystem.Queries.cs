using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public sealed partial class BuildingSystem
    {
        private readonly List<PlacedBuilding> ruleCandidates = new List<PlacedBuilding>(32);
        public int CountNearby(Vector3 point, float radius, string tag = null, BuildingDefinition definition = null,
            PlacedBuilding exclude = null, bool completedOnly = true)
        {
            EnsureServices(); Synchronize(); GetNearbyBuildings(point, radius, ruleCandidates);
            int count = 0; float squareRadius = radius * radius;
            foreach (var building in ruleCandidates)
            {
                if (building == null || building == exclude || completedOnly && !building.IsComplete) continue;
                if (definition != null && building.Definition != definition || !string.IsNullOrEmpty(tag) && !building.Definition.HasTag(tag)) continue;
                if ((building.WorldBounds.ClosestPoint(point) - point).sqrMagnitude <= squareRadius) count++;
            }
            return count;
        }
        internal int CountAfter(in BuildingContext context, BuildingDefinition definition, string tag, string ownerId)
        {
            int count = 0;
            foreach (var building in buildings.Values)
            {
                if (building == null || currentPlan != null && ignoredRoots.Contains(building.transform) || currentPlan == null && building == context.Existing) continue;
                if (Matches(building.Definition, building.OwnerId, definition, tag, ownerId)) count++;
            }
            if (currentPlan != null)
            {
                foreach (var entry in currentPlan)
                    if (entry.Edit.Action != BuildingAction.Remove && Matches(entry.Edit.Definition,
                        entry.Edit.Existing != null ? entry.Edit.Existing.OwnerId : context.Actor?.Id ?? "", definition, tag, ownerId)) count++;
            }
            else if ((context.Action == BuildingAction.Place || context.Action == BuildingAction.Move || context.Action == BuildingAction.Upgrade) &&
                Matches(context.Definition, context.Existing != null ? context.Existing.OwnerId : context.Actor?.Id ?? "", definition, tag, ownerId)) count++;
            return count;
        }
        private static bool Matches(BuildingDefinition value, string actualOwner, BuildingDefinition definition, string tag, string owner)
            => (definition == null || value == definition) && (string.IsNullOrEmpty(tag) || value.HasTag(tag)) && (owner == null || actualOwner == owner);
    }
}
