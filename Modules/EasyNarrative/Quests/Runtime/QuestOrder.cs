using System;
using UnityEngine;

namespace EasyQuests
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Order")]
    public sealed class QuestOrder : MonoBehaviour
    {
        public QuestWorld world;
        public QuestDefinition template;
        [Tooltip("Permanent, unique ID for this order in the world and saved games.")] public string instanceId;
        [Tooltip("Display name for this order. Leave empty to show the template name.")] public string title;
        public string titleKey;
        [Tooltip("Bind role names such as recipient to permanent character IDs, group IDs or Quest Target IDs.")] public QuestRoleBinding[] roles=Array.Empty<QuestRoleBinding>();
        public QuestRun Run => world ? world.Session.Find(instanceId) : null;

        /// <summary>Can be connected directly to an interaction's UnityEvent. Reusing the same ID resumes its existing order.</summary>
        public void StartOrder() => world.Session.StartInstance(template,instanceId,roles,title,titleKey);
        public void SendSignal(string signal) => world.Session.SignalFor(instanceId,signal,1,name);
        public void SetFlag(string fact) => world.Session.SetFact(new FactAddress(FactScope.QuestInstance,instanceId,fact),1,name);
        public void Track() => world.Session.Track(instanceId);
        public void Pin() => world.Session.Pin(instanceId);
        public void Unpin() => world.Session.Pin(instanceId,false);
    }
}
