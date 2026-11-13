using System;
using System.IO;
using EasyCore.Samples;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyCore.Verification.Editor
{
    public static class CoreVerificationBuild
    {
        private const string ScenePath = "Assets/PackageTests/EasyGameFramework/EasyCore/EasyCoreFirstRun.unity";

        public static string Build(string destination)
        {
            var original = SceneManager.GetActiveScene();
            bool replaceEmptyBatchScene = Application.isBatchMode && string.IsNullOrEmpty(original.path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceEmptyBatchScene ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var go = new GameObject("EasyCore First Run");
                go.AddComponent<CoreFirstRun>();
                go.AddComponent<CoreVerification>();
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save the EasyCore test scene.");
            }
            finally
            {
                if (!replaceEmptyBatchScene)
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = destination,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            string json = JsonUtility.ToJson(new Result
            {
                result = report.summary.result.ToString(), errors = report.summary.totalErrors,
                warnings = report.summary.totalWarnings, seconds = report.summary.totalTime.TotalSeconds,
                path = report.summary.outputPath, unity = Application.unityVersion
            }, true);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(destination), "build-result.json"), json);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException(json);
            return json;
        }

        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--easycore-build") { Build(args[i + 1]); return; }
            throw new ArgumentException("Supply --easycore-build <absolute exe path>.");
        }

        [Serializable]
        private sealed class Result
        {
            public string result, path, unity;
            public int errors, warnings;
            public double seconds;
        }
    }
}
