using UnityEngine;

namespace EasyNarrative
{
    /// <summary>Resolve the outcome of an available reply without mutating the world or wallet.</summary>
    public abstract class DialogueSkillCheck : ScriptableObject
    {
        public abstract bool Succeeds(DialogueContext context);
    }
}
