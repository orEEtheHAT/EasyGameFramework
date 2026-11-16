using System;
using System.Linq;
using EasyResources.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyResources.Tests
{
    public sealed class ResourceDatabaseAuthoringTests
    {
        private string folder, databasePath;
        private ResourceDatabase database;

        [SetUp]
        public void SetUp()
        {
            EnsureFolder("Assets/PackageTests/EasyResources");
            folder = "Assets/PackageTests/EasyResources/DatabaseTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/PackageTests/EasyResources", folder.Substring(folder.LastIndexOf('/') + 1));
            databasePath = folder + "/Resources.asset";
            database = ScriptableObject.CreateInstance<ResourceDatabase>();
            AssetDatabase.CreateAsset(database, databasePath);
            AssetDatabase.SaveAssets();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(folder);
            ResourceIdValidator.Invalidate();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }

        private static void SetContents(ResourceContainer container, ResourceDefinition resource)
        {
            var data = new SerializedObject(container);
            var rows = data.FindProperty("initialContents").FindPropertyRelative("items");
            rows.arraySize = 1;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("resource").objectReferenceValue = resource;
            rows.GetArrayElementAtIndex(0).FindPropertyRelative("amount").longValue = 1;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void AddedEntriesLiveInOneAssetAndAllAreDiscoveredAfterImport()
        {
            var first = ResourceDatabaseEditorUtility.AddResource(database, "Coin");
            var second = ResourceDatabaseEditorUtility.AddResource(database, "Crystal");
            var firstId = first.Id; var secondId = second.Id;
            Assert.That(AssetDatabase.GetAssetPath(first), Is.EqualTo(databasePath));
            Assert.That(AssetDatabase.GetAssetPath(second), Is.EqualTo(databasePath));
            Assert.That(ResourceDatabaseEditorUtility.IsOwnedResource(database, first) && ResourceDatabaseEditorUtility.IsOwnedResource(database, second), Is.True);
            Assert.That(ResourceDatabaseEditorUtility.FindDatabase(first), Is.SameAs(database));
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(databasePath);
            Assert.DoesNotThrow(() => ResourceDatabaseEditorUtility.ValidateAsset(reloaded));
            Assert.That(reloaded.Resources.Select(resource => resource.Id), Is.EquivalentTo(new[] { firstId, secondId }));
            ResourceIdValidator.Invalidate();
            Assert.That(ResourceIdValidator.GetDefinitions().Select(resource => resource.Id), Does.Contain(firstId).And.Contain(secondId));
            Assert.That(ResourceIdValidator.GetAllDefinitions().Select(resource => resource.Id), Does.Contain(firstId).And.Contain(secondId));
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<ResourceDefinition>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void EntryEditUndoKeepsIdentityAndAddRemoveHaveUndoRedo()
        {
            var entry = ResourceDatabaseEditorUtility.AddResource(database, "Original");
            var id = entry.Id;
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out _), Is.True);
            Undo.IncrementCurrentGroup();
            var data = new SerializedObject(entry);
            data.FindProperty("displayName").stringValue = "Edited";
            data.FindProperty("decimalPlaces").intValue = 2;
            data.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
            Assert.That(entry.DisplayName, Is.EqualTo("Edited"));
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out var located), Is.True); Assert.That(located.DecimalPlaces, Is.EqualTo(2));
            Undo.PerformUndo();
            Assert.That(entry.DisplayName, Is.EqualTo("Original")); Assert.That(entry.Id, Is.EqualTo(id));
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out located), Is.True); Assert.That(located.DecimalPlaces, Is.Zero);
            Undo.PerformRedo();
            Assert.That(entry.DisplayName, Is.EqualTo("Edited")); Assert.That(entry.DecimalPlaces, Is.EqualTo(2));
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out located), Is.True); Assert.That(located.DecimalPlaces, Is.EqualTo(2));

            // Warm caches must be invalidated by a child's identity edit and its Undo, not only by editing the parent list.
            Undo.IncrementCurrentGroup();
            var editedId = Guid.NewGuid().ToString("N");
            data.Update(); data.FindProperty("id").stringValue = editedId;
            data.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out _), Is.False);
            Assert.That(database.TryGet(editedId, out located), Is.True); Assert.That(located, Is.SameAs(entry));
            Undo.PerformUndo();
            database.ValidateConfiguration(); Assert.That(database.TryGet(id, out located), Is.True);
            Assert.That(database.TryGet(editedId, out _), Is.False); Assert.That(entry.Id, Is.EqualTo(id));

            Undo.IncrementCurrentGroup();
            data.Update(); data.FindProperty("decimalPlaces").intValue = 10;
            data.ApplyModifiedProperties(); Undo.FlushUndoRecordObjects();
            Assert.Throws<ArgumentException>(() => database.ValidateConfiguration(), "Child OnValidate must invalidate previously validated configuration.");
            Undo.PerformUndo();
            Assert.DoesNotThrow(() => database.ValidateConfiguration());
            Assert.That(database.TryGet(id, out located), Is.True); Assert.That(located.DecimalPlaces, Is.EqualTo(2));

            var extra = ResourceDatabaseEditorUtility.AddResource(database, "Extra"); var extraId = extra.Id;
            Undo.PerformUndo();
            Assert.That(database.Resources.Any(resource => resource != null && resource.Id == extraId), Is.False);
            Undo.PerformRedo();
            Assert.That(database.TryGet(extraId, out extra), Is.True);
            Assert.That(ResourceDatabaseEditorUtility.TryRemoveResource(database, extra, out var error), Is.True, error);
            Undo.PerformUndo();
            Assert.That(database.TryGet(extraId, out extra), Is.True);
            Assert.That(ResourceDatabaseEditorUtility.IsOwnedResource(database, extra), Is.True);
            Undo.PerformRedo();
            Assert.That(database.Resources.Any(resource => resource != null && resource.Id == extraId), Is.False);
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            Assert.That(AssetDatabase.LoadAssetAtPath<ResourceDatabase>(databasePath).Resources.Count, Is.EqualTo(1));
        }

        [Test]
        public void PersistentDatabaseRejectsExternalCatalogEntriesAndOrphanSubassets()
        {
            var external = ResourceDefinition.Create("External");
            AssetDatabase.CreateAsset(external, folder + "/External.asset");
            var data = new SerializedObject(database);
            var rows = data.FindProperty("resources"); rows.arraySize = 1;
            rows.GetArrayElementAtIndex(0).objectReferenceValue = external;
            data.ApplyModifiedPropertiesWithoutUndo(); database.OnAfterDeserialize();
            Assert.That(Assert.Throws<ArgumentException>(() => ResourceDatabaseEditorUtility.ValidateAsset(database)).Message,
                Does.Contain("external"));
            rows.arraySize = 0; data.ApplyModifiedPropertiesWithoutUndo(); database.OnAfterDeserialize();
            var orphan = ResourceDefinition.Create("Orphan"); AssetDatabase.AddObjectToAsset(orphan, database);
            Assert.That(Assert.Throws<ArgumentException>(() => ResourceDatabaseEditorUtility.ValidateAsset(database)).Message,
                Does.Contain("unlisted"));
        }

        [Test]
        public void UsedEntryCannotBeDeletedButOtherEntryInSameDatabaseCan()
        {
            var used = ResourceDatabaseEditorUtility.AddResource(database, "Used");
            var unused = ResourceDatabaseEditorUtility.AddResource(database, "Unused");
            var profile = ResourceRewardProfile.Create("Reward", database, new ResourceRewardRange(used, 1, 1));
            var profilePath = folder + "/Reward.asset"; AssetDatabase.CreateAsset(profile, profilePath); AssetDatabase.SaveAssets();
            Assert.That(ResourceDatabaseEditorUtility.TryRemoveResource(database, used, out var error), Is.False);
            Assert.That(error, Does.Contain(profilePath));
            Assert.That(database.Contains(used), Is.True);
            Assert.That(profile.Ranges[0].Resource, Is.SameAs(used));
            Assert.That(ResourceDatabaseEditorUtility.TryRemoveResource(database, unused, out error), Is.True, error);
            AssetDatabase.DeleteAsset(profilePath);
        }

        [Test]
        public void UnsavedSceneReferencePreventsRemovalWithoutSavingOrReplacingIt()
        {
            var entry = ResourceDatabaseEditorUtility.AddResource(database, "Coin");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var owner = new GameObject("Unsaved resource consumer"); SceneManager.MoveGameObjectToScene(owner, scene);
                SetContents(owner.AddComponent<ResourceContainer>(), entry);
                Assert.That(ResourceDatabaseEditorUtility.TryRemoveResource(database, entry, out var error), Is.False);
                Assert.That(error, Does.Contain("Unsaved resource consumer"));
                Assert.That(database.Contains(entry), Is.True);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void SavedPrefabReferencePreventsDeletionAfterItsSceneObjectIsGone()
        {
            var entry = ResourceDatabaseEditorUtility.AddResource(database, "Coin");
            var scene = EditorSceneManager.NewPreviewScene();
            var path = folder + "/Consumer.prefab";
            try
            {
                var owner = new GameObject("Saved resource consumer"); SceneManager.MoveGameObjectToScene(owner, scene);
                SetContents(owner.AddComponent<ResourceContainer>(), entry);
                PrefabUtility.SaveAsPrefabAsset(owner, path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            Assert.That(ResourceDatabaseEditorUtility.TryRemoveResource(database, entry, out var error), Is.False);
            Assert.That(error, Does.Contain(path));
            Assert.That(database.Contains(entry), Is.True);
            AssetDatabase.DeleteAsset(path);
        }

        [Test]
        public void ImportPreservesAllAuthoredFieldsAndReturnsExplicitRemapWithoutChangingOriginal()
        {
            var original = ResourceDefinition.Create("Fuel", decimalPlaces: 3, weightPerUnit: 7, volumePerUnit: 9, unit: "L");
            var path = folder + "/Original.asset"; AssetDatabase.CreateAsset(original, path); AssetDatabase.SaveAssets();
            var json = EditorJsonUtility.ToJson(original); var id = original.Id;
            var map = ResourceDatabaseEditorUtility.ImportDefinitions(database, new[] { original });
            var imported = map[original];
            Assert.That(imported, Is.Not.SameAs(original)); Assert.That(imported.Id, Is.EqualTo(id));
            Assert.That(imported.DisplayName, Is.EqualTo(original.DisplayName)); Assert.That(imported.DecimalPlaces, Is.EqualTo(3));
            Assert.That(imported.WeightPerUnit, Is.EqualTo(7)); Assert.That(imported.VolumePerUnit, Is.EqualTo(9)); Assert.That(imported.Unit, Is.EqualTo("L"));
            Assert.That(ResourceDatabaseEditorUtility.IsOwnedResource(database, imported), Is.True); Assert.That(AssetDatabase.GetAssetPath(original), Is.EqualTo(path));
            Assert.That(EditorJsonUtility.ToJson(original), Is.EqualTo(json));
            Assert.That(Assert.Throws<ArgumentException>(() => ResourceDatabaseEditorUtility.ImportDefinitions(database, new[] { original })).Message,
                Does.Contain("duplicate"));
            Assert.That(database.Resources.Count, Is.EqualTo(1));
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            database = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(databasePath);
            Assert.That(database.TryGet(id, out var loaded), Is.True);
            Assert.That(ResourceDatabaseEditorUtility.IsOwnedResource(database, loaded), Is.True);
            Assert.That(loaded.DecimalPlaces, Is.EqualTo(3)); Assert.That(loaded.Unit, Is.EqualTo("L"));
        }

        [Test]
        public void HiddenNativeRecordsHaveDistinctFileIdentitiesBeforeAndAfterSynchronousImport()
        {
            var first = ResourceDatabaseEditorUtility.AddResource(database, "First");
            var second = ResourceDatabaseEditorUtility.AddResource(database, "Second");
            var ids = new[] { first.Id, second.Id };
            foreach (var flags in new[] { HideFlags.HideInHierarchy, HideFlags.None, HideFlags.HideInHierarchy })
            {
                foreach (var resource in database.Resources) { resource.hideFlags = flags; EditorUtility.SetDirty(resource); }
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                database = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(databasePath);
                Assert.DoesNotThrow(() => ResourceDatabaseEditorUtility.ValidateAsset(database));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(databasePath), Is.SameAs(database));
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(database, out string guid, out long mainId), Is.True);
                var fileIds = new System.Collections.Generic.HashSet<long> { mainId };
                foreach (var id in ids)
                {
                    Assert.That(database.TryGet(id, out var resource), Is.True);
                    Assert.That(resource.hideFlags, Is.EqualTo(flags));
                    Assert.That(ResourceDatabaseEditorUtility.IsOwnedResource(database, resource), Is.True);
                    Assert.That(EditorUtility.IsPersistent(resource), Is.True);
                    Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(resource, out string childGuid, out long fileId), Is.True);
                    Assert.That(childGuid, Is.EqualTo(guid)); Assert.That(fileId, Is.Not.Zero);
                    Assert.That(fileIds.Add(fileId), Is.True);
                    Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).Any(asset => ReferenceEquals(asset, resource)), Is.True);
                }
            }
        }

        [Test]
        public void ImportRejectsLateDuplicateBeforeCreatingAnyRecord()
        {
            var first = ResourceDefinition.Create("First"); var copy = ResourceDefinition.Create("Copy", stableId: first.Id);
            try
            {
                Assert.Throws<ArgumentException>(() => ResourceDatabaseEditorUtility.ImportDefinitions(database, new[] { first, copy }));
                Assert.That(database.Resources.Count, Is.Zero);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(databasePath).OfType<ResourceDefinition>(), Is.Empty);
            }
            finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(copy); }
        }

        [Test]
        public void SnapshotResolvesEmbeddedRecordByStableIdAfterDatabaseAssetImport()
        {
            var entry = ResourceDatabaseEditorUtility.AddResource(database, "Energy");
            var data = new SerializedObject(entry); data.FindProperty("decimalPlaces").intValue = 2; data.ApplyModifiedPropertiesWithoutUndo();
            ResourceContainerState state;
            using (var original = new ResourceStore(new ResourceCost(new ResourceAmount(entry, 125)))) state = original.CaptureState();
            var id = entry.Id;
            AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(databasePath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<ResourceDatabase>(databasePath);
            Assert.That(reloaded.TryGet(id, out var record), Is.True);
            using var restored = new ResourceStore();
            restored.ValidateState(state, reloaded);
            restored.RestoreState(state, reloaded);
            Assert.That(restored.GetTotal(record), Is.EqualTo(125));
            Assert.That(record.DecimalPlaces, Is.EqualTo(2));
        }
    }
}
