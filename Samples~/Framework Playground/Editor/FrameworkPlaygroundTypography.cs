using System;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkPlaygroundTypography
    {
        internal static void Upgrade(EasySuiteLab lab)
        {
            if (!lab.Windows) throw new InvalidOperationException("Restore the sample Window Manager before updating the Playground.");
            var root = lab.Windows.transform;
            lab.Hud = HudLabel(root, AssignedLabel(lab, nameof(lab.Hud)), "HUD Header/Player and world");
            lab.Performance = HudLabel(root, AssignedLabel(lab, nameof(lab.Performance)), "HUD Header/Performance");
            lab.Prompt = HudLabel(root, AssignedLabel(lab, nameof(lab.Prompt)), "HUD Prompts/Interaction Prompt");
            lab.Notice = HudLabel(root, AssignedLabel(lab, nameof(lab.Notice)), "HUD Prompts/Notice");
            HudLabel(root, null, "HUD Header/Title");
            HudLabel(root, null, "Crosshair");

            string menuPath = EasySuiteSceneBuilder.Content + "/UI/Lab Menu.prefab";
            if (!lab.MenuWindow || AssetDatabase.GetAssetPath(lab.MenuWindow.Prefab) != menuPath) return;
            var menu = PrefabUtility.LoadPrefabContents(menuPath);
            try
            {
                bool changed = false;
                foreach (var label in menu.GetComponentsInChildren<EasySuiteLabel>(true))
                {
                    if (!label.TryGetComponent<Text>(out var legacy)) continue;
                    Replace(legacy);
                    changed = true;
                }
                if (changed && !PrefabUtility.SaveAsPrefabAsset(menu, menuPath))
                    throw new InvalidOperationException("Could not save the updated Playground menu.");
            }
            finally { PrefabUtility.UnloadPrefabContents(menu); }
            EditorUtility.SetDirty(lab);
        }

        private static Component AssignedLabel(EasySuiteLab lab, string field)
        {
            // Older scenes can still point at their legacy Text until this explicit migration runs.
            return new SerializedObject(lab).FindProperty(field).objectReferenceValue as Component;
        }

        private static TMP_Text HudLabel(Transform root, Component assigned, string path)
        {
            if (assigned is TMP_Text label) return label;
            if (assigned is Text original) return Replace(original);
            var target = root.Find(path);
            if (target && target.TryGetComponent<TMP_Text>(out var current)) return current;
            if (target && target.TryGetComponent<Text>(out var legacy)) return Replace(legacy);
            throw new InvalidOperationException("Restore the Playground HUD label before updating: " + path);
        }

        private static TMP_Text Replace(Text old)
        {
            var target = old.gameObject;
            string value = old.text;
            int size = old.fontSize;
            var alignment = Alignment(old.alignment);
            var color = old.color;
            bool raycast = old.raycastTarget;
            bool rich = old.supportRichText;
            bool enabled = old.enabled;
            bool wrap = old.horizontalOverflow == HorizontalWrapMode.Wrap;
            bool truncate = old.verticalOverflow == VerticalWrapMode.Truncate;
            var style = old.fontStyle;
            var rect = old.rectTransform;
            var pivot = rect.pivot;
            var sizeDelta = rect.sizeDelta;
            var position = rect.anchoredPosition3D;
            UnityEngine.Object.DestroyImmediate(old);
            var text = UIText.Add(target, value, size, alignment);
            text.color = color;
            text.raycastTarget = raycast;
            text.richText = rich;
            text.enabled = enabled;
            text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            text.overflowMode = truncate ? TextOverflowModes.Truncate : TextOverflowModes.Overflow;
            if (style == FontStyle.Bold || style == FontStyle.BoldAndItalic) text.fontStyle |= FontStyles.Bold;
            if (style == FontStyle.Italic || style == FontStyle.BoldAndItalic) text.fontStyle |= FontStyles.Italic;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition3D = position;
            return text;
        }

        internal static TextAlignmentOptions Alignment(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };
    }
}
