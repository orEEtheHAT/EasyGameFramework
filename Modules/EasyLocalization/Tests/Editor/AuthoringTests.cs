using System;
using System.Linq;
using EasyLocalization.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TestTools;

namespace EasyLocalization.Tests
{
    public sealed class AuthoringTests
    {
        private string folder, collectionName;
        private LocalizationSettings previousActive, previousInstance, isolated;
        private Locale isolatedLocale;
        private Locale[] previousLocales;
        private SheetMapping mapping;
        private LocalizedCatalog catalog;

        [SetUp] public void Setup()
        {
            folder = "Assets/PackageTests/EasyLocalization/Authoring/" + Guid.NewGuid().ToString("N");
            collectionName = "Authoring " + Guid.NewGuid().ToString("N");
            previousActive = LocalizationEditorSettings.ActiveLocalizationSettings;
            previousInstance = LocalizationSettings.HasSettings ? LocalizationSettings.Instance : null;
            previousLocales = LocalizationEditorSettings.GetLocales().ToArray();
            isolated = ScriptableObject.CreateInstance<LocalizationSettings>();
            isolatedLocale = Locale.CreateLocale("fr");
            LocalizationSettings.Instance = isolated; LocalizationSettings.ProjectLocale = isolatedLocale;
            LocalizationEditorSettings.ActiveLocalizationSettings = null;
        }

