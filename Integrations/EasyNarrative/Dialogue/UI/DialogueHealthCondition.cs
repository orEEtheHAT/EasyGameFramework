using EasyCombat;
using UnityEngine;

namespace EasyNarrative.UI
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Condition/Character Health")]
    public sealed class DialogueHealthCondition : DialogueCondition
    {
        public bool checkSpeaker = true;
        [Range(0, 1)] public float minimumHealth = .25f;
        public override bool Matches(DialogueContext context)
        {
            var actor = checkSpeaker ? context.Speaker : context.Listener;
            var health = actor ? actor.GetComponentInParent<Health>() : null;
            return health && health.IsAlive && health.Current >= health.Maximum * minimumHealth;
        }
        public override string Explain(DialogueContext context) => (checkSpeaker ? "Speaker" : "Listener") + " health ≥ " + minimumHealth.ToString("P0");
    }
}
