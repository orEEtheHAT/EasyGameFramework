using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    public sealed class NarrativeKeyPicker : PopupWindowContent
    {
        private readonly string[] keys;
        private readonly Action<string> select;
        private string query = "";
        private Vector2 scroll;
        private bool focus = true;

        private NarrativeKeyPicker(string[] keys, Action<string> select)
        {
            this.keys = keys;
            this.select = select;
        }

        // Work is done on opening the selector, never during gameplay or inspector repaints.
        public static string[] KnownKeys(WorldFactDatabase db, bool targets) => KnownKeys(db, targets, false);

        public static string[] KnownKeys(WorldFactDatabase db, bool targets, bool group)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            void Add(string key) { if (!string.IsNullOrWhiteSpace(key)) keys.Add(key); }
            bool InWorld(QuestWorld world) => world && (!db || world.database == db);
            foreach (var guid in AssetDatabase.FindAssets("t:QuestDefinition"))
            {
                var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (!quest || (db && quest.database != db) || quest.steps == null) continue;
                foreach (var step in quest.steps)
                    if (step?.type == QuestStepType.Objectives && step.goals != null)
                        foreach (var goal in step.goals)
                            if (goal != null && (targets ? goal.targetGroup == group : goal.mode == GoalMode.NewEvents)) Add(targets ? goal.target : goal.signal);
            }
            if (targets)
            {
                foreach (var target in Loaded<QuestTarget>()) if (InWorld(target.world)) Add(group ? target.group : target.id);
            }
            else
            {
                foreach (var binding in Loaded<QuestEventBinding>()) if (InWorld(binding.world)) Add(binding.signal);
                foreach (var zone in Loaded<QuestZone>()) if (InWorld(zone.world)) Add(zone.signal);
                foreach (var action in Loaded<QuestInteractionAction>()) if (InWorld(action.world)) Add(action.signal);
                foreach (var guid in AssetDatabase.FindAssets("t:DialogueDefinition"))
                {
                    var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!dialogue || (db && dialogue.database && dialogue.database != db) || dialogue.lines == null) continue;
                    foreach (var line in dialogue.lines)
                        if (line?.replies != null)
                            foreach (var reply in line.replies)
                                if (reply?.action?.kind == DialogueActionType.SendEvent) Add(reply.action.key);
                }
            }
            return keys.ToArray();
        }

        private static T[] Loaded<T>() where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        public static void Draw(SerializedProperty key, WorldFactDatabase db, bool targets) => Draw(key, db, targets, false);

        public static void Draw(SerializedProperty key, WorldFactDatabase db, bool targets, bool group)
        {
            var rect = EditorGUILayout.GetControlRect();
            var button = EditorGUI.PrefixLabel(rect, new GUIContent(targets ? (group ? "Marker group" : "Marker target") : "Quest event"));
            if (!EditorGUI.DropdownButton(button, new GUIContent(string.IsNullOrEmpty(key.stringValue) ? "Choose or create…" : key.stringValue), FocusType.Keyboard)) return;
            var owner = key.serializedObject.targetObject;
            var path = key.propertyPath;
            PopupWindow.Show(button, new NarrativeKeyPicker(KnownKeys(db, targets, group), value =>
            {
                if (!owner) return;
                using var data = new SerializedObject(owner);
                var property = data.FindProperty(path);
                if (property == null) return;
                property.stringValue = value;
                data.ApplyModifiedProperties();
                if (owner is DialogueDefinition dialogue) dialogue.Invalidate();
            }));
        }

        public override Vector2 GetWindowSize() => new Vector2(320, 280);
        public override void OnGUI(Rect rect)
        {
            GUI.SetNextControlName("KeySearch");
            query = EditorGUILayout.TextField(query, EditorStyles.toolbarSearchField);
            if (focus) { EditorGUI.FocusTextInControl("KeySearch"); focus = false; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var key in keys)
                if (key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 && GUILayout.Button(key, EditorStyles.miniButton))
                    Pick(key);
            EditorGUILayout.EndScrollView();
            string candidate = query.Trim();
            if (candidate.Length > 0 && Array.IndexOf(keys, candidate) < 0 && GUILayout.Button("Create: " + candidate)) Pick(candidate);
            if (GUILayout.Button("Clear selection", EditorStyles.miniButton)) Pick("");
        }

        private void Pick(string key) { select(key); editorWindow.Close(); GUIUtility.ExitGUI(); }
    }
}
