using System;
using EasyInteraction;
using EasyResources;
using UnityEngine;
using Health = EasyCombat.Health;

namespace EasyQuests.Integrations
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Event Binding")]
    public sealed class QuestEventBinding : MonoBehaviour
    {
        public enum EventSource { SuccessfulInteraction, Death, ResourcesReceived }
        public QuestWorld world;
        public string signal;
        public EventSource source;
        public InteractionActor actor;
        public InteractionAction action;
        public Health health;
        public ResourceContainer wallet;
        [Tooltip("The event count uses the resource's integer storage units.")] public ResourceDefinition resource;
        private Health observedHealth;
        private InteractionActor observedActor;
        private ResourceStore observedStore;

        private void OnEnable()
        {
            if (!world || string.IsNullOrWhiteSpace(signal))
            {
                Debug.LogWarning("Assign a Quest World and an event name.", this);
                return;
            }
            switch (source)
            {
                case EventSource.Death when health:
                    observedHealth = health;
                    observedHealth.Died += Died;
                    break;
                case EventSource.SuccessfulInteraction when actor && action:
                    observedActor = actor;
                    observedActor.ActionStateChanged += Interacted;
                    break;
                case EventSource.ResourcesReceived when wallet && resource:
                    observedStore = wallet.Store;
                    observedStore.DetailedChanged += Received;
                    break;
                default:
                    Debug.LogWarning("Assign the source for this quest binding.", this);
                    break;
            }
        }

        private void OnDisable()
        {
            if (observedHealth) observedHealth.Died -= Died;
            if (observedActor) observedActor.ActionStateChanged -= Interacted;
            if (observedStore != null) observedStore.DetailedChanged -= Received;
            observedHealth = null;
            observedActor = null;
            observedStore = null;
        }

        private void Died(Health _) => world.Session.SignalFrom(signal, 1, name);
        private void Interacted(InteractionActionEvent e)
        {
            if (e.Action == action && e.State == InteractionActionState.Completed) world.Session.SignalFrom(signal, 1, name);
        }
        private void Received(ResourceChange change)
        {
            if (change.Kind == ResourceChangeKind.Restore || change.Kind == ResourceChangeKind.Replica) return;
            if (change.Resource == resource && change.TotalDelta > 0)
                world.Session.SignalFrom(signal, (int)Math.Min(change.TotalDelta, int.MaxValue), name);
        }
    }
}
