using System;
using EasyQuests.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyQuests.Tests
{
    public sealed class QuestQuickStartTests
    {
        [Test]
        public void UndoSecondInteractionPreservesFirstWorldAndAuthoredAssets()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var scene = EditorSceneManager.NewPreviewScene();
            string guid = AssetDatabase.CreateFolder("Assets", "NarrativeTest" + Guid.NewGuid().ToString("N"));
            string folder = AssetDatabase.GUIDToAssetPath(guid);
            try
            {
                var locker = new GameObject("Locker");
                SceneManager.MoveGameObjectToScene(locker, scene);
                var first = QuestQuickStart.Create(locker, folder + "/Locker.asset");
                var gate = new GameObject("Gate");
                SceneManager.MoveGameObjectToScene(gate, scene);
                var second = QuestQuickStart.Create(gate, folder + "/Gate.asset");
                Assert.AreSame(first.world, second.world);
                Assert.AreSame(second, QuestQuickStart.Create(gate, folder + "/Unused.asset"));
                CollectionAssert.Contains(NarrativeKeyPicker.KnownKeys(first.world.database, false), second.signal);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.IsFalse(second);
                CollectionAssert.AreEqual(new[] { first.quest }, first.world.quests);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<QuestDefinition>(folder + "/Gate.asset"));
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(undoGroup);
                EditorSceneManager.ClosePreviewScene(scene);
                if (AssetDatabase.AssetPathToGUID(folder) == guid) AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
