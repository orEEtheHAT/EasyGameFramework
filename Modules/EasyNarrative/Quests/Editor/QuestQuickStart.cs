using System;
using System.IO;
using System.Linq;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyQuests.Editor
{
    public static class QuestQuickStart
    {
        private const string Menu = "GameObject/EasyGameFramework/Narrative/Create interaction quest";
        [MenuItem(Menu, true)]
        private static bool CanCreate() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            Selection.activeGameObject && Selection.activeGameObject.scene.IsValid() && PrefabStageUtility.GetCurrentPrefabStage() == null;

        [MenuItem(Menu, false, 21)]
        private static void CreateSelected()
        {
            var target = Selection.activeGameObject;
            var existing = target.GetComponent<QuestInteractionAction>();
            if (existing && existing.quest) { QuestGraphWindow.Open(existing.quest); return; }
            var path = EditorUtility.SaveFilePanelInProject("Create interaction quest", "First Quest", "asset", "Choose where to keep the quest graph.");
            if (string.IsNullOrEmpty(path)) return;
            try { QuestGraphWindow.Open(Create(target, path).quest); }
            catch (Exception error) { EditorUtility.DisplayDialog("Interaction quest", error.Message, "OK"); }
        }

        public static QuestInteractionAction Create(GameObject target, string path)
        {
            if (Application.isPlaying || !target || !target.scene.IsValid() || EditorUtility.IsPersistent(target) ||
                PrefabStageUtility.GetPrefabStage(target) != null) throw new ArgumentException("Select an object in an editable scene.");
            var existing = target.GetComponent<QuestInteractionAction>();
            if (existing && existing.quest) return existing;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Split('/').Any(s => s.Length == 0 || s == "." || s == "..") ||
                !AssetDatabase.IsValidFolder(path.Substring(0, path.LastIndexOf('/'))) ||
                !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || File.Exists(path))
                throw new ArgumentException("Choose an unused asset path inside Assets.");
            var marker = target.GetComponent<QuestTarget>();
            var worlds = target.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<QuestWorld>(true)).ToArray();
            var world = existing && existing.world ? existing.world : marker ? marker.world : null;
            if (!world && worlds.Length > 1) throw new InvalidOperationException("Several Quest Worlds exist. Assign the intended world to a Quest Target on this object.");
            if (!world) world = worlds.FirstOrDefault();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create interaction quest");
            string createdDatabase = null;
            bool createdQuest = false;
            try
            {
                if (!world)
                {
                    var host = new GameObject("Quest World");
                    SceneManager.MoveGameObjectToScene(host, target.scene);
                    Undo.RegisterCreatedObjectUndo(host, "Create quest world");
                    world = Undo.AddComponent<QuestWorld>(host);
                }
                Undo.RegisterCompleteObjectUndo(world, "Register quest");
                if (!world.database)
                {
                    var db = ScriptableObject.CreateInstance<WorldFactDatabase>();
                    var directory = path.Substring(0, path.LastIndexOf('/'));
                    var dbPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/World Facts.asset");
                    if (string.Equals(dbPath, path, StringComparison.OrdinalIgnoreCase))
                        dbPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/Quest World Facts.asset");
                    AssetDatabase.CreateAsset(db, dbPath);
                    createdDatabase = dbPath;
                    world.database = db;
                }
                if (!marker) marker = Undo.AddComponent<QuestTarget>(target);
                Undo.RegisterCompleteObjectUndo(marker, "Configure quest target");
                marker.world = world;
                var quest = ScriptableObject.CreateInstance<QuestDefinition>();
                quest.title = Path.GetFileNameWithoutExtension(path);
                quest.database = world.database;
                string name = string.Concat(target.name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
                string suffix = (name.Length > 0 ? name + "." : "") + quest.id.Substring(0, 8);
                if (string.IsNullOrWhiteSpace(marker.id)) marker.id = "target." + suffix;
                string signal = "interact." + suffix;
                var end = new QuestStep { type = QuestStepType.End, name = "Done", position = new Vector2(450, 70) };
                var objective = new QuestStep { type = QuestStepType.Objectives, name = "Interact", position = new Vector2(235, 70), next = end.id,
                    goals = new[] { new QuestGoal { title = "Interact with " + target.name, mode = GoalMode.NewEvents, signal = signal, marker = MarkerStyle.Target, target = marker.id } } };
                quest.steps = new[] { new QuestStep { type = QuestStepType.Start, name = "Start", next = objective.id, position = new Vector2(20, 70) }, objective, end };
                quest.ValidateGraph();
                AssetDatabase.CreateAsset(quest, path);
                createdQuest = true;
                world.quests = (world.quests ?? Array.Empty<QuestDefinition>()).Append(quest).ToArray();
                if (!target.GetComponentInChildren<Collider>()) Undo.AddComponent<BoxCollider>(target);
                var action = existing ? existing : Undo.AddComponent<QuestInteractionAction>(target);
                Undo.RegisterCompleteObjectUndo(action, "Configure quest interaction");
                action.world = world; action.quest = quest; action.signal = signal;
                action.ActionLabel = "Inspect " + target.name;
                action.ActionId = signal;
                foreach (var component in new Component[] { world, marker, action })
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    EditorUtility.SetDirty(component);
                }
                EditorSceneManager.MarkSceneDirty(target.scene);
                Undo.CollapseUndoOperations(group);
                return action;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (createdQuest) AssetDatabase.DeleteAsset(path);
                if (createdDatabase != null) AssetDatabase.DeleteAsset(createdDatabase);
                throw;
            }
        }
    }
}
