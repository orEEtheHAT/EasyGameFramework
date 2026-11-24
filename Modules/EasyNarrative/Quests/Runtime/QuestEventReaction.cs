using UnityEngine;
using UnityEngine.Events;

namespace EasyQuests
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Event Reaction")]
    public sealed class QuestEventReaction : MonoBehaviour
    {
        public QuestWorld world;
        public QuestDefinition quest;
        public QuestTransitionKind trigger = QuestTransitionKind.QuestCompleted;
        [Tooltip("The graph card to observe for step events.")]
        public string stepId;
        [Tooltip("Leave empty to react to all instances of this template.")] public string instanceId;
        [Tooltip("Runs once for each matching live transition while this component is enabled.")]
        public UnityEvent onTriggered = new();
        private QuestSession session;

        private void OnEnable()
        {
            if (!world || !world.database || !quest) return;
            session = world.Session;
            session.Transitioned += OnTransition;
        }

        private void OnDisable()
        {
            if (session != null) session.Transitioned -= OnTransition;
            session = null;
        }

        private void OnTransition(QuestTransition transition)
        {
            if (session == null || transition.Quest != quest || transition.Kind != trigger) return;
            if (!string.IsNullOrEmpty(instanceId) && transition.Run.Id != instanceId) return;
            if ((trigger == QuestTransitionKind.StepEntered || trigger == QuestTransitionKind.StepCompleted || trigger == QuestTransitionKind.StepInterrupted)
                && transition.Step.id != stepId) return;
            onTriggered.Invoke();
        }
    }
}
