using System;
using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Rules/Required Capabilities", fileName = "Required Capabilities")]
    public sealed class BuildingCapabilityRule : BuildingRule
    {
        public string[] Required = Array.Empty<string>();
        [TextArea] public string RejectionMessage = "Unlock the required technology or capability first.";
        public override bool Allows(in BuildingContext context, out string reason)
        {
            reason = RejectionMessage;
            if (context.Actor == null || context.Actor.IsAdministrator) return true;
            if (Required == null) return true;
            foreach (var capability in Required) if (!context.Actor.HasCapability(capability)) return false;
            return true;
        }
    }
}
