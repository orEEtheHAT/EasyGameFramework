using System;
using UnityEngine;

namespace EasyBuilding
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Building/Rules/Rule Group", fileName = "Rule Group")]
    public sealed class BuildingRuleGroup : BuildingRule
    {
        public bool Any;
        public BuildingRule[] Rules = Array.Empty<BuildingRule>();
        public override bool Allows(in BuildingContext context, out string reason)
        {
            reason = null;
            if (context.RuleDepth >= 32) { reason = "Rule groups are cyclic or nested too deeply."; return false; }
            if (Rules == null || Rules.Length == 0) { reason = "Add at least one rule to the group."; return false; }
            bool applicable = false; var nested = context.NestedRule();
            foreach (var rule in Rules)
            {
                if (rule == null) { reason = "Assign every rule in the group."; return false; }
                if ((rule.Actions & context.Action) == 0) continue;
                applicable = true;
                bool allowed = rule.Allows(nested, out var rejected);
                if (Any && allowed) return true;
                if (!Any && !allowed) { reason = rejected; return false; }
                if (!allowed && reason == null) reason = rejected;
            }
            return !applicable || !Any;
        }
    }
}
