using EasyQuests;
using UnityEngine;

namespace EasyNarrative
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Fact Skill Check")]
    public sealed class FactSkillCheck : DialogueSkillCheck
    {
        public FactCondition threshold = new();
        public override bool Succeeds(DialogueContext context) =>
            context.World != null && threshold != null && !string.IsNullOrEmpty(threshold.fact) &&
            (threshold.scope == FactScope.World || (!string.IsNullOrWhiteSpace(threshold.subject) && !threshold.subject.StartsWith("$", System.StringComparison.Ordinal))) &&
            context.World.TryGetFact(threshold.fact, out _) && context.World.Matches(threshold);
    }
}
