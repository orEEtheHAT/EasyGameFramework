using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace EasyGameFramework.Editor
{
    public sealed partial class FrameworkSetupWindow : EditorWindow
    {
        [SerializeField] private int tab;
        private Vector2 scroll;
        private PackageInfo package;
        private IReadOnlyList<SetupDiagnostic> diagnostics = Array.Empty<SetupDiagnostic>();
        private Sample[] samples = Array.Empty<Sample>();
        private bool refreshPending, importing;
        private string message;
        private GUIStyle headingStyle, wrapped;

        [MenuItem("Tools/EasyGameFramework/Setup", priority = 0)]
        public static void Open()
        {
            var installed = FrameworkSetupCatalog.FindPackage();
            FrameworkSetupWelcome.MarkShown(installed?.version);
            var window = GetWindow<FrameworkSetupWindow>("EasyGameFramework");
            window.minSize = new Vector2(720, 560); window.Show();
        }
        private void OnEnable()
        { minSize = new Vector2(720, 560); refreshPending = true; EditorApplication.projectChanged += MarkRefresh; }
        private void OnDisable() => EditorApplication.projectChanged -= MarkRefresh;
        private void MarkRefresh() => refreshPending = true;
        private void OnInspectorUpdate()
        { if (refreshPending && !EditorApplication.isCompiling && !EditorApplication.isUpdating) { RefreshData(); Repaint(); } }
        public void RefreshData()
        {
            refreshPending = false;
            package = FrameworkSetupCatalog.FindPackage(); diagnostics = FrameworkSetupCatalog.Diagnose();
            try { samples = package == null ? Array.Empty<Sample>() : Sample.FindByPackage(package.name, package.version).ToArray(); }
            catch (Exception e) { samples = Array.Empty<Sample>(); message = "Samples could not be read: " + e.Message; }
        }
        private void OnGUI()
        {
            headingStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 23 };
            wrapped ??= new GUIStyle(EditorStyles.wordWrappedLabel);
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(18, 18, 14, 14) }))
            {
                GUILayout.Label("EasyGameFramework", headingStyle);
                GUILayout.Label(package != null ? "Version " + package.version + "  ·  12 built-in modules  ·  Unity 6 / URP" : "Waiting for Package Manager registration…", EditorStyles.miniLabel);
                GUILayout.Space(8);
                using (new EditorGUILayout.HorizontalScope())
                {
                    tab = GUILayout.Toolbar(tab, new[] { "Get started", "Modules", "Samples" });
                    if (GUILayout.Button("Refresh", GUILayout.Width(76))) RefreshData();
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) EditorGUILayout.HelpBox("Unity is importing or compiling. Setup actions will be available when it finishes.", MessageType.Info);
                if (EditorUtility.scriptCompilationFailed) EditorGUILayout.HelpBox("Resolve the compilation errors in Console first. Loaded assemblies may describe the previous successful compilation.", MessageType.Error);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                if (tab == 0) DrawStart(); else if (tab == 1) DrawModules(); else DrawSamples();
                EditorGUILayout.EndScrollView();
                if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
                GUILayout.Space(8);
                GUILayout.Label("Closing keeps your current settings. Modules run when their components are used.", EditorStyles.wordWrappedMiniLabel);
            }
        }
        private void DrawStart()
        {
            GUILayout.Space(12);
            GUILayout.Label("Start with a working example", EditorStyles.boldLabel);
            GUILayout.Label("1. Check the dependencies below.\n2. Import a sample from the Samples tab.\n3. Follow its guide and open its scene. Then add only the components your game needs.", wrapped);
            using (new EditorGUILayout.HorizontalScope())
            {
                GuideButton("Framework guide", "README.md");
                GuideButton("Setup guide (RU)", "Editor/Setup/Guide.ru.md");
                if (GUILayout.Button("Package Manager")) UnityEditor.PackageManager.UI.Window.Open(FrameworkSetupCatalog.PackageName);
            }
            GUILayout.Space(14);
            GUILayout.Label("Project dependencies", EditorStyles.boldLabel);
            foreach (var item in diagnostics)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(item.Title, EditorStyles.boldLabel, GUILayout.Width(185));
                        GUILayout.Label(item.Detail, wrapped);
                        if (!string.IsNullOrEmpty(item.SettingsPath) && GUILayout.Button("Settings", GUILayout.Width(72))) SettingsService.OpenProjectSettings(item.SettingsPath);
                    }
                    if (item.Severity == MessageType.Error) GUILayout.Label("Action required", EditorStyles.miniBoldLabel);
                    else if (item.Severity == MessageType.Warning) GUILayout.Label("Review recommended", EditorStyles.miniLabel);
                }
            }
            GUILayout.Space(14); DrawCombat();
        }
        private void DrawModules()
        {
            GUILayout.Space(12);
            GUILayout.Label("All modules are included. These rows report their loaded assemblies; they do not enable global services.", wrapped);
            foreach (var module in FrameworkSetupCatalog.Modules)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(module.Name, EditorStyles.boldLabel, GUILayout.Width(145));
                        GUILayout.Label(FrameworkSetupCatalog.IsLoaded(module) ? "Loaded" : "Not loaded — check Console", GUILayout.Width(190));
                        GUILayout.FlexibleSpace(); GuideButton("Guide", module.Guide);
                    }
                    GUILayout.Label(module.Purpose, wrapped);
                }
            }
        }
        private void DrawSamples()
        {
            GUILayout.Space(12);
            GUILayout.Label("Production integrations are already installed. Samples add editable examples to Assets.", wrapped);
            if (package == null) { EditorGUILayout.HelpBox("Install EasyGameFramework through Package Manager to discover its samples.", MessageType.Info); return; }
            if (samples.Length == 0) { EditorGUILayout.HelpBox("This package version does not register any samples.", MessageType.Info); return; }
            foreach (var sample in samples)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(sample.displayName, EditorStyles.boldLabel);
                    GUILayout.Label(sample.description ?? "", wrapped);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(sample.isImported ? "Imported into Assets" : "Ready to import", EditorStyles.miniLabel);
                        GUILayout.FlexibleSpace();
                        if (sample.isImported)
                        { if (GUILayout.Button("Reveal", GUILayout.Width(100))) EditorUtility.RevealInFinder(sample.importPath); }
                        else using (new EditorGUI.DisabledScope(Busy))
                            if (GUILayout.Button("Import", GUILayout.Width(100))) Import(sample);
                    }
                }
            }
        }
        private bool Busy => importing || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed;
        private void Import(Sample sample)
        {
            importing = true;
            try
            {
                // Public UPM API, with its default existing-import safeguards. Never silently override user examples.
                bool imported = sample.Import((Sample.ImportOptions)0);
                message = imported ? "Imported " + sample.displayName + ". Use Reveal to open its folder and follow the sample guide." : "Import was cancelled or did not complete. See Console for details.";
            }
            catch (ExitGUIException) { throw; }
            catch (Exception e) { message = "Import failed: " + e.Message; }
            finally { importing = false; MarkRefresh(); }
        }
        private void GuideButton(string label, string relative)
        {
            var path = FrameworkSetupCatalog.DocumentationPath(package, relative);
            using (new EditorGUI.DisabledScope(path == null))
                if (GUILayout.Button(label, GUILayout.MinWidth(70), GUILayout.MaxWidth(180))) Application.OpenURL(new Uri(path).AbsoluteUri);
        }
        partial void DrawCombat();
    }
}
