using System;
using System.Collections.Generic;
using System.Text;
using EasyQuests;
using EasyResources;

namespace EasyNarrative
{
    /// <summary>One cursor per conversation. Available replies and displayed replies are separate views.</summary>
    public sealed class DialogueRun : IDisposable
    {
        private readonly DialogueDefinition.Program graph;
        private readonly DialogueContext context;
        private readonly List<DialogueReply> replies = new(), scratch = new(), displayed = new();
        private bool committing;
        public DialogueDefinition Definition { get; }
        public DialogueLine Current { get; private set; }
        public IReadOnlyList<DialogueReply> Replies => replies;
        public IReadOnlyList<DialogueReply> Displayed => displayed;
        public int Revision { get; private set; }
        public int Visit { get; private set; }
        public bool IsOpen => Current != null;
        public event Action Changed;

        public DialogueRun(DialogueDefinition dialogue, QuestSession world = null)
            : this(dialogue, new DialogueContext(world, null, null, null)) { }

        public DialogueRun(DialogueDefinition dialogue, DialogueContext context, string entry = null)
        {
            if (!dialogue) throw new ArgumentNullException(nameof(dialogue));
            Definition = dialogue;
            graph = dialogue.GetProgram();
            var world = context.World;
            if (graph.needsWorld && world == null) throw new ArgumentException("This dialogue needs a Quest World.");
            if (graph.usesFacts && world.Database != dialogue.database) throw new ArgumentException("Use the dialogue's world fact database.");
            foreach (var quest in graph.quests)
                if (!world.Contains(quest)) throw new ArgumentException("Register the quest on the world: " + quest.title);
            this.context = context;
            Current = graph.nodes[string.IsNullOrEmpty(entry) || !graph.nodes.ContainsKey(entry) ? dialogue.entry : entry];
            Rebuild();
            if (world != null)
            {
                world.Changed += OnWorldChanged;
                world.ScopedFactChanged += OnScopedFactChanged;
                world.Restored += Close;
            }
            if (context.Wallet != null) context.Wallet.Changed += RefreshConditions;
        }

        private void Rebuild()
        {
            replies.Clear();
            displayed.Clear();
            if (Current == null) return;
            foreach (var reply in Current.replies)
            {
                bool available = reply.Available(context);
                if (available) replies.Add(reply);
                if (available || reply.locked != LockedReply.Hidden) displayed.Add(reply);
            }
        }

        private void OnScopedFactChanged(FactAddress address, int value) => OnWorldChanged();

        private void OnWorldChanged()
        {
            if (committing || Current == null) return;
            scratch.Clear(); scratch.AddRange(replies);
            Rebuild();
            if (scratch.Count == replies.Count)
            {
                int i = 0;
                while (i < replies.Count && replies[i] == scratch[i]) i++;
                if (i == replies.Count) return;
            }
            Revision++;
            Changed?.Invoke();
        }

        public void RefreshConditions()
        {
            if (committing || Current == null) return;
            Rebuild(); Revision++;
            Changed?.Invoke();
        }

        public bool IsAvailable(DialogueReply reply) => reply != null && replies.Contains(reply);

