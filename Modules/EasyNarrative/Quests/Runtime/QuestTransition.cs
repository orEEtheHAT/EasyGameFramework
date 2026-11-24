namespace EasyQuests
{
    public enum QuestTransitionKind { QuestStarted, StepEntered, StepCompleted, QuestCompleted, StepInterrupted }

    /// <summary>Identifies the exact card visited, even when several cards are traversed in one action.</summary>
    public readonly struct QuestTransition
    {
        public readonly QuestTransitionKind Kind;
        public readonly QuestDefinition Quest;
        public readonly QuestRun Run;
        public readonly QuestStep Step;

        internal QuestTransition(QuestTransitionKind kind, QuestRun run, QuestStep step)
        {
            Kind = kind;
            Run = run;
            Quest = run.Definition;
            Step = step;
        }
    }
}
