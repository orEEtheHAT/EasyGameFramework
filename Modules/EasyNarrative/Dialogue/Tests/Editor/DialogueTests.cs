using System;
using EasyQuests;
using NUnit.Framework;
using UnityEngine;

namespace EasyNarrative.Tests
{
    public sealed class DialogueTests
    {
        private WorldFactDatabase db;
        private QuestDefinition quest;
        private DialogueDefinition graph;
        private QuestSession world;

        [SetUp]
        public void SetUp()
        {
            db = ScriptableObject.CreateInstance<WorldFactDatabase>();
            db.facts = new[] { new WorldFact { id = "known", name = "Knows the secret" } };
            quest = ScriptableObject.CreateInstance<QuestDefinition>(); quest.id = "test"; quest.database = db;
            quest.steps = new[]{new QuestStep{id="s",type=QuestStepType.Start,next="g"},
                new QuestStep{id="g",type=QuestStepType.Objectives,next="e",goals=new[]{new QuestGoal{id="x",mode=GoalMode.NewEvents,signal="go",count=2}}},
                new QuestStep{id="e",type=QuestStepType.End}};
            world = new QuestSession(db, new[] { quest });
            graph = ScriptableObject.CreateInstance<DialogueDefinition>(); graph.entry = "hello"; graph.database = db;
            graph.lines = new[] { new DialogueLine { id = "hello", replies = new[] { new DialogueReply { id = "more", next = "next" } } }, new DialogueLine { id = "next" } };
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(graph); UnityEngine.Object.DestroyImmediate(quest); UnityEngine.Object.DestroyImmediate(db); }
        [Test]
        public void PricedChoiceRejectsWithoutEffectsThenCommitsOnlyOnce()
        {
            var coin = EasyResources.ResourceDefinition.Create("Coin");
            using var wallet = new EasyResources.ResourceStore();
            try
            {
                var reply = graph.lines[0].replies[0];
                reply.price = new EasyResources.ResourceCost(EasyResources.ResourceAmount.FromQuantity(coin, 5));
                reply.locked = LockedReply.Explain;
                reply.action = new DialogueAction { kind = DialogueActionType.SetFact, key = "known", value = 1 };
                reply.additionalActions = new[] { new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest } };
                using var run = new DialogueRun(graph, new DialogueContext(world, wallet, null, null));
                Assert.AreEqual(1, run.Displayed.Count);
                Assert.IsEmpty(run.Replies);
                Assert.IsFalse(run.Choose(reply.id, run.Revision));
                Assert.AreEqual(0, world.GetFact("known"));
                Assert.IsNull(world.Find(quest.id));
                wallet.Add(coin, 5);
                bool notifiedWithCompleteState = false;
                world.FactChanged += (_, _) => notifiedWithCompleteState = wallet.GetTotal(coin) == 0 && world.Find(quest.id) != null;
                int revision = run.Revision;
                Assert.IsTrue(run.Choose(reply.id, revision));
                Assert.AreEqual(0, wallet.GetTotal(coin));
                Assert.IsFalse(run.Choose(reply.id, revision));
                Assert.AreEqual(1, world.GetFact("known"));
                Assert.IsNotNull(world.Find(quest.id));
                Assert.IsTrue(notifiedWithCompleteState);
            }
            finally { UnityEngine.Object.DestroyImmediate(coin); }
        }

