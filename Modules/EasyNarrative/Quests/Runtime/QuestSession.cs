using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EasyResources;

namespace EasyQuests
{
    [Serializable] public sealed class SavedFact { public string id, subject, source; public FactScope scope; public int value; }
    [Serializable] public sealed class SavedGoal { public string id, source; public int count; }
    [Serializable]
    public sealed class SavedQuest
    {
        public string id;
        public string definition, title, titleKey;
        public QuestRoleBinding[] roles;
        public string[] path;
        public string[] takenRules;
        public SavedGoal[] progress;
        public bool rewardClaimed;
        public int rewardSeed;
    }
    [Serializable]
    public sealed class QuestSnapshot
    {
        public int version = 1;
        public SavedFact[] facts;
        public SavedQuest[] quests;
        public string tracked, trackedGoal;
        public string[] pinned;
    }

    public sealed class QuestRun
    {
        internal readonly Dictionary<string, int> counts = new();
        internal Dictionary<string,string> evidence;
        public string Evidence(QuestGoal goal) => evidence != null && evidence.TryGetValue(goal.id,out var origin) ? origin : null;
        internal readonly List<string> history = new();
        internal List<string> takenRules;
        public bool HasTakenRule(string id) => takenRules != null && takenRules.Contains(id);
        internal readonly HashSet<(bool signal, FactAddress id)> watches = new();
        public QuestDefinition Definition { get; }
        public QuestStep Current { get; internal set; }
        public IReadOnlyList<string> History => history;
        public bool Finished => Current.type == QuestStepType.End;
        public bool RewardClaimed { get; internal set; }
        public string LastRewardFailure { get; internal set; }
        internal int Seed { get; set; } = Guid.NewGuid().GetHashCode();
        public int RewardSeed => Seed;
        public bool RewardPending => Finished && Current.HasReward && !RewardClaimed;
        public int Progress(QuestGoal goal) => counts.TryGetValue(goal.id, out int value) ? value : 0;
        public bool Satisfied(QuestGoal goal) => Progress(goal) >= (goal.mode == GoalMode.WorldState ? 1 : goal.count);
        private static readonly IReadOnlyDictionary<string,string> NoRoles = new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>());
        private readonly Dictionary<string,string> roles, tokens;
        public string Id { get; }
        public string Title { get; }
        public string TitleKey { get; }
        public IReadOnlyDictionary<string,string> Roles => roles ?? NoRoles;
        public string ResolveTarget(string target)
        {
            if (string.IsNullOrEmpty(target) || !target.StartsWith("$",StringComparison.Ordinal)) return target;
            return tokens != null && tokens.TryGetValue(target, out var result) ? result : null;
        }
        internal QuestRun(QuestDefinition definition, string id, string title, QuestRoleBinding[] bindings, string titleKey = null)
        {
            Definition = definition; Id = id; Title = title; TitleKey = titleKey;
            if (bindings?.Length > 0)
            {
                roles = new(StringComparer.Ordinal); tokens = new(StringComparer.Ordinal);
                foreach (var item in bindings)
                    if (item == null || string.IsNullOrWhiteSpace(item.role) || string.IsNullOrWhiteSpace(item.target) ||
                        item.role.StartsWith("$", StringComparison.Ordinal) || !roles.TryAdd(item.role,item.target))
                        throw new ArgumentException("Give each instance role a unique name and target.");
                    else tokens.Add("$" + item.role, item.target);
            }
        }
    }

    /// <summary>One isolated, main-thread world per game slot. Definitions are shared read-only.</summary>
    public sealed partial class QuestSession
    {
        private sealed class Graph
        {
            public QuestDefinition definition;
            public Dictionary<string, QuestStep> steps;
            public QuestStep start;
            public Dictionary<string, HashSet<string>> rulePaths;

            public void IndexRules()
            {
                if (definition.rules == null || definition.rules.Length == 0) return;
                rulePaths = new();
                foreach (var rule in definition.rules)
                {
                    var path = new HashSet<string>();
                    void Collect(string node)
                    {
                        if (!path.Add(node)) return;
                        foreach (var next in steps[node].Outputs()) Collect(next);
                    }
                    Collect(rule.destination);
                    rulePaths.Add(rule.id, path);
                }
            }
        }
        private readonly Dictionary<string, WorldFact> schema;
        private readonly Dictionary<string, Graph> catalog = new();
        private readonly Dictionary<FactAddress, int> values = new();
        private readonly Dictionary<FactAddress, string> sources = new();
        private readonly Dictionary<string, QuestRun> runs = new();
        private readonly Dictionary<(bool signal, FactAddress id), List<QuestRun>> listeners = new();
        private readonly Dictionary<FactAddress, List<Graph>> starters = new();
        private readonly Queue<(FactAddress key, QuestRun writer)> changedFacts = new();
        private readonly List<QuestRun> dispatchBuffer = new();
        private readonly Queue<(string name, int amount, string source)> rewardEvents = new();
        private readonly Queue<QuestTransition> transitions = new();
        /// <summary>Live graph transitions in traversal order, delivered after the world settles. Restore never replays them.</summary>
        public event Action<QuestTransition> Transitioned;
        public event Action<string, int> FactChanged;
        public event Action<FactAddress,int> ScopedFactChanged;
        private bool changing, claiming;
        private bool dirty;
        public event Action Changed;
        /// <summary>Raised after a successful restore, before presentation is refreshed.</summary>
        public event Action Restored;
        public WorldFactDatabase Database { get; }
        public bool Contains(QuestDefinition quest) => quest && catalog.TryGetValue(quest.id, out var graph) && graph.definition == quest;
        public IEnumerable<QuestRun> Journal => runs.Values;
        public string Tracked { get; private set; }
        public long Evaluations { get; private set; }

        private readonly Func<ResourceDefinition, long> available;
        private readonly Dictionary<ResourceDefinition, (string received, string balance)> resourceKeys = new();
        public QuestSession(WorldFactDatabase database, IEnumerable<QuestDefinition> quests) : this(database, quests, null) { }

        public QuestSession(WorldFactDatabase database, IEnumerable<QuestDefinition> quests, Func<ResourceDefinition, long> available)
        {
            this.available = available;
            if (!database) throw new ArgumentNullException(nameof(database));
            Database = database;
            schema = database.BuildIndex();
            foreach (var fact in schema.Values) values.Add(fact.id, fact.initial);
            foreach (var q in quests)
            {
                if (!q || q.database != database) throw new ArgumentException("Use one world fact database for all quests in the session.");
                var graph = new Graph { definition = q, steps = q.Compile(schema), start = q.steps.First(s => s.type == QuestStepType.Start) };
                graph.IndexRules();
                foreach (var step in q.steps)
                    foreach (var goal in step.goals)
                        if (goal.UsesResource && !resourceKeys.ContainsKey(goal.resource))
                            resourceKeys.Add(goal.resource, ("wallet.received/" + goal.resource.Id, "wallet.balance/" + goal.resource.Id));
                if (!catalog.TryAdd(q.id, graph)) throw new ArgumentException("Duplicate quest ID: " + q.id);
                if (q.autoStart) Add(starters, Address(q.startWhen, null), graph);
            }
        }

        private static void Add<K, T>(Dictionary<K, List<T>> map, K key, T item)
        {
            if (!map.TryGetValue(key, out var list)) map.Add(key, list = new List<T>());
            list.Add(item);
        }
        public bool TryGetFact(string id, out int value)
        {
            value = 0;
            return !string.IsNullOrWhiteSpace(id) && values.TryGetValue(id,out value);
        }
        public int GetFact(string id) => values[id];
        public string LastSource(string id) => sources.TryGetValue(id, out var source) ? source : "Initial value";
        public QuestRun Find(string questId) => questId != null && runs.TryGetValue(questId, out var run) ? run : null;
        public QuestStep Step(string quest, string node) => catalog[Find(quest)?.Definition.id ?? quest].steps[node];
        public void StartAvailable() => Change(() =>
        {
            foreach (var list in starters.Values) foreach (var graph in list) TryAutoStart(graph);
        });
        public void StartQuest(string questId) => Change(() => Begin(catalog[questId]));
        public void Track(string questId) => Change(() =>
        {
            if (questId != null && (Find(questId) == null || Find(questId).Definition.hidden))
                throw new ArgumentException("Track a visible quest in this journal.");
            if (Tracked != questId || TrackedGoal != null) { Tracked = questId; TrackedGoal = null; pinned.Remove(questId ?? ""); dirty = true; }
        });
        public void SetFact(string id, int value, string source = "Game") => ApplyWorldInput(false, id, value, source);
        public void SetFlag(string id, bool value, string source = "Game")
        {
            if (schema[id].type != FactType.Flag) throw new ArgumentException("Expected a flag: " + id);
            SetFact(id, value ? 1 : 0, source);
        }
        public void Signal(string signal, int amount = 1)
        {
            if (string.IsNullOrWhiteSpace(signal) || amount < 1) throw new ArgumentException("Supply an event name and positive amount.");
            if (claiming) { rewardEvents.Enqueue((signal, amount, dispatchSource)); return; }
            if (!listeners.ContainsKey((true, signal)))
            {
                traceBuffer?.Add("Unhandled signal",null,null,signal,amount,dispatchSource);
                return;
            }
            ApplyWorldInput(true, signal, amount, null);
        }

        private void ApplyWorldInput(bool isSignal, string id, int value, string source)
        {
            if (claiming || changing) throw new InvalidOperationException("Finish the current operation before changing the quest world.");
            TraceInput(isSignal,id,value,source);
            changing = true;
            try
            {
                if (isSignal) Dispatch((true, id), id, value, source: source ?? dispatchSource);
                else WriteFact(id, value, source);
                ProcessChangedFacts();
            }
            finally { changing = false; }
            Notify();
        }

        private void ProcessChangedFacts()
        {
            while (changedFacts.Count != 0)
            {
                var (key, writer) = changedFacts.Dequeue();
                Dispatch((false, key), null, 0, writer);
                if (starters.TryGetValue(key, out var list)) foreach (var graph in list) TryAutoStart(graph);
                if (FactChanged != null || ScopedFactChanged != null) pendingNotifications.Enqueue((key, values[key]));
            }
        }

        private readonly Queue<(FactAddress key, int value)> pendingNotifications = new();
        private bool notifying;

        private void Change(Action operation)
        {
            if (changing || claiming) throw new InvalidOperationException("Finish the current quest operation before changing its world.");
            changing = true;
            try
            {
                operation();
                ProcessChangedFacts();
            }
            finally { changing = false; }
            Notify();
        }
        private void Notify()
        {
            if (notifying) return;
            notifying = true;
            try
            {
                do
                {
                    while (pendingNotifications.Count > 0)
                    {
                        var fact = pendingNotifications.Dequeue();
                        if (fact.key.Scope == FactScope.World) FactChanged?.Invoke(fact.key.Fact, fact.value);
                        ScopedFactChanged?.Invoke(fact.key, fact.value);
                    }
                    while (transitions.Count > 0)
                    {
                        var transition = transitions.Dequeue();
                        Transitioned?.Invoke(transition);
                    }
                    if (dirty) { dirty = false; Changed?.Invoke(); }
                }
                while (dirty || pendingNotifications.Count > 0 || transitions.Count > 0);
            }
            finally { notifying = false; }
        }
        private void WriteFact(FactAddress id, int value, string source, QuestRun writer = null)
        {
            if (!schema.TryGetValue(id.Fact, out var def) || !def.Accepts(value)) throw new ArgumentException("Unknown fact or invalid value: " + id);
            if (GetFact(id) == value) return;
            values[id] = value;
            sources[id] = source;
            changedFacts.Enqueue((id, writer));
        }
        private void TryAutoStart(Graph graph)
        {
            var q = graph.definition;
            if (!runs.ContainsKey(q.id) && Matches(q.startWhen)) Begin(graph);
        }
        private void Begin(Graph graph, string instanceId = null, string title = null, QuestRoleBinding[] roles = null, string titleKey = null)
        {
            string id = instanceId ?? graph.definition.id;
            if (runs.ContainsKey(id)) return;
            var run = new QuestRun(graph.definition, id, title, roles, titleKey);
            ValidateRoles(run);
            runs.Add(id, run);
            if (string.IsNullOrEmpty(Tracked) && !graph.definition.hidden) Tracked = id;
            Record(QuestTransitionKind.QuestStarted, run, graph.start);
            Advance(run, graph.start);
        }

        private void Dispatch((bool signal, FactAddress id) key, string signal, int amount, QuestRun writer = null, string source = null)
        {
            if (!listeners.TryGetValue(key, out var interested)) return;
            // Taking a snapshot permits branches to replace their subscriptions during delivery.
            dispatchBuffer.Clear();
            dispatchBuffer.AddRange(interested);
            foreach (var run in dispatchBuffer)
            {
                Evaluations++;
                var graph = catalog[run.Definition.id];
                var rule = key.signal || run == writer ? null : MatchingRule(graph, run, key.id);
                if (rule != null) Advance(run, TakeRule(graph, run, rule), true);
                else if (Evaluate(run, signal, amount, source)) Advance(run, graph.steps[run.Current.next]);
            }
        }
        private void Record(QuestTransitionKind kind, QuestRun run, QuestStep step)
        {
            TraceTransition(kind,run,step);
            if (Transitioned != null) transitions.Enqueue(new QuestTransition(kind, run, step));
        }

        private QuestRule MatchingRule(Graph graph, QuestRun run, FactAddress? changedFact)
        {
            if (graph.rulePaths == null) return null;
            foreach (var rule in graph.definition.rules)
            {
                if (changedFact.HasValue && !Address(rule.when,run).Equals(changedFact.Value)) continue;
                if (!Matches(rule.when,run)) continue;
                var downstream = graph.rulePaths[rule.id];
                bool passed = false;
                foreach (var node in run.history)
                    if (downstream.Contains(node)) { passed = true; break; }
                if (!passed) return rule;
            }
            return null;
        }

        private QuestStep TakeRule(Graph graph, QuestRun run, QuestRule rule)
        {
            TraceRule(run,rule);
            (run.takenRules ??= new List<string>()).Add(rule.id);
            return graph.steps[rule.destination];
        }

        private void Advance(QuestRun run, QuestStep step, bool interrupted = false)
        {
            Unwatch(run);
            var graph = catalog[run.Definition.id];
            while (true)
            {
                dirty = true;
                if (run.Current != null) Record(interrupted ? QuestTransitionKind.StepInterrupted : QuestTransitionKind.StepCompleted, run, run.Current);
                interrupted = false;
                if (run.Id == Tracked) TrackedGoal = null;
                if (step.type == QuestStepType.End) pinned.Remove(run.Id);
                run.Current = step;
                run.history.Add(step.id);
                Record(QuestTransitionKind.StepEntered, run, step);
                if (step.type == QuestStepType.End)
                {
                    Record(QuestTransitionKind.QuestCompleted, run, step);
                    return;
                }
                string next = step.next;
                if (step.type == QuestStepType.Objectives && !Evaluate(run, null, 0)) { Watch(run); return; }
                QuestRule redirect = null;
                if (step.type == QuestStepType.Start) redirect = MatchingRule(graph, run, null);
                if (step.type == QuestStepType.SetFact)
                {
                    var address = ResolveAddress(step.factScope, step.factSubject, step.fact, run);
                    if (GetFact(address) != step.value)
                    {
                        WriteFact(address, step.value, run.Definition.title + " / " + step.name, run);
                        redirect = MatchingRule(graph, run, address);
                    }
                }
                if (redirect != null) { step = TakeRule(graph, run, redirect); continue; }
                if (step.type == QuestStepType.Branch)
                    foreach (var route in step.routes)
                        if (Matches(route.when,run)) { next = route.next; break; }
                step = graph.steps[next];
            }
        }
        private bool Evaluate(QuestRun run, string signal, int amount, string source = null)
        {
            int required = 0, done = 0;
            foreach (var goal in run.Current.goals)
            {
                int old = run.Progress(goal);
                int value = old;
                if (goal.mode == GoalMode.WorldState) value = Matches(goal.condition,run) ? 1 : 0;
                else if (goal.mode == GoalMode.HaveResource)
                    value = (int)Math.Clamp(available?.Invoke(goal.resource) ?? 0L, 0L, (long)goal.count);
                else if (goal.mode != GoalMode.TurnInResource && signal != null &&
                    (goal.mode == GoalMode.ReceiveResource ? goal.resourceSignal : goal.signal) == signal) value = (int)Math.Min(goal.count, (long)old + amount);
                if (old != value)
                {
                    run.counts[goal.id] = value; dirty = true; TraceProgress(run,goal,value);
                    if (Tracked == run.Id && TrackedGoal == goal.id && run.Satisfied(goal)) TrackedGoal = null;
                    string evidence = goal.mode == GoalMode.WorldState && value > 0 ? LastSource(Address(goal.condition,run)) : source;
                    if (!string.IsNullOrWhiteSpace(evidence)) (run.evidence ??= new())[goal.id] = evidence;
                    else run.evidence?.Remove(goal.id);
                }
                if (goal.optional) continue;
                required++;
                if (run.Satisfied(goal)) done++;
            }
            return run.Current.join == GoalJoin.All ? done == required : done > 0;
        }
        private void Watch(QuestRun run)
        {
            if (run.Current.type != QuestStepType.Objectives) return;
            if (run.Definition.rules != null)
                foreach (var rule in run.Definition.rules)
                {
                    var address = Address(rule.when,run);
                    if (run.watches.Add((false,address))) Add(listeners,(false,address),run);
                }
            foreach (var goal in run.Current.goals)
            {
                if (goal.mode == GoalMode.TurnInResource) continue;
                var key = goal.mode == GoalMode.WorldState ? (false, Address(goal.condition,run)) : (true, (FactAddress)(goal.UsesResource ? goal.resourceSignal : goal.signal));
                if (run.watches.Add(key)) Add(listeners, key, run);
            }
        }
        private void Unwatch(QuestRun run)
        {
            foreach (var key in run.watches)
            {
                var list = listeners[key];
                list.Remove(run);
                if (list.Count == 0) listeners.Remove(key);
            }
            run.watches.Clear();
        }

        public void ObserveWalletChange(ResourceChange change)
        {
            if (change.Kind == ResourceChangeKind.Restore || change.Kind == ResourceChangeKind.Replica ||
                !change.Resource || !resourceKeys.TryGetValue(change.Resource, out var keys)) return;
            if (change.TotalDelta > 0) Signal(keys.received, (int)Math.Min(change.TotalDelta, int.MaxValue));
            Signal(keys.balance);
        }

        public void RefreshWalletBalances()
        {
            foreach (var keys in resourceKeys.Values) Signal(keys.balance);
        }

        public bool TryTurnIn(QuestDefinition quest, QuestGoal goal, ResourceStore wallet)
        {
            if (!Contains(quest) || wallet == null || goal == null || !goal.resource) return false;
            return TryTurnIn(Find(quest.id), goal, wallet);
        }
        public bool TryTurnIn(QuestRun run, QuestGoal goal, ResourceStore wallet)
        {
            if (run == null || Find(run.Id) != run || wallet == null || goal == null || !goal.resource) return false;
            if (claiming || changing || run == null || run.Finished || goal.mode != GoalMode.TurnInResource ||
                Array.IndexOf(run.Current.goals, goal) < 0 || run.Satisfied(goal)) return false;
            claiming = true;
            try
            {
                if (!wallet.TrySpend(new ResourceCost(new ResourceAmount(goal.resource, goal.count)),out var failure))
                { if (IsTracing) TraceDenied(run,goal,failure.ToString()); return false; }
                run.counts[goal.id] = goal.count;
                dirty = true;
                if (Evaluate(run, null, 0)) Advance(run, catalog[run.Definition.id].steps[run.Current.next]);
                ProcessChangedFacts();
            }
            finally { EndWalletOperation(); }
            Notify();
            return true;
        }

        private void EndWalletOperation()
        {
            claiming = false;
            while (rewardEvents.Count > 0)
            {
                var e = rewardEvents.Dequeue();
                SignalFrom(e.name, e.amount, e.source);
            }
        }

        public bool TryClaimReward(string id, Func<bool> grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            var run = Find(id);
            if (claiming || changing || run == null || !run.RewardPending) return false;
            claiming = true;
            try { if (!grant()) return false; run.RewardClaimed = true; dirty = true; }
            finally { EndWalletOperation(); }
            Notify();
            return true;
        }

        public QuestSnapshot Capture()
        {
            if (changing || claiming) throw new InvalidOperationException("Capture quests between gameplay operations.");
            return new QuestSnapshot
            {
                tracked = Tracked, trackedGoal = TrackedGoal,
                pinned = pinned.ToArray(),
                facts = values.Select(p => new SavedFact { id = p.Key.Fact, scope = p.Key.Scope, subject = p.Key.Subject,
                    value = p.Value, source = sources.TryGetValue(p.Key, out var origin) ? origin : null }).ToArray(),
                quests = runs.Values.Select(r => new SavedQuest
                {
                    id = r.Id, definition = r.Definition.id, title = r.Title, titleKey = r.TitleKey,
                    roles = r.Roles.Select(p => new QuestRoleBinding { role = p.Key, target = p.Value }).ToArray(),
                    path = r.history.ToArray(),
                    takenRules = r.takenRules?.ToArray(),
                    rewardClaimed = r.RewardClaimed,
                    rewardSeed = r.Seed,
                    progress = r.counts.Select(p => new SavedGoal { id = p.Key, count = p.Value, source = r.evidence != null && r.evidence.TryGetValue(p.Key,out var origin) ? origin : null }).ToArray()
                }).ToArray()
            };
        }

        public void Validate(QuestSnapshot data)
        {
            if (data == null || data.version != 1 || data.facts == null || data.quests == null) throw new ArgumentException("Unsupported quest snapshot.");
            var factIds = new HashSet<FactAddress>();
            foreach (var f in data.facts)
                if (f == null || f.id == null || !schema.TryGetValue(f.id, out var def) || !def.Accepts(f.value) || !factIds.Add(new FactAddress(f.scope,f.subject,f.id)))
                    throw new ArgumentException("Invalid saved fact.");
            var ids = new HashSet<string>();
            var visible = new HashSet<string>();
            var pinnable = new HashSet<string>();
            foreach (var q in data.quests)
            {
                if (q == null || q.id == null || !catalog.TryGetValue(string.IsNullOrEmpty(q.definition) ? q.id : q.definition, out var graph) || !ids.Add(q.id) || q.path == null ||
                    q.path.Length == 0 || q.path[0] != graph.start.id || q.progress == null) throw new ArgumentException("Invalid saved quest.");
                if (string.IsNullOrWhiteSpace(q.id) || (catalog.ContainsKey(q.id) && q.id != graph.definition.id)) throw new ArgumentException("Invalid instance ID.");
                ValidateRoles(new QuestRun(graph.definition,q.id,q.title,q.roles,q.titleKey));
                if (!graph.definition.hidden) visible.Add(q.id);
                var redirects = new Dictionary<string, QuestRule>();
                foreach (var id in q.takenRules ?? Array.Empty<string>())
                {
                    var rule = Array.Find(graph.definition.rules ?? Array.Empty<QuestRule>(), r => r.id == id);
                    if (rule == null || !redirects.TryAdd(rule.destination, rule)) throw new ArgumentException("Unknown or duplicate saved quest rule.");
                }
                var interrupted = new HashSet<string>();
                var visited = new HashSet<string>();
                QuestStep last = null;
                var allowed = new Dictionary<string, int>();
                foreach (var node in q.path)
                {
                    if (node == null || !graph.steps.TryGetValue(node, out var step) || visited.Contains(node)) throw new ArgumentException($"Saved quest {q.id}: step {node} is missing or repeated.");
                    if (redirects.Remove(node, out var rule))
                    {
                        if (last == null || (last.type != QuestStepType.Start && last.type != QuestStepType.SetFact && last.type != QuestStepType.Objectives)
                            || graph.rulePaths[rule.id].Overlaps(visited)) throw new ArgumentException("Invalid saved rule transition.");
                        if (last.type == QuestStepType.Objectives) interrupted.Add(last.id);
                    }
                    else if (last != null && !last.Outputs().Contains(node)) throw new ArgumentException($"Saved quest {q.id}: the link from {last.id} to {node} is missing.");
                    visited.Add(node);
                    if (step.type == QuestStepType.Objectives) foreach (var g in step.goals) allowed.Add(g.id, g.mode == GoalMode.WorldState ? 1 : g.count);
                    last = step;
                }
                if (redirects.Count != 0 || last.type != QuestStepType.End && last.type != QuestStepType.Objectives ||
                    q.rewardClaimed && (last.type != QuestStepType.End || !last.HasReward)) throw new ArgumentException("Invalid saved quest boundary.");
                foreach (var g in q.progress)
                    if (g == null || g.id == null || !allowed.TryGetValue(g.id, out var limit) || g.count < 0 || g.count > limit || !allowed.Remove(g.id))
                        throw new ArgumentException($"Saved quest {q.id}: missing, duplicate or out-of-range goal {g?.id}.");
                if (!graph.definition.hidden && last.type != QuestStepType.End) pinnable.Add(q.id);
                var progress = q.progress.ToDictionary(row => row.id, row => row.count);
                foreach (var id in q.path)
                {
                    var step = graph.steps[id];
                    if (step.type != QuestStepType.Objectives) continue;
                    bool IsDone(QuestGoal goal) => progress.TryGetValue(goal.id, out var n) && n >= (goal.mode == GoalMode.WorldState ? 1 : goal.count);
                    var required = step.goals.Where(goal => !goal.optional);
                    bool complete = step.join == GoalJoin.All ? required.All(IsDone) : required.Any(IsDone);
                    if (complete == (step == last || interrupted.Contains(id))) throw new ArgumentException("Saved objective progress disagrees with the active step.");
                }
            }
            if (!string.IsNullOrEmpty(data.tracked) && !visible.Contains(data.tracked)) throw new ArgumentException("Invalid tracked quest.");
            if (!string.IsNullOrEmpty(data.trackedGoal))
            {
                var primary = Array.Find(data.quests,q=>q.id==data.tracked) ?? throw new ArgumentException("Unknown tracked objective.");
                var step = catalog[string.IsNullOrEmpty(primary.definition) ? primary.id : primary.definition].steps[primary.path[primary.path.Length-1]];
                if (step.type!=QuestStepType.Objectives || !Array.Exists(step.goals,g=>!g.hidden && g.id==data.trackedGoal))
                    throw new ArgumentException("Unknown tracked objective.");
            }
            var pins = new HashSet<string>();
            foreach (string id in data.pinned ?? Array.Empty<string>())
                if (!pinnable.Contains(id) || id == data.tracked || !pins.Add(id) || pins.Count > MaxPins) throw new ArgumentException("Invalid pinned quest.");
            foreach (var fact in factIds) if (fact.Scope == FactScope.QuestInstance && !ids.Contains(fact.Subject)) throw new ArgumentException("Unknown saved fact owner.");
        }

        public void Restore(QuestSnapshot data)
        {
            if (changing || claiming) throw new InvalidOperationException("Restore between gameplay operations.");
            Validate(data);
            listeners.Clear(); runs.Clear(); sources.Clear(); changedFacts.Clear(); pendingNotifications.Clear(); transitions.Clear();
            values.Clear();
            foreach (var def in schema.Values) values[def.id] = def.initial;
            foreach (var f in data.facts) { var address = new FactAddress(f.scope,f.subject,f.id); values[address] = f.value; if (!string.IsNullOrEmpty(f.source)) sources[address] = f.source; }
            foreach (var q in data.quests)
            {
                var graph = catalog[string.IsNullOrEmpty(q.definition) ? q.id : q.definition];
                var run = new QuestRun(graph.definition,q.id,q.title,q.roles,q.titleKey) { Current = graph.steps[q.path[q.path.Length - 1]], RewardClaimed = q.rewardClaimed, Seed = q.rewardSeed };
                run.history.AddRange(q.path);
                if (q.takenRules?.Length > 0) run.takenRules = new List<string>(q.takenRules);
                foreach (var p in q.progress)
                {
                    run.counts.Add(p.id, p.count);
                    if (!string.IsNullOrEmpty(p.source)) (run.evidence ??= new())[p.id] = p.source;
                }
                runs.Add(q.id, run);
                Watch(run);
            }
            pinned.Clear();
            if (data.pinned != null) pinned.AddRange(data.pinned);
            TrackedGoal = string.IsNullOrEmpty(data.trackedGoal) ? null : data.trackedGoal;
            Tracked = string.IsNullOrEmpty(data.tracked) ? null : data.tracked;
            Restored?.Invoke();
            dirty = true;
            Notify();
        }
    }
}
