using System;
using System.Collections.Generic;
using EasyQuests;
using EasyResources;
using UnityEngine;

namespace EasyNarrative
{
    public enum DialogueRequirementType { WorldFact, QuestNotStarted, QuestActive, QuestAtStep, QuestFinished, ResourceAvailable, Custom }
    public enum DialogueMatch { All, Any, None }
    public enum LockedReply { Hidden, Disabled, Explain }
    public enum DialogueActionType { None, SetFact, StartQuest, SendEvent, Custom }

    [Serializable]
    public sealed class DialogueRequirement
    {
        public DialogueRequirementType kind;
        public FactCondition fact = new FactCondition();
        public QuestDefinition quest;
        public string stepId;
        public string instanceId;
        public ResourceDefinition resource;
        [Min(1)] public int quantity = 1;
        public DialogueCondition custom;

        public bool Passes(QuestSession world) => Passes(new DialogueContext(world, null, null, null));

        public bool Passes(DialogueContext context)
        {
            if (kind == DialogueRequirementType.Custom) return custom && custom.Matches(context);
            if (kind == DialogueRequirementType.ResourceAvailable) return resource && context.Wallet != null && context.Wallet.GetAvailable(resource) >= resource.ToUnits(quantity);
            var world = context.World;
            if (world == null) return false;
            if (kind == DialogueRequirementType.WorldFact) return world.Matches(fact);
            var run = world.Find(string.IsNullOrEmpty(instanceId) ? quest.id : instanceId);
            if (run != null && run.Definition != quest) run = null;
            return kind switch
            {
                DialogueRequirementType.QuestNotStarted => run == null,
                DialogueRequirementType.QuestActive => run != null && !run.Finished,
                DialogueRequirementType.QuestAtStep => run != null && !run.Finished && run.Current.id == stepId,
                DialogueRequirementType.QuestFinished => run != null && run.Finished,
                _ => false
            };
        }
    }

    [Serializable]
    public sealed class DialogueAction
    {
        public DialogueActionType kind;
        public string key;
        public int value = 1;
        public QuestDefinition quest;

        public DialogueEffect custom;
        internal bool Ready(DialogueContext context) => kind != DialogueActionType.Custom || (custom && custom.CanApply(context));

        internal void Apply(DialogueContext context, string source)
        {
            var world = context.World;
            switch (kind)
            {
                case DialogueActionType.Custom: if (context.Preview) custom.Preview(context); else custom.Apply(context); break;
                case DialogueActionType.SetFact: world.SetFact(key, value, source); break;
                case DialogueActionType.StartQuest: world.StartQuest(quest.id); break;
                case DialogueActionType.SendEvent: world.Signal(key); break;
            }
        }
    }

    [Serializable]
    public sealed class DialogueRequirementGroup
    {
        public DialogueMatch match;
        public DialogueRequirement[] rules = Array.Empty<DialogueRequirement>();
        internal static bool Evaluate(DialogueRequirement[] rules, DialogueMatch match, DialogueContext context)
        {
            if (rules == null || rules.Length == 0) return true;
            foreach (var rule in rules)
            {
                bool passes = rule.Passes(context);
                if (match == DialogueMatch.All && !passes) return false;
                if (match == DialogueMatch.Any && passes) return true;
                if (match == DialogueMatch.None && passes) return false;
            }
            return match != DialogueMatch.Any;
        }
    }

    [Serializable]
    public sealed class DialogueReply
    {
        public string id = Guid.NewGuid().ToString("N");
        public string text = "Goodbye.", textKey, next;
        public DialogueRequirement[] when = Array.Empty<DialogueRequirement>();
        public DialogueAction action = new DialogueAction();

        public DialogueMatch match;
        public DialogueRequirementGroup[] groups = Array.Empty<DialogueRequirementGroup>();
        public LockedReply locked;
        public string lockedReason, lockedReasonKey;
        public ResourceCost price = new();
        public DialogueAction[] additionalActions = Array.Empty<DialogueAction>();
        public DialogueSkillCheck skill;
        public string failedNext;
        public DialogueAction[] failureActions = Array.Empty<DialogueAction>();

