using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Rules/Nearby Building", fileName = "Nearby Building")]
    public sealed class BuildingNearbyRule : BuildingRule
    {
        public BuildingDefinition RequiredDefinition;
        public string RequiredTag;
        [Min(0)] public float Radius = 10;
        [Min(1)] public int MinimumCount = 1;
        public bool CompletedOnly = true;
        public override bool Allows(in BuildingContext context, out string reason)
        {
            reason = "A required building must be nearby.";
            if (!BuildingMath.Finite(Radius) || Radius < 0 || MinimumCount < 1) { reason = "Set a finite radius and a positive minimum count on Nearby Building."; return false; }
            return context.System.CountNearby(context.Pose.position, Radius, RequiredTag, RequiredDefinition, context.Existing, CompletedOnly) >= MinimumCount;
        }
    }
}
