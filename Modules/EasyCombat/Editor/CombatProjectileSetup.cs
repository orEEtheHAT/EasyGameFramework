using System;
using EasyCombat;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>Project projectile choice for the framework Setup; reads never create assets.</summary>
    public static class CombatProjectileSetup
    {
        public const string DefaultAssetPath = "Assets/EasyGameFramework/Resources/EasyCombatProjectileExecution.asset";
        public static ProjectileExecutionMode RequestedMode => ProjectileExecutionSettings.RequestedMode;
        public static ProjectileExecutionMode EffectiveMode => ProjectileExecutionSettings.EffectiveMode;
        public static bool ParallelAvailable => ProjectileExecutionSettings.Resolve(ProjectileExecutionMode.Parallel) == ProjectileExecutionMode.Parallel;
        public static string SupportDiagnostic => ParallelAvailable
            ? "Editor has job workers. Parallel batches flight; small volleys may cost more to schedule. Unity 6.3 Web Players use Single Thread."
            : "No job workers available: execution falls back to Single Thread. The requested choice remains saved.";
        public static bool HasExplicitSelection => FindSettings() != null;
        public static string SettingsAssetPath => AssetDatabase.GetAssetPath(FindSettings());

        public static void SetMode(ProjectileExecutionMode mode)
        {
            if (mode != ProjectileExecutionMode.Parallel && mode != ProjectileExecutionMode.SingleThread)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || AssetDatabase.IsAssetImportWorkerProcess())
                throw new InvalidOperationException("Choose the projectile mode outside Play mode, after compilation.");
            var settings = FindSettings();
            if (settings == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(DefaultAssetPath) != null)
                    throw new InvalidOperationException("Projectile settings path is occupied: " + DefaultAssetPath);
                EasyCombat.Editor.CombatAssetLibrary.EnsureFolder("Assets/EasyGameFramework/Resources");
                settings = ScriptableObject.CreateInstance<ProjectileExecutionSettings>();
                settings.mode = mode;
                AssetDatabase.CreateAsset(settings, DefaultAssetPath);
                Undo.RegisterCreatedObjectUndo(settings, "Choose combat projectile mode");
            }
            else { Undo.RecordObject(settings, "Choose combat projectile mode"); settings.mode = mode; }
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            ProjectileExecutionSettings.Reload();
        }

        private static ProjectileExecutionSettings FindSettings()
        {
            ProjectileExecutionSettings result = null;
            foreach (var guid in AssetDatabase.FindAssets("t:ProjectileExecutionSettings"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/Resources/" + ProjectileExecutionSettings.ResourceName + ".asset", StringComparison.Ordinal)) continue;
                if (result != null)
                    throw new InvalidOperationException("Multiple projectile settings in Resources. Keep one project choice: " + AssetDatabase.GetAssetPath(result) + " and " + path);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Projectile settings must belong to the project under Assets: " + path);
                result = AssetDatabase.LoadAssetAtPath<ProjectileExecutionSettings>(path);
            }
            return result;
        }
    }
}