        public string Explain(DialogueReply reply)
        {
            if (reply == null || reply.Available(context)) return "";
            if (!string.IsNullOrWhiteSpace(reply.lockedReason)) return reply.lockedReason;
            if (!reply.action.Ready(context)) return reply.action.custom.Explain(context);
            if (reply.additionalActions != null) foreach (var effect in reply.additionalActions)
                if (!effect.Ready(context)) return effect.custom.Explain(context);
            if (reply.skill && reply.failureActions != null) foreach (var effect in reply.failureActions)
                if (!effect.Ready(context)) return effect.custom.Explain(context);
            if (reply.price != null && !reply.price.IsEmpty && (context.Wallet == null || !context.Wallet.CanAfford(reply.price)))
            {
                var result = new StringBuilder();
                foreach (var row in reply.price.Items)
                {
                    if (result.Length > 0) result.Append(", ");
                    result.Append(row.Resource.DisplayName).Append(" ").Append(row.Resource.Format(context.Wallet?.GetAvailable(row.Resource) ?? 0))
                        .Append("/").Append(row.Resource.Format(row.Amount));
                }
                return result.ToString();
            }
            var reason = new StringBuilder();
            ExplainGroup(reply.when, reply.match);
            if (reply.groups != null) foreach (var group in reply.groups) ExplainGroup(group.rules, group.match);
            return reason.ToString();

            void ExplainGroup(DialogueRequirement[] rules, DialogueMatch match)
            {
                if (DialogueRequirementGroup.Evaluate(rules, match, context)) return;
                foreach (var r in rules)
                {
                    bool passes = r.Passes(context);
                    if (match == DialogueMatch.None ? !passes : passes) continue;
                    if (reason.Length > 0) reason.Append(match == DialogueMatch.Any ? " / " : " · ");
                    if (match == DialogueMatch.None) reason.Append("Must not match: ");
                    if (r.kind == DialogueRequirementType.Custom) reason.Append(r.custom.Explain(context));
                    else if (r.kind == DialogueRequirementType.ResourceAvailable) reason.Append(r.resource.DisplayName).Append(" ≥ ").Append(r.quantity);
                    else if (r.kind == DialogueRequirementType.WorldFact)
                    {
                        string name = Array.Find(context.World.Database.facts, f => f.id == r.fact.fact)?.name ?? r.fact.fact;
                        reason.Append(name).Append(" ").Append(r.fact.comparison).Append(" ").Append(r.fact.value);
                    }
                    else
                    {
                        var active = context.World.Find(string.IsNullOrEmpty(r.instanceId) ? r.quest.id : r.instanceId);
                        var wanted = r.kind == DialogueRequirementType.QuestAtStep ? Array.Find(r.quest.steps, s => s.id == r.stepId)?.name : r.kind.ToString();
                        reason.Append(r.quest.title).Append(" · ").Append(wanted).Append(" · ").Append(active?.Current.name ?? "not started");
                    }
                }
            }
        }

        public bool Choose(string replyId, int revision)
        {
            if (!IsOpen || committing || revision != Revision) return false;
            foreach (var reply in replies)
                if (reply.id == replyId && reply.Available(context)) return Advance(reply, null);
            return false;
        }

        public bool Continue(int revision) =>
            IsOpen && !committing && revision == Revision && replies.Count == 0 && Advance(null, Current.next);

        public bool Redirect(string lineId)
        {
            if (!IsOpen || committing || string.IsNullOrEmpty(lineId) || !graph.nodes.ContainsKey(lineId)) return false;
            return Advance(null, lineId);
        }

        private bool Advance(DialogueReply reply, string next)
        {
            committing = true;
            try
            {
                bool paid = reply?.price != null && !reply.price.IsEmpty;
                bool passed = reply == null || !reply.skill || reply.skill.Succeeds(context);
                bool Pay() => context.Wallet != null && context.Wallet.TrySpend(reply.price);
                void Apply()
                {
                    if (reply != null)
                    {
                        next = passed ? reply.next : reply.failedNext;
                        if (IsOpen) Current = string.IsNullOrEmpty(next) ? null : graph.nodes[next];
                        if (passed) reply.action.Apply(context, Definition.name);
                        var effects = passed ? reply.additionalActions : reply.failureActions;
                        if (effects != null) foreach (var effect in effects) effect.Apply(context, Definition.name);
                    }
                    else Current = string.IsNullOrEmpty(next) ? null : graph.nodes[next];
                    Visit++;
                }
                if (context.World != null) return context.World.TryCommitDialogue(paid ? Pay : null, Apply);
                if (paid && !Pay()) return false;
                Apply();
                return true;
            }
            finally
            {
                Revision++;
                committing = false;
                Rebuild();
                if (!IsOpen) Unsubscribe();
                Changed?.Invoke();
            }
        }
        private void Unsubscribe()
        {
            if (context.World != null)
            {
                context.World.Changed -= OnWorldChanged;
                context.World.ScopedFactChanged -= OnScopedFactChanged;
                context.World.Restored -= Close;
            }
            if (context.Wallet != null) context.Wallet.Changed -= RefreshConditions;
        }
        public void Close()
        {
            if (!IsOpen) return;
            Current = null;
            Revision++;
            Rebuild();
            Unsubscribe();
            Changed?.Invoke();
        }
        public void Dispose() => Close();
    }
}
