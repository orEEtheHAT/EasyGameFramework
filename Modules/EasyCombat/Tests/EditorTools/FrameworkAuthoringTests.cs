using System;
using System.IO;
using EasyCombat.Editor;
using EasyGameFramework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyCombat.Tests
{
    public sealed class FrameworkAuthoringTests
    {
        private string folder;
        [TearDown] public void Cleanup() => DeleteTestFolder();

        private void DeleteTestFolder()
        {
            if(string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/EasyCombatFrameworkTest_",StringComparison.Ordinal)
                || !AssetDatabase.IsValidFolder(folder)) return;
            if(AssetDatabase.GetAssetPath(Selection.activeObject).StartsWith(folder+"/",StringComparison.Ordinal)) Selection.activeObject=null;
            Undo.FlushUndoRecordObjects();
            foreach(var guid in AssetDatabase.FindAssets("",new[] { folder }))
                foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    if(asset)
                    {
                        Undo.ClearUndo(asset);
                        if(asset is GameObject root)
                            foreach(var transform in root.GetComponentsInChildren<Transform>(true))
                            {
                                foreach(var component in transform.GetComponents<Component>()) if(component) Undo.ClearUndo(component);
                                Undo.ClearUndo(transform.gameObject);
                            }
                    }
            AssetDatabase.DeleteAsset(folder);
        }

        [Test] public void PlaygroundFactoryHasLocalCompleteRecipesWithoutImportedSamples()
        {
            folder="Assets/EasyCombatFrameworkTest_"+Guid.NewGuid().ToString("N");
            var first=FrameworkCombatSampleFactory.Create(folder);
            Assert.That(first.Fireball.IsValid,Is.True);
            Assert.That(first.Fireball.Damage,Is.EqualTo(22));
            Assert.That(first.Fireball.ProjectilePrefab,Is.Not.Null);
            Assert.That(first.Fireball.ProjectilePrefab.GetComponent<Collider>(),Is.Null);
            Assert.That(first.Fireball.ImpactArea.Prefab,Is.Not.Null);
            Assert.That(first.Fireball.ImpactArea.Pulses.Duration,Is.EqualTo(4));
            Assert.That(first.Fireball.OnHitStatuses[0].Status,Is.SameAs(first.Fireball.ImpactArea.Pulses.Attack.OnHitStatuses[0].Status));
            Assert.That(first.Fireball.OnHitStatuses[0].Status.Stacking,Is.EqualTo(StatusStacking.RefreshDuration));
            var graph=CombatDependencyAnalyzer.Analyze(new ScriptableObject[] { first.Fireball,first.MovementSpeed });
            Assert.That(graph.Cycles,Is.Zero); Assert.That(graph.Issues.FindAll(issue=>issue.Error),Is.Empty);
            foreach(var path in AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(first.Fireball),true))
                if(AssetDatabase.LoadMainAssetAtPath(path) is ScriptableObject)
                    Assert.That(path.StartsWith(folder+"/",StringComparison.Ordinal),Is.True,path);
            first.Fireball.Damage=37; EditorUtility.SetDirty(first.Fireball); AssetDatabase.SaveAssets();
            var before=AssetDatabase.FindAssets("",new[] { folder });
            var second=FrameworkCombatSampleFactory.Create(folder);
            Assert.That(second,Is.SameAs(first)); Assert.That(second.Fireball.Damage,Is.EqualTo(37));
            CollectionAssert.AreEquivalent(before,AssetDatabase.FindAssets("",new[] { folder }));
        }

        [Test] public void PartialFactoryManifestIsDiagnosedWithoutCreatingAnotherRecipe()
        {
            folder="Assets/EasyCombatFrameworkTest_"+Guid.NewGuid().ToString("N");
            CombatAssetLibrary.EnsureFolder(folder);
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<FrameworkCombatSampleAssets>(),folder+"/CombatSampleAssets.asset");
            var before=AssetDatabase.FindAssets("",new[] { folder });
            var error=Assert.Throws<InvalidOperationException>(()=>FrameworkCombatSampleFactory.Create(folder));
            Assert.That(error.Message,Does.Contain("incomplete"));
            CollectionAssert.AreEquivalent(before,AssetDatabase.FindAssets("",new[] { folder }));
        }

        [Test] public void MissingFactoryReferenceDoesNotResetUserTunedDamage()
        {
            folder="Assets/EasyCombatFrameworkTest_"+Guid.NewGuid().ToString("N");
            var assets=FrameworkCombatSampleFactory.Create(folder); assets.Fireball.Damage=37;
            EditorUtility.SetDirty(assets.Fireball); AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(assets.MovementSpeed));
            var before=AssetDatabase.FindAssets("",new[] { folder });
            Assert.Throws<InvalidOperationException>(()=>FrameworkCombatSampleFactory.Create(folder));
            Assert.That(assets.Fireball.Damage,Is.EqualTo(37));
            CollectionAssert.AreEquivalent(before,AssetDatabase.FindAssets("",new[] { folder }));
        }

        [Test] public void ReadingSetupAndRejectingInvalidModeNeverCreatesOrChangesSelection()
        {
            var before=AssetDatabase.FindAssets("t:ProjectileExecutionSettings");
            var mode=CombatProjectileSetup.RequestedMode;
            _=CombatProjectileSetup.EffectiveMode; _=CombatProjectileSetup.ParallelAvailable; _=CombatProjectileSetup.SupportDiagnostic;
            _=CombatProjectileSetup.HasExplicitSelection; _=CombatProjectileSetup.SettingsAssetPath;
            Assert.Throws<ArgumentOutOfRangeException>(()=>CombatProjectileSetup.SetMode((ProjectileExecutionMode)123));
            CollectionAssert.AreEquivalent(before,AssetDatabase.FindAssets("t:ProjectileExecutionSettings"));
            Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(mode));
        }

        [Category("CombatSetupUndo")]
        [Test] public void ExplicitModePersistsAndRejectsAmbiguousResourcesWithoutChangingExistingChoice()
        {
            var existed=CombatProjectileSetup.HasExplicitSelection; var old=CombatProjectileSetup.RequestedMode;
            var originalPath=CombatProjectileSetup.SettingsAssetPath;
            var originalText=existed?File.ReadAllBytes(originalPath):null;
            Undo.IncrementCurrentGroup(); var fixtureUndoGroup=Undo.GetCurrentGroup();
            try
            {
                CombatProjectileSetup.SetMode(ProjectileExecutionMode.SingleThread);
                var selectedPath=CombatProjectileSetup.SettingsAssetPath;
                var singleThreadBytes=File.ReadAllBytes(selectedPath);
                Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(ProjectileExecutionMode.SingleThread));
                Undo.FlushUndoRecordObjects(); Undo.IncrementCurrentGroup();
                CombatProjectileSetup.SetMode(ProjectileExecutionMode.Parallel);
                Undo.FlushUndoRecordObjects();
                Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(ProjectileExecutionMode.Parallel));
                Assert.That(File.ReadAllBytes(selectedPath),Is.Not.EqualTo(singleThreadBytes));
                Undo.PerformUndo(); Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(ProjectileExecutionMode.SingleThread));
                Undo.PerformRedo(); Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(ProjectileExecutionMode.Parallel));
                folder="Assets/EasyCombatFrameworkTest_"+Guid.NewGuid().ToString("N");
                CombatAssetLibrary.EnsureFolder(folder+"/Resources");
                var duplicate=ScriptableObject.CreateInstance<ProjectileExecutionSettings>();
                AssetDatabase.CreateAsset(duplicate,folder+"/Resources/EasyCombatProjectileExecution.asset");
                var before=File.ReadAllBytes(selectedPath);
                Assert.Throws<InvalidOperationException>(()=>CombatProjectileSetup.SetMode(ProjectileExecutionMode.SingleThread));
                CollectionAssert.AreEqual(before,File.ReadAllBytes(selectedPath));
            }
            finally
            {
                DeleteTestFolder();
                Undo.FlushUndoRecordObjects();
                if(existed)
                {
                    // Return only this synchronous test's records while the borrowed asset is alive.
                    // Do not clear its pre-existing user Undo history or register another SetMode undo.
                    Undo.RevertAllDownToGroup(fixtureUndoGroup);
                    File.WriteAllBytes(originalPath,originalText);
                    AssetDatabase.ImportAsset(originalPath,ImportAssetOptions.ForceSynchronousImport);
                    Assert.That(CombatProjectileSetup.RequestedMode,Is.EqualTo(old));
                }
                else
                {
                    var owned=AssetDatabase.LoadAssetAtPath<ProjectileExecutionSettings>(CombatProjectileSetup.DefaultAssetPath);
                    if(owned) Undo.ClearUndo(owned); // Clear the created-object record before destroying this test-owned asset.
                    AssetDatabase.DeleteAsset(CombatProjectileSetup.DefaultAssetPath);
                }
                Undo.IncrementCurrentGroup();
            }
        }
    }
}
