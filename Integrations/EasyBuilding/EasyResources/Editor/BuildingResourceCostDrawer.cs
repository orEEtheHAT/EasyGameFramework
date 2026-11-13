using EasyResources;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyBuilding.Integrations.Editor
{
    [CustomPropertyDrawer(typeof(BuildingCost))]
    public sealed class BuildingResourceCostDrawer : PropertyDrawer
    {
        private static Dictionary<string, ResourceDefinition> resources;
        static BuildingResourceCostDrawer() => EditorApplication.projectChanged += () => resources = null;
        private static ResourceDefinition Resolve(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (resources == null)
            {
                resources = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
                foreach (var guid in AssetDatabase.FindAssets("t:ResourceDefinition"))
                {
                    var resource = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                    if (resource == null || string.IsNullOrWhiteSpace(resource.Id)) continue;
                    if (!resources.TryAdd(resource.Id, resource)) resources[resource.Id] = null;
                }
            }
            resources.TryGetValue(id, out var found); return found;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            var id = property.FindPropertyRelative("ResourceId"); var amount = property.FindPropertyRelative("Amount");
            var resource = Resolve(id.stringValue);
            var left = new Rect(position.x, position.y, position.width * 0.62f - 4, EditorGUIUtility.singleLineHeight);
            var right = new Rect(left.xMax + 8, position.y, position.width - left.width - 8, left.height);
            EditorGUI.BeginChangeCheck();
            var selected = (ResourceDefinition)EditorGUI.ObjectField(left, new GUIContent("", "Drag an EasyResources definition. Its stable ID is stored in the building cost."), resource, typeof(ResourceDefinition), false);
            if (EditorGUI.EndChangeCheck()) id.stringValue = selected != null ? selected.Id : "";
            EditorGUI.PropertyField(right, amount, new GUIContent("", "Integer storage units, respecting the resource's decimal precision. Example: 100 units = 1.00 when Decimal Places = 2."));
            var idLine = new Rect(position.x, position.y + EditorGUIUtility.singleLineHeight + 2, position.width, EditorGUIUtility.singleLineHeight);
            if (selected == null) EditorGUI.PropertyField(idLine, id, new GUIContent("ID / alias"));
            else EditorGUI.LabelField(idLine, selected.DisplayName, selected.Format(amount.longValue), EditorStyles.miniLabel);
            EditorGUI.EndProperty();
        }
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight * 2 + 2;
    }
}
