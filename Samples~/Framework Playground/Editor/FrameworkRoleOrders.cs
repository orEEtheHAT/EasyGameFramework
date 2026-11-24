using System;
using System.Collections.Generic;
using System.Linq;
using EasyLocalization;
using EasyQuests;
using EasyQuests.UI;
using EasyResources;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkRoleOrders
    {
        private const string Folder = EasySuiteSceneBuilder.Content + "/Quests/";
        private static readonly (OrderStation station, string en, string ru)[] Stations = {
            (OrderStation.Board,"Accept two orders","Принять два заказа"), (OrderStation.AmberParcel,"Take amber parcel","Взять янтарную посылку"),
            (OrderStation.BlueParcel,"Take blue parcel","Взять синюю посылку"), (OrderStation.Departure,"Announce Alice's departure","Объявить об отъезде Алисы"),
            (OrderStation.Witness,"Ask the witness","Спросить свидетеля"), (OrderStation.Letter,"Read the letter","Прочитать письмо"),
            (OrderStation.Alice,"Deliver to Alice","Передать Алисе"), (OrderStation.Boris,"Deliver to Boris","Передать Борису"),
            (OrderStation.Assistant,"Deliver to the assistant","Передать помощнику")
        };

        internal static void Apply(EasySuiteLab lab)
        {
            var world = lab.GetComponent<QuestWorld>();
            var facts = new List<WorldFact>(world.database.facts);
            foreach (string id in new[] {RoleOrderStation.Loaded, RoleOrderStation.Away, RoleOrderStation.Clue})
                if (!facts.Exists(f=>f.id==id)) facts.Add(new WorldFact {id=id,name=id});
            world.database.facts=facts.ToArray();
            EditorUtility.SetDirty(world.database);

            var strings=Stations.Select(x=>("orders.action."+x.station,x.en,x.ru)).ToList();
            strings.AddRange(Stations.Select(x=>(RoleOrderStation.TargetId(x.station),x.en,x.ru)));
            strings.AddRange(new[] {
                ("orders.nothing","The recipient is still at the counter.","Получатель всё ещё у стойки."),
                ("orders.amber.title","Amber delivery","Янтарный заказ"), ("orders.blue.title","Blue delivery","Синий заказ"),
                ("orders.pickup","Collect the assigned parcel","Заберите назначенную посылку"),("orders.handover","Deliver to the recipient","Передайте получателю"),
                ("orders.knowledge","Ask the witness or read the letter","Спросите свидетеля или прочитайте письмо"),
                ("orders.alternate","Deliver to the assistant","Передайте помощнику"),("orders.done","Order delivered","Заказ доставлен"),
                ("quests.search","Search quests","Найти задание"), ("quests.noMatches","No matching quests","Ничего не найдено"),
                ("quests.category.All","All","Все"), ("quests.category.Main","Main","Основные"), ("quests.category.Side","Side","Побочные"),
                ("quests.category.Completed","Completed","Завершённые"), ("quests.trackSelected","Track","Отслеживать"),
                ("quests.unpin","Unpin","Открепить"), ("quests.pin","Pin","Закрепить"), ("quests.routeChanged","Route changed","Маршрут изменился"),
                ("quests.newObjective","New objective","Новая цель"), ("quests.completed","Quest completed","Задание завершено"),
                ("quests.rewardReady","Reward available","Доступна награда")
            });
            FrameworkDialoguePlayground.InstallStrings(lab,strings.ToArray());
            var reward=AssetDatabase.LoadAssetAtPath<ResourceRewardProfile>(Folder+"Order reward.asset");
            if (!reward)
            {
                reward=ResourceRewardProfile.Create("Order reward",lab.ResourceDatabase,ResourceRewardRange.FromQuantity(lab.Coin,2,2));
                AssetDatabase.CreateAsset(reward,Folder+"Order reward.asset");
            }
            var quest=AssetDatabase.LoadAssetAtPath<QuestDefinition>(Folder+"Repeatable delivery.asset");
            if (!quest)
            {
                quest=Graph(world.database,reward);
                quest.iconLibrary=AssetDatabase.LoadAssetAtPath<QuestIconLibrary>(Folder+"Marker Icons.asset");
                quest.defaultIconId=quest.iconLibrary.icons.FirstOrDefault(e=>e.name=="Side")?.Id;
                AssetDatabase.CreateAsset(quest,Folder+"Repeatable delivery.asset");
            }
            if (!world.quests.Contains(quest)) world.quests=world.quests.Append(quest).ToArray();
            EditorUtility.SetDirty(world);

            var journal=lab.Windows.GetComponentInChildren<QuestJournal>(true);
            if (!journal.GetComponent<QuestJournalBrowser>()) journal.gameObject.AddComponent<QuestJournalBrowser>();
            var hud=lab.Windows.GetComponentInChildren<QuestMarkerHud>(true);
            hud.clusterMarkers=true;
            var notifications=hud.GetComponent<QuestNotifications>() ?? hud.gameObject.AddComponent<QuestNotifications>();
            notifications.world=world;notifications.translations=lab.Texts;notifications.feedback=lab.UiFeedback;
            const string rootName="Repeatable orders • two recipients";
            var root=lab.gameObject.scene.GetRootGameObjects().FirstOrDefault(o=>o.name==rootName) ?? new GameObject(rootName);
            var existingOrders=root.GetComponentsInChildren<QuestOrder>(true);
            var existingStations=root.GetComponentsInChildren<RoleOrderStation>(true);
            QuestOrder Order(string id,string title,OrderStation parcel,OrderStation recipient)
            {
                var order=existingOrders.FirstOrDefault(o=>o.instanceId==id);
                if(order)return order;
                var go=new GameObject(title);go.transform.SetParent(root.transform,false);
                order=go.AddComponent<QuestOrder>();order.world=world;order.template=quest;order.instanceId=id;order.title=title;order.titleKey=id+".title";
                QuestRoleBinding Bind(string role,string target)=>new(){role=role,target=target};
                order.roles=new[]{Bind("parcel",RoleOrderStation.TargetId(parcel)),Bind("recipient",RoleOrderStation.TargetId(recipient)),
                    Bind("assistant",RoleOrderStation.TargetId(OrderStation.Assistant)),Bind("witness",RoleOrderStation.TargetId(OrderStation.Witness)),
                    Bind("traveler",RoleOrderStation.Hero)};
                return order;
            }
            var amber=Order(RoleOrderStation.Amber,"Amber delivery",OrderStation.AmberParcel,OrderStation.Alice);
            var blue=Order(RoleOrderStation.Blue,"Blue delivery",OrderStation.BlueParcel,OrderStation.Boris);
            var collection=LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var material=lab.Crate.GetComponent<Renderer>().sharedMaterial;
            GameObject alice=null,assistant=null;
            for(int i=0;i<Stations.Length;i++)
            {
                var row=Stations[i];
                var existing=existingStations.FirstOrDefault(s=>s.station==row.station);
                if(existing)
                {
                    if(row.station==OrderStation.Board){existing.amberOrder=amber;existing.blueOrder=blue;EditorUtility.SetDirty(existing);}
                    if(row.station==OrderStation.Alice)alice=existing.gameObject;
                    if(row.station==OrderStation.Assistant)assistant=existing.gameObject;
                    continue;
                }
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name=row.station.ToString();go.transform.SetParent(root.transform,false);
                go.transform.position=new Vector3(-2.7f+(i%5)*2.05f,.45f,i<5?9.95f:7.65f);
                go.transform.localScale=new Vector3(.72f,.9f,.72f);
                go.GetComponent<Renderer>().sharedMaterial=material;
                var action=go.AddComponent<RoleOrderStation>();action.station=row.station;action.world=world;action.lab=lab;
                if(row.station==OrderStation.Board){action.amberOrder=amber;action.blueOrder=blue;}
                action.ActionId=RoleOrderStation.TargetId(row.station);action.ActionLabel=row.en;action.LabelKey="orders.action."+row.station;
                action.Target.DisplayName=row.en;action.Target.PersistentId=action.ActionId;
                var anchor=go.AddComponent<QuestTarget>();anchor.world=world;anchor.id=action.ActionId;anchor.offset=Vector3.up*1.1f;
                var sign=new GameObject("Station sign",typeof(RectTransform));sign.transform.SetParent(go.transform,false);sign.transform.localPosition=Vector3.up*1.1f;
                var text=sign.AddComponent<TextMeshPro>();text.font=UIText.DefaultFont;text.fontSize=1.8f;text.alignment=TextAlignmentOptions.Center;
                text.rectTransform.sizeDelta=new Vector2(2.4f,1f);text.text=row.en;
                var loc=sign.AddComponent<LocalizedText>();loc.fallback=row.en;
                loc.text=new LocalizedString(collection.SharedData.TableCollectionNameGuid,collection.SharedData.GetEntry(action.LabelKey).Id);
                sign.AddComponent<EasySuiteLabel>();
                if(row.station==OrderStation.Alice)alice=go;
                if(row.station==OrderStation.Assistant)assistant=go;
            }
            var reaction=root.GetComponent<WorldStateReaction>() ?? root.AddComponent<WorldStateReaction>();reaction.world=world;
            reaction.condition=new FactCondition{scope=FactScope.Character,subject=RoleOrderStation.TargetId(OrderStation.Alice),fact=RoleOrderStation.Away};
            reaction.showWhenTrue=assistant;reaction.showWhenFalse=alice;
            AssetDatabase.SaveAssets();
        }

        private static QuestDefinition Graph(WorldFactDatabase database,ResourceRewardProfile reward)
        {
            var q=ScriptableObject.CreateInstance<QuestDefinition>();
            q.id="suite.orders";q.title="Parcel order";q.category=QuestCategory.Side;q.database=database;
            QuestGoal Cargo(string id)=>new(){id=id,title="Collect the assigned parcel",textKey="orders.pickup",mode=GoalMode.WorldState,
                condition=new FactCondition{scope=FactScope.QuestInstance,fact=RoleOrderStation.Loaded},marker=MarkerStyle.Target,target="$parcel"};
            QuestGoal Handover(string id,string who,string text,string key)=>new(){id=id,title=text,textKey=key,mode=GoalMode.NewEvents,
                signal="order.delivered",marker=MarkerStyle.Target,target=who};
            QuestStep Stage(string id,string next,int x,int y,params QuestGoal[] goals)=>new(){id=id,name=goals[0].title,nameKey=goals[0].textKey,
                type=QuestStepType.Objectives,next=next,goals=goals,position=new Vector2(x,y)};
            var clue=new QuestGoal{id="interview",title="Ask the witness or read the letter",textKey="orders.knowledge",mode=GoalMode.WorldState,
                condition=new FactCondition{scope=FactScope.Character,subject="$traveler",fact=RoleOrderStation.Clue},marker=MarkerStyle.Target,target="$witness"};
            q.steps=new[]{
                new QuestStep{id="start",type=QuestStepType.Start,next="pickup",position=new Vector2(20,50)},
                Stage("pickup","handover",230,50,Cargo("pickup-parcel")),
                Stage("handover","done",450,50,Handover("deliver","$recipient","Deliver to the recipient","orders.handover")),
                Stage("investigate","alternate",230,210,clue,Cargo("redirect-parcel")),
                Stage("alternate","done",450,210,Handover("deliver-alternate","$assistant","Deliver to the assistant","orders.alternate")),
                new QuestStep{id="done",type=QuestStepType.End,name="Delivered",outcome="Order delivered",outcomeKey="orders.done",
                    rewardMode=QuestRewardMode.Automatic,rewardProfile=reward,position=new Vector2(680,90)}
            };
            q.rules=new[]{new QuestRule{id="recipient-away",when=new FactCondition{scope=FactScope.Character,subject="$recipient",fact=RoleOrderStation.Away},destination="investigate"}};
            q.ValidateGraph();return q;
        }
    }
}
