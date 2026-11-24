using System.Collections.Generic;
using EasyCombat;
using UnityEngine;

namespace EasyQuests.Integrations
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Group Deaths")]
    public sealed class QuestGroupDeaths : MonoBehaviour
    {
        public QuestWorld world;
        public string group, signal;
        private readonly Dictionary<QuestTarget, Health> anchors = new();
        private readonly Dictionary<Health, int> sources = new();

        private void OnEnable()
        {
            if (!world || string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(signal)) return;
            world.TargetRegistrationChanged += Membership;
            var members = world.Group(group);
            for (int i = 0; i < members.Count; i++) Membership(members[i], true);
        }
        private void OnDisable()
        {
            if (world) world.TargetRegistrationChanged -= Membership;
            foreach (var source in sources.Keys) if (source) source.Died -= Died;
            anchors.Clear(); sources.Clear();
        }
        private void Membership(QuestTarget target, bool joined)
        {
            if (joined)
            {
                if (target.group != group) return;
                var health = target.GetComponentInParent<Health>();
                if (!health || !anchors.TryAdd(target, health)) return;
                sources.TryGetValue(health, out int count);
                if (count == 0) health.Died += Died;
                sources[health] = count + 1;
            }
            else if (anchors.Remove(target, out var old))
            {
                if (--sources[old] == 0) { sources.Remove(old); if (old) old.Died -= Died; }
            }
        }
        private void Died(Health victim) => world.Session.SignalFrom(signal, 1, victim.name);
    }
}
