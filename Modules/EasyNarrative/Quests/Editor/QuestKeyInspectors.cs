using EasyQuests.Integrations;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    [CustomEditor(typeof(QuestEventBinding))]
    internal sealed class QuestSignalBindingInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            Field("world");
            var world = serializedObject.FindProperty("world").objectReferenceValue as QuestWorld;
            NarrativeKeyPicker.Draw(serializedObject.FindProperty("signal"), world ? world.database : null, false);
            Field("source");
            switch ((QuestEventBinding.EventSource)serializedObject.FindProperty("source").enumValueIndex)
            {
                case QuestEventBinding.EventSource.Death: Field("health"); break;
                case QuestEventBinding.EventSource.ResourcesReceived: Field("wallet"); Field("resource"); break;
                default: Field("actor"); Field("action"); break;
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(QuestZone))]
    internal sealed class QuestZoneInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => QuestKeyFields.Draw(serializedObject, "signal", false);
    }

    [CustomEditor(typeof(QuestTarget))]
    internal sealed class QuestTargetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI() => QuestKeyFields.Draw(serializedObject, "id", true);
    }

    [CustomEditor(typeof(QuestInteractionAction))]
    internal sealed class QuestInteractionInspector : UnityEditor.Editor
    {
        private bool advanced;
        public override void OnInspectorGUI()
        {
            var action = (QuestInteractionAction)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("world"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("quest"));
            NarrativeKeyPicker.Draw(serializedObject.FindProperty("signal"), action.world ? action.world.database : null, false);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("actionLabel"));
            advanced = EditorGUILayout.Foldout(advanced, "Interaction options", true);
            if (advanced) DrawPropertiesExcluding(serializedObject, "m_Script", "world", "quest", "signal", "actionLabel");
            serializedObject.ApplyModifiedProperties();
            if (action.quest && GUILayout.Button("Open quest graph")) QuestGraphWindow.Open(action.quest);
            if (Application.isPlaying && GUILayout.Button("Test interaction")) action.Interact();
        }
    }

    internal static class QuestKeyFields
    {
        internal static void Draw(SerializedObject so, string key, bool target)
        {
            so.Update();
            var world = so.FindProperty("world");
            EditorGUILayout.PropertyField(world);
            var owner = world.objectReferenceValue as QuestWorld;
            NarrativeKeyPicker.Draw(so.FindProperty(key), owner ? owner.database : null, target);
            if (target)
            {
                NarrativeKeyPicker.Draw(so.FindProperty("group"), owner ? owner.database : null, true, true);
                EditorGUILayout.PropertyField(so.FindProperty("offset"));
            }
            else EditorGUILayout.PropertyField(so.FindProperty("actor"));
            so.ApplyModifiedProperties();
        }
    }
}
