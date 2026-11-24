using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    internal abstract class QuestReactionEditor : UnityEditor.Editor
    {
        protected SerializedProperty Field(string name)
        {
            var property = serializedObject.FindProperty(name);
            EditorGUILayout.PropertyField(property);
            return property;
        }

        protected QuestWorld World()
        {
            var world = Field("world").objectReferenceValue as QuestWorld;
            if (!world || !world.database) EditorGUILayout.HelpBox("Assign the Quest World and its fact database.", MessageType.Info);
            return world;
        }

        protected void Quest(QuestWorld world, bool pickStep, Func<QuestStep, bool> valid)
        {
            var quest = Field("quest").objectReferenceValue as QuestDefinition;
            if (!quest || !world) return;
            if (world.quests == null || Array.IndexOf(world.quests, quest) < 0)
                EditorGUILayout.HelpBox("Add this quest to the selected Quest World's catalog.", MessageType.Warning);
            if (!pickStep) return;
            var steps = (quest.steps ?? Array.Empty<QuestStep>()).Where(s => s != null && valid(s)).ToArray();
            var id = serializedObject.FindProperty("stepId");
            int current = Array.FindIndex(steps, s => s.id == id.stringValue) + 1;
            var labels = new[] { "Select a graph step" }.Concat(steps.Select(s => s.name)).ToArray();
            int next = EditorGUILayout.Popup("Step", current, labels);
            if (next != current) id.stringValue = next == 0 ? "" : steps[next - 1].id;
            if (next == 0) EditorGUILayout.HelpBox("Choose a step from the assigned graph.", MessageType.Info);
        }
    }

    [CustomEditor(typeof(WorldStateReaction))]
    internal sealed class WorldStateReactionEditor : QuestReactionEditor
    {
        private bool advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var world = World();
            var source = (WorldStateSource)Field("source").enumValueIndex;
            if (source == WorldStateSource.Fact)
            {
                if (world && world.database)
                    QuestGraphWindow.DrawCondition(serializedObject.FindProperty("condition"), world.database);
            }
            else Quest(world, source == WorldStateSource.ActiveStep, s => s.type == QuestStepType.Objectives);
            EditorGUILayout.HelpBox("Applies on enable, on load and when the condition changes. Keep this listener on an active object.", MessageType.Info);
            Field("instanceId");
            var yes = Field("showWhenTrue").objectReferenceValue as GameObject;
            var no = Field("showWhenFalse").objectReferenceValue as GameObject;
            var self = ((WorldStateReaction)target).transform;
            if ((yes && self.IsChildOf(yes.transform)) || (no && self.IsChildOf(no.transform)) || (yes && yes == no))
                EditorGUILayout.HelpBox("Choose two distinct objects. Keep the listener and its parents active.", MessageType.Warning);
            var reaction = (WorldStateReaction)target;
            bool wired = reaction.whenTrue.GetPersistentEventCount() + reaction.whenFalse.GetPersistentEventCount() > 0;
            advanced = EditorGUILayout.Foldout(advanced, wired ? "Additional actions (configured)" : "Additional actions", true);
            if (advanced) { Field("whenTrue"); Field("whenFalse"); }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(QuestEventReaction))]
    internal sealed class QuestEventReactionEditor : QuestReactionEditor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var world = World();
            var trigger = (QuestTransitionKind)Field("trigger").enumValueIndex;
            Quest(world, trigger == QuestTransitionKind.StepEntered || trigger == QuestTransitionKind.StepCompleted || trigger == QuestTransitionKind.StepInterrupted,
                s => trigger == QuestTransitionKind.StepInterrupted ? s.type == QuestStepType.Objectives :
                    trigger != QuestTransitionKind.StepCompleted || s.type != QuestStepType.End);
            EditorGUILayout.HelpBox("Runs for new gameplay transitions only. Loading a save does not replay this event.", MessageType.Info);
            Field("instanceId");
            Field("onTriggered");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