        public bool CanBeLocked => (action?.kind == DialogueActionType.Custom) || (additionalActions != null && Array.Exists(additionalActions, effect => effect?.kind == DialogueActionType.Custom)) ||
            (skill && failureActions != null && Array.Exists(failureActions, effect => effect?.kind == DialogueActionType.Custom)) || when.Length != 0 || (groups != null && Array.Exists(groups, g => g?.rules?.Length > 0)) || (price != null && !price.IsEmpty);
        internal bool Available(DialogueContext context)
        {
            if (!action.Ready(context)) return false;
            if (additionalActions != null) foreach (var effect in additionalActions) if (!effect.Ready(context)) return false;
            if (skill && failureActions != null) foreach (var effect in failureActions) if (!effect.Ready(context)) return false;
            if (price != null && !price.IsEmpty && (context.Wallet == null || !context.Wallet.CanAfford(price))) return false;
            if (!DialogueRequirementGroup.Evaluate(when, match, context)) return false;
            if (groups != null) foreach (var group in groups)
                if (!DialogueRequirementGroup.Evaluate(group.rules, group.match, context)) return false;
            return true;
        }
    }

    [Serializable]
    public sealed class DialogueLine
    {
        public string id = Guid.NewGuid().ToString("N");
        public string name = "New line", speaker, speakerKey, textKey;
        [TextArea(2, 6)] public string text = "Hello.";
        [Tooltip("When no reply is available, Continue follows this link. An empty link ends the conversation.")]
        public string next;
        public DialogueReply[] replies = Array.Empty<DialogueReply>();
        public SpeakerProfile profile;
        public DialogueVoice[] voices = Array.Empty<DialogueVoice>();
        public string SpeakerName => profile ? profile.displayName : speaker;
        public string SpeakerKey => profile ? profile.nameKey : speakerKey;
        public Vector2 position;
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Dialogue")]
    public sealed class DialogueDefinition : ScriptableObject
    {
        [Tooltip("Required for world facts. Use the same database as the Quest World.")]
        public WorldFactDatabase database;
        public string entry;
        public DialogueLine[] lines = Array.Empty<DialogueLine>();
        [NonSerialized] private Program compiled;
        public void Invalidate() => compiled = null;
        private void OnValidate() => Invalidate();
        public void ValidateGraph() => Compile();
        internal Program GetProgram() => compiled ??= Compile();

        internal sealed class Program
        {
            public readonly Dictionary<string, DialogueLine> nodes = new(StringComparer.Ordinal);
            public readonly HashSet<QuestDefinition> quests = new();
            public bool needsWorld, usesFacts;
        }

