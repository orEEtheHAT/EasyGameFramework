using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyResources;
using EasyResources.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using ResourceDefinition = EasyResources.ResourceDefinition;

namespace EasySuite.Example.Editor
{
    /// <summary>Scoped, backed-up conversion of this example's resources; never migrates another game's content.</summary>
    public static class FrameworkResourceDatabaseMigration
    {
        public const string DatabasePath = EasySuiteSceneBuilder.Content + "/Definitions/Resources/Resources_Sample.asset";
        private const string Audit = "Audits/EasyGameFramework/ResourcesDatabase-20261116/BeforeMigration";
        private static readonly string[] OldPaths =
        {
            EasySuiteSceneBuilder.Content + "/Definitions/Wood.asset",
            EasySuiteSceneBuilder.Content + "/Definitions/Energy.asset",
            EasySuiteSceneBuilder.Content + "/Definitions/Resources/Coin_Sample.asset",
            EasySuiteSceneBuilder.Content + "/Definitions/Resources/Crystal_Sample.asset"
        };
        private static readonly string[] Ids = { "suite.wood", "suite.energy", "framework.coin.sample", "framework.crystal.sample" };
        private static readonly string[] Names = { "Wood", "Energy", "Coin_Sample", "Crystal_Sample" };

        [Serializable] private sealed class RetiredResource
        {
            public string StableId, SourcePath, SourceGuid, DatabaseGuid;
            public long SourceFileId, DatabaseFileId;
        }
        [Serializable] private sealed class MigrationReport { public RetiredResource[] Resources; }

        /// <summary>Ensure four native records, remap only the example scope, and bind its cached runtime references.</summary>
        public static void Prepare(EasySuiteLab lab)
        {
            if (!lab || Application.isPlaying) throw new InvalidOperationException("Update the saved example outside Play Mode.");
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            AssetDatabase.Refresh();
            var database = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(DatabasePath);
            if (!database)
            {
                database = ResourceDatabase.Create("Resources_Sample");
                AssetDatabase.CreateAsset(database, DatabasePath);
            }
            ResourceDatabaseEditorUtility.ValidateAsset(database);
            var remap = new Dictionary<ResourceDefinition, ResourceDefinition>();
            var pending = new List<ResourceDefinition>();
            var transient = new List<ResourceDefinition>();
            try
            {
                for (int i = 0; i < Ids.Length; i++)
                {
                    var source = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(OldPaths[i]);
                    if (source && source.Id != Ids[i])
                        throw new InvalidOperationException("The sample resource at " + OldPaths[i] + " has an unexpected stable ID. Restore it before migration.");
                    if (database.TryGet(Ids[i], out var existing))
                    {
                        if (source)
                        {
                            if (!SameConfiguration(source, existing)) throw new InvalidOperationException("Source and database disagree for " + source.DisplayName + ". Resolve the edited records before retiring either copy.");
                            remap.Add(source, existing);
                        }
                        continue;
                    }
                    if (!source)
                    {
                        source = ResourceDefinition.Create(Names[i], Ids[i], decimalPlaces: i == 1 ? 2 : 0);
                        transient.Add(source);
                    }
                    else BackupSource(OldPaths[i]);
                    pending.Add(source);
                }
                foreach (var pair in ResourceDatabaseEditorUtility.ImportDefinitions(database, pending)) remap.Add(pair.Key, pair.Value);
                if (remap.Count != 0)
                {
                    BackupScope(lab.gameObject.scene.path);
                    RemapContent(remap);
                    RemapScene(lab.gameObject.scene, remap);
                }
                lab.ResourceDatabase = database;
                lab.Wood = Get(database, Ids[0]); lab.Mana = Get(database, Ids[1]);
                lab.Coin = Get(database, Ids[2]); lab.Crystal = Get(database, Ids[3]);
                foreach (var path in ContentAssetPaths().Where(path => path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)))
                    foreach (var profile in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ResourceRewardProfile>())
                    {
                        profile.Database = database; // Rows have already been remapped; foreign rows fail without clearing them.
                        EditorUtility.SetDirty(profile); AssetDatabase.SaveAssetIfDirty(profile);
                    }
                ResourceDatabaseEditorUtility.ValidateAsset(database);
                EditorUtility.SetDirty(lab); AssetDatabase.SaveAssetIfDirty(database);
            }
            finally
            {
                foreach (var source in transient) if (source) Object.DestroyImmediate(source);
            }
        }

