using System;
using TMPro;
using UnityEngine;

namespace EasyUI
{
    /// <summary>Creates TextMeshPro labels using the enclosing window manager’s font.</summary>
    public static class UIText
    {
        private static TMP_FontAsset defaultFont;

        /// <summary>The bundled font with Latin, Cyrillic, and a fallback for UI symbols.</summary>
        public static TMP_FontAsset DefaultFont
        {
            get
            {
                if (!defaultFont) defaultFont = Resources.Load<TMP_FontAsset>("EasyUI/Default Font");
                if (!defaultFont) throw new InvalidOperationException("The EasyUI default font is missing. Check the EasyGameFramework package installation.");
                return defaultFont;
            }
        }

        public static TMP_FontAsset ResolveFont(Transform context)
        {
            var manager = context ? context.GetComponentInParent<WindowManager>(true) : null;
            return manager && manager.Font ? manager.Font : DefaultFont;
        }

        /// <summary>Opt an existing UI label into per-player typography.</summary>
        public static UIStyledText Style(TextMeshProUGUI label, UITextRole role)
        {
            if (!label) throw new ArgumentNullException(nameof(label));
            if (!label.TryGetComponent<UIStyledText>(out var binding)) binding=label.gameObject.AddComponent<UIStyledText>();
            binding.Role=role;
            return binding;
        }

        public static TextMeshProUGUI Add(GameObject target, string text) => Add(target,text,UITextRole.Body);

        public static TextMeshProUGUI Add(GameObject target,string text,UITextRole role,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, TMP_FontAsset font = null)
        {
            var label=Add(target,text,18f,alignment,font);
            Style(label,role);
            return label;
        }

        /// <summary>Adds a noninteractive label. Parent the GameObject before calling this method.</summary>
        public static TextMeshProUGUI Add(GameObject target, string text, float fontSize = 18,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, TMP_FontAsset font = null)
        {
            var label = target.AddComponent<TextMeshProUGUI>();
            label.font = font ? font : ResolveFont(target.transform);
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }
    }
}
