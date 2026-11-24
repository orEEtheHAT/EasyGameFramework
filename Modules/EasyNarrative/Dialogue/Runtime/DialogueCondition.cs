using EasyQuests;
using EasyResources;
using UnityEngine;

namespace EasyNarrative
{
    public readonly struct DialogueContext
    {
        public readonly QuestSession World;
        public readonly ResourceStore Wallet;
        public readonly GameObject Speaker, Listener;
        public readonly bool Preview;

        public DialogueContext(QuestSession world, ResourceStore wallet, GameObject speaker, GameObject listener, bool preview = false)
        {
            World = world;
            Wallet = wallet;
            Speaker = speaker;
            Listener = listener;
            Preview = preview;
        }
    }

    /// <summary>Evaluate without side effects. Call DialogueRun.RefreshConditions after changing external game state.</summary>
    public abstract class DialogueCondition : ScriptableObject
    {
        public abstract bool Matches(DialogueContext context);
        public virtual string Explain(DialogueContext context) => name;
    }
}
