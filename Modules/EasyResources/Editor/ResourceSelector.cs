using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace EasyResources.Editor
{
    /// <summary>Editor-only selection of direct resource references. No runtime registry or lookup is introduced.</summary>
    [InitializeOnLoad]
    public static class ResourceSelector
    {
        private sealed class Choice
        {
            internal ResourceDatabase Database;
            internal ResourceDefinition Resource;
        }

        private static List<Choice> choices;
        static ResourceSelector()
        {
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
        }

        public static void Invalidate() => choices = null;

        /// <summary>Draw a searchable selector. Restricted selectors keep a foreign current value visible until explicitly fixed.</summary>
        public static void Draw(Rect position, SerializedProperty property, GUIContent label,
            ResourceDatabase database = null, bool restrictToDatabase = false)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.LabelField(position, label, new GUIContent("Resource selector needs an object reference."));
                return;
            }

            var field = string.IsNullOrEmpty(label.text) ? position : EditorGUI.PrefixLabel(position, label);
            var resource = property.objectReferenceValue as ResourceDefinition;
            var caption = resource == null
                ? (property.objectReferenceInstanceIDValue != 0 ? "Missing Resource" : "None (Resource)")
                : resource.DisplayName;
            if (resource != null)
            {
                if (restrictToDatabase && (database == null || !SafeContains(database, resource)))
                    caption = "Foreign: " + caption;
                else if (!restrictToDatabase)
                {
                    var owner = ResourceDatabaseEditorUtility.FindDatabase(resource);
                    caption = owner == null ? "Outside database: " + caption : owner.name + " / " + caption;
                }
            }
            var tooltip = restrictToDatabase
                ? "Select a resource from the assigned Database. Foreign and missing references are retained until you explicitly replace them."
                : "Search resources grouped by their Database. The selected value is a direct resource reference.";

            EditorGUI.BeginProperty(position, label, property);
            var priorMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            try
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    if (EditorGUI.DropdownButton(field, new GUIContent(caption, tooltip), FocusType.Keyboard))
                    {
                        var targets = property.serializedObject.targetObjects;
                        var path = property.propertyPath;
                        var dropdown = new ResourceDropdown(database, restrictToDatabase, selected =>
                        {
                            // The source property belongs to a prior GUI frame; reacquire it before applying Undoable edits.
                            if (Application.isPlaying) return;
                            foreach (var target in targets) if (target == null) return;
                            using (var serialized = new SerializedObject(targets))
                            {
                                serialized.Update();
                                var value = serialized.FindProperty(path);
                                if (value == null || value.propertyType != SerializedPropertyType.ObjectReference) return;
                                if (restrictToDatabase && selected != null)
                                {
                                    var currentDatabaseProperty = serialized.FindProperty("database");
                                    var currentDatabase = currentDatabaseProperty == null ? database : currentDatabaseProperty.objectReferenceValue as ResourceDatabase;
                                    if (currentDatabase == null || !SafeContains(currentDatabase, selected)) return;
                                }
                                value.objectReferenceValue = selected;
                                serialized.ApplyModifiedProperties();
                            }
                        });
                        dropdown.Show(field);
                    }
            }
            finally { EditorGUI.showMixedValue = priorMixed; EditorGUI.EndProperty(); }

            var evt = Event.current;
            if (Application.isPlaying || !field.Contains(evt.mousePosition) ||
                evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return;
            var dragged = DragAndDrop.objectReferences;
            var candidate = dragged.Length == 1 ? dragged[0] as ResourceDefinition : null;
            var ownerDatabase = candidate == null ? null : ResourceDatabaseEditorUtility.FindDatabase(candidate);
            var allowed = candidate != null && (restrictToDatabase
                ? database != null && SafeContains(database, candidate)
                : ownerDatabase != null && SafeContains(ownerDatabase, candidate));
            DragAndDrop.visualMode = allowed ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (allowed && evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                property.objectReferenceValue = candidate;
                GUI.changed = true;
            }
            evt.Use();
        }

        private static bool SafeContains(ResourceDatabase database, ResourceDefinition resource)
        {
            if (database == null) return false;
            try { return database.Contains(resource); }
            catch (ArgumentException) { return false; } // Inspector below reports the malformed database.
        }
        private static List<Choice> GetChoices()
        {
            if (choices != null) return choices;
            choices = new List<Choice>();
            foreach (var guid in AssetDatabase.FindAssets("t:ResourceDatabase"))
            {
                var database = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(AssetDatabase.GUIDToAssetPath(guid));
                if (database == null) continue;
                var members = SafeResources(database);
                foreach (var resource in members)
                    if (resource != null && ResourceDatabaseEditorUtility.FindDatabase(resource) == database)
                        choices.Add(new Choice { Database = database, Resource = resource });
            }
            choices.Sort((a, b) =>
            {
                var result = string.Compare(a.Database.name, b.Database.name, StringComparison.OrdinalIgnoreCase);
                if (result != 0) return result;
                result = string.Compare(AssetDatabase.GetAssetPath(a.Database), AssetDatabase.GetAssetPath(b.Database), StringComparison.OrdinalIgnoreCase);
                return result != 0 ? result : string.Compare(a.Resource.DisplayName, b.Resource.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            return choices;
        }

        private static IReadOnlyList<ResourceDefinition> SafeResources(ResourceDatabase database)
        {
            try { return database.Resources; }
            catch (ArgumentException) { return Array.Empty<ResourceDefinition>(); }
        }

        private sealed class ResourceItem : AdvancedDropdownItem
        {
            internal readonly ResourceDefinition Resource;
            internal ResourceItem(string label, ResourceDefinition resource) : base(label)
            {
                Resource = resource;
                id = resource == null ? int.MinValue : resource.GetInstanceID();
            }
        }

        private sealed class ResourceDropdown : AdvancedDropdown
        {
            private readonly ResourceDatabase database;
            private readonly bool restricted;
            private readonly Action<ResourceDefinition> selected;
            internal ResourceDropdown(ResourceDatabase database, bool restricted, Action<ResourceDefinition> selected)
                : base(new AdvancedDropdownState())
            {
                this.database = database; this.restricted = restricted; this.selected = selected;
                minimumSize = new Vector2(330, 290);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem(restricted ? (database == null ? "Select a Database first" : database.name) : "Resources") { id = int.MinValue + 1 };
                root.AddChild(new ResourceItem("None", null));
                if (restricted)
                {
                    if (database != null)
                        foreach (var resource in SafeResources(database))
                            if (resource != null)
                                root.AddChild(new ResourceItem(ChoiceLabel(database, resource), resource));
                    return root;
                }
                var groups = new Dictionary<ResourceDatabase, AdvancedDropdownItem>();
                var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var choice in GetChoices())
                    if (!groups.ContainsKey(choice.Database))
                    {
                        names.TryGetValue(choice.Database.name, out var count);
                        names[choice.Database.name] = count + 1;
                        groups.Add(choice.Database, null);
                    }
                groups.Clear();
                foreach (var choice in GetChoices())
                {
                    if (!groups.TryGetValue(choice.Database, out var group))
                    {
                        var name = names[choice.Database.name] > 1
                            ? choice.Database.name + " (" + AssetDatabase.GetAssetPath(choice.Database) + ")"
                            : choice.Database.name;
                        group = new AdvancedDropdownItem(name) { id = choice.Database.GetInstanceID() };
                        groups.Add(choice.Database, group); root.AddChild(group);
                    }
                    group.AddChild(new ResourceItem(ChoiceLabel(choice.Database, choice.Resource), choice.Resource));
                }
                if (groups.Count == 0)
                    root.AddChild(new AdvancedDropdownItem("Create a Resource Database and add resources first") { enabled = false });
                return root;
            }

            private static string ChoiceLabel(ResourceDatabase owner, ResourceDefinition resource)
            {
                foreach (var other in SafeResources(owner))
                    if (other != null && other != resource && other.DisplayName == resource.DisplayName)
                    {
                        var stableId = resource.Id ?? "";
                        return resource.DisplayName + " [" + stableId.Substring(0, Math.Min(8, stableId.Length)) + "]";
                    }
                return resource.DisplayName;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is ResourceItem choice)
                {
                    if (choice.Resource != null && restricted && (database == null || !SafeContains(database, choice.Resource))) return;
                    selected(choice.Resource);
                }
            }
        }
    }

    [CustomPropertyDrawer(typeof(ResourceDefinition), true)]
    public sealed class ResourceDefinitionReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) => ResourceSelector.Draw(position, property, label);
    }
}
