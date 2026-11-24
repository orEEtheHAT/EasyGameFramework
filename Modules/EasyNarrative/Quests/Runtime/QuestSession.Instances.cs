using System;
using System.Collections.Generic;

namespace EasyQuests
{
    public sealed partial class QuestSession
    {
        public string TrackedGoal { get; private set; }
        public void TrackGoal(string id,string goalId) => Change(() =>
        {
            var run = Find(id);
            if (run == null || run.Finished || run.Definition.hidden || run.Current.type != QuestStepType.Objectives ||
                !Array.Exists(run.Current.goals,g=>g.id == goalId && !g.hidden && !run.Satisfied(g)))
                throw new ArgumentException("Select a visible objective on the current step.");
            pinned.Remove(id);
            Tracked = id; TrackedGoal = goalId; dirty = true;
        });

        public const int MaxPins = 3;
        private readonly List<string> pinned = new(MaxPins);
        public IReadOnlyList<string> Pinned => pinned;

        public void Pin(string id, bool enable = true) => Change(() =>
        {
            if (!enable) { if (pinned.Remove(id)) dirty = true; return; }
            var run = Find(id);
            if (run == null || run.Definition.hidden || run.Finished) throw new ArgumentException("Pin an active visible quest.");
            if (id == Tracked || pinned.Contains(id)) return;
            if (pinned.Count >= MaxPins) throw new InvalidOperationException("Unpin a quest first.");
            pinned.Add(id);
            dirty = true;
        });

        public QuestRun StartInstance(QuestDefinition quest, string instanceId, QuestRoleBinding[] roles = null, string title = null, string titleKey = null)
        {
            if (!Contains(quest) || string.IsNullOrWhiteSpace(instanceId) || catalog.ContainsKey(instanceId))
                throw new ArgumentException("Register the template and choose a separate, stable order ID.");
            var existing = Find(instanceId);
            if (existing != null)
            {
                if (existing.Definition != quest) throw new ArgumentException("This order ID belongs to another template.");
                return existing;
            }
            Change(() => Begin(catalog[quest.id], instanceId, title, roles, titleKey));
            return Find(instanceId);
        }

        /// <summary>Deliver an event only to this order. The ordinary Signal API broadcasts to all interested orders.</summary>
        public bool SignalFor(string instanceId, string signal, int amount = 1, string source = null)
        {
            if (string.IsNullOrWhiteSpace(signal) || amount < 1) throw new ArgumentException("Supply an event and positive amount.");
            var run = Find(instanceId);
            if (run == null || run.Finished || !run.watches.Contains((true,signal)))
            {
                traceBuffer?.Add("Unhandled signal",instanceId,run?.Current.id,signal,amount,source);
                return false;
            }
            var previous = dispatchSource;
            try
            {
                dispatchSource = source;
                Change(() =>
                {
                    traceBuffer?.Add("Signal",instanceId,run.Current.id,signal,amount,source);
                    if (Evaluate(run,signal,amount,source)) Advance(run,catalog[run.Definition.id].steps[run.Current.next]);
                });
            }
            finally { dispatchSource = previous; }
            return true;
        }

        public int GetFact(FactAddress address)
        {
            if (!schema.TryGetValue(address.Fact,out var fact)) throw new ArgumentException("Unknown fact: " + address.Fact);
            return values.TryGetValue(address,out int value) ? value : fact.initial;
        }
        public string LastSource(FactAddress address) => sources.TryGetValue(address,out var origin) ? origin : "Initial value";
        public void SetFact(FactAddress address, int value, string source = "Game")
        {
            if (address.Scope == FactScope.World) { SetFact(address.Fact,value,source); return; }
            if (address.Scope == FactScope.QuestInstance && Find(address.Subject) == null)
                throw new ArgumentException("Start the quest instance before writing its facts.");
            Change(() =>
            {
                if (IsTracing) TraceInput(false,address.ToString(),value,source);
                WriteFact(address,value,source);
            });
        }
        public void Learn(string characterId, string clue, string source)
        {
            if (!schema.TryGetValue(clue,out var fact) || fact.type != FactType.Flag || fact.initial != 0)
                throw new ArgumentException("Define the clue as an initially false flag.");
            SetFact(new FactAddress(FactScope.Character,characterId,clue),1,source);
        }
        public bool Knows(string characterId, string clue) => GetFact(new FactAddress(FactScope.Character, characterId, clue)) != 0;

        public bool Matches(FactCondition condition, QuestRun run = null) => condition.Matches(GetFact(Address(condition,run)));
        public FactAddress ConditionAddress(FactCondition condition, QuestRun run = null) => Address(condition,run);
        private static FactAddress Address(FactCondition condition, QuestRun run) =>
            ResolveAddress(condition.scope,condition.subject,condition.fact,run);

        private static FactAddress ResolveAddress(FactScope scope, string subject, string fact, QuestRun run)
        {
            if (scope == FactScope.World) return fact;
            if (scope == FactScope.QuestInstance && string.IsNullOrEmpty(subject)) subject = run?.Id;
            if (subject != null && subject.StartsWith("$",StringComparison.Ordinal)) subject = run?.ResolveTarget(subject);
            return new FactAddress(scope,subject,fact);
        }

        private static void ValidateRoles(QuestRun run)
        {
            void Check(string reference)
            {
                if (reference != null && reference.StartsWith("$",StringComparison.Ordinal) && run.ResolveTarget(reference) == null)
                    throw new ArgumentException("Assign the quest role " + reference + " before starting " + run.Definition.title);
            }
            foreach (var rule in run.Definition.rules ?? Array.Empty<QuestRule>()) Check(rule.when.subject);
            foreach (var step in run.Definition.steps)
            {
                if (step.type == QuestStepType.SetFact) Check(step.factSubject);
                if (step.type == QuestStepType.Branch) foreach (var route in step.routes) Check(route.when.subject);
                if (step.type != QuestStepType.Objectives) continue;
                foreach (var goal in step.goals)
                {
                    Check(goal.target);
                    if (goal.mode == GoalMode.WorldState) Check(goal.condition.subject);
                }
            }
        }
    }
}
