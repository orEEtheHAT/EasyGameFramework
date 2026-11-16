using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace EasyResources.Editor
{
    [CustomPropertyDrawer(typeof(ResourceRewardRange))]
    public sealed class ResourceRewardRangeDrawer : PropertyDrawer
    {
        private readonly Dictionary<string, string> errors = new Dictionary<string, string>();
        private static string Key(SerializedProperty property) => property.serializedObject.targetObject.GetInstanceID() + ":" + property.propertyPath;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) =>
            EditorGUIUtility.singleLineHeight * 2 + 4 + (errors.ContainsKey(Key(property)) ? 38 : 0);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using var disabled = new EditorGUI.DisabledScope(Application.isPlaying);
            EditorGUI.BeginProperty(position, label, property);
            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            var resource = property.FindPropertyRelative("resource");
            var databaseProperty = property.serializedObject.FindProperty("database");
            var database = databaseProperty == null ? null : databaseProperty.objectReferenceValue as ResourceDatabase;
            ResourceSelector.Draw(line, resource,
                new GUIContent("Resource", "Every row is awarded; duplicate resources roll independently and add together."),
                database, databaseProperty != null);
            var definition = resource.objectReferenceValue as ResourceDefinition;
            line.y += EditorGUIUtility.singleLineHeight + 4;
            var minRect = new Rect(line.x, line.y, (line.width - 8) / 2, line.height);
            var maxRect = new Rect(minRect.xMax + 8, line.y, minRect.width, line.height);
            var key = Key(property);
            var previousWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 32;
            try
            {
                Quantity(minRect, property.FindPropertyRelative("min"), definition, "Min", key);
                Quantity(maxRect, property.FindPropertyRelative("max"), definition, "Max", key);
            }
            finally { EditorGUIUtility.labelWidth = previousWidth; EditorGUI.showMixedValue = false; }
            if (errors.TryGetValue(key, out var error))
                EditorGUI.HelpBox(new Rect(position.x, line.yMax + 2, position.width, 36), error, MessageType.Error);
            EditorGUI.EndProperty();
        }

        private void Quantity(Rect rect, SerializedProperty property, ResourceDefinition definition, string label, string key)
        {
            var amount = property.longValue;
            var display = definition == null ? amount.ToString(CultureInfo.CurrentCulture) : definition.FromUnits(amount).ToString(CultureInfo.CurrentCulture);
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var text = EditorGUI.DelayedTextField(rect, new GUIContent(label,
                "Inclusive whole resource quantity. Fractions use the Resource's Decimal Places; fixed Min = Max consumes no randomness."), display);
            if (!EditorGUI.EndChangeCheck())
                return;
            try
            {
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var quantity) || quantity < 0)
                    throw new ArgumentException("Enter a nonnegative decimal quantity using your system decimal separator.");
                var units = definition == null ? decimal.ToInt64(quantity) : definition.ToUnits(quantity);
                if (definition == null && quantity != units)
                    throw new ArgumentException("Assign a Resource before entering a fractional quantity.");
                property.longValue = units;
                errors.Remove(key);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            { errors[key] = exception.Message; }
        }
    }

    [CustomEditor(typeof(ResourceRewardProfile))]
    public sealed class ResourceRewardProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Choose a Resource Database, then add rewards from its records. Every listed resource is awarded. Min and Max are inclusive whole quantities; 1.25–1.27 for a two-decimal resource rolls 1.25, 1.26 or 1.27. Equal bounds are fixed; 0–0 awards none.", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("database"), new GUIContent("Database", "The resource records available to every reward row. Changing this keeps existing rows visible; foreign rows must be explicitly corrected."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("ranges"), new GUIContent("Rewards"), true);
            }
            serializedObject.ApplyModifiedProperties();
            var database = ((ResourceRewardProfile)target).Database;
            if (database != null && GUILayout.Button("Edit Resources in Database"))
            {
                Selection.activeObject = database;
                EditorGUIUtility.PingObject(database);
            }
            try { ((ResourceRewardProfile)target).ValidateConfiguration(); }
            catch (Exception exception)
            { EditorGUILayout.HelpBox(exception.Message + " Correct the reward rows before use.", MessageType.Error); }
            EditorGUILayout.HelpBox("Roll once, then grant that result to a Resource Container. All rows fit or none are granted. Retry the same result after a capacity failure; rolling again changes the reward. The profile stores configuration only.", MessageType.None);
        }
    }
}
