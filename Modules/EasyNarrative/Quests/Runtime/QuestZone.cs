using UnityEngine;

namespace EasyQuests
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Zone")]
    public sealed class QuestZone : MonoBehaviour
    {
        public QuestWorld world;
        [Tooltip("Root of the actor whose entry counts.")] public Transform actor;
        public string signal;
        private readonly System.Collections.Generic.HashSet<Collider> inside = new();
        private void OnTriggerEnter(Collider other)
        {
            if (!world || !actor || string.IsNullOrWhiteSpace(signal) || !other.transform.IsChildOf(actor)) return;
            inside.RemoveWhere(c => !c || !c.enabled || !c.gameObject.activeInHierarchy);
            if (inside.Add(other) && inside.Count == 1) world.Session.SignalFrom(signal, 1, name);
        }
        private void OnTriggerExit(Collider other) => inside.Remove(other);
        private void OnDisable() => inside.Clear();
    }
}