        [TearDown] public void Cleanup()
        {
            LocalizationEditorSettings.ActiveLocalizationSettings = previousActive;
            LocalizationSettings.Instance = previousInstance;
            foreach(var locale in LocalizationEditorSettings.GetLocales().ToArray())
                if (!previousLocales.Contains(locale) && AssetDatabase.GetAssetPath(locale).StartsWith(folder + "/", StringComparison.Ordinal))
                    LocalizationEditorSettings.RemoveLocale(locale);
            if (catalog) { Undo.ClearUndo(catalog); UnityEngine.Object.DestroyImmediate(catalog); }
            if (isolated) { Undo.ClearUndo(isolated); UnityEngine.Object.DestroyImmediate(isolated); }
            if (isolatedLocale) { Undo.ClearUndo(isolatedLocale); UnityEngine.Object.DestroyImmediate(isolatedLocale); }
            if (AssetDatabase.IsValidFolder(folder))
            {
                // Only this fixture's UUID folder. Production setup/table Undo remains enabled.
                foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                        if (asset) Undo.ClearUndo(asset);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private void Create()
        {
            mapping = LocalizationSetup.Create(folder, "en", new[] { "ru" }, collectionName);
            mapping.collection.SharedData.AddKey("original.key", 1001);
            ((StringTable)mapping.collection.GetTable("en")).AddEntry(1001, "Original");
            ((StringTable)mapping.collection.GetTable("ru")).AddEntry(1001, "");
            EditorUtility.SetDirty(mapping.collection.SharedData);
            EditorUtility.SetDirty(mapping.collection.GetTable("en")); EditorUtility.SetDirty(mapping.collection.GetTable("ru"));
            AssetDatabase.SaveAssets();
        }

        [Test] public void FirstSetupCreatesMissingSettingsAndLocalesWithoutChangingCachedOtherSettings()
        {
            Create();
            var created = LocalizationEditorSettings.ActiveLocalizationSettings;
            Assert.That(created, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(created), Does.StartWith(folder + "/"));
            Assert.That(LocalizationEditorSettings.GetLocale("en"), Is.Not.Null);
            Assert.That(LocalizationEditorSettings.GetLocale("ru"), Is.Not.Null);
            Assert.That(LocalizationSettings.Instance, Is.SameAs(isolated));
            Assert.That(LocalizationSettings.ProjectLocale, Is.SameAs(isolatedLocale));
            try { LocalizationSettings.Instance = created; Assert.That(LocalizationSettings.ProjectLocale.Identifier.Code, Is.EqualTo("en")); }
            finally { LocalizationSettings.Instance = isolated; }
            Assert.That(mapping.connection, Is.EqualTo(SheetConnection.PublishedCsv));
        }

        [Test] public void RepeatedFirstSetupPreservesSourceTablesIdsValuesAndUserMapping()
        {
            Create();
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            string snapshot = JsonUtility.ToJson(mapping.collection.SharedData);
            string fingerprint = UnityTableStore.Read(mapping).Fingerprint();
            mapping.connection = SheetConnection.GoogleOAuth; mapping.spreadsheetId = "user-mapping-preserved";
            var again = LocalizationSetup.Create(folder, "en", new[] { "ru" }, collectionName);
            Assert.That(again, Is.SameAs(mapping));
            Assert.That(LocalizationEditorSettings.ActiveLocalizationSettings, Is.SameAs(settings));
            Assert.That(JsonUtility.ToJson(mapping.collection.SharedData), Is.EqualTo(snapshot));
            Assert.That(UnityTableStore.Read(mapping).Fingerprint(), Is.EqualTo(fingerprint));
            Assert.That(again.spreadsheetId, Is.EqualTo("user-mapping-preserved"));
            Assert.That(again.connection, Is.EqualTo(SheetConnection.GoogleOAuth));
        }

        [Test] public void StableReferenceRemainsValidAfterKeyRenameAndEmptyTranslationIsIntentional()
        {
            Create();
            var reference = new LocalizedString(mapping.collection.SharedData.TableCollectionNameGuid, 1001);
            mapping.collection.SharedData.RenameKey(1001, "renamed.key");
            EditorUtility.SetDirty(mapping.collection.SharedData); AssetDatabase.SaveAssets();
            string before = UnityTableStore.Read(mapping).Fingerprint();
            Assert.That(LocalizationDiagnostics.Inspect(reference), Is.Empty);
            Assert.That(UnityTableStore.Read(mapping).Fingerprint(), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(mapping.collection.SharedData), Is.False);
        }

        [Test] public void DiagnosticsIdentifyMissingTableKeyAndTranslationWithoutChangingData()
        {
            Create();
            var missingTable = new LocalizedString(Guid.NewGuid(), 1001);
            var wrongAsset = new LocalizedString(new Guid(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mapping))), 1001);
            var missingName = new LocalizedString("Missing table " + Guid.NewGuid().ToString("N"), 1001);
            var missingKey = new LocalizedString(mapping.collection.SharedData.TableCollectionNameGuid, 9001);
            Assert.That(LocalizationDiagnostics.Inspect(missingTable).Any(i => i.Code == "table.missing"), Is.True);
            Assert.That(LocalizationDiagnostics.Inspect(wrongAsset).Any(i => i.Code == "table.missing"), Is.True);
            Assert.That(LocalizationDiagnostics.Inspect(missingName).Any(i => i.Code == "table.missing"), Is.True);
            Assert.That(LocalizationDiagnostics.Inspect(missingKey).Any(i => i.Code == "key.missing"), Is.True);
            ((StringTable)mapping.collection.GetTable("ru")).RemoveEntry(1001);
            var reference = new LocalizedString(mapping.collection.SharedData.TableCollectionNameGuid, 1001);
            var issues = LocalizationDiagnostics.Inspect(reference);
            Assert.That(issues.Count(i => i.Code == "translation.missing"), Is.EqualTo(1));
            Assert.That(((StringTable)mapping.collection.GetTable("ru")).GetEntry(1001), Is.Null);
            LogAssert.NoUnexpectedReceived();
        }

        [Test] public void CatalogDiagnosticsIdentifyDuplicateAndUnassignedStableIds()
        {
            catalog = ScriptableObject.CreateInstance<LocalizedCatalog>();
            catalog.entries = new[] { new CatalogTranslation { id = "resource.stable" }, new CatalogTranslation { id = "resource.stable" }, new CatalogTranslation() };
            var issues = LocalizationDiagnostics.Inspect(catalog);
            Assert.That(issues.Count(i => i.Code == "catalog.id.duplicate"), Is.EqualTo(1));
            Assert.That(issues.Count(i => i.Code == "catalog.id.empty"), Is.EqualTo(1));
            Assert.That(issues.Count(i => i.Code == "reference.empty"), Is.EqualTo(3));
        }
    }
}
