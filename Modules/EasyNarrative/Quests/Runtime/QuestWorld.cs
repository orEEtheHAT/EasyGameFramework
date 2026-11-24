using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyQuests
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest World")]
    public sealed partial class QuestWorld : MonoBehaviour
    {
        public WorldFactDatabase database;
        public QuestDefinition[] quests = Array.Empty<QuestDefinition>();
        private QuestSession session;
        public QuestSession Session => session ??= CreateSession();
        private readonly Dictionary<string, QuestTarget> targets = new();
        private readonly Dictionary<string, List<QuestTarget>> groups = new();
        public int TargetRevision { get; private set; }
        public IReadOnlyList<QuestTarget> Group(string key) =>
            key != null && groups.TryGetValue(key, out var members) ? members : Array.Empty<QuestTarget>();

        private void Start() { ConnectWallet(); Session.StartAvailable(); }
        public void StartQuest(QuestDefinition quest) => Session.StartQuest(quest.id);
        public void Signal(string signal) => Session.Signal(signal);
        public QuestTarget Target(string id) => id != null && targets.TryGetValue(id, out var target) ? target : null;
        public event Action<QuestTarget, bool> TargetRegistrationChanged;
        internal void Register(QuestTarget target, string id, string group)
        {
            bool indexed = false;
            if (!string.IsNullOrWhiteSpace(id))
            {
                indexed = targets.TryAdd(id, target);
                if (!indexed) Debug.LogError("Duplicate quest target ID: " + id, target);
            }
            if (!string.IsNullOrWhiteSpace(group))
            {
                if (!groups.TryGetValue(group, out var members)) groups.Add(group, members = new List<QuestTarget>());
                members.Add(target);
                indexed = true;
            }
            else if (string.IsNullOrWhiteSpace(id)) Debug.LogWarning("Assign a quest target ID or marker group.", target);
            if (indexed) { TargetRevision++; TargetRegistrationChanged?.Invoke(target, true); }
        }

        internal void Unregister(QuestTarget target, string id, string group)
        {
            bool changed = id != null && Target(id) == target && targets.Remove(id);
            if (group != null && groups.TryGetValue(group, out var members) && members.Remove(target))
            {
                changed = true;
                if (members.Count == 0) groups.Remove(group);
            }
            if (changed) { TargetRevision++; TargetRegistrationChanged?.Invoke(target, false); }
        }
    }
}