        [Test]
        public void AnyAndNoneGroupsRequireTheWholePriceWithoutPartialSpending()
        {
            var coin = EasyResources.ResourceDefinition.Create("Coin");
            var ore = EasyResources.ResourceDefinition.Create("Ore");
            using var wallet = new EasyResources.ResourceStore();
            try
            {
                DialogueRequirement Amount(EasyResources.ResourceDefinition resource, int quantity) =>
                    new() { kind = DialogueRequirementType.ResourceAvailable, resource = resource, quantity = quantity };
                var reply = graph.lines[0].replies[0];
                reply.match = DialogueMatch.Any;
                reply.when = new[] { Amount(coin, 10), Amount(ore, 1) };
                reply.groups = new[] { new DialogueRequirementGroup {
                    match = DialogueMatch.None,
                    rules = new[] { new DialogueRequirement { fact = new FactCondition { fact = "known", value = 1 } } }
                } };
                reply.price = new EasyResources.ResourceCost(
                    EasyResources.ResourceAmount.FromQuantity(coin, 2),
                    EasyResources.ResourceAmount.FromQuantity(ore, 1));
                using var run = new DialogueRun(graph, new DialogueContext(world, wallet, null, null));
                wallet.Add(coin, 10);
                Assert.IsFalse(run.Choose(reply.id, run.Revision));
                Assert.AreEqual(10, wallet.GetAvailable(coin));
                wallet.Add(ore, 1);
                world.SetFlag("known", true);
                Assert.IsFalse(run.Choose(reply.id, run.Revision));
                Assert.AreEqual(1, wallet.GetAvailable(ore));
                world.SetFlag("known", false);
                Assert.IsTrue(run.Choose(reply.id, run.Revision));
                Assert.AreEqual(8, wallet.GetAvailable(coin));
                Assert.AreEqual(0, wallet.GetAvailable(ore));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ore);
                UnityEngine.Object.DestroyImmediate(coin);
            }
        }

        [Test]
        public void StartingAnUnboundQuestTemplateIsRejectedBeforeOpeningAPaidDialogue()
        {
            var coin = EasyResources.ResourceDefinition.Create("Coin");
            using var wallet = new EasyResources.ResourceStore();
            try
            {
                wallet.Add(coin, 5);
                quest.steps[1].goals[0].target = "$recipient";
                var reply = graph.lines[0].replies[0];
                reply.price = new EasyResources.ResourceCost(EasyResources.ResourceAmount.FromQuantity(coin, 5));
                reply.action = new DialogueAction { kind = DialogueActionType.SetFact, key = "known", value = 1 };
                reply.additionalActions = new[] { new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest } };
                Assert.Throws<ArgumentException>(() => new DialogueRun(graph, new DialogueContext(world, wallet, null, null)));
                Assert.AreEqual(5, wallet.GetAvailable(coin));
                Assert.AreEqual(0, world.GetFact("known"));
            }
            finally { UnityEngine.Object.DestroyImmediate(coin); }
        }

        [Test]
        public void PaymentRefusedAtCommitCannotPublishAnyConsequence()
        {
            bool effectRan = false;
            Assert.IsFalse(world.TryCommitDialogue(() => false, () => {
                effectRan = true;
                world.SetFlag("known", true);
                world.StartQuest(quest.id);
            }));
            Assert.IsFalse(effectRan);
            Assert.AreEqual(0, world.GetFact("known"));
            Assert.IsNull(world.Find(quest.id));
        }

        [Test]
        public void AdditionalGroupsAndSkillFailureChooseTheirOwnRoute()
        {
            var skill = ScriptableObject.CreateInstance<FactSkillCheck>();
            try
            {
                var need = new DialogueRequirement { kind = DialogueRequirementType.WorldFact,
                    fact = new FactCondition { fact = "known", value = 1 } };
                var reply = graph.lines[0].replies[0];
                reply.groups = new[] { new DialogueRequirementGroup { match = DialogueMatch.None, rules = new[] { need } } };
                skill.threshold = need.fact;
                reply.skill = skill;
                reply.failedNext = "hello";
                reply.failureActions = new[] { new DialogueAction { kind = DialogueActionType.SetFact, key = "known", value = 1 } };
                using var run = new DialogueRun(graph, world);
                Assert.IsTrue(run.Choose(reply.id, run.Revision));
                Assert.AreEqual("hello", run.Current.id);
                Assert.IsEmpty(run.Replies);
                Assert.IsTrue(run.Continue(run.Revision));
            }
            finally { UnityEngine.Object.DestroyImmediate(skill); }
        }

        [Test]
        public void StandaloneCursorsAreIndependentAndContinueEnds()
        {
            graph.database = null;
            using var a = new DialogueRun(graph); using var b = new DialogueRun(graph);
            Assert.True(a.Choose("more", a.Revision)); Assert.AreEqual("hello", b.Current.id);
            Assert.True(a.Continue(a.Revision)); Assert.False(a.IsOpen);
        }
        [Test]
        public void StaleChoiceCannotRunTwiceInALoop()
        {
            graph.lines = new[] { graph.lines[0] }; graph.lines[0].replies[0].next = "hello";
            graph.lines[0].replies[0].action = new DialogueAction { kind = DialogueActionType.SendEvent, key = "go" };
            world.StartQuest(quest.id);
            using var run = new DialogueRun(graph, world); int before = run.Revision;
            Assert.True(run.Choose("more", before)); Assert.False(run.Choose("more", before));
            Assert.False(world.Find(quest.id).Finished); Assert.True(run.Choose("more", run.Revision)); Assert.True(world.Find(quest.id).Finished);
        }
        [Test]
        public void FactsAreSharedAndAnUnavailableChoiceUsesTheFallback()
        {
            graph.lines[0].replies[0].when = new[] { new DialogueRequirement { fact = new FactCondition { fact = "known", value = 1 } } };
            graph.lines[0].next = "next";
            using var a = new DialogueRun(graph, world); Assert.IsEmpty(a.Replies); world.SetFlag("known", true);
            Assert.AreEqual(1, a.Replies.Count); int old = a.Revision;
            world.SetFlag("known", false); Assert.False(a.Choose("more", old)); Assert.True(a.Continue(a.Revision)); Assert.AreEqual("next", a.Current.id);
        }
        [Test]
        public void StartingAQuestChangesOptionsInAnotherConversation()
        {
            var r = graph.lines[0].replies[0]; r.when = new[] { new DialogueRequirement { kind = DialogueRequirementType.QuestNotStarted, quest = quest } };
            r.action = new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest };
            using var a = new DialogueRun(graph, world); using var b = new DialogueRun(graph, world);
            Assert.True(a.Choose("more", a.Revision)); Assert.IsEmpty(b.Replies); Assert.NotNull(world.Find(quest.id)); Assert.False(b.Choose("more", 0));
        }
        [Test]
        public void RestoreClosesConversationWithoutReplayingAnEffect()
        {
            var initial = world.Capture();
            graph.lines[0].replies[0].action = new DialogueAction { kind = DialogueActionType.SetFact, key = "known", value = 1 };
            using var a = new DialogueRun(graph, world); a.Choose("more", a.Revision);
            Assert.AreEqual(1, world.GetFact("known")); var committed = world.Capture(); world.Restore(initial);
            Assert.False(a.IsOpen); Assert.AreEqual(0, world.GetFact("known")); world.Restore(committed); Assert.AreEqual(1, world.GetFact("known"));
        }
        [Test]
        public void CancelAndOpeningHaveNoEffects()
        {
            graph.lines[0].replies[0].action = new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest };
            using var a = new DialogueRun(graph, world); a.Close(); Assert.IsNull(world.Find(quest.id));
        }
        [Test]
        public void DesignerGetsErrorsForBrokenLinksUnreachableLinesAndDuplicateIds()
        {
            graph.lines[0].replies[0].next = "missing"; Assert.Throws<ArgumentException>(graph.ValidateGraph);
            graph.lines[0].replies[0].next = ""; Assert.Throws<ArgumentException>(graph.ValidateGraph);
            graph.lines[1].id = "hello"; Assert.Throws<ArgumentException>(graph.ValidateGraph);
        }
        [Test]
        public void MissingWorldOrUnregisteredQuestIsRejectedBeforeOpening()
        {
            graph.lines[0].replies[0].action = new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest };
            Assert.Throws<ArgumentException>(() => new DialogueRun(graph));
            var other = new QuestSession(db, Array.Empty<QuestDefinition>());
            Assert.Throws<ArgumentException>(() => new DialogueRun(graph, other));
        }
    }
}
