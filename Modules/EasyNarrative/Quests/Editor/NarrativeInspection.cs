using System;
using System.Collections.Generic;
using System.Linq;
using EasyInteraction;
using EasyNarrative.Interaction;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Editor
{
    public static class NarrativeInspection
    {
        public sealed class Usage
        {
            public QuestDefinition quest;
            public QuestStep step;
            public Component component;
            public string reason;
            public string signal;
        }

        public static T[] Assets<T>() where T : Object => AssetDatabase.FindAssets("t:" + typeof(T).Name, new[]{"Assets"})
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<T>).Where(a => a).ToArray();

        public static List<Usage> Scan(QuestWorld world)
        {
            var found = new List<Usage>();
            if (!world) return found;
            var components = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var q in world.quests)
            {
                if (!q || q.steps == null) continue;
                foreach (var step in q.steps)
                {
                    if (step == null) continue;
                    var signals = new HashSet<string>((step.goals ?? Array.Empty<QuestGoal>()).Where(g => g != null && g.mode == GoalMode.NewEvents).Select(g=>g.signal));
                    foreach (var c in components)
                    {
                        string why = null, signal = null;
                        switch (c)
                        {
                            case QuestHandInAction a when a.World == world && a.Definition == q && a.stepId == step.id: why = string.IsNullOrEmpty(a.goalId) ? "Reward recipient" : "Hand-in interaction"; break;
                            case QuestOrder order when order.world == world && order.template == q && step.type == QuestStepType.Start: why = "Repeatable order"; break;
                            case WorldStateReaction state when state.world == world && state.quest == q && (state.stepId == step.id || (state.source == WorldStateSource.QuestFinished && step.type == QuestStepType.End)): why = "World state reaction"; break;
                            case QuestEventReaction r when r.world == world && r.quest == q && (r.stepId == step.id || string.IsNullOrEmpty(r.stepId)): why = "Scene reaction"; break;
                            case QuestEventBinding b when b.world == world && signals.Contains(b.signal): signal=b.signal; why = b.source.ToString(); break;
                            case QuestZone z when z.world == world && signals.Contains(z.signal): signal=z.signal; why = "Trigger zone"; break;
                            case QuestGroupDeaths g when g.world == world && signals.Contains(g.signal): signal=g.signal; why = "Defeat group"; break;
                            case QuestInteractionAction a when a.world == world && a.quest == q && signals.Contains(a.signal): signal=a.signal; why = "Interaction event"; break;
                            case QuestTarget t when t.world == world && step.goals != null && step.goals.Any(g => g != null && g.marker != MarkerStyle.Hidden && !string.IsNullOrEmpty(g.target) && g.target == (g.targetGroup ? t.group : t.id)): why = "Marker target"; break;
                            case InteractionAction a when signals.Count > 0:
                                var ev = a.OnCompleted;
                                for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                                {
                                    if (ev.GetPersistentTarget(i) != world || ev.GetPersistentMethodName(i) != "Signal") continue;
                                    using var so = new SerializedObject(a);
                                    var key = so.FindProperty($"onCompleted.m_PersistentCalls.m_Calls.Array.data[{i}].m_Arguments.m_StringArgument");
                                    if (key != null && signals.Contains(key.stringValue)) found.Add(new Usage {quest=q,step=step,component=c,reason="Interaction event",signal=key.stringValue});
                                }
                                break;
                        }
                        if (why != null) found.Add(new Usage { quest=q, step=step, component=c, reason=why, signal=signal });
                    }
                }
                foreach (var talk in components.OfType<TalkAction>())
                    if (talk.dialogue && talk.dialogue.lines.Any(l => l?.replies != null && l.replies.Any(r => r != null &&
                        (r.action?.quest == q || r.additionalActions.Any(a => a?.quest == q) || r.failureActions.Any(a => a?.quest == q) ||
                         r.when.Any(c => c?.quest == q) || r.groups.Any(g => g != null && g.rules.Any(c => c?.quest == q))))))
                        found.Add(new Usage { quest=q, step=q.steps.FirstOrDefault(s=>s?.type==QuestStepType.Start), component=talk, reason="Dialogue references quest" });
            }
            return found;
        }

        // Exact serialized references, including objects in unopened quest/dialogue assets.
        public static List<string> References(string key, Object excluded = null, string excludedPath = null)
        {
            var rows = new List<string>();
            if (string.IsNullOrEmpty(key)) return rows;
            var assets = Assets<QuestDefinition>().Cast<Object>().Concat(Assets<EasyNarrative.DialogueDefinition>());
            foreach (var asset in assets)
            {
                using var so = new SerializedObject(asset);
                var it = so.GetIterator();
                while (it.Next(true))
                {
                    if (asset == excluded && excludedPath != null && (it.propertyPath == excludedPath || it.propertyPath.StartsWith(excludedPath + ".",StringComparison.Ordinal))) continue;
                    if (it.propertyType == SerializedPropertyType.String && it.stringValue == key && it.name != "id")
                        rows.Add(asset.name + " / " + it.propertyPath);
                }
            }
            foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if (!component) continue;
                var ns = component.GetType().Namespace ?? "";
                if (!ns.StartsWith("EasyQuests", StringComparison.Ordinal) && !ns.StartsWith("EasyNarrative",StringComparison.Ordinal) && !(component is InteractionAction)) continue;
                using var so = new SerializedObject(component);
                var property = so.GetIterator();
                while(property.Next(true))
                    if(property.propertyType==SerializedPropertyType.String && property.stringValue==key)
                        rows.Add(component.gameObject.scene.name+" / "+component.name+" ("+component.GetType().Name+") / "+property.propertyPath);
            }
            return rows;
        }
        public static bool ConfirmRemoval(string label, string key, Object owner, string path)
        {
            var refs=References(key,owner,path);
            return EditorUtility.DisplayDialog("Remove " + label + "?", refs.Count == 0 ? "No other serialized narrative fields reference this key." :
                "Review these references before removal:\n" + string.Join("\n",refs.Take(12)) + (refs.Count>12 ? "\n…" : ""), "Remove", "Cancel");
        }

        public static List<string> Explain(QuestWorld world, QuestDefinition quest, QuestStep step, IReadOnlyList<Usage> usage, QuestRun run)
        {
            var rows = new List<string>();
            if (!world || !quest || step == null) return rows;
            if (world.database != quest.database) rows.Add("Fact database differs from the world.");
            if (!world.quests.Contains(quest)) rows.Add("Quest is absent from the selected world's catalog.");
            rows.Add(run == null ? (Application.isPlaying ? "Quest has not started." : "Edit Mode · scene wiring") :
                "Current: " + run.Current.name + " · " + (run.Finished ? "finished" : "active"));
            foreach (var goal in step.goals ?? Array.Empty<QuestGoal>())
            {
                if (goal == null) continue;
                string line=goal.title;
                if (run != null) line+=" · "+goal.DisplayCount(run.Progress(goal))+"/"+goal.DisplayCount(goal.mode==GoalMode.WorldState?1:goal.count);
                if (goal.UsesResource && goal.resource)
                {
                    if (!world.wallet) line+=" · assign Quest World → Wallet";
                    else if (Application.isPlaying)
                        line+= $" · wallet {goal.resource.Format(world.wallet.GetTotal(goal.resource))}, reserved {goal.resource.Format(world.wallet.GetReserved(goal.resource))}, available {goal.resource.Format(world.wallet.GetAvailable(goal.resource))}";
                    if (Application.isPlaying && world.wallet && goal.mode == GoalMode.TurnInResource && world.wallet.GetAvailable(goal.resource) < goal.count)
                        line += " · short by " + goal.resource.Format(goal.count-world.wallet.GetAvailable(goal.resource));
                    if (goal.mode==GoalMode.TurnInResource && !usage.Any(r=>r.quest==quest && r.step==step && r.component is QuestHandInAction a && a.goalId==goal.id))
                        line+=" · no hand-in object in the loaded scenes";
                }
                if (goal.mode==GoalMode.WorldState && Application.isPlaying && run!=null)
                {
                    try
                    {
                        var address=world.Session.ConditionAddress(goal.condition,run);
                        line+=" · "+address+" = "+world.Session.GetFact(address)+" ("+world.Session.LastSource(address)+")";
                    }
                    catch(ArgumentException e){ line+=" · "+e.Message; }
                }
                if (goal.mode == GoalMode.NewEvents)
                {
                    line += " · waits for " + goal.signal;
                    if (!usage.Any(u => u.step == step && u.signal == goal.signal))
                        line += " · no emitter found in loaded scenes (code or another scene can supply this signal)";
                }
                foreach(var u in usage.Where(u => u.step == step && u.component is QuestHandInAction hand && hand.goalId == goal.id && !hand.isActiveAndEnabled))
                    line += " · inactive hand-in: " + u.component.name;
                if (goal.marker != MarkerStyle.Hidden)
                {
                    string target = run == null ? goal.target : run.ResolveTarget(goal.target);
                    if (run == null && target != null && target.StartsWith("$",StringComparison.Ordinal))
                        line += " · role " + target + " is bound when this quest starts";
                    else
                    {
                        int count = run != null ? (goal.targetGroup ? world.Group(target).Count : (world.Target(target) ? 1 : 0)) :
                            usage.Count(r => r.quest == quest && r.step == step && r.component is QuestTarget t && (goal.targetGroup ? t.group : t.id) == target);
                        line += " · " + (goal.targetGroup ? "group " : "target ") + target + " (" + count + " loaded)";
                    }
                }
                rows.Add(line);
            }
            if (step.type==QuestStepType.End && step.HasReward)
            {
                rows.Add("Reward: "+step.rewardMode+" · "+(step.rewardProfile?step.rewardProfile.name:"external profile")+" · "+
                    (!world.wallet && step.rewardMode!=QuestRewardMode.None?"wallet missing":run?.Current!=step?"ending not reached":run.RewardClaimed?"paid":run.LastRewardFailure!=null?"refused: "+run.LastRewardFailure:"pending"));
                if(step.rewardMode==QuestRewardMode.AtInteraction && !usage.Any(r=>r.quest==quest&&r.step==step&&r.component is QuestHandInAction a&&string.IsNullOrEmpty(a.goalId)))
                    rows.Add("No reward recipient in the loaded scenes.");
            }
            return rows;
        }
    }
}
