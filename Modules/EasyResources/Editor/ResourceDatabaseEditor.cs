using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Editor
{
    [CustomEditor(typeof(ResourceDatabase))]
    public sealed class ResourceDatabaseEditor : UnityEditor.Editor
    {
        private readonly HashSet<int> expanded = new HashSet<int>();
        private string search = "", newName = "New Resource", operationError;
        private bool showIdentity;

        public override void OnInspectorGUI()
        {
            var database = (ResourceDatabase)target;
            EditorGUILayout.HelpBox("Create and edit all resources here. Each record is stored inside this one Database asset; consumers keep direct references and stable IDs. Balances belong to runtime containers.", MessageType.Info);
            search = EditorGUILayout.TextField(new GUIContent("Search", "Filter by name or stable ID. This does not change the database."), search);
            var persistent = AssetDatabase.Contains(database);
            if (!persistent)
                EditorGUILayout.HelpBox("Save this Resource Database as an asset before adding nested resources.", MessageType.Warning);
            using (new EditorGUI.DisabledScope(Application.isPlaying || !persistent))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    newName = EditorGUILayout.TextField(new GUIContent("New resource", "The visible name can be changed later; stable ID is assigned automatically."), newName);
                    if (GUILayout.Button("Add", GUILayout.Width(52)))
                    {
                        try
                        {
                            var resource = ResourceDatabaseEditorUtility.AddResource(database, newName);
                            expanded.Add(resource.GetInstanceID()); search = ""; operationError = null;
                            ResourceSelector.Invalidate();
                        }
                        catch (Exception exception) { operationError = exception.Message; }
                    }
                }
            }
            if (operationError != null)
                EditorGUILayout.HelpBox(operationError, MessageType.Error);

            try { ResourceDatabaseEditorUtility.ValidateAsset(database); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }

            IReadOnlyList<ResourceDefinition> resources;
            try { resources = database.Resources; }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(exception.Message + " Restore the database record list before continuing.", MessageType.Error);
                return;
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resources: " + resources.Count, EditorStyles.boldLabel);
            var visible = 0;
            foreach (var resource in resources)
            {
                if (resource == null)
                {
                    EditorGUILayout.HelpBox("Missing resource record. Undo the deletion or repair the database before use.", MessageType.Error);
                    continue;
                }
                if (!string.IsNullOrEmpty(search) &&
                    resource.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (resource.Id ?? "").IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                visible++;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var key = resource.GetInstanceID();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var open = EditorGUILayout.Foldout(expanded.Contains(key), resource.DisplayName, true);
                        if (open) expanded.Add(key); else expanded.Remove(key);
                        using (new EditorGUI.DisabledScope(Application.isPlaying))
                            if (GUILayout.Button(new GUIContent("Remove", "Only unused resources can be removed. References in assets, scenes and loaded objects are checked; Undo restores the record."), GUILayout.Width(68)))
                            {
                                if (ResourceDatabaseEditorUtility.TryRemoveResource(database, resource, out var error))
                                {
                                    expanded.Remove(key); operationError = null; ResourceSelector.Invalidate();
                                    GUIUtility.ExitGUI();
                                }
                                else operationError = error;
                            }
                    }
                    if (!expanded.Contains(key)) continue;
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                        DrawResourceParameters(resource, ref showIdentity);
                }
            }
            if (visible == 0)
                EditorGUILayout.HelpBox(resources.Count == 0 ? "Add the first resource above." : "No resource matches the search.", MessageType.Info);
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Resource configuration is read-only during Play. Inspect balances on Resource Container.", MessageType.Info);
        }

        internal static void DrawResourceParameters(ResourceDefinition resource, ref bool showIdentity)
        {
            using (var serialized = new SerializedObject(resource))
            {
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("displayName"), new GUIContent("Name"));
                EditorGUILayout.PropertyField(serialized.FindProperty("icon"));
                EditorGUILayout.PropertyField(serialized.FindProperty("decimalPlaces"), new GUIContent("Decimal Places", "0 for whole items. With 2, one smallest unit is 0.01. Changing precision after authoring costs or saves requires converting their values."));
                EditorGUILayout.PropertyField(serialized.FindProperty("unit"));
                EditorGUILayout.PropertyField(serialized.FindProperty("weightPerUnit"), new GUIContent("Weight per Smallest Unit"));
                EditorGUILayout.PropertyField(serialized.FindProperty("volumePerUnit"), new GUIContent("Volume per Smallest Unit"));
                ResourceInspectorValidation.Identity(serialized, resource, ref showIdentity);
                if (serialized.ApplyModifiedProperties())
                {
                    ResourceIdValidator.Invalidate(); ResourceSelector.Invalidate();
                }
            }
            try { resource.ValidateDefinition(); }
            catch (Exception exception) { EditorGUILayout.HelpBox(exception.Message, MessageType.Error); }
        }
    }
}
