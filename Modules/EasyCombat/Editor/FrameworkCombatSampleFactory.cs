using System;
using System.IO;
using EasyCombat;
using EasyCombat.Editor;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>Explicit, self-contained authoring for the framework Playground. No sample import needed.</summary>
    public static class FrameworkCombatSampleFactory
    {
        public static FrameworkCombatSampleAssets Create(string outputFolder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Create combat samples outside Play mode, after compilation.");
            outputFolder=(outputFolder ?? "").Replace('\\','/').TrimEnd('/');
            if (!outputFolder.StartsWith("Assets/",StringComparison.Ordinal) || outputFolder.Contains(".."))
                throw new ArgumentException("Choose a dedicated output folder under Assets.",nameof(outputFolder));
            var manifestPath=outputFolder+"/CombatSampleAssets.asset";
            var existing=AssetDatabase.LoadMainAssetAtPath(manifestPath);
            if (existing)
            {
                if (!(existing is FrameworkCombatSampleAssets ready) || !IsComplete(ready))
                    throw new InvalidOperationException("Combat sample assets are incomplete or invalid: "+manifestPath+". Repair referenced assets or choose a new empty output folder. Existing data was not changed.");
                return ready;
            }
            if (AssetDatabase.IsValidFolder(outputFolder) && Array.Exists(AssetDatabase.FindAssets("",new[] { outputFolder }),guid=>AssetDatabase.GUIDToAssetPath(guid)!=outputFolder))
                throw new InvalidOperationException("Combat output folder contains assets without its manifest: "+outputFolder+". Choose an empty dedicated folder; existing recipes will not be duplicated or overwritten.");
            CombatAssetLibrary.EnsureFolder(outputFolder);
            // Record ownership first. A partial run remains diagnosable on retry.
            var manifest=ScriptableObject.CreateInstance<FrameworkCombatSampleAssets>();
            AssetDatabase.CreateAsset(manifest,manifestPath);
            AssetDatabase.SaveAssetIfDirty(manifest);
            var attack = (AttackDefinition)CombatTemplates.Create(CombatTemplateKind.FireballAndGround, outputFolder);
            var folder = Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(attack))).Replace('\\', '/');
            var error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(attack), "Fireball_Sample");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            attack.Damage = 22;
            attack.CriticalChance = 0;
            var burn = attack.OnHitStatuses[0].Status;
            burn.Stacking = StatusStacking.RefreshDuration;
            burn.MaximumStacks = 1;
            attack.OnHitStatuses = new[] { new StatusApplication(burn) };
            var pulses = attack.ImpactArea.Pulses;
            pulses.Duration = 4;
            pulses.ApplyImmediately = true;
            pulses.OverlapGroup.ApplyImmediately = true;

            CombatAssetLibrary.EnsureFolder(folder + "/Stats");
            var speed = ScriptableObject.CreateInstance<CombatStatDefinition>();
            speed.DisplayName = "Movement speed";
            speed.Description = "Assign this key to MovementInfluences.MovementSpeedStat and to movement status modifiers.";
            AssetDatabase.CreateAsset(speed, folder + "/Stats/MovementSpeed_Sample.asset");
            foreach (var asset in new ScriptableObject[] { attack, burn, pulses, pulses.OverlapGroup, speed }) EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            if (!attack.IsValid || !pulses.IsValid || !burn.IsValid) throw new InvalidOperationException("Generated Playground combat recipe is invalid.");
            manifest.Initialize(folder,attack,speed);
            EditorUtility.SetDirty(manifest); AssetDatabase.SaveAssetIfDirty(manifest);
            return manifest;
        }
        private static bool IsComplete(FrameworkCombatSampleAssets assets)
            => assets.Complete && assets.Fireball && assets.MovementSpeed && assets.Fireball.IsValid
                && assets.Fireball.ProjectilePrefab && assets.Fireball.Area && assets.Fireball.ImpactArea
                && assets.Fireball.ImpactArea.IsValid && assets.Fireball.ImpactArea.Prefab
                && assets.Fireball.ImpactArea.Pulses && assets.Fireball.ImpactArea.Pulses.IsValid;
    }
}
