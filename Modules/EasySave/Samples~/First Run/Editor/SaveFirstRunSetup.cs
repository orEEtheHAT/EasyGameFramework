using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasySave.Samples.Editor
{
    public static class SaveFirstRunSetup
    {
        public const string ScenePath = "Assets/PackageTests/EasySave/FirstRun.unity";
        [MenuItem("Tools/EasyGameFramework/EasySave/Create First Run Scene")]
        public static void Create()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (File.Exists(ScenePath)) { Debug.Log("EasySave first-run scene already exists: " + ScenePath); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    throw new InvalidOperationException("Save the current untitled scene first (File > Save As), then create the EasySave first-run scene.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cameraObject = new GameObject("First Run Camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .07f, .1f);
            var demo = new GameObject("EasySave First Run"); SceneManager.MoveGameObjectToScene(demo, scene); demo.AddComponent<SaveFirstRun>();
            EditorSceneManager.SaveScene(scene, ScenePath); EditorSceneManager.CloseScene(scene, true); AssetDatabase.Refresh();
            Debug.Log("Open " + ScenePath + ", enter Play Mode, then use Space, F5/F9 and F6/F10.");
        }
    }
}
