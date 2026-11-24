using EasyInteraction;
using EasyQuests;

namespace EasySuite.Example
{
    public sealed class PatrolMarkerAction : InteractionAction
    {
        public QuestWorld world;
        public QuestDefinition quest;
        public EasySuiteLab lab;
        // Empty on the exercise board. Each training target has its own persistent fact.
        public string inspectedFact;
        public override string GetLabel(InteractionActor actor) => lab.T(LabelKey, ActionLabel);
        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!string.IsNullOrEmpty(inspectedFact) && world.Session.GetFact(inspectedFact) != 0)
                return InteractionAvailability.Unavailable(lab.T("patrol.inspected", "Already inspected"));
            return base.GetAvailability(actor);
        }
        protected override bool OnExecute(InteractionActor actor)
        {
            var session = world.Session;
            world.StartQuest(quest);
            session.Track(quest.id);
            var run = session.Find(quest.id);
            if (run.Finished || string.IsNullOrEmpty(inspectedFact))
            {
                lab.Message(run.Finished ? lab.T("patrol.done", "Patrol inspected") : lab.T("patrol.goal", "Inspect the patrol targets"));
                return false;
            }
            if (session.GetFact(inspectedFact) == 0)
            {
                session.SetFlag(inspectedFact, true, name);
                world.Signal("patrol.inspected");
                if (run.Finished) lab.Message(lab.T("patrol.done", "Patrol inspected"));
            }
            return false;
        }
    }
}
