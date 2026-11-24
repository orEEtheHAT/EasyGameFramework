using EasyInteraction;
using EasyNarrative.UI;
using UnityEngine;

namespace EasyNarrative.Interaction
{
    [AddComponentMenu("EasyGameFramework/Narrative/Talk Action")]
    public sealed class TalkAction : InteractionAction
    {
        public DialogueDefinition dialogue;
        public DialogueInterruption interruption = new();
        private void Reset() { ActionLabel = "Talk"; }
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!dialogue || !actor) return InteractionAvailability.Unavailable("Assign a dialogue.");
            var presenter = actor.GetComponentInParent<DialoguePresenter>();
            return presenter && presenter.isActiveAndEnabled && presenter.manager && presenter.Conversation == null
                ? InteractionAvailability.Available : InteractionAvailability.Unavailable("Dialogue presenter unavailable.");
        }
        protected override bool OnExecute(InteractionActor actor)
        {
            actor.GetComponentInParent<DialoguePresenter>().OpenFrom(dialogue, transform, interruption);
            return false;
        }
    }
}