        /// <summary>Call after saving the scene. Verify persisted references before retiring backed-up standalone files.</summary>
        public static void FinalizeSavedScene(EasySuiteLab lab, bool sealPreparedEdits = false)
        {
            var old = OldPaths.Where(path => AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path)).ToArray();
            if (old.Length == 0)
            {
                if (sealPreparedEdits && lab && lab.ResourceDatabase) SealImportUndo(lab.ResourceDatabase);
                return;
            }
            if (!lab || lab.gameObject.scene.isDirty || string.IsNullOrEmpty(lab.gameObject.scene.path))
                throw new InvalidOperationException("Save the migrated Playground before retiring its old resources.");
            var database = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(DatabasePath);
            ResourceDatabaseEditorUtility.ValidateAsset(database);
            foreach (var path in old)
            {
                BackupSource(path);
                var source = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
                if (!database.TryGet(source.Id, out var record) || !SameConfiguration(source, record))
                    throw new InvalidOperationException("The backed-up resource differs from its database record: " + path);
            }
            RejectExternalConsumers(old, lab.gameObject.scene.path);
            VerifyContent(database, old);
            VerifySavedScene(lab.gameObject.scene, database, old);
            var report = new MigrationReport { Resources = old.Select(path =>
            {
                var source = AssetDatabase.LoadAssetAtPath<ResourceDefinition>(path);
                var record = Get(database, source.Id);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string sourceGuid, out long sourceId);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(record, out string databaseGuid, out long recordId);
                return new RetiredResource { StableId = source.Id, SourcePath = path, SourceGuid = sourceGuid, SourceFileId = sourceId, DatabaseGuid = databaseGuid, DatabaseFileId = recordId };
            }).ToArray() };
            Directory.CreateDirectory(Audit);
            File.WriteAllText(Audit + "/resource-reference-remap.json", JsonUtility.ToJson(report, true));
            foreach (var path in old)
                if (!AssetDatabase.DeleteAsset(path)) throw new IOException("Could not retire the verified resource asset: " + path);
            ResourceIdValidator.Invalidate();
            SealImportUndo(database);
        }

        // This one-time disk migration is recoverable from backups, not a partial Undo which could orphan saved refs.
        private static void SealImportUndo(ResourceDatabase database)
        {
            foreach (var resource in database.Resources) Undo.ClearUndo(resource);
            Undo.ClearUndo(database);
        }

        private static ResourceDefinition Get(ResourceDatabase database, string id) => database.TryGet(id, out var value)
            ? value : throw new InvalidOperationException("Missing sample database resource: " + id);
        private static bool SameConfiguration(ResourceDefinition first, ResourceDefinition second) =>
            first.Id == second.Id && first.DisplayName == second.DisplayName && first.Icon == second.Icon &&
            first.DecimalPlaces == second.DecimalPlaces && first.Unit == second.Unit &&
            first.WeightPerUnit == second.WeightPerUnit && first.VolumePerUnit == second.VolumePerUnit;

        private static string[] ContentAssetPaths() => AssetDatabase.FindAssets("", new[] { EasySuiteSceneBuilder.Content })
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => !AssetDatabase.IsValidFolder(path)).Distinct().ToArray();

        private static void BackupSource(string path)
        {
            foreach (var source in new[] { path, path + ".meta" })
            {
                if (!File.Exists(source)) continue;
                string destination = Audit + "/OriginalFiles/" + source;
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                if (!File.Exists(destination)) File.Copy(source, destination);
            }
        }
        private static void BackupScope(string scenePath)
        {
            foreach (var path in ContentAssetPaths()) BackupSource(path);
            if (!string.IsNullOrEmpty(scenePath)) BackupSource(scenePath);
        }

        private static bool RemapObject(Object value, Dictionary<ResourceDefinition, ResourceDefinition> remap)
        {
            if (!value) return false;
            var serialized = new SerializedObject(value); var property = serialized.GetIterator(); bool changed = false;
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is ResourceDefinition source && remap.TryGetValue(source, out var target))
                { property.objectReferenceValue = target; changed = true; }
            if (changed) { serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(value); }
            return changed;
        }
        private static void RemapScene(Scene scene, Dictionary<ResourceDefinition, ResourceDefinition> remap)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var component in root.GetComponentsInChildren<Component>(true)) RemapObject(component, remap);
        }
        private static void RemapContent(Dictionary<ResourceDefinition, ResourceDefinition> remap)
        {
            foreach (var path in ContentAssetPaths())
            {
                if (OldPaths.Contains(path) || path == DatabasePath) continue;
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        bool changed = false;
                        foreach (var component in root.GetComponentsInChildren<Component>(true)) changed |= RemapObject(component, remap);
                        if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>())
                        if (RemapObject(asset, remap)) AssetDatabase.SaveAssetIfDirty(asset);
            }
        }

        private static void VerifyObject(Object value, ResourceDatabase database, HashSet<string> oldPaths)
        {
            if (!value) return;
            var serialized = new SerializedObject(value); var property = serialized.GetIterator();
            while (property.Next(true))
                if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is ResourceDefinition resource)
                {
                    if (oldPaths.Contains(AssetDatabase.GetAssetPath(resource)) || !database.Contains(resource))
                        throw new InvalidOperationException("Unmigrated resource reference in " + value.name + ": " + property.propertyPath);
                }
        }
        private static void VerifyContent(ResourceDatabase database, string[] old)
        {
            var paths = new HashSet<string>(old, StringComparer.Ordinal);
            foreach (var path in ContentAssetPaths())
            {
                if (paths.Contains(path) || path == DatabasePath) continue;
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try { foreach (var component in root.GetComponentsInChildren<Component>(true)) VerifyObject(component, database, paths); }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                else if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableObject>())
                    {
                        VerifyObject(asset, database, paths);
                        if (asset is ResourceRewardProfile profile) profile.ValidateConfiguration();
                    }
                }
            }
        }
        private static void VerifySavedScene(Scene live, ResourceDatabase database, string[] old)
        {
            string temporary = "Assets/EasyGameFramework/Playground/__ResourceVerification_" + Guid.NewGuid().ToString("N") + ".unity";
            var previous = SceneManager.GetActiveScene(); Scene verification = default;
            try
            {
                File.Copy(live.path, temporary); AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
                verification = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
                var oldPaths = new HashSet<string>(old, StringComparer.Ordinal);
                foreach (var root in verification.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<Component>(true)) VerifyObject(component, database, oldPaths);
                var labs = verification.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EasySuiteLab>(true)).ToArray();
                if (labs.Length != 1 || labs[0].ResourceDatabase != database || !database.Contains(labs[0].Wood) ||
                    !database.Contains(labs[0].Mana) || !database.Contains(labs[0].Coin) || !database.Contains(labs[0].Crystal))
                    throw new InvalidOperationException("The saved scene did not restore the expected database references.");
            }
            finally
            {
                if (verification.IsValid() && verification.isLoaded) EditorSceneManager.CloseScene(verification, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(temporary)) AssetDatabase.DeleteAsset(temporary);
                else if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        private static void RejectExternalConsumers(string[] old, string mainScene)
        {
            var oldPaths = new HashSet<string>(old, StringComparer.Ordinal);
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path == mainScene ||
                    path.StartsWith(EasySuiteSceneBuilder.Content + "/", StringComparison.Ordinal) || oldPaths.Contains(path) || AssetDatabase.IsValidFolder(path)) continue;
                if (AssetDatabase.GetDependencies(path, false).Any(oldPaths.Contains))
                    throw new InvalidOperationException("Another asset uses an old sample resource. Preserve it and extend the migration scope before retirement: " + path);
            }
        }
    }
}
