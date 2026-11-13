using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Rules/Builder Range", fileName = "Builder Range")]
    public sealed class BuildingRangeRule : BuildingRule
    {
        [Min(0)] public float MaximumDistance = 6;
        public override bool Allows(in BuildingContext context, out string reason)
        {
            reason = "Move closer to the building position.";
            if (!BuildingMath.Finite(MaximumDistance) || MaximumDistance < 0) { reason = "Set a finite nonnegative Maximum Distance on the range rule."; return false; }
            return context.Actor == null || context.Actor.IsAdministrator || (context.Actor.Position - context.Pose.position).sqrMagnitude <= MaximumDistance * MaximumDistance;
        }
    }
}
