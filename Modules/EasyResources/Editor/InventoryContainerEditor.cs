using System;
using System.Collections.Generic;
using EasyResources.Inventory;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Editor
{
    [CustomEditor(typeof(InventoryContainer))]
    public sealed class InventoryContainerEditor : UnityEditor.Editor
    {
        private readonly List<InventorySlot> slots = new List<InventorySlot>();
        private bool showStorage, showEvents, validationPending = true;
        private string configurationError;

        private void OnEnable()
        {
            EditorApplication.projectChanged += InvalidateConfiguration;
            Undo.undoRedoPerformed += InvalidateConfiguration;
        }

        private void OnDisable()
        {
            EditorApplication.projectChanged -= InvalidateConfiguration;
            Undo.undoRedoPerformed -= InvalidateConfiguration;
        }

        private void InvalidateConfiguration() => validationPending = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("slots"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("initialContents"), true);
                showStorage = EditorGUILayout.Foldout(showStorage, "Storage Limits (optional)", true);
                if (showStorage)
                {
                    Limit("limitWeight", "maximumWeight");
                    Limit("limitVolume", "maximumVolume");
                    EditorGUILayout.HelpBox("Stacks share slots only when their item and properties match. Unique or durable items occupy one slot each.", MessageType.Info);
                }
            }
            showEvents = EditorGUILayout.Foldout(showEvents, "Events", true);
            if (showEvents)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onChanged"));
            if (serializedObject.ApplyModifiedProperties())
                validationPending = true;

            var container = (InventoryContainer)target;
            if (!Application.isPlaying)
            {
                if (validationPending)
                {
                    validationPending = false;
                    try { container.ValidateConfiguration(); configurationError = null; }
                    catch (Exception exception) { configurationError = exception.Message; }
                }
                if (configurationError != null)
                    EditorGUILayout.HelpBox(configurationError + " Adjust Slots, Initial Contents or Storage Limits before Play.", MessageType.Error);
                return;
            }

            try
            {
                var store = container.Store;
                store.CopySlotsTo(slots);
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Runtime: Slot / Item / Count", EditorStyles.boldLabel);
                for (var i = 0; i < slots.Count; i++)
                    if (!slots[i].IsEmpty)
                        EditorGUILayout.LabelField($"{i + 1}: {slots[i].Item.DisplayName}", slots[i].Count.ToString());
                EditorGUILayout.LabelField("Weight / Volume", $"{store.UsedWeight} / {store.UsedVolume}");
            }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
        }

        private void Limit(string toggle, string value)
        {
            var enabled = serializedObject.FindProperty(toggle);
            EditorGUILayout.PropertyField(enabled);
            if (enabled.boolValue)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(value));
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
