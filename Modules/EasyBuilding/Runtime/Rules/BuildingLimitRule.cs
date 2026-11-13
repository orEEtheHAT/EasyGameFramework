using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Rules/Building Limit", fileName = "Building Limit")]
    public sealed class BuildingLimitRule : BuildingRule
    {
        [Min(0)] public int Maximum = 10;
        public bool ThisDefinitionOnly = true;
        public string Tag;
        public bool PerOwner;
        public override bool Allows(in BuildingContext context, out string reason)
        {
            reason = "This operation would exceed the building limit.";
            if (Maximum < 0) { reason = "Maximum on Building Limit must be nonnegative."; return false; }
            if (context.Action == BuildingAction.Remove) return true;
            return context.CountAfter(ThisDefinitionOnly ? context.Definition : null, Tag, PerOwner ? context.Actor?.Id ?? "" : null) <= Maximum;
        }
    }
}
