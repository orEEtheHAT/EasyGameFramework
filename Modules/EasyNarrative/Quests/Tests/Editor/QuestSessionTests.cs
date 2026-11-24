using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace EasyQuests.Tests
{
    public sealed class QuestSessionTests
    {
        private WorldFactDatabase db;
        private QuestDefinition q;
        private QuestSession game;
        [SetUp]
        public void Prepare()
        {
            db = ScriptableObject.CreateInstance<WorldFactDatabase>();
            db.facts = new[] { new WorldFact { id = "bridge", name = "Bridge" }, new WorldFact { id = "counter", type = FactType.Integer } };
            q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.database = db; q.id = "order";
            q.steps = new[]{
                new QuestStep{id="begin",type=QuestStepType.Start,next="wait"},
                new QuestStep{id="wait",type=QuestStepType.Objectives,join=GoalJoin.Any,next="deliver",goals=new[]{
                    new QuestGoal{id="bridge",mode=GoalMode.WorldState,condition=new FactCondition{fact="bridge",value=1}},
                    new QuestGoal{id="ferry",mode=GoalMode.NewEvents,signal="ferry",count=2}}},
                new QuestStep{id="deliver",type=QuestStepType.Objectives,next="done",goals=new[]{new QuestGoal{id="give",mode=GoalMode.NewEvents,signal="give"}}},
                new QuestStep{id="done",type=QuestStepType.End,grantReward=true}};
            game = new QuestSession(db, new[] { q });
        }
        [TearDown] public void Clean() { UnityEngine.Object.DestroyImmediate(q); UnityEngine.Object.DestroyImmediate(db); }

        [Test]
        public void PooledGroupMemberMovesBetweenWorldsWithoutLosingOtherMarkers()
        {
            var root = new GameObject("Registry test");
            try
            {
                var one = root.AddComponent<QuestWorld>();
                var two = root.AddComponent<QuestWorld>();
                var a = root.AddComponent<QuestTarget>();
                var b = root.AddComponent<QuestTarget>();
                a.Configure(one, "scout", "patrol");
                b.Configure(one, null, "patrol");
                Assert.AreSame(a, one.Target("scout"));
                Assert.AreEqual(2, one.Group("patrol").Count);
                a.Configure(two, "scout", "guards");
                Assert.IsNull(one.Target("scout"));
                CollectionAssert.AreEqual(new[] { b }, one.Group("patrol"));
                CollectionAssert.AreEqual(new[] { a }, two.Group("guards"));
                a.Configure(null, null);
                Assert.IsEmpty(two.Group("guards"));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private QuestSession WithRule()
        {
            q.steps = q.steps.Append(new QuestStep { id = "assistant", type = QuestStepType.Objectives, next = "done",
                goals = new[] { new QuestGoal { id = "assist", mode = GoalMode.NewEvents, signal = "assist" } } }).ToArray();
            q.rules = new[] { new QuestRule { id = "away", when = new FactCondition { fact = "counter", value = 1 }, destination = "assistant" } };
            return new QuestSession(db, new[] { q });
        }

        [Test]
        public void RuleInterruptsAnObjectiveAndRestoresWithoutReplay()
        {
            game = WithRule();
            game.StartQuest(q.id);
            game.Signal("ferry");
            var before = game.Capture();
            var events = new System.Collections.Generic.List<QuestTransitionKind>();
            game.Transitioned += transition => events.Add(transition.Kind);
            game.SetFact("counter", 1);
            CollectionAssert.AreEqual(new[] { QuestTransitionKind.StepInterrupted, QuestTransitionKind.StepEntered }, events);
            var after = game.Capture();
            Assert.AreEqual(1, game.Find(q.id).Progress(q.steps[1].goals[1]));
            events.Clear();
            game.Restore(before);
            Assert.AreEqual("wait", game.Find(q.id).Current.id);
            game.Restore(after);
            Assert.IsTrue(game.Find(q.id).HasTakenRule("away"));
            Assert.AreEqual("assistant", game.Find(q.id).Current.id);
            Assert.IsEmpty(events);
            game.Signal("ferry"); game.SetFact("counter", 0); game.SetFact("counter", 1);
            Assert.IsEmpty(events);
            game.Signal("assist");
            Assert.IsTrue(game.Find(q.id).Finished);
            var invalid = after;
            invalid.quests[0].takenRules = Array.Empty<string>();
            Assert.Throws<ArgumentException>(() => game.Restore(invalid));
            Assert.IsTrue(game.Find(q.id).Finished);
        }

        [Test]
        public void AlreadyTrueRuleTakesFirstMatchAtStartAndCannotRewindAfterMerge()
        {
            WithRule();
            var second = new QuestRule { id = "fallback", when = new FactCondition { fact = "counter", value = 1 }, destination = "deliver" };
            q.rules = q.rules.Append(second).ToArray();
            game = new QuestSession(db, new[] { q });
            game.SetFact("counter", 1);
            game.StartQuest(q.id);
            CollectionAssert.AreEqual(new[] { "begin", "assistant" }, game.Find(q.id).History);
            q.steps.Last().next = "deliver";
            game = new QuestSession(db, new[] { q });
            game.StartQuest(q.id); game.SetFlag("bridge", true);
            game.SetFact("counter", 1);
            Assert.AreEqual("deliver", game.Find(q.id).Current.id);
            Assert.IsFalse(game.Find(q.id).HasTakenRule("away"));
        }

        [Test]
        public void FactWrittenInsideSameGraphCanRedirectBeforeNormalEnding()
        {
            WithRule();
            var write = new QuestStep { id = "depart", type = QuestStepType.SetFact, fact = "counter", value = 1, next = "done" };
            q.steps = q.steps.Append(write).ToArray();
            q.steps[2].next = "depart";
            game = new QuestSession(db, new[] { q });
            game.StartQuest(q.id); game.SetFlag("bridge", true); game.Signal("give");
            Assert.AreEqual("assistant", game.Find(q.id).Current.id);
            game.Restore(game.Capture());
            game.Signal("assist"); Assert.IsTrue(game.Find(q.id).Finished);
        }

        [Test]
        public void BrokenAndCyclicRuleRoutesAreDiagnosed()
        {
            WithRule();
            q.rules[0].destination = "missing"; Assert.Throws<ArgumentException>(q.ValidateGraph);
            q.rules[0].destination = "assistant"; q.steps.Last().next = "assistant";
            Assert.Throws<ArgumentException>(q.ValidateGraph);
        }

        [Test]
        public void EarlierWorldStateCountsButEarlierSignalDoesNot()
        {
            game.Signal("give"); game.SetFlag("bridge", true); game.StartQuest(q.id);
            Assert.AreEqual("deliver", game.Find(q.id).Current.id); game.Signal("give"); Assert.IsTrue(game.Find(q.id).Finished);
        }
        [Test]
        public void SaveInParallelProgressAndRestoreTwice()
        {
            game.StartQuest(q.id); game.Signal("ferry"); var snapshot = game.Capture();
            game.Signal("ferry"); game.Signal("give"); game.Restore(snapshot); game.Restore(snapshot);
            Assert.AreEqual(1, game.Find(q.id).Progress(q.steps[1].goals[1])); game.Signal("ferry"); Assert.AreEqual("deliver", game.Find(q.id).Current.id);
            Assert.AreEqual(0, game.GetFact("bridge"));
        }
        [Test]
        public void CompletedRewardIsCommittedOnceAndStaysClaimed()
        {
            game.SetFlag("bridge", true); game.StartQuest(q.id); game.Signal("give");
            int paid = 0; Assert.False(game.TryClaimReward(q.id, () => false));
            Assert.True(game.TryClaimReward(q.id, () => { paid++; return true; }));
            game.Restore(game.Capture()); game.Signal("give"); Assert.False(game.TryClaimReward(q.id, () => { paid++; return true; })); Assert.AreEqual(1, paid);
        }
        [Test]
        public void InvalidRestoreLeavesOriginalStateUntouched()
        {
            game.StartQuest(q.id); var data = game.Capture(); data.quests[0].path = new[] { "begin", "done" };
            Assert.Throws<ArgumentException>(() => game.Restore(data)); Assert.AreEqual("wait", game.Find(q.id).Current.id);
        }
        [Test]
        public void AutoStartAndSeparateSlots()
        {
            q.autoStart = true; q.startWhen = new FactCondition { fact = "counter", comparison = Comparison.AtLeast, value = 3 };
            var one = new QuestSession(db, new[] { q }); var two = new QuestSession(db, new[] { q });
            one.SetFact("counter", 3); Assert.NotNull(one.Find(q.id)); Assert.IsNull(two.Find(q.id)); Assert.AreEqual(0, two.GetFact("counter"));
        }
        [Test]
        public void OptionalGoalDoesNotGateAllAndLosingWaitIsRemoved()
        {
            q.steps[1].join = GoalJoin.All; q.steps[1].goals[1].optional = true;
            game.StartQuest(q.id); game.SetFlag("bridge", true); long before = game.Evaluations; game.Signal("ferry");
            Assert.AreEqual(before, game.Evaluations); Assert.AreEqual("deliver", game.Find(q.id).Current.id);
        }
        [Test]
        public void ObserversCanRespondAfterTheWorldHasSettled()
        {
            game.StartQuest(q.id);
            game.FactChanged += (id, value) => { if (id == "bridge" && value == 1) game.Signal("give"); };
            game.SetFlag("bridge", true);
            Assert.That(game.Find(q.id).Finished);
        }
        [Test]
        public void LiveTransitionsKeepEveryCardAndDoNotReplayOnLoad()
        {
            var seen = new System.Collections.Generic.List<string>();
            game.Transitioned += t =>
            {
                Assert.AreSame(q, t.Quest);
                seen.Add(t.Kind + "/" + t.Step.id);
                if (t.Kind == QuestTransitionKind.StepEntered && t.Step.id == "deliver") game.Signal("give");
            };
            game.SetFlag("bridge", true);
            game.StartQuest(q.id);
            CollectionAssert.AreEqual(new[] {
                "QuestStarted/begin", "StepEntered/begin", "StepCompleted/begin",
                "StepEntered/wait", "StepCompleted/wait", "StepEntered/deliver",
                "StepCompleted/deliver", "StepEntered/done", "QuestCompleted/done"
            }, seen);
            seen.Clear();
            game.Restore(game.Capture());
            game.StartQuest(q.id);
            Assert.IsEmpty(seen);
        }

        [Test]
        public void SharedSignalCompletesBothQuestsRegardlessOfTracking()
        {
            var other = UnityEngine.Object.Instantiate(q);
            try
            {
                other.id = "second-order";
                var world = new QuestSession(db, new[] { q, other });
                var finished = new System.Collections.Generic.List<QuestDefinition>();
                world.Transitioned += t => { if (t.Kind == QuestTransitionKind.QuestCompleted) finished.Add(t.Quest); };
                world.SetFlag("bridge", true);
                world.StartQuest(q.id);
                world.StartQuest(other.id);
                world.Track(other.id);
                world.Signal("give");
                CollectionAssert.AreEquivalent(new[] { q, other }, finished);
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }

        [Test]
        public void CorruptProgressCannotSkipAnObjective()
        {
            game.SetFlag("bridge", true); game.StartQuest(q.id); game.Signal("give");
            var bad = game.Capture(); bad.quests[0].progress = Array.Empty<SavedGoal>();
            Assert.Throws<ArgumentException>(() => game.Restore(bad));
            Assert.IsTrue(game.Find(q.id).Finished);
        }
        [Test]
        public void BrokenLinkAndCycleHaveDiagnostics()
        {
            q.steps[1].next = "unknown"; Assert.Throws<ArgumentException>(q.ValidateGraph);
            q.steps[1].next = "begin"; Assert.Throws<ArgumentException>(q.ValidateGraph);
        }
        [Test]
        public void UnrelatedCatalogEntriesDoNotReceiveEvents()
        {
            game.StartQuest(q.id); long before = game.Evaluations; game.SetFact("counter", 42); game.Signal("other"); Assert.AreEqual(before, game.Evaluations);
        }
    }
}
