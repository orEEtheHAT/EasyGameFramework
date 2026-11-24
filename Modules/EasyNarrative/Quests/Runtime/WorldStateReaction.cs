using UnityEngine;
using UnityEngine.Events;

namespace EasyQuests
{
    public enum WorldStateSource { Fact, ActiveStep, QuestFinished }

    [AddComponentMenu("EasyGameFramework/Narrative/World State Reaction")]
    public sealed class WorldStateReaction : MonoBehaviour
    {
        public QuestWorld world;
        public WorldStateSource source;
        public FactCondition condition = new();
        public QuestDefinition quest;
        public string stepId;
        [Tooltip("Optional repeatable order ID.")] public string instanceId;
        public GameObject showWhenTrue;
        public GameObject showWhenFalse;
        [Tooltip("Applied on enable, on load, and when the condition becomes true. Use for persistent scene state.")]
        public UnityEvent whenTrue = new();
        [Tooltip("Applied on enable, on load, and when the condition becomes false.")]
        public UnityEvent whenFalse = new();
        private QuestSession session;
        private bool applied;
        private bool last;
        private bool RelativeFact => condition != null &&
            (condition.scope == FactScope.QuestInstance && string.IsNullOrEmpty(condition.subject) ||
             !string.IsNullOrEmpty(condition.subject) && condition.subject.StartsWith("$",System.StringComparison.Ordinal));

        private void OnEnable()
        {
            if (!world || !world.database) return;
            session = world.Session;
            session.Restored += Synchronize;
            if (source == WorldStateSource.Fact) session.ScopedFactChanged += OnFact;
            if (source != WorldStateSource.Fact || RelativeFact) session.Transitioned += OnTransition;
            Synchronize();
        }

        private void OnDisable()
        {
            if (session == null) return;
            session.Restored -= Synchronize;
            session.ScopedFactChanged -= OnFact;
            session.Transitioned -= OnTransition;
            session = null;
        }

        private void OnFact(FactAddress address, int value)
        {
            if (condition != null && address.Fact == condition.fact && address.Scope == condition.scope) Refresh();
        }

        private void OnTransition(QuestTransition transition)
        {
            if (source == WorldStateSource.Fact)
            {
                if (transition.Kind == QuestTransitionKind.QuestStarted && transition.Run.Id == instanceId) Refresh();
                return;
            }
            if (transition.Quest != quest || transition.Run.Id != (string.IsNullOrEmpty(instanceId) ? quest.id : instanceId)) return;
            if (source == WorldStateSource.QuestFinished && transition.Kind != QuestTransitionKind.QuestCompleted) return;
            if (source == WorldStateSource.ActiveStep && transition.Step.id != stepId) return;
            Refresh();
        }

        private void Synchronize()
        {
            applied = false;
            Refresh();
        }

        private void Refresh()
        {
            if (session == null) return;
            bool matches;
            if (source == WorldStateSource.Fact)
            {
                if (condition == null || !session.TryGetFact(condition.fact, out _)) return;
                var instance = session.Find(instanceId);
                matches = (!RelativeFact || instance != null) && session.Matches(condition,instance);
            }
            else
            {
                if (!session.Contains(quest)) return;
                var run = session.Find(string.IsNullOrEmpty(instanceId) ? quest.id : instanceId);
                matches = run != null && (source == WorldStateSource.QuestFinished
                    ? run.Finished : !run.Finished && run.Current.id == stepId);
            }
            if (applied && last == matches) return;
            last = matches;
            applied = true;
            SetVisible(showWhenTrue, matches);
            SetVisible(showWhenFalse, !matches);
            (matches ? whenTrue : whenFalse).Invoke();
        }
        private void SetVisible(GameObject item, bool visible)
        {
            if (item && item.activeSelf != visible && !transform.IsChildOf(item.transform))
                item.SetActive(visible);
        }
    }
}
