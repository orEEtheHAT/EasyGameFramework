using System;
using System.Linq;
using EasyQuests.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Tests
{
    public sealed class NarrativeSuiteTests
    {
        private QuestDefinition quest;
        private WorldFactDatabase database;

        [SetUp]
        public void SetUp()
        {
            database = ScriptableObject.CreateInstance<WorldFactDatabase>();
            quest = ScriptableObject.CreateInstance<QuestDefinition>();
            quest.database = database;
            quest.id = "order";
            quest.steps = new[] {
                new QuestStep { id = "start", type = QuestStepType.Start, next = "carry" },
                new QuestStep { id = "carry", type = QuestStepType.Objectives, next = "end", goals = new[] {
                    new QuestGoal { id = "picked", mode = GoalMode.NewEvents, signal = "pickup", count = 30, marker = MarkerStyle.Target, target = "crate" } } },
                new QuestStep { id = "end", type = QuestStepType.End }
            };
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearUndo(quest);
            Object.DestroyImmediate(quest);
            Object.DestroyImmediate(database);
        }

        [Test]
        public void DuplicatingSelectionReconnectsCopiesAndLeavesOriginalIdsAlone()
        {
            var ids = NarrativeGraphEditing.Duplicate(quest, new[] { "start", "carry", "end" });
            Assert.That(ids, Has.Length.EqualTo(2));
            var copy = quest.steps.Single(s => s.id == ids[0]);
            Assert.That(copy.next, Is.EqualTo(ids[1]));
            Assert.That(copy.goals.Single().id, Is.Not.EqualTo("picked"));
            Assert.That(quest.steps[1].next, Is.EqualTo("end"));
            Assert.That(quest.steps[1].goals[0].id, Is.EqualTo("picked"));
            Assert.That(quest.database, Is.SameAs(database));
        }

        [Test]
        public void NewVariantBindsChosenTargetAndKeepsItsOwnSaveIdentity()
        {
            var copy = QuestVariantWindow.Make(quest,"Order two",null,null,"crate","barrel");
            try
            {
                Assert.That(copy.id, Is.Not.EqualTo(quest.id));
                Assert.That(copy.steps[1].goals[0].target, Is.EqualTo("barrel"));
                Assert.That(quest.steps[1].goals[0].target, Is.EqualTo("crate"));
                Assert.That(copy.steps[1].goals[0].signal, Is.EqualTo("pickup"));
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void ZoomKeepsPointUnderCursorAndFitFramesBothEnds()
        {
            var pan = new Vector2(162,48);
            var zoom = .75f;
            var cursor = new Vector2(270,123);
            var before = pan + cursor / zoom;
            NarrativeGraphEditing.ZoomAt(cursor,-3,ref pan,ref zoom);
            Assert.That(Vector2.Distance(before,pan + cursor / zoom), Is.LessThan(.001f));
            var size = new Vector2(900,600);
            var card = new Vector2(170,55);
            var points = new[] {new Vector2(500,-130), new Vector2(1200,850)};
            NarrativeGraphEditing.Fit(points,card,size,ref pan,ref zoom);
            foreach (var point in points)
            {
                var start = (point-pan)*zoom;
                var end = (point+card-pan)*zoom;
                Assert.That(start.x >= 0 && start.y >= 0 && end.x <= size.x && end.y <= size.y);
            }
        }

        [Test]
        public void EarlyEventIsVisibleInDiagnosticsAndDoesNotCountAsANewObjective()
        {
            var session = new QuestSession(database,new[] {quest});
            session.EnableTrace();
            session.SignalFrom("pickup",1,"before quest");
            Assert.That(session.Trace.Last().Kind, Is.EqualTo("Unhandled signal"));
            session.StartQuest(quest.id);
            var run = session.Find(quest.id);
            Assert.That(run.Progress(quest.steps[1].goals[0]), Is.Zero);
            session.SignalFrom("pickup",1,"after quest");
            Assert.That(run.Progress(quest.steps[1].goals[0]), Is.EqualTo(1));
        }

        [Test]
        public void TraceRetainsRecentEventsWithTheirSourceAndCanBeDisabled()
        {
            var session = new QuestSession(database,new[] {quest});
            session.StartQuest(quest.id);
            Assert.That(session.Trace.Count, Is.Zero);
            session.EnableTrace(3);
            for (int i=0; i<6; i++) session.SignalFrom("pickup",1,"shelf");
            Assert.That(session.Trace.Count, Is.EqualTo(3));
            Assert.That(session.Trace[2].Sequence-session.Trace[0].Sequence, Is.EqualTo(2));
            Assert.That(session.Trace.Any(e=>e.Source=="shelf"));
            session.DisableTrace();
            session.Signal("pickup");
            Assert.That(session.Trace.Count, Is.Zero);
        }
    }
}
