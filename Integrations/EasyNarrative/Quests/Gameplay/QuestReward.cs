using EasyResources;
using UnityEngine;

namespace EasyQuests.Integrations
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Reward")]
    public sealed class QuestReward : MonoBehaviour
    {
        public QuestWorld world;
        public QuestDefinition quest;
        public ResourceRewardProfile profile;
        public ResourceContainer wallet;
        public bool Claim()
        {
            if (!world || !quest) return false;
            var run = world.Session.Find(quest.id);
            if (run == null || run.Current.rewardMode != QuestRewardMode.None || !profile || !wallet) return false;
            return world.Session.TryClaimReward(quest.id, () => wallet.TryGrant(profile.Roll(new System.Random(world.Session.Find(quest.id).RewardSeed)), out _));
        }
    }
}
