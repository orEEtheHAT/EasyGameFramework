using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace EasyGameFramework.Editor
{
    public readonly struct FrameworkModuleInfo
    {
        public readonly string Name, Purpose;
        public readonly string[] Assemblies;
        public string Guide => "Modules/" + Name + "/README.md";
        public FrameworkModuleInfo(string name, string purpose, params string[] assemblies)
        { Name = name; Purpose = purpose; Assemblies = assemblies; }
    }
    public readonly struct SetupDiagnostic
    {
        public readonly string Title, Detail, SettingsPath;
        public readonly MessageType Severity;
        public SetupDiagnostic(string title, string detail, MessageType severity, string settingsPath = null)
        { Title = title; Detail = detail; Severity = severity; SettingsPath = settingsPath; }
    }
    public static class FrameworkSetupCatalog
    {
        public const string PackageName = "com.oreethehat.easy-game-framework";
        // Only known former module packages collide with the unified assemblies.
        // Framework addons and unrelated packages may legitimately share the author prefix.
        private static readonly HashSet<string> legacyModulePackages = new HashSet<string>(StringComparer.Ordinal) {
            "com.oreethehat.easy-core", "com.oreethehat.easy-save", "com.oreethehat.easy-settings",
            "com.oreethehat.easy-input", "com.oreethehat.easy-movement", "com.oreethehat.easy-combat",
            "com.oreethehat.easy-feedbacks", "com.oreethehat.easy-interaction", "com.oreethehat.easy-building",
            "com.oreethehat.easy-resources", "com.oreethehat.easy-localization", "com.oreethehat.easy-ui"
        };
        internal static bool IsLegacyModulePackage(string packageName) => packageName != null && legacyModulePackages.Contains(packageName);
        private static readonly FrameworkModuleInfo[] modules = {
            new FrameworkModuleInfo("EasyCore", "Small shared primitives", "EasyCore.Runtime"),
            new FrameworkModuleInfo("EasySave", "Settings, slots and storage", "EasySave.Runtime"),
            new FrameworkModuleInfo("EasySettings", "URP graphics and display confirmation", "EasySettings.Runtime"),
            new FrameworkModuleInfo("EasyInput", "Player actions, bindings and contexts", "EasyInput.Runtime"),
            new FrameworkModuleInfo("EasyMovement", "ECM2 first-person movement", "EasyMovement.FirstPerson", "EasyFootsteps.Runtime"),
            new FrameworkModuleInfo("EasyCombat", "Health, damage, statuses and attacks", "EasyCombat.Runtime"),
            new FrameworkModuleInfo("EasyFeedbacks", "Audio, visuals, camera and haptics", "EasyFeedbacks.Runtime"),
            new FrameworkModuleInfo("EasyInteraction", "Targets, actions and approach", "EasyInteraction.Runtime"),
            new FrameworkModuleInfo("EasyBuilding", "Placement and construction", "EasyBuilding.Runtime"),
            new FrameworkModuleInfo("EasyResources", "Inventories, costs and reservations", "EasyResources.Runtime"),
            new FrameworkModuleInfo("EasyLocalization", "Language, tables and translated data", "EasyLocalization.Runtime"),
            new FrameworkModuleInfo("EasyUI", "Windows, focus, dialogs and radial menus", "EasyUI.Runtime")
        };
        public static IReadOnlyList<FrameworkModuleInfo> Modules { get; } = Array.AsReadOnly(modules);
        public static PackageInfo FindPackage() => PackageInfo.GetAllRegisteredPackages()?.FirstOrDefault(p => p.name == PackageName);
        public static bool IsLoaded(FrameworkModuleInfo module)
        {
            var names = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name);
            return module.Assemblies.All(name => names.Contains(name));
        }
        public static IReadOnlyList<SetupDiagnostic> Diagnose()
        {
            var results = new List<SetupDiagnostic>();
            var packages = PackageInfo.GetAllRegisteredPackages() ?? Array.Empty<PackageInfo>();
            var expected = new[] {
                ("com.unity.inputsystem", "Input System"), ("com.unity.ugui", "uGUI"),
                ("com.unity.render-pipelines.universal", "Universal Render Pipeline"),
                ("com.unity.cinemachine", "Cinemachine"), ("com.unity.visualeffectgraph", "VFX Graph"),
                ("com.unity.localization", "Unity Localization"), ("com.unity.burst", "Burst") };
            foreach (var entry in expected)
            {
                var package = packages.FirstOrDefault(p => p.name == entry.Item1);
                results.Add(new SetupDiagnostic(entry.Item2, package != null ? "Installed · " + package.version
                    : "Missing " + entry.Item1 + ". Resolve the framework dependencies in Package Manager.", package != null ? MessageType.None : MessageType.Error));
            }
            bool ecm = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("ECM2.Character", false) != null);
            results.Add(new SetupDiagnostic("ECM2", ecm ? "Character runtime loaded" : "Import your licensed Easy Character Movement 2 asset into this project.", ecm ? MessageType.None : MessageType.Error));
            var pipeline = QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            results.Add(new SetupDiagnostic("Active render pipeline", pipeline is UniversalRenderPipelineAsset ? pipeline.name
                : "Assign a Universal Render Pipeline asset in Graphics / Quality. EasySettings supports URP.", pipeline is UniversalRenderPipelineAsset ? MessageType.None : MessageType.Error, "Project/Graphics"));
#if ENABLE_INPUT_SYSTEM
#if ENABLE_LEGACY_INPUT_MANAGER
            results.Add(new SetupDiagnostic("Input handling", "Both backends enabled. For new projects choose Input System Package (New) under Player > Other Settings.", MessageType.Warning, "Project/Player"));
#else
            results.Add(new SetupDiagnostic("Input handling", "Input System Package (New)", MessageType.None, "Project/Player"));
#endif
#else
            results.Add(new SetupDiagnostic("Input handling", "Choose Input System Package (New) under Player > Other Settings, then accept Unity's restart if requested.", MessageType.Error, "Project/Player"));
#endif
            if (packages.Any(p => IsLegacyModulePackage(p.name)))
                results.Add(new SetupDiagnostic("Standalone modules", "This project also contains older standalone Easy packages. Resolve duplicate assemblies before using the unified framework. Existing standalone EasyFeedbacks projects remain separate products.", MessageType.Error));
            return results;
        }
        public static string DocumentationPath(PackageInfo package, string relative)
        {
            if (package == null || string.IsNullOrWhiteSpace(relative)) return null;
            string root = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(root, relative));
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
        }
    }
}
