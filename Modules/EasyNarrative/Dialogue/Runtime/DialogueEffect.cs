using UnityEngine;

namespace EasyNarrative
{
    /// <summary>
    /// A game-specific consequence. Validate every prerequisite in CanApply without modifying state.
    /// Apply runs only after the payment is accepted. Implementations must accept states that passed CanApply.
    /// </summary>
    public abstract class DialogueEffect : ScriptableObject
    {
        public virtual bool CanApply(DialogueContext context) => true;
        public virtual string Explain(DialogueContext context) => name;
        public abstract void Apply(DialogueContext context);
        public virtual void Preview(DialogueContext context) { }
    }
}
