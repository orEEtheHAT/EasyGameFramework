using EasyInteraction;
using EasyQuests;
using UnityEngine;

namespace EasySuite.Example
{
    public enum OrderStation { Board, AmberParcel, BlueParcel, Departure, Witness, Letter, Alice, Boris, Assistant }

    public sealed class RoleOrderStation : InteractionAction, INarrativeFactSource
    {
        public const string Amber = "orders.amber", Blue = "orders.blue", Hero = "orders.hero";
        public const string Loaded = "order.loaded", Away = "order.recipient.away", Clue = "order.redirect.known";
        public EasySuiteLab lab;
        public QuestWorld world;
        public QuestOrder amberOrder, blueOrder;
        public OrderStation station;
        public static string TargetId(OrderStation station) => "orders.station." + station;
        public static FactAddress Cargo(string order) => new(FactScope.QuestInstance, order, Loaded);
        public static FactAddress Absence => new(FactScope.Character,TargetId(OrderStation.Alice),Away);
        public void DescribeFacts(System.Action<WorldFactDatabase,string,FactAccess> add)
        {
            if (!world) return;
            switch (station)
            {
                case OrderStation.Departure: add(world.database,Away,FactAccess.Write); break;
                case OrderStation.Witness:
                case OrderStation.Letter:
                    add(world.database,Away,FactAccess.Read);
                    add(world.database,Clue,FactAccess.Write); break;
                case OrderStation.AmberParcel:
                case OrderStation.BlueParcel: add(world.database,Loaded,FactAccess.Write); break;
            }
        }
        public override string GetLabel(InteractionActor actor) => lab.T(LabelKey,ActionLabel);

        protected override bool OnExecute(InteractionActor actor)
        {
            Apply();
            return false;
        }

        public void Apply()
        {
            var session = world.Session;
            string origin = TargetId(station);
            switch (station)
            {
                case OrderStation.Board:
                    bool first = session.Find(Amber) == null;
                    amberOrder.StartOrder();
                    blueOrder.StartOrder();
                    if (first)
                    {
                        session.Track(Amber);
                        if (session.Pinned.Count < QuestSession.MaxPins) session.Pin(Blue);
                    }
                    return;
                case OrderStation.Departure:
                    session.SetFact(Absence,1,origin);
                    break;
                case OrderStation.Witness:
                case OrderStation.Letter:
                    if (session.GetFact(Absence) == 1) session.Learn(Hero, Clue, origin);
                    else lab.Message(lab.T("orders.nothing","The recipient is still at the counter."));
                    break;
                case OrderStation.AmberParcel:
                case OrderStation.BlueParcel:
                    string id = station == OrderStation.AmberParcel ? Amber : Blue;
                    if (session.Find(id) is { Finished: false }) session.SetFact(Cargo(id),1,origin);
                    break;
                case OrderStation.Alice: Deliver(session,Amber,"handover",origin); break;
                case OrderStation.Boris: Deliver(session,Blue,"handover",origin); break;
                case OrderStation.Assistant: Deliver(session,Amber,"alternate",origin); break;
            }
        }

        private static void Deliver(QuestSession session,string id,string step,string origin)
        {
            if (session.Find(id)?.Current.id == step) session.SignalFor(id,"order.delivered",1,origin);
        }
    }
}
