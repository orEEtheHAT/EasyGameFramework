using EasyInteraction;
using EasyQuests;
using EasyQuests.Integrations;
using EasyUI;
using UnityEngine;

namespace EasySuite.Example
{
    public enum DeliveryStation { Board, Bridge, Ferry, Recipient, Dispatch, Assistant }

    public sealed class EasySuiteQuestStation : InteractionAction, INarrativeFactSource
    {
        public const string CourierPassFact = "ferryman.courier-pass";
        public DeliveryStation station;
        public EasySuiteLab lab;
        public QuestWorld world;
        public QuestDefinition delivery;
        public QuestReward reward;
        public UIWindow journal;
        private const string BridgeRepaired = "bridge.repaired", RecipientAway = "recipient.away";
        public void DescribeFacts(System.Action<WorldFactDatabase,string,FactAccess> add)
        {
            if (!world) return;
            switch (station)
            {
                case DeliveryStation.Board: add(world.database,CourierPassFact,FactAccess.Read | FactAccess.Write); break;
                case DeliveryStation.Bridge: add(world.database,BridgeRepaired,FactAccess.Write); break;
                case DeliveryStation.Dispatch: add(world.database,RecipientAway,FactAccess.Write); break;
            }
        }
        public override string GetLabel(InteractionActor actor) => lab.T("quest.action." + station, ActionLabel);
        protected override bool OnExecute(InteractionActor actor)
        {
            var session = world.Session;
            switch (station)
            {
                case DeliveryStation.Board:
                    world.StartQuest(delivery);
                    CheckPickup();
                    if (session.TryGetFact(CourierPassFact, out int pass) && pass == 0)
                    {
                        session.SetFlag(CourierPassFact, true, name);
                        lab.Message(lab.T("dialogue.advice.permit.issued", "Courier pass issued. Show it to the ferryman for directions."));
                    }
                    lab.Windows.Open(journal);
                    break;
                case DeliveryStation.Bridge:
                    session.SetFlag(BridgeRepaired, true, "Bridge station");
                    lab.Message(lab.T("quest.bridge.done", "Bridge repaired."));
                    break;
                case DeliveryStation.Ferry:
                    world.Signal("ferry.crossed");
                    break;
                case DeliveryStation.Dispatch:
                    session.SetFlag(RecipientAway, true, "Dispatch desk");
                    lab.Message(lab.T("quest.away", "The recipient has left. Bring the crate to the assistant."));
                    break;
                case DeliveryStation.Assistant:
                case DeliveryStation.Recipient:
                    var run = session.Find(delivery.id);
                    if (run == null) break;
                    if (run.RewardPending) { reward.Claim(); break; }
                    if (run.Current.id != (station == DeliveryStation.Assistant ? "assistant-handover" : "handover")) break;
                    if (Vector3.Distance(lab.Crate.position, transform.position) > 3f)
                    {
                        lab.Message(lab.T("quest.lost", "Bring the physical crate to the recipient.")); break;
                    }
                    world.Signal(station == DeliveryStation.Assistant ? "parcel.delivered.assistant" : "parcel.delivered");
                    if (reward.Claim()) lab.Message(lab.T("quest.reward", "Delivery complete. Reward received."));
                    break;
            }
            return false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (station == DeliveryStation.Board && lab && lab.Actor) lab.Actor.ActionStateChanged += OnAction;
        }
        protected override void OnDisable()
        {
            if (station == DeliveryStation.Board && lab && lab.Actor) lab.Actor.ActionStateChanged -= OnAction;
            base.OnDisable();
        }
        private void OnAction(InteractionActionEvent action)
        {
            if (action.State == InteractionActionState.Committed && action.Action is GrabAction && action.Action.transform == lab.Crate)
                CheckPickup();
        }
        private void CheckPickup()
        {
            var run = world.Session.Find(delivery.id);
            if (run?.Current.id == "pickup" && lab.Crate.TryGetComponent<GrabAction>(out var grab) && grab.IsHeld)
                world.Signal("parcel.collected");
        }
    }
}