        private Program Compile()
        {
            var program = new Program();
            var facts = new Dictionary<string, WorldFact>(StringComparer.Ordinal);
            if (database)
            {
                database.ValidateDatabase();
                foreach (var f in database.facts) facts.Add(f.id, f);
            }
            void Require(bool ok, string message) { if (!ok) throw new ArgumentException(name + ": " + message); }
            void CheckFact(string id, int value)
            {
                Require(!string.IsNullOrEmpty(id) && facts.TryGetValue(id, out var f) && f.Accepts(value),
                    "Choose a valid fact and value in the dialogue database.");
                program.needsWorld = program.usesFacts = true;
            }
            void CheckQuest(QuestDefinition quest)
            {
                Require(quest, "Assign the quest referenced by this reply.");
                if (database) Require(quest.database == database, "The dialogue and quest use different fact databases.");
                program.quests.Add(quest);
                program.needsWorld = true;
            }
            void CheckAction(DialogueAction a)
            {
                Require(a != null && Enum.IsDefined(typeof(DialogueActionType), a.kind), "Invalid reply consequence.");
                if (a.kind == DialogueActionType.Custom) Require(a.custom, "Choose a custom consequence.");
                if (a.kind == DialogueActionType.SetFact) CheckFact(a.key, a.value);
                if (a.kind == DialogueActionType.StartQuest)
                {
                    CheckQuest(a.quest);
                    QuestSession.ValidateDialogueQuestStart(a.quest);
                }
                if (a.kind == DialogueActionType.SendEvent)
                {
                    Require(!string.IsNullOrWhiteSpace(a.key), "Enter a quest event name.");
                    program.needsWorld = true;
                }
            }
            Require(lines != null && lines.Length > 0, "Add a line or use the conversation template.");
            var replyIds = new HashSet<string>();
            foreach (var line in lines)
            {
                Require(line != null && !string.IsNullOrWhiteSpace(line.id) && program.nodes.TryAdd(line.id, line), "Give every line a unique ID.");
                Require(!string.IsNullOrWhiteSpace(line.text) || !string.IsNullOrWhiteSpace(line.textKey), "Enter the line text for " + line.name);
                Require(line.replies != null, "Missing reply list.");
                foreach (var reply in line.replies)
                {
                    Require(reply != null && !string.IsNullOrWhiteSpace(reply.id) && replyIds.Add(reply.id), "Give every reply a unique ID.");
                    Require(!string.IsNullOrWhiteSpace(reply.text) || !string.IsNullOrWhiteSpace(reply.textKey), "Enter the reply text for " + line.name);
                    Require(reply.when != null && reply.action != null, "Missing reply settings.");
                    void CheckCondition(DialogueRequirement condition)
                    {
                        Require(condition != null && Enum.IsDefined(typeof(DialogueRequirementType), condition.kind), "Invalid reply condition.");
                        if (condition.kind == DialogueRequirementType.WorldFact)
                        {
                            Require(condition.fact != null && Enum.IsDefined(typeof(Comparison), condition.fact.comparison), "Invalid fact comparison.");
                            Require(condition.fact.scope == FactScope.World ||
                                (!string.IsNullOrWhiteSpace(condition.fact.subject) && !condition.fact.subject.StartsWith("$", StringComparison.Ordinal)),
                                "Choose a stable owner for the dialogue's scoped fact.");
                            CheckFact(condition.fact.fact, condition.fact.value);
                        }
                        else if (condition.kind == DialogueRequirementType.Custom) Require(condition.custom, "Choose a custom condition.");
                        else if (condition.kind == DialogueRequirementType.ResourceAvailable)
                            Require(condition.resource && condition.quantity > 0, "Choose a resource and a positive quantity.");
                        else
                        {
                            CheckQuest(condition.quest);
                            if (condition.kind == DialogueRequirementType.QuestAtStep)
                                Require(Array.Exists(condition.quest.steps, n => n.id == condition.stepId), "Choose an existing quest step.");
                        }
                    }
                    foreach (var condition in reply.when) CheckCondition(condition);
                    if (reply.groups != null) foreach (var group in reply.groups)
                    {
                        Require(group != null && group.rules != null && Enum.IsDefined(typeof(DialogueMatch), group.match), "Invalid condition group.");
                        foreach (var rule in group.rules) CheckCondition(rule);
                    }
                    Require(Enum.IsDefined(typeof(DialogueMatch), reply.match) && Enum.IsDefined(typeof(LockedReply), reply.locked), "Invalid reply presentation.");
                    reply.price?.Prepare();
                    CheckAction(reply.action);
                    foreach (var extra in reply.additionalActions ?? Array.Empty<DialogueAction>()) CheckAction(extra);
                    foreach (var extra in reply.failureActions ?? Array.Empty<DialogueAction>()) CheckAction(extra);
                }
            }
            Require(entry != null && program.nodes.ContainsKey(entry), "Choose the opening line.");
            var pending = new Stack<string>();
            var reached = new HashSet<string>();
            pending.Push(entry);
            void Follow(string next)
            {
                if (string.IsNullOrEmpty(next)) return;
                Require(program.nodes.ContainsKey(next), "Reconnect a missing destination: " + next);
                pending.Push(next);
            }
            while (pending.Count != 0)
            {
                string id = pending.Pop();
                if (!reached.Add(id)) continue;
                var line = program.nodes[id];
                // Fallback is possible only if all authored replies can be hidden by conditions.
                if (Array.TrueForAll(line.replies, r => r.CanBeLocked)) Follow(line.next);
                else if (!string.IsNullOrEmpty(line.next)) Require(program.nodes.ContainsKey(line.next), "Missing Continue destination.");
                foreach (var reply in line.replies)
                {
                    Follow(reply.next);
                    if (reply.skill) Follow(reply.failedNext);
                }
            }
            Require(reached.Count == lines.Length, "At least one line cannot be reached from the opening line.");
            return program;
        }
    }
}
