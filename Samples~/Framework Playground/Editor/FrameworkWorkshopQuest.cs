using System;
using System.Linq;
using EasyInteraction;
using EasyLocalization;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyQuests;
using EasyQuests.Editor;
using EasyResources;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkWorkshopQuest
    {
        private const string Folder = EasySuiteSceneBuilder.Content + "/Quests/";
        private const string Alternate = "workshop.calibration";
        internal static void Apply(EasySuiteLab lab)
        {
            var world = lab.GetComponent<QuestWorld>();
            world.wallet = lab.Wallet;
            var db = world.database;
            if (!db.facts.Any(f => f.id == Alternate))
                db.facts = db.facts.Append(new WorldFact { id = Alternate, name = "Workshop calibration route" }).ToArray();
            EditorUtility.SetDirty(db);
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("workshop.title","Workshop order","Заказ мастерской"),("workshop.master","Workshop master","Мастер"),
                ("workshop.ask","Bring three crystals, or calibrate the instrument for the workshop.","Принесите три кристалла или откалибруйте прибор для мастерской."),
                ("workshop.accept","I'll take the order.","Я возьму заказ."), ("workshop.alternate","I'll calibrate the instrument instead.","Лучше откалибрую прибор."),
                ("workshop.leave","Goodbye.","До встречи."),("workshop.have","Have three crystals","Соберите три кристалла"),
                ("workshop.give","Hand over three crystals","Передайте три кристалла"),("workshop.calibrate","Calibrate instrument","Откалибруйте прибор"),
                ("workshop.paid","Crystals delivered","Кристаллы переданы"),("workshop.claim","Collect eight coins","Заберите восемь монет"),
                ("workshop.talk","Talk to the workshop master","Поговорить с мастером")
            });
            ResourceRewardProfile Reward(int coins)
            {
                string path=Folder+"Workshop Reward "+coins+".asset";
                var profile=AssetDatabase.LoadAssetAtPath<ResourceRewardProfile>(path);
                if (!profile) { profile=ResourceRewardProfile.Create("Workshop reward",lab.ResourceDatabase,ResourceRewardRange.FromQuantity(lab.Coin,coins,coins)); AssetDatabase.CreateAsset(profile,path); }
                return profile;
            }
            var q=AssetDatabase.LoadAssetAtPath<QuestDefinition>(Folder+"Workshop Order.asset");
            if (!q)
            {
                q=ScriptableObject.CreateInstance<QuestDefinition>();q.database=db;q.id="suite.workshop";q.title="Workshop order";q.titleKey="workshop.title";
                QuestStep Step(string id,QuestStepType kind,string name,string next,int x,int y) =>
                    new QuestStep{id=id,type=kind,name=name,next=next,position=new Vector2(x,y)};
                var start=Step("start",QuestStepType.Start,"Accepted","have",20,80);
                var have=Step("have",QuestStepType.Objectives,"Have three crystals","give",215,80);
                var give=Step("give",QuestStepType.Objectives,"Hand over crystals","paid",410,80);
                var paid=Step("paid",QuestStepType.End,"Materials delivered",null,605,80);
                var tune=Step("tune",QuestStepType.Objectives,"Calibration","desk",410,240);
                var desk=Step("desk",QuestStepType.End,"Calibration complete",null,605,240);
                int units=checked((int)lab.Crystal.ToUnits(3));
                have.nameKey="workshop.have";have.goals=new[]{ new QuestGoal{id="stock",mode=GoalMode.HaveResource,resource=lab.Crystal,count=units,title=have.name,textKey=have.nameKey} };
                give.nameKey="workshop.give";give.goals=new[]{ new QuestGoal{id="pay",mode=GoalMode.TurnInResource,resource=lab.Crystal,count=units,title=give.name,textKey=give.nameKey,marker=MarkerStyle.Target,target="workshop.counter"} };
                tune.nameKey="workshop.calibrate";tune.goals=new[]{new QuestGoal{id="tuned",mode=GoalMode.NewEvents,signal="workshop.tuned",title=tune.name,textKey=tune.nameKey,marker=MarkerStyle.Target,target="workshop.instrument"}};
                paid.rewardMode=QuestRewardMode.Automatic;paid.rewardProfile=Reward(5);paid.outcome="Crystals delivered";paid.outcomeKey="workshop.paid";
                desk.rewardMode=QuestRewardMode.AtInteraction;desk.rewardProfile=Reward(8);desk.outcome="Collect eight coins";desk.outcomeKey="workshop.claim";
                q.rules=new[]{new QuestRule{id="alternate",when=new FactCondition{fact=Alternate,value=1},destination=tune.id}};
                q.steps=new[]{start,have,give,paid,tune,desk};q.ValidateGraph();AssetDatabase.CreateAsset(q,Folder+"Workshop Order.asset");
            }
            if (!world.quests.Contains(q)) world.quests=world.quests.Append(q).ToArray();
            EditorUtility.SetDirty(world);
            var d=AssetDatabase.LoadAssetAtPath<DialogueDefinition>(Folder+"Workshop Master.asset");
            if (!d)
            {
                d=ScriptableObject.CreateInstance<DialogueDefinition>();d.database=db;d.entry="hello";
                var line=new DialogueLine{id="hello",name="Order",speaker="Workshop master",speakerKey="workshop.master",text="Bring three crystals or calibrate the instrument.",textKey="workshop.ask"};
                line.replies=new[]{
                    new DialogueReply{id="accept",text="Take the order",textKey="workshop.accept",when=new[]{new DialogueRequirement{kind=DialogueRequirementType.QuestNotStarted,quest=q}},action=new DialogueAction{kind=DialogueActionType.StartQuest,quest=q}},
                    new DialogueReply{id="alternate",text="Calibrate instead",textKey="workshop.alternate",when=new[]{new DialogueRequirement{kind=DialogueRequirementType.QuestActive,quest=q},new DialogueRequirement{kind=DialogueRequirementType.WorldFact,fact=new FactCondition{fact=Alternate,value=0}}},action=new DialogueAction{kind=DialogueActionType.SetFact,key=Alternate,value=1}},
                    new DialogueReply{id="bye",text="Goodbye",textKey="workshop.leave"} };
                d.lines=new[]{line};d.ValidateGraph();AssetDatabase.CreateAsset(d,Folder+"Workshop Master.asset");
            }
            var parent=new GameObject("Workshop quest").transform;
            var collection=LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            GameObject Station(string name,string key,string mat,float x)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.position=new Vector3(x,.65f,-8.8f);
                go.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(EasySuiteSceneBuilder.Content+"/Materials/"+mat+".mat");
                var sign=new GameObject("Station sign",typeof(RectTransform));sign.transform.SetParent(go.transform,false);sign.transform.localPosition=Vector3.up*1.15f;
                var text=sign.AddComponent<TextMeshPro>();text.font=UIText.DefaultFont;text.fontSize=2;text.rectTransform.sizeDelta=new Vector2(2.4f,1);text.alignment=TextAlignmentOptions.Center;text.text=name;
                var loc=sign.AddComponent<LocalizedText>();loc.fallback=name;loc.text=new LocalizedString(collection.SharedData.TableCollectionNameGuid,collection.SharedData.GetEntry(key).Id);sign.AddComponent<EasySuiteLabel>();
                return go;
            }
            var master=Station("Workshop master","workshop.master","Gold",1.8f).AddComponent<TalkAction>();master.dialogue=d;master.ActionLabel="Talk";master.LabelKey="workshop.talk";
            var counter=Station("Crystal counter","workshop.give","Blue",4.1f);
            var marker=counter.AddComponent<QuestTarget>();marker.world=world;marker.id="workshop.counter";
            var giveStep=q.steps.Single(s=>s.id=="give");
            QuestObjectiveWizard.BindHandIn(counter,world,q,giveStep,giveStep.goals[0]).LabelKey="workshop.give";
            var instrument=Station("Instrument","workshop.calibrate","Slate",6.4f);
            marker=instrument.AddComponent<QuestTarget>();marker.world=world;marker.id="workshop.instrument";
            var calibrate=instrument.AddComponent<EventAction>();calibrate.ActionLabel="Calibrate";calibrate.LabelKey="workshop.calibrate";
            UnityEventTools.AddStringPersistentListener(calibrate.OnCompleted,world.Signal,"workshop.tuned");
            QuestObjectiveWizard.BindHandIn(Station("Cash desk","workshop.claim","Gold",8.7f),world,q,q.steps.Single(s=>s.id=="desk"),null).LabelKey="workshop.claim";
            Undo.FlushUndoRecordObjects();
            AssetDatabase.SaveAssets();
        }
    }
}
