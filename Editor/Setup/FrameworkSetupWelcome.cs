using System;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    [FilePath("UserSettings/EasyGameFrameworkSetup.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class FrameworkSetupWelcomeState : ScriptableSingleton<FrameworkSetupWelcomeState>
    {
        [SerializeField] private string shownVersion;
        internal string ShownVersion => shownVersion;
        internal void Remember(string version) { shownVersion = version; Save(true); }
    }
    internal static class FrameworkSetupWelcome
    {
        private static double deadline;
        internal static bool ShouldShow(string version, string seen) => !string.IsNullOrEmpty(version) && !string.Equals(version, seen, StringComparison.Ordinal);
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            if (Application.isBatchMode || AssetDatabase.IsAssetImportWorkerProcess()) return;
            UnityEditor.PackageManager.Events.registeredPackages += _ => Schedule();
            Schedule();
        }
        private static void Schedule()
        {
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= TryShow;
            EditorApplication.update += TryShow;
        }
        private static void TryShow()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            { deadline = EditorApplication.timeSinceStartup + 120; return; }
            if (EditorApplication.timeSinceStartup > deadline) { EditorApplication.update -= TryShow; return; }
            var package = FrameworkSetupCatalog.FindPackage(); if (package == null) return;
            EditorApplication.update -= TryShow;
            if (ShouldShow(package.version, FrameworkSetupWelcomeState.instance.ShownVersion)) FrameworkSetupWindow.Open();
        }
        internal static void MarkShown(string version)
        { if (!string.IsNullOrEmpty(version)) FrameworkSetupWelcomeState.instance.Remember(version); }
    }
}
