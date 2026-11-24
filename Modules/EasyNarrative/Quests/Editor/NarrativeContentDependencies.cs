using System;
using System.Collections.Generic;
using System.Linq;
using EasyInteraction;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyNarrative.UI;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Editor
{
    /// <summary>On-demand authoring report for one exact fact or event key.</summary>
    public static class NarrativeContentDependencies
    {
        public static List<string> Describe(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return new();
            var scene = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var quests = NarrativeInspection.Assets<QuestDefinition>();
            var dialogues = NarrativeInspection.Assets<DialogueDefinition>()
                .Concat(scene.OfType<TalkAction>().Select(t => t.dialogue)).Where(d => d).Distinct();
            var rows = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var quest in quests) DescribeQuest(quest, key, rows);
            var speakers = scene.OfType<TalkAction>().Where(t => t.dialogue).ToLookup(t => t.dialogue);
            foreach (var graph in dialogues) DescribeDialogue(graph, speakers[graph], key, rows);
            foreach (var component in scene)
                if (component && component.gameObject.scene.isLoaded) DescribeComponent(component, key, rows);
            return rows.ToList();
        }

        private static string TextKeys(params string[] keys)
        {
            string joined = string.Join(", ", keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct());
            return joined.Length == 0 ? "" : " · text: " + joined;
        }

        private static string ScenePath(Component component)
        {
            var path = component.name;
            for (var t = component.transform.parent; t; t = t.parent) path = t.name + "/" + path;
            return component.gameObject.scene.name + "/" + path;
        }

        private static string Location(Object asset) => AssetDatabase.GetAssetPath(asset) is string path && path.Length > 0 ? path : asset.name;

        private static void DescribeQuest(QuestDefinition quest, string key, SortedSet<string> rows)
        {
            string root = Location(quest);
            if (quest.autoStart && quest.startWhen?.fact == key)
                rows.Add("Reads fact · " + root + " · automatic start" + TextKeys(quest.titleKey));
            var steps = quest.steps ?? Array.Empty<QuestStep>();
            QuestStep Destination(string id) => Array.Find(steps, step => step != null && step.id == id);
            foreach (var rule in quest.rules ?? Array.Empty<QuestRule>())
                if (rule?.when?.fact == key)
                {
                    var dest = Destination(rule.destination);
                    rows.Add("Reads fact · " + root + " · quest rule → " + (dest?.name ?? rule.destination) + TextKeys(quest.titleKey, dest?.nameKey));
                }
            foreach (var step in steps)
            {
                if (step == null) continue;
                string node = root + " · " + step.name + " [" + step.id + "]";
                if (step.type == QuestStepType.SetFact && step.fact == key)
                    rows.Add("Writes fact · " + node + " · " + step.value + TextKeys(quest.titleKey, step.nameKey));
                if (step.type == QuestStepType.Branch)
                    foreach (var route in step.routes ?? Array.Empty<QuestRoute>())
                        if (route?.when?.fact == key)
                        {
                            var dest = Destination(route.next);
                            rows.Add("Reads fact · " + node + " → " + (dest?.name ?? route.next) + TextKeys(quest.titleKey, step.nameKey, dest?.nameKey));
                        }
                if (step.type != QuestStepType.Objectives) continue;
                foreach (var goal in step.goals ?? Array.Empty<QuestGoal>())
                {
                    if (goal == null) continue;
                    string role;
                    if (goal.mode == GoalMode.WorldState && goal.condition?.fact == key) role = "Reads fact";
                    else if (goal.mode == GoalMode.NewEvents && goal.signal == key) role = "Receives event";
                    else continue;
                    rows.Add(role + " · " + node + " · " + goal.title + TextKeys(quest.titleKey, step.nameKey, goal.textKey));
                }
            }
        }

        private static void DescribeDialogue(DialogueDefinition graph, IEnumerable<TalkAction> speakers, string key, SortedSet<string> rows)
        {
            string asset = Location(graph);
            foreach (var line in graph.lines ?? Array.Empty<DialogueLine>())
            {
                if (line == null) continue;
                foreach (var reply in line.replies ?? Array.Empty<DialogueReply>())
                {
                    if (reply == null) continue;
                    void Add(string role, string detail, string next = null)
                    {
                        var dest = string.IsNullOrEmpty(next) ? null : Array.Find(graph.lines, l => l != null && l.id == next);
                        string location = asset + " · " + line.name + " [" + line.id + "] · " + reply.text + " [" + reply.id + "] · " + detail;
                        string keys = TextKeys(line.SpeakerKey, line.textKey, reply.textKey, reply.lockedReasonKey, dest?.SpeakerKey, dest?.textKey);
                        rows.Add(role + " · " + location + keys);
                        foreach (var talk in speakers)
                            rows.Add("NPC · " + ScenePath(talk) + " · " + role + " · " + location + keys + TextKeys(talk.LabelKey));
                    }
                    void Rules(DialogueRequirement[] requirements, string detail)
                    {
                        foreach (var r in requirements ?? Array.Empty<DialogueRequirement>())
                            if (r?.kind == DialogueRequirementType.WorldFact && r.fact?.fact == key) Add("Reads fact", detail);
                    }
                    Rules(reply.when, "reply " + reply.match);
                    int index = 0;
                    foreach (var group in reply.groups ?? Array.Empty<DialogueRequirementGroup>())
                    {
                        index++;
                        if (group != null) Rules(group.rules, "group " + index + " · " + group.match);
                    }
                    if (reply.skill is FactSkillCheck skill && skill.threshold?.fact == key) Add("Reads fact", "skill " + skill.name);
                    void Action(DialogueAction action, string detail, string next)
                    {
                        if (action == null || action.key != key) return;
                        if (action.kind == DialogueActionType.SetFact) Add("Writes fact", detail, next);
                        if (action.kind == DialogueActionType.SendEvent) Add("Sends event", detail, next);
                    }
                    Action(reply.action, "selection", reply.next);
                    foreach (var extra in reply.additionalActions ?? Array.Empty<DialogueAction>()) Action(extra, "success", reply.next);
                    if (reply.skill)
                        foreach (var extra in reply.failureActions ?? Array.Empty<DialogueAction>()) Action(extra, "failed check", reply.failedNext);
                }
            }
        }

        private static void DescribeComponent(MonoBehaviour component, string key, SortedSet<string> rows)
        {
            void Add(string role, string detail) => rows.Add(role + " · " + ScenePath(component) + " · " + detail);
            switch (component)
            {
                case WorldStateReaction r when r.source == WorldStateSource.Fact && r.condition?.fact == key:
                    Add("Reads fact", "World State Reaction"); break;
                case QuestEventBinding b when b.signal == key: Add("Sends event", b.source.ToString()); break;
                case QuestZone z when z.signal == key: Add("Sends event", "Quest Zone"); break;
                case QuestGroupDeaths d when d.signal == key: Add("Sends event", "Defeat group " + d.group); break;
                case QuestInteractionAction a when a.signal == key: Add("Sends event", "Quest Interaction" + TextKeys(a.LabelKey)); break;
                case DialogueBark bark:
                    var graph = bark.script;
                    var line = graph ? Array.Find(graph.lines ?? Array.Empty<DialogueLine>(), l => l != null && l.id == (string.IsNullOrEmpty(bark.lineId) ? graph.entry : bark.lineId)) : null;
                    string text = TextKeys(line?.SpeakerKey, line?.textKey);
                    if (bark.once && bark.playedFlag == key) { Add("Reads fact", "Bark already spoken" + text); Add("Writes fact", "Bark played" + text); }
                    if (Array.Exists(bark.conditions ?? Array.Empty<FactCondition>(), c => c?.fact == key)) Add("Reads fact", "Bark condition" + text);
                    break;
            }
            if (!(component is InteractionAction action)) return;
            var completed = action.OnCompleted;
            for (int i = 0; i < completed.GetPersistentEventCount(); i++)
            {
                if (!(completed.GetPersistentTarget(i) is QuestWorld) || completed.GetPersistentMethodName(i) != nameof(QuestWorld.Signal)) continue;
                using var serialized = new SerializedObject(action);
                var argument = serialized.FindProperty($"onCompleted.m_PersistentCalls.m_Calls.Array.data[{i}].m_Arguments.m_StringArgument");
                if (argument?.stringValue == key) Add("Sends event", "On completed" + TextKeys(action.LabelKey));
            }
        }
    }
}
