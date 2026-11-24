using System;
using UnityEngine;

namespace EasyQuests
{
    public enum MarkerLabelMode { IconDefault, Always, OnScreenOnly, OffScreenOnly, Hidden }
    public enum MarkerEdgeMode { IconDefault, Pin, Hide }

    [Serializable]
    public sealed class QuestIcon
    {
        [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        public string name = "New icon";
        public Sprite sprite;
        [Tooltip("Optional arrow drawn pointing up. The HUD rotates it towards the target. Leave empty to keep the normal icon upright at the edge.")]
        public Sprite edgeSprite;
        [Tooltip("Optional shared sprite material. Pulse and Shimmer are supplied with the framework.")]
        public Material spriteMaterial;
        public MarkerLabelMode labelMode = MarkerLabelMode.Hidden;
        public MarkerEdgeMode edgeMode = MarkerEdgeMode.Pin;
        public bool overrideEdgePadding;
        [Min(0), Tooltip("Gap in Canvas units from the outside of the icon to the edge. Zero is flush.")]
        public float edgePadding;
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/Narrative/Quest Icon Library")]
    public sealed class QuestIconLibrary : ScriptableObject
    {
        public QuestIcon[] icons = Array.Empty<QuestIcon>();

        public QuestIcon Find(string id)
        {
            if (string.IsNullOrEmpty(id) || icons == null) return null;
            foreach (var icon in icons)
                if (icon != null && icon.Id == id) return icon;
            return null;
        }
    }
}
