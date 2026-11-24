using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EasyResources;

namespace EasyQuests
{
    public enum QuestStepType { Start, Objectives, Branch, SetFact, End }
    public enum QuestCategory { Main, Side }
    public enum GoalMode { WorldState, NewEvents, ReceiveResource, HaveResource, TurnInResource }
    public enum QuestRewardMode { None, Automatic, AtInteraction }
    public enum GoalJoin { All, Any }
    public enum Comparison { Equals, NotEquals, AtLeast, AtMost }
    public enum MarkerStyle { Hidden, Target, SearchArea }

    [Serializable]
    public sealed class FactCondition
    {
        public string fact;
        public FactScope scope;
        public string subject;
        public Comparison comparison;
        public int value = 1;
        public bool Matches(int actual) => comparison switch
        {
            Comparison.Equals => actual == value,
            Comparison.NotEquals => actual != value,
            Comparison.AtLeast => actual >= value,
            Comparison.AtMost => actual <= value,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    [Serializable]
    public sealed class QuestGoal
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "New objective", textKey;
        public GoalMode mode;
        public FactCondition condition = new FactCondition();
        public string signal;
        public ResourceDefinition resource;
        internal string resourceSignal;
        public bool UsesResource => mode == GoalMode.ReceiveResource || mode == GoalMode.HaveResource || mode == GoalMode.TurnInResource;
        public string DisplayCount(int units) => UsesResource && resource ? resource.Format(units, false) : units.ToString();
        [Min(1)] public int count = 1;
        public bool optional, hidden;
        public MarkerStyle marker;
        [Tooltip("A group draws one marker for every active Quest Target in the group.")]
        public bool targetGroup;
        public string target;
        public MarkerLabelMode labelMode;
        [Min(0), Tooltip("Hide the caption beyond this camera distance in world units. Zero is unlimited.")]
        public float labelMaxDistance;
        public MarkerEdgeMode edgeMode;
        public string iconId;
        public bool edgeArrow = true;
        [Min(1)] public float searchRadius = 5;
    }

    [Serializable]
    public sealed class QuestRule
    {
        public string id = Guid.NewGuid().ToString("N");
        public FactCondition when = new();
        public string destination;
    }

    [Serializable] public sealed class QuestRoute { public FactCondition when = new FactCondition(); public string next; }

    [Serializable]
    public sealed class QuestStep
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "Step";
        [Tooltip("Optional localization key for the stage title on the tracked quest HUD.")] public string nameKey;
        public QuestStepType type;
        public Vector2 position;
        public string next;
        public QuestRoute[] routes = Array.Empty<QuestRoute>();
        public GoalJoin join;
        public QuestGoal[] goals = Array.Empty<QuestGoal>();
        public string fact;
        public FactScope factScope;
        public string factSubject;
        public int value = 1;
        public string outcome = "Completed", outcomeKey;
        public bool grantReward; // Legacy externally managed payout.
        public QuestRewardMode rewardMode;
        public ResourceRewardProfile rewardProfile;
        public bool HasReward => grantReward || rewardMode != QuestRewardMode.None;

        public IEnumerable<string> Outputs()
        {
            if (type == QuestStepType.End) yield break;
            if (type == QuestStepType.Branch)
                foreach (var route in routes) yield return route.next;
            yield return next;
        }
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Quest Graph")]
    public sealed class QuestDefinition : ScriptableObject
    {
        public string id = Guid.NewGuid().ToString("N");
        public string title = "New quest", titleKey;
        public bool hidden, autoStart;
        public QuestCategory category;
        public FactCondition startWhen = new FactCondition();
        public WorldFactDatabase database;
        public QuestIconLibrary iconLibrary;
        public string defaultIconId;
        public QuestRule[] rules = Array.Empty<QuestRule>();
        public QuestStep[] steps = Array.Empty<QuestStep>();

        public void ValidateGraph() => Compile(database ? database.BuildIndex() : throw new ArgumentException("Assign the world fact database."));

