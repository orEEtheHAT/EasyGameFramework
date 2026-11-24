using System;
using EasyQuests;
using UnityEngine;

namespace EasyNarrative.UI
{
    /// <summary>Connect Play to an interaction, quest reaction or gameplay UnityEvent.</summary>
    [AddComponentMenu("EasyGameFramework/Narrative/Ambient Dialogue Line")]
    public sealed class DialogueBark : MonoBehaviour
    {
        public DialoguePresenter listener;
        public DialogueDefinition script;
        [Tooltip("Leave empty to use the dialogue's opening line.")] public string lineId;
        [Min(0)] public float maxDistance = 12;
        [Min(0)] public float cooldown = 5;
        [Min(.1f)] public float subtitleSeconds = 3;
        public int priority;
        public bool once;
        [Tooltip("Optional world flag for once per save slot. Without a flag, once lasts for this emitter's lifetime.")]
        public string playedFlag;
        public FactCondition[] conditions = Array.Empty<FactCondition>();
        private float nextTime;
        private bool played;

        public void Play() => TryPlay();
        public bool TryPlay()
        {
            if (!isActiveAndEnabled || !listener || !script || Time.unscaledTime < nextTime) return false;
            if (maxDistance > 0 && (listener.transform.position - transform.position).sqrMagnitude > maxDistance * maxDistance) return false;
            var world = listener.world ? listener.world.Session : null;
            bool durable = once && !string.IsNullOrEmpty(playedFlag);
            if (once && (durable ? world == null || !world.TryGetFact(playedFlag, out int flag) || flag != 0 : played)) return false;
            if (conditions != null) foreach (var condition in conditions)
                if (world == null || condition == null || !world.TryGetFact(condition.fact, out _) || !world.Matches(condition)) return false;
            var id = string.IsNullOrEmpty(lineId) ? script.entry : lineId;
            var spoken = Array.Find(script.lines, l => l.id == id);
            if (!listener.TryBark(spoken, priority, subtitleSeconds)) return false;
            played = true;
            nextTime = Time.unscaledTime + cooldown;
            if (durable) world.SetFlag(playedFlag, true, name);
            return true;
        }
    }
}
