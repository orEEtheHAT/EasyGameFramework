using System.IO;
using System.Linq;
using EasyGameFramework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyUI.Tests.Setup
{
    public sealed class SetupTests
    {
        [TestCase("com.oreethehat.easy-core")]
        [TestCase("com.oreethehat.easy-save")]
        [TestCase("com.oreethehat.easy-settings")]
        [TestCase("com.oreethehat.easy-input")]
        [TestCase("com.oreethehat.easy-movement")]
        [TestCase("com.oreethehat.easy-combat")]
        [TestCase("com.oreethehat.easy-feedbacks")]
        [TestCase("com.oreethehat.easy-interaction")]
        [TestCase("com.oreethehat.easy-building")]
        [TestCase("com.oreethehat.easy-resources")]
        [TestCase("com.oreethehat.easy-localization")]
        [TestCase("com.oreethehat.easy-ui")]
        public void FormerModulePackagesConflictWithUnifiedAssemblies(string packageName)
        {
            Assert.IsTrue(FrameworkSetupCatalog.IsLegacyModulePackage(packageName));
        }
        [TestCase(FrameworkSetupCatalog.PackageName)]
        [TestCase("com.oreethehat.easy-game-framework.netcode")]
        [TestCase("com.oreethehat.easy-game-framework.xr")]
        [TestCase("com.oreethehat.easy-game-framework.ar")]
        [TestCase("com.oreethehat.easy-game-framework.storage")]
        [TestCase("com.oreethehat.easy-feedbacks-tools")]
        [TestCase("com.oreethehat.easy-custom")]
        [TestCase("com.unity.inputsystem")]
        [TestCase("")]
        [TestCase(null)]
        public void FrameworkAddonsAndOtherPackagesAreNotLegacyModuleConflicts(string packageName)
        {
            Assert.IsFalse(FrameworkSetupCatalog.IsLegacyModulePackage(packageName));
        }
        [Test] public void CatalogContainsThirteenUniqueBuiltInModulesWithGuides()
        {
            var modules = FrameworkSetupCatalog.Modules;
            Assert.AreEqual(13, modules.Count); Assert.AreEqual(13, modules.Select(m => m.Name).Distinct().Count());
            var package = FrameworkSetupCatalog.FindPackage(); Assert.IsNotNull(package, "Run after framework CUTOVER.");
            foreach (var module in modules) Assert.IsNotNull(FrameworkSetupCatalog.DocumentationPath(package, module.Guide), module.Name);
            Assert.IsNull(FrameworkSetupCatalog.DocumentationPath(package, "../manifest.json"), "Links cannot escape the package.");
        }
        [Test] public void WelcomeIsOncePerProjectIncludingPackageUpdates()
        {
            Assert.IsTrue(FrameworkSetupWelcome.ShouldShow("0.1.0", false));
            Assert.IsFalse(FrameworkSetupWelcome.ShouldShow("0.1.0", true));
            Assert.IsFalse(FrameworkSetupWelcome.ShouldShow("0.2.0", true));
            Assert.IsFalse(FrameworkSetupWelcome.ShouldShow(null, false));
        }
        [Test] public void PersistentWelcomeStateHasItsOwnReloadableScript()
        {
            var script = MonoScript.FromScriptableObject(FrameworkSetupWelcomeState.instance);
            Assert.IsNotNull(script, "Welcome state must have a persistent script association.");
            Assert.AreEqual(typeof(FrameworkSetupWelcomeState), script.GetClass(), "Keep the state in its own matching script file so native reload can restore it.");
        }
        [Test] public void OpeningRefreshingAndClosingSetupKeepsCombatSelectionAndAssetUnchanged()
        {
            var requested = CombatProjectileSetup.RequestedMode;
            var explicitChoice = CombatProjectileSetup.HasExplicitSelection;
            string path = CombatProjectileSetup.SettingsAssetPath;
            string before = !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null;
            var window = ScriptableObject.CreateInstance<FrameworkSetupWindow>();
            try { window.RefreshData(); window.Show(); window.Close(); }
            finally { if (window) Object.DestroyImmediate(window); }
            Assert.AreEqual(requested, CombatProjectileSetup.RequestedMode);
            Assert.AreEqual(explicitChoice, CombatProjectileSetup.HasExplicitSelection);
            Assert.AreEqual(before, !string.IsNullOrEmpty(path) && File.Exists(path) ? File.ReadAllText(path) : null);
        }
    }
}