        internal Dictionary<string, QuestStep> Compile(Dictionary<string, WorldFact> facts)
        {
            void Require(bool ok, string message) { if (!ok) throw new ArgumentException(title + ": " + message); }
            void CheckFact(string key, int v, bool assignment)
            {
                Require(key != null && facts.ContainsKey(key), "Select an existing fact: " + key);
                if (assignment || facts[key].type != FactType.Integer)
                    Require(facts[key].Accepts(v), "Invalid value for " + facts[key].name);
            }
            void Owner(FactScope scope, string subject)
            {
                Require(Enum.IsDefined(typeof(FactScope), scope), "Choose a valid fact scope.");
                if (scope == FactScope.Character || scope == FactScope.Group)
                    Require(!string.IsNullOrWhiteSpace(subject) && subject != "$", "Choose an owner ID or $role.");
            }
            void Condition(FactCondition c) { Require(c != null, "Missing condition."); Owner(c.scope, c.subject); Require(Enum.IsDefined(typeof(Comparison), c.comparison), "Unknown comparison."); CheckFact(c.fact, c.value, false); }

            Require(steps != null && steps.Length != 0, "Create a Start step and a destination.");
            Require(!string.IsNullOrWhiteSpace(id), "Missing permanent quest ID.");
            if (autoStart) { Condition(startWhen); Require(startWhen.scope != FactScope.QuestInstance && !(startWhen.subject ?? "").StartsWith("$", StringComparison.Ordinal), "Automatic starts need a world fact or a fixed owner."); }
            var nodes = new Dictionary<string, QuestStep>(StringComparer.Ordinal);
            var goals = new HashSet<string>();
            foreach (var step in steps)
            {
                Require(step != null && !string.IsNullOrWhiteSpace(step.id) && nodes.TryAdd(step.id, step), "Missing or duplicate step ID.");
                Require(Enum.IsDefined(typeof(QuestStepType), step.type) && step.routes != null && step.goals != null, "Invalid step: " + step.name);
                if (step.type == QuestStepType.Branch) foreach (var route in step.routes) Condition(route?.when);
                if (step.type == QuestStepType.SetFact) { CheckFact(step.fact, step.value, true); Owner(step.factScope, step.factSubject); }
                if (step.type == QuestStepType.End && step.rewardMode != QuestRewardMode.None)
                {
                    Require(Enum.IsDefined(typeof(QuestRewardMode), step.rewardMode) && step.rewardProfile, step.name + ": select a reward profile.");
                    step.rewardProfile.ValidateConfiguration();
                }
                if (step.type == QuestStepType.End) Require(!string.IsNullOrWhiteSpace(step.outcome), "Name the outcome.");
                if (step.type != QuestStepType.Objectives) continue;
                Require(step.goals.Length > 0 && step.goals.Any(g => g != null && !g.optional), step.name + " needs a required goal.");
                foreach (var goal in step.goals)
                {
                    Require(goal != null && !string.IsNullOrWhiteSpace(goal.id) && goals.Add(goal.id), "Each objective needs a unique ID.");
                    Require(Enum.IsDefined(typeof(GoalMode), goal.mode) && Enum.IsDefined(typeof(MarkerStyle), goal.marker), "Unknown objective mode.");
                    if (goal.marker != MarkerStyle.Hidden)
                    {
                        Require(Enum.IsDefined(typeof(MarkerLabelMode), goal.labelMode) && Enum.IsDefined(typeof(MarkerEdgeMode), goal.edgeMode),
                            goal.title + ": choose a valid caption and edge behaviour.");
                        Require(float.IsFinite(goal.labelMaxDistance) && goal.labelMaxDistance >= 0,
                            goal.title + ": caption distance must be zero or a positive finite distance.");
                    }
                    if (goal.UsesResource)
                    {
                        Require(goal.resource && goal.count > 0, goal.title + ": select a resource and positive quantity.");
                        goal.resourceSignal = (goal.mode == GoalMode.HaveResource ? "wallet.balance/" : "wallet.received/") + goal.resource.Id;
                    }
                    else if (goal.mode == GoalMode.WorldState) Condition(goal.condition);
                    else Require(!string.IsNullOrWhiteSpace(goal.signal) && goal.count > 0, goal.title + ": set an event and positive count.");
                    if (goal.marker != MarkerStyle.Hidden) Require(!string.IsNullOrWhiteSpace(goal.target), goal.title + ": choose a target ID.");
                }
            }
            Require(steps.Count(s => s.type == QuestStepType.Start) == 1, "Create exactly one Start.");
            foreach (var step in steps) foreach (var next in step.Outputs())
                Require(next != null && nodes.ContainsKey(next), "Connect all outputs of " + step.name);
            var visiting = new HashSet<string>();
            var reached = new HashSet<string>();
            void Visit(QuestStep step)
            {
                Require(!visiting.Contains(step.id), "Cycle through " + step.name + ". Give this quest a finite route.");
                if (!reached.Add(step.id)) return;
                visiting.Add(step.id);
                foreach (string next in step.Outputs()) Visit(nodes[next]);
                visiting.Remove(step.id);
            }
            Visit(steps.First(s => s.type == QuestStepType.Start));
            var ruleIds = new HashSet<string>();
            foreach (var rule in rules ?? Array.Empty<QuestRule>())
            {
                Require(rule != null && !string.IsNullOrWhiteSpace(rule.id) && ruleIds.Add(rule.id), "Every quest rule needs a unique ID.");
                Condition(rule.when);
                Require(rule.destination != null && nodes.TryGetValue(rule.destination, out var target) && target.type != QuestStepType.Start,
                    "Choose a destination other than Start for each quest rule.");
                Visit(nodes[rule.destination]);
            }
            Require(reached.Count == steps.Length, "At least one step cannot be reached from Start.");
            return nodes;
        }
    }
}
