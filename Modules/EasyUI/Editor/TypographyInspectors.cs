using TMPro;
using UnityEditor;
using UnityEngine;

namespace EasyUI.Editor
{
    [CustomEditor(typeof(UITypography))]
    internal sealed class TypographyInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("Sizes use Canvas reference units. Use a layout that can grow for long or enlarged text.", MessageType.Info);
            if (!DrawDefaultInspector() || !Application.isPlaying) return;
            var changed=(UITypography)target;
            foreach (var item in Object.FindObjectsByType<UIStyledText>(FindObjectsSortMode.None))
            {
                var manager=item.GetComponentInParent<WindowManager>(true);
                var theme=item.themeOverride ? item.themeOverride : manager && manager.Typography ? manager.Typography : UITypography.Default;
                if (theme==changed) item.Apply();
            }
        }
    }

    [CustomEditor(typeof(UIStyledText)),CanEditMultipleObjects]
    internal sealed class StyledTextInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            bool changed=DrawDefaultInspector();
            bool preview=!Application.isPlaying && GUILayout.Button("Apply preview to label");
            if (!preview && (!Application.isPlaying || !changed)) return;
            foreach (var obj in targets)
            {
                var binding=(UIStyledText)obj;
                var label=binding.GetComponent<TextMeshProUGUI>();
                if (preview) Undo.RecordObject(label,"Apply text style");
                binding.Apply();
                if (preview) PrefabUtility.RecordPrefabInstancePropertyModifications(label);
            }
        }
    }
}
