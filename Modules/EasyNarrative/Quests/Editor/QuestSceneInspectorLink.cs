using EasyInteraction;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    [InitializeOnLoad]
    internal static class QuestSceneInspectorLink
    {
        static QuestSceneInspectorLink() => UnityEditor.Editor.finishedDefaultHeaderGUI += Draw;
        private static void Draw(UnityEditor.Editor inspector)
        {
            if (inspector.targets.Length != 1 || !(inspector.target is GameObject go) || !go.scene.IsValid()) return;
            if (!go.TryGetComponent<InteractionTarget>(out _) && !go.TryGetComponent<QuestTarget>(out _) &&
                !go.TryGetComponent<QuestOrder>(out _) && !go.TryGetComponent<QuestZone>(out _) &&
                !go.TryGetComponent<WorldStateReaction>(out _)) return;
            if (GUILayout.Button("Find quests using this object",EditorStyles.miniButton))
                QuestInspectorWindow.FindObjectUses(go);
        }
    }
}
