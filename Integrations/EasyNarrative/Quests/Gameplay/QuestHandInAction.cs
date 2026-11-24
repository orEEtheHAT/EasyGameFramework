using EasyInteraction;
using UnityEngine;

namespace EasyQuests.Integrations
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Hand-In Action")]
    public sealed class QuestHandInAction : InteractionAction
    {
        public QuestWorld world;
        public QuestDefinition quest;
        [Tooltip("Select a Quest Order for a particular instance of a repeatable quest.")]
        public QuestOrder order;
        public QuestDefinition Definition => order ? order.template : quest;
        public QuestWorld World => order ? order.world : world;
        private QuestRun BoundRun => order ? order.Run : (world && quest ? world.Session.Find(quest.id) : null);
        public string stepId, goalId;

        private QuestGoal Goal(QuestRun run)
        {
            if (run == null || run.Finished || run.Current.id != stepId) return null;
            foreach (var goal in run.Current.goals)
                if (goal.id == goalId && goal.mode == GoalMode.TurnInResource) return goal;
            return null;
        }

        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!World || !World.wallet || !Definition) return InteractionAvailability.Unavailable("Assign the quest and its wallet.");
            var run = BoundRun;
            if (string.IsNullOrEmpty(goalId)) return run != null && run.RewardPending && run.Current.id == stepId &&
                run.Current.rewardMode == QuestRewardMode.AtInteraction ? InteractionAvailability.Available : InteractionAvailability.Unavailable("No pending reward.");
            var goal = Goal(run);
            if (goal == null || run.Satisfied(goal)) return InteractionAvailability.Unavailable("This objective is not active.");
            return World.wallet.GetAvailable(goal.resource) >= goal.count ? InteractionAvailability.Available :
                InteractionAvailability.Unavailable(goal.resource.DisplayName + ": " + goal.DisplayCount(goal.count) + " required.");
        }
        public bool Confirm()
        {
            var run = BoundRun;
            if (run == null || run.Definition != Definition) return false;
            if (string.IsNullOrEmpty(goalId)) return World.TryCollect(run.Id, stepId);
            return World.TryTurnIn(run.Id, Goal(run));
        }
        protected override bool OnExecute(InteractionActor actor) { Confirm(); return false; }
    }
}
