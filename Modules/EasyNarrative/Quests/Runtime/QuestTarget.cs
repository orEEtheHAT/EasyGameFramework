using UnityEngine;

namespace EasyQuests
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Target")]
    public sealed class QuestTarget : MonoBehaviour
    {
        public QuestWorld world;
        public string id;
        [Tooltip("Shared by all objects to mark together. Group-only targets can leave ID empty.")]
        public string group;
        private QuestWorld registeredWorld;
        private string registeredId, registeredGroup;

        public Vector3 offset = Vector3.up;
        public Vector3 Position => transform.TransformPoint(offset);
        /// <summary>Bind a spawned or pooled marker, or move it to another group.</summary>
        public void Configure(QuestWorld owner, string targetId, string groupKey = null)
        {
            OnDisable();
            world = owner;
            id = targetId;
            group = groupKey;
            if (isActiveAndEnabled) OnEnable();
        }
        private void OnEnable()
        {
            registeredWorld = world;
            registeredId = id;
            registeredGroup = group;
            if (world) world.Register(this, id, group);
        }
        private void OnDisable()
        {
            if (registeredWorld) registeredWorld.Unregister(this, registeredId, registeredGroup);
            registeredWorld = null;
        }
    }
}
