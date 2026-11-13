using System;
using System.IO;
using System.Linq;
using EasyMovement.Samples.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyMovement.Samples.Tests
{
    public sealed class SampleBuilderTests
    {
        [Test]
        public void FirstRunCreatesWiredProfileAndPreservesExistingSceneAndEdits()
        {
            string folder = "Assets/PackageTests/EasyMovement/Generated-" + Guid.NewGuid().ToString("N");
            var original = SceneManager.GetActiveScene();
            int sceneCount = SceneManager.sceneCount;
            Scene opened = default;
            try
            {
                string path = RigidbodyMotorSampleBuilder.Create(folder);
                Assert.AreEqual(original, SceneManager.GetActiveScene());
                Assert.AreEqual(sceneCount, SceneManager.sceneCount);
                byte[] first = File.ReadAllBytes(path);
                Assert.AreEqual(path, RigidbodyMotorSampleBuilder.Create(folder));
                CollectionAssert.AreEqual(first, File.ReadAllBytes(path));
                opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var motor = opened.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<RigidbodyMotor>()).Single();
                Assert.IsNotNull(motor.Profile);
                Assert.AreEqual("CoastingMotor_Sample", motor.Profile.name);
                Assert.AreSame(motor, motor.GetComponent<RigidbodyMotorSample>().Motor);
                motor.name = "Designer edit";
                EditorSceneManager.SaveScene(opened);
                byte[] edited = File.ReadAllBytes(path);
                RigidbodyMotorSampleBuilder.Create(folder, true);
                CollectionAssert.AreEqual(edited, File.ReadAllBytes(path));
                Assert.AreEqual(1, opened.GetRootGameObjects().Count(go => go.name == "Designer edit"));
            }
            finally
            {
                ClearFixtureUndo(opened, folder);
                if (opened.IsValid() && opened.isLoaded) EditorSceneManager.CloseScene(opened, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void ClearFixtureUndo(Scene scene, string folder)
        {
            Undo.FlushUndoRecordObjects();
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                {
                    var hierarchy = root.GetComponentsInChildren<Transform>(true);
                    for (int i = hierarchy.Length - 1; i >= 0; --i)
                    {
                        var current = hierarchy[i];
                        foreach (var component in current.GetComponents<Component>())
                            if (component) Undo.ClearUndo(component);
                        Undo.ClearUndo(current.gameObject);
                    }
                }
            // Only assets created in this fixture's unique folder. Do not clear package profiles,
            // the pre-existing scene, shared materials or records owned by another fixture.
            if (!AssetDatabase.IsValidFolder(folder)) return;
            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if (asset) Undo.ClearUndo(asset);
        }

        [Test]
        public void BuilderRejectsPathsOutsideAssetsBeforeCreatingAnything()
        {
            Assert.Throws<ArgumentException>(() => RigidbodyMotorSampleBuilder.Create("Library/Example"));
            Assert.Throws<ArgumentException>(() => RigidbodyMotorSampleBuilder.Create("Assets/../Library/Example"));
        }
    }
}
