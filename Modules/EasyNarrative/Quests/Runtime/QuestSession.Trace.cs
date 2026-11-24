using System;
using System.Collections;
using System.Collections.Generic;

namespace EasyQuests
{
    public readonly struct QuestTraceEntry
    {
        public readonly long Sequence;
        public readonly string Kind, Quest, Step, Key, Source;
        public readonly int Value;
        internal QuestTraceEntry(long sequence, string kind, string quest, string step, string key, int value, string source)
        {
            Sequence = sequence; Kind = kind; Quest = quest; Step = step; Key = key; Value = value; Source = source;
        }
    }

    public sealed partial class QuestSession
    {
        private sealed class TraceBuffer : IReadOnlyList<QuestTraceEntry>
        {
            private readonly QuestTraceEntry[] items;
            private int write;
            private long sequence;
            public int Count { get; private set; }
            public TraceBuffer(int capacity) { items = new QuestTraceEntry[capacity]; }
            public QuestTraceEntry this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                    return items[(write - Count + index + items.Length) % items.Length];
                }
            }
            public void Add(string kind, string quest, string step, string key, int value, string source)
            {
                items[write] = new QuestTraceEntry(++sequence, kind, quest, step, key, value, source);
                write = (write + 1) % items.Length;
                Count = Math.Min(Count + 1, items.Length);
            }
            public IEnumerator<QuestTraceEntry> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private TraceBuffer traceBuffer;
        private string dispatchSource;
        public bool IsTracing => traceBuffer != null;
        public IReadOnlyList<QuestTraceEntry> Trace => (IReadOnlyList<QuestTraceEntry>)traceBuffer ?? Array.Empty<QuestTraceEntry>();
        public void EnableTrace(int capacity = 128) => traceBuffer = new TraceBuffer(Math.Clamp(capacity, 1, 4096));
        public void DisableTrace() => traceBuffer = null;

        public void SignalFrom(string signal, int amount, string source)
        {
            var previous = dispatchSource;
            try { dispatchSource = source; Signal(signal, amount); }
            finally { dispatchSource = previous; }
        }
        private void TraceInput(bool signal, string key, int value, string source) =>
            traceBuffer?.Add(signal ? "Signal" : "Fact", null, null, key, value, source ?? dispatchSource);
        private void TraceProgress(QuestRun run, QuestGoal goal, int value) =>
            traceBuffer?.Add("Goal progress", run.Id, run.Current.id, goal.id, value, dispatchSource);
        private void TraceTransition(QuestTransitionKind kind, QuestRun run, QuestStep step) =>
            traceBuffer?.Add(kind.ToString(), run.Id, step.id, step.name, 0, dispatchSource);
        private void TraceRule(QuestRun run, QuestRule rule) =>
            traceBuffer?.Add("Rule", run.Id, run.Current?.id, rule.id, 0, rule.when.fact);
        private void TraceDenied(QuestRun run, QuestGoal goal, string reason) =>
            traceBuffer?.Add("Refused", run.Id, run.Current.id, goal.id, 0, reason);
    }
}
