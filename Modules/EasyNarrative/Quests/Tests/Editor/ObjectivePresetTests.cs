using System;
using System.Linq;
using EasyInteraction;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyQuests.Editor;
using EasyQuests.Integrations;
using EasyResources;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Health = EasyCombat.Health;

namespace EasyQuests.Tests
{
    public sealed class ObjectivePresetTests
    {
        private QuestDefinition quest;
        private WorldFactDatabase database;
        private ResourceDefinition crystals;
        private GameObject root;
        private QuestWorld world;
        private QuestStep step;
        private DialogueDefinition dialogue;
        private int undoGroup;

        [SetUp]
        public void SetUp()
        {
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            root = new GameObject("Workshop test");
            database = ScriptableObject.CreateInstance<WorldFactDatabase>();
            quest = ScriptableObject.CreateInstance<QuestDefinition>();
            crystals = ResourceDefinition.Create("Crystals", decimalPlaces: 2);
            dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            world = root.AddComponent<QuestWorld>(); world.database = database; world.quests = new[] { quest };
            quest.database = database;
            step = new QuestStep { id = "wait", type = QuestStepType.Objectives, next = "end" };
            quest.steps = new[] { new QuestStep { id = "start", type = QuestStepType.Start, next = step.id }, step,
                new QuestStep { id = "end", type = QuestStepType.End } };
        }

        [TearDown]
        public void TearDown()
        {
            // Undo.AddComponent records must be reverted while their temporary objects still exist.
            Undo.FlushUndoRecordObjects();
            Undo.RevertAllDownToGroup(undoGroup);
            Object.DestroyImmediate(root); Object.DestroyImmediate(quest); Object.DestroyImmediate(database);
            Object.DestroyImmediate(crystals); Object.DestroyImmediate(dialogue);
        }

        [TestCase(ObjectivePreset.Talk)]
        [TestCase(ObjectivePreset.Interact)]
        [TestCase(ObjectivePreset.EnterZone)]
        [TestCase(ObjectivePreset.Defeat)]
        [TestCase(ObjectivePreset.DefeatGroup)]
        [TestCase(ObjectivePreset.GiveResource)]
        [TestCase(ObjectivePreset.CustomEvent)]
        public void PresetWiresTheAuthoredSource(ObjectivePreset kind)
        {
            var target = new GameObject("Source"); target.transform.SetParent(root.transform);
            var actor = root.AddComponent<InteractionActor>();
            if (kind == ObjectivePreset.EnterZone) target.AddComponent<BoxCollider>().isTrigger = true;
            if (kind == ObjectivePreset.Defeat) target.AddComponent<Health>();
            var goal = QuestObjectiveWizard.Add(quest, step, kind, world, target, actor, "guards", crystals, 1, dialogue: dialogue);
            Assert.DoesNotThrow(quest.ValidateGraph);
            Assert.AreSame(goal, step.goals.Single());
            switch (kind)
            {
                case ObjectivePreset.Talk:
                    Assert.AreSame(dialogue,target.GetComponent<TalkAction>().dialogue);
                    goto case ObjectivePreset.Interact;
                case ObjectivePreset.Interact:
                    var effect = target.GetComponent<InteractionAction>().OnCompleted;
                    Assert.AreSame(world,effect.GetPersistentTarget(0));Assert.AreEqual("Signal",effect.GetPersistentMethodName(0));
                    break;
                case ObjectivePreset.EnterZone: Assert.AreEqual(goal.signal,target.GetComponent<QuestZone>().signal);break;
                case ObjectivePreset.Defeat: Assert.AreEqual(goal.signal,target.GetComponent<QuestEventBinding>().signal);break;
                case ObjectivePreset.DefeatGroup: Assert.AreEqual(goal.target,world.GetComponent<QuestGroupDeaths>().group);break;
                case ObjectivePreset.GiveResource: Assert.AreEqual(goal.id,target.GetComponent<QuestHandInAction>().goalId);break;
                default: Assert.IsNotEmpty(goal.signal);break;
            }
        }

        [TestCase(ObjectivePreset.ReceiveResource)]
        [TestCase(ObjectivePreset.HaveResource)]
        public void ResourceObjectivesTrackFractionalAmountsAndOldStock(ObjectivePreset kind)
        {
            var goal = QuestObjectiveWizard.Add(quest, step, kind, world, null, null, null, crystals, 300);
            using var wallet = new ResourceStore(); wallet.Add(crystals,150);
            var session = new QuestSession(database, new[] { quest }, wallet.GetAvailable);
            wallet.DetailedChanged += session.ObserveWalletChange;
            session.StartQuest(quest.id);
            var run = session.Find(quest.id);
            Assert.AreEqual(kind == ObjectivePreset.HaveResource ? 150 : 0, run.Progress(goal));
            wallet.Add(crystals,25);
            Assert.AreEqual(kind == ObjectivePreset.HaveResource ? 175 : 25, run.Progress(goal));
            var saved=session.Capture();session.Restore(saved);
            wallet.Add(crystals,kind == ObjectivePreset.HaveResource ? 125 : 275);
            Assert.IsTrue(session.Find(quest.id).Finished);
        }
    }
}
