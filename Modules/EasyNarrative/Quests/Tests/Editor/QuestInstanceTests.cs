using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace EasyQuests.Tests
{
    public sealed class QuestInstanceTests
    {
        private WorldFactDatabase database;
        private QuestDefinition template;
        private QuestSession session;
        private QuestGoal cargo,knowledge;
        [SetUp] public void Prepare()
        {
            database=ScriptableObject.CreateInstance<WorldFactDatabase>();
            database.facts=new[]{new WorldFact{id="loaded"},new WorldFact{id="clue"}};
            template=ScriptableObject.CreateInstance<QuestDefinition>();template.id="order";template.database=database;
            cargo=new QuestGoal{id="cargo",mode=GoalMode.WorldState,condition=new FactCondition{scope=FactScope.QuestInstance,fact="loaded"}};
            knowledge=new QuestGoal{id="clue",mode=GoalMode.WorldState,condition=new FactCondition{scope=FactScope.Character,subject="$recipient",fact="clue"}};
            template.steps=new[]{
                new QuestStep{id="start",type=QuestStepType.Start,next="find"},
                new QuestStep{id="find",type=QuestStepType.Objectives,goals=new[]{cargo,knowledge},next="deliver"},
                new QuestStep{id="deliver",type=QuestStepType.Objectives,goals=new[]{new QuestGoal{id="give",mode=GoalMode.NewEvents,signal="give",marker=MarkerStyle.Target,target="$recipient"}},next="done"},
                new QuestStep{id="done",type=QuestStepType.End,grantReward=true}
            };
            session=new QuestSession(database,new[]{template});
        }
        [TearDown] public void Clean(){UnityEngine.Object.DestroyImmediate(template);UnityEngine.Object.DestroyImmediate(database);}
        private QuestRun Start(string id,string who)=>session.StartInstance(template,id,new[]{new QuestRoleBinding{role="recipient",target=who}},id);
        private FactAddress Load(string id)=>new(FactScope.QuestInstance,id,"loaded");

        [Test] public void RolesFactsEvidenceAndFocusSurviveSaveAsIndependentOrders()
        {
            var a=Start("a","Alice");var b=Start("b","Boris");
            session.TrackGoal("a","clue");session.Pin("b");
            session.SetFact(Load("a"),1,"crate");session.SetFact(new FactAddress(FactScope.Group,"couriers","clue"),1,"guild");
            Assert.AreEqual(1,a.Progress(cargo));Assert.AreEqual(0,b.Progress(cargo));
            Assert.False(session.Knows("Alice","clue"));
            var saved=JsonUtility.FromJson<QuestSnapshot>(JsonUtility.ToJson(session.Capture()));
            int transitions=0;session.Transitioned+=_=>transitions++;session.Restore(saved);
            Assert.AreEqual(0,transitions);Assert.AreEqual("clue",session.TrackedGoal);CollectionAssert.AreEqual(new[]{"b"},session.Pinned);
            a=session.Find("a");b=session.Find("b");
            Assert.AreEqual("Alice",a.ResolveTarget("$recipient"));Assert.AreEqual("Boris",b.ResolveTarget("$recipient"));
            session.Learn("Alice","clue","letter");session.Learn("Alice","clue","witness");
            Assert.AreEqual("letter",a.Evidence(knowledge));Assert.AreEqual("deliver",a.Current.id);
            Assert.AreEqual("find",b.Current.id);Assert.IsNull(session.TrackedGoal);
            Assert.False(session.SignalFor("b","give"));Assert.True(session.SignalFor("a","give",1,"counter"));
            Assert.AreEqual("counter",a.Evidence(template.steps[2].goals[0]));
            Assert.True(session.TryClaimReward("a",()=>true));Assert.False(session.TryClaimReward("a",()=>true));
            session.Restore(session.Capture());
            Assert.True(session.Find("a").RewardClaimed);Assert.False(session.Find("b").Finished);
            Assert.AreEqual("letter",session.LastSource(new FactAddress(FactScope.Character,"Alice","clue")));
        }

        [Test] public void InvalidInstanceBindingsOrSnapshotCannotReplaceLiveOrders()
        {
            Assert.Throws<ArgumentException>(()=>session.StartInstance(template,"bad"));
            Assert.IsNull(session.Find("bad"));
            Start("a","Alice");var corrupt=session.Capture();corrupt.quests[0].roles=Array.Empty<QuestRoleBinding>();
            Assert.Throws<ArgumentException>(()=>session.Restore(corrupt));Assert.AreEqual("Alice",session.Find("a").ResolveTarget("$recipient"));
            Assert.Throws<ArgumentException>(()=>session.SetFact(Load("unknown"),1));
            Assert.Throws<ArgumentException>(()=>Start("order","Alice"));
        }

        [Test] public void OldSingletonSnapshotsWithoutInstanceFieldsRemainReadable()
        {
            knowledge.condition.scope=FactScope.World;knowledge.condition.subject=null;
            cargo.condition.scope=FactScope.World;template.steps[2].goals[0].target="recipient";
            session=new QuestSession(database,new[]{template});session.StartQuest(template.id);var saved=session.Capture();
            saved.quests.Single().definition=string.Empty;saved.quests.Single().roles=null;saved.pinned=null;saved.trackedGoal="";
            session.Restore(saved);Assert.AreEqual("find",session.Find(template.id).Current.id);Assert.IsNull(session.TrackedGoal);
        }
    }
}
