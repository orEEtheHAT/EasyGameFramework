using EasyInteraction;
using UnityEngine;

namespace EasyQuests.Integrations
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Interaction Action")]
    public sealed class QuestInteractionAction : InteractionAction
    {
        public QuestWorld world;
        public QuestDefinition quest;
        public string signal;

        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!world || !world.database || !world.Session.Contains(quest) || string.IsNullOrWhiteSpace(signal))
                return InteractionAvailability.Unavailable("Configure the quest and its interaction event.");
            return world.Session.Find(quest.id)?.Finished == true
                ? InteractionAvailability.Unavailable("Quest completed.") : InteractionAvailability.Available;
        }

        public void Interact()
        {
            if (!world || !world.database || !world.Session.Contains(quest) || string.IsNullOrWhiteSpace(signal)) return;
            var run = world.Session.Find(quest.id);
            if (run?.Finished == true) return;
            if (run == null) world.StartQuest(quest);
            world.Session.SignalFrom(signal,1,name);
        }

        protected override bool OnExecute(InteractionActor actor) { Interact(); return false; }
    }
}
