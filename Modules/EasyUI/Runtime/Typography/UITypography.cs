using System;
using TMPro;
using UnityEngine;

namespace EasyUI
{
    public enum UITextRole { Title, Speaker, Body, Button, Caption, Marker }
    public enum UITextFinish { Paper, Ink, Gilded, Beacon }

    [Serializable]
    public sealed class UITextStyle
    {
        [Min(1)] public float size;
        public UITextFinish finish;
        public Color color;
        public bool gradient;
        public Color bottomColor = Color.white;
        public FontStyles emphasis;
        [Tooltip("Optional TMP SDF material for this role. An empty field uses Finish.")]
        public Material customMaterial;
        [Tooltip("Static alternative for the custom material. Reduced Motion uses plain ink if this is empty.")]
        public Material customStillMaterial;
        public UITextStyle(float size, UITextFinish finish, Color color)
        { this.size = size; this.finish = finish; this.color = color; }
    }

    [Serializable]
    public struct UIReadingPreferences
    {
        [Range(.8f, 1.6f)] public float scale;
        public bool reducedMotion;
        public bool highContrast;
        public static UIReadingPreferences Default => new UIReadingPreferences { scale = 1f };
        public bool IsValid => scale >= .8f && scale <= 1.6f;
        internal float EffectiveScale => float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0 ? 1f : Mathf.Clamp(scale, .8f, 1.6f);
    }

    [CreateAssetMenu(menuName = "EasyGameFramework/UI/Typography Theme")]
    public sealed class UITypography : ScriptableObject
    {
        public UITextStyle title = new(32, UITextFinish.Gilded, new Color(1f, .86f, .55f)) { gradient = true, bottomColor = new Color(.91f, .64f, .33f) };
        public UITextStyle speaker = new(20, UITextFinish.Ink, new Color(1f, .83f, .53f));
        public UITextStyle body = new(22, UITextFinish.Paper, new Color(.94f, .95f, .92f));
        public UITextStyle button = new(20, UITextFinish.Paper, Color.white);
        public UITextStyle caption = new(16, UITextFinish.Paper, new Color(.78f, .85f, .89f));
        public UITextStyle marker = new(18, UITextFinish.Beacon, new Color(.77f, .98f, 1f));

        private static UITypography standard;
        public static UITypography Default
        {
            get
            {
                if (!standard) standard = Resources.Load<UITypography>("EasyUI/Typography/Standard");
                if (!standard) throw new InvalidOperationException("The standard EasyUI typography theme is missing.");
                return standard;
            }
        }
        public UITextStyle Get(UITextRole role) => role switch
        {
            UITextRole.Title => title,
            UITextRole.Speaker => speaker,
            UITextRole.Button => button,
            UITextRole.Caption => caption,
            UITextRole.Marker => marker,
            _ => body
        };
    }
}
