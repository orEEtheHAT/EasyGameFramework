using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    [CustomEditor(typeof(QuestIconLibrary))]
    internal sealed class QuestIconLibraryEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var library = (QuestIconLibrary)target;
            serializedObject.Update();
            var entries = serializedObject.FindProperty("icons");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                using var row = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
                var name = entry.FindPropertyRelative("name");
                EditorGUILayout.PropertyField(name);
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("sprite"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("edgeSprite"));
                EditorGUILayout.PropertyField(entry.FindPropertyRelative("spriteMaterial"));
                var labelMode = entry.FindPropertyRelative("labelMode");
                labelMode.enumValueIndex = EditorGUILayout.Popup("Caption", labelMode.enumValueIndex == 0 ? 3 : labelMode.enumValueIndex - 1,
                    new[] { "Always", "On screen only", "Off screen only", "Hidden" }) + 1;
                var edgeMode = entry.FindPropertyRelative("edgeMode");
                edgeMode.enumValueIndex = EditorGUILayout.Popup("Outside view", Mathf.Max(0, edgeMode.enumValueIndex - 1),
                    new[] { "Pin to edge", "Hide" }) + 1;
                var custom = entry.FindPropertyRelative("overrideEdgePadding");
                EditorGUILayout.PropertyField(custom);
                if (custom.boolValue) EditorGUILayout.PropertyField(entry.FindPropertyRelative("edgePadding"));
                if (string.IsNullOrWhiteSpace(name.stringValue) || Array.Exists(library.icons,
                    e => e != library.icons[i] && string.Equals(e?.name, name.stringValue, StringComparison.OrdinalIgnoreCase)))
                    EditorGUILayout.HelpBox("Give this icon a distinct display name.", MessageType.Warning);
                if (GUILayout.Button("Remove " + name.stringValue) && EditorUtility.DisplayDialog("Remove icon?", "Goals using this icon will fall back to the quest default.", "Remove", "Cancel"))
                {
                    entries.DeleteArrayElementAtIndex(i);
                    break;
                }
            }
            serializedObject.ApplyModifiedProperties();
            if (GUILayout.Button("Add icon"))
            {
                Undo.RecordObject(library, "Add quest icon");
                library.icons = (library.icons ?? Array.Empty<QuestIcon>()).Append(new QuestIcon()).ToArray();
                EditorUtility.SetDirty(library);
            }
        }

        internal static void Pick(SerializedProperty id, QuestIconLibrary library, string label, string fallback)
        {
            if (!library) return;
            var entries = library.icons ?? Array.Empty<QuestIcon>();
            int current = Array.FindIndex(entries, e => e != null && e.Id == id.stringValue) + 1;
            var names = new[] { fallback }.Concat(entries.Select(e => e?.name ?? "(missing)")).ToArray();
            if (current == 0 && !string.IsNullOrEmpty(id.stringValue))
            {
                names = names.Append("(missing icon)").ToArray();
                current = names.Length - 1;
                EditorGUILayout.HelpBox("This icon is missing. Select a replacement or use the default.", MessageType.Warning);
            }
            int next = EditorGUILayout.Popup(label, current, names);
            if (next != current) id.stringValue = next == 0 ? "" : entries[next - 1].Id;
        }
    }
}
