using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using EasyLocalization.Integrations;

namespace EasyLocalization.IntegrationTests
{
    public sealed class ResourceNamesTests
    {
        private sealed class Locales : ILocalesProvider
        {
            private readonly List<Locale> values = new List<Locale>();
            public List<Locale> LocalesList => values;
            List<Locale> ILocalesProvider.Locales => values;
            public Locale GetLocale(LocaleIdentifier id) => values.Find(l => l.Identifier == id);
            public void AddLocale(Locale l) => values.Add(l);
            public bool RemoveLocale(Locale l) => values.Remove(l);
        }
        private sealed class Tables : ITableProvider
        {
            public readonly Dictionary<string, StringTable> Values = new Dictionary<string, StringTable>();
            public AsyncOperationHandle<T> ProvideTableAsync<T>(string name, Locale locale) where T : LocalizationTable =>
                name == "Adapter Test" ? Addressables.ResourceManager.CreateCompletedOperation(Values[locale.Identifier.Code] as T, (string)null) : default;
        }
        private readonly List<UnityEngine.Object> objects = new List<UnityEngine.Object>();
        private LocalizationSettings previous, settings;
        private LocalizationConfig previousConfig;
        private bool previousReady;
        private Tables tables;
        private T Own<T>(T value) where T : UnityEngine.Object { objects.Add(value); return value; }

        [UnitySetUp] public IEnumerator Setup()
        {
            previous = LocalizationSettings.GetInstanceDontCreateDefault(); previousReady = previous && Localization.IsReady; previousConfig = previousReady ? Localization.Configuration : null;
            Localization.Reset();
            settings = Own(ScriptableObject.CreateInstance<LocalizationSettings>());
            var locales = new Locales(); tables = new Tables();
            var shared = Own(ScriptableObject.CreateInstance<SharedTableData>());
            shared.TableCollectionName = "Adapter Test"; shared.AddKey("name", 123);
            foreach (string code in new[] { "en", "ru" })
            {
                var locale = Own(Locale.CreateLocale(code)); locales.AddLocale(locale);
                var table = Own(ScriptableObject.CreateInstance<StringTable>());
                table.SharedData = shared; table.LocaleIdentifier = code;
                table.AddEntry(123, code == "en" ? "Translated name" : "Переведённое имя");
                tables.Values.Add(code, table);
            }
            settings.SetAvailableLocales(locales);
            settings.SetStringDatabase(new LocalizedStringDatabase { TableProvider = tables });
            settings.GetStartupLocaleSelectors().Clear();
            settings.GetStartupLocaleSelectors().Add(new SpecificLocaleSelector { LocaleId = "en" });
            LocalizationSettings.Instance = settings;
            var init = Localization.ReinitializeAsync(); yield return Wait(() => init.IsCompleted);
            Assert.That(init.IsCompletedSuccessfully, Is.True, init.Exception?.ToString());
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            // Destroy components first so no enabled view outlives the temporary settings.
            for(int i = objects.Count - 1; i >= 0; i--) if(objects[i] is GameObject go) UnityEngine.Object.DestroyImmediate(go);
            Localization.Reset(); LocalizationSettings.Instance = previous;
            try
            {
                if(previousReady) { var restored = Localization.ReinitializeAsync(previousConfig); yield return Wait(() => restored.IsCompleted); Assert.That(restored.IsCompletedSuccessfully, Is.True, restored.Exception?.ToString()); }
            }
            finally
            {
                if(settings) settings.ResetState();
                foreach(var value in objects) if(value) UnityEngine.Object.DestroyImmediate(value);
                objects.Clear();
            }
        }

        private static IEnumerator Wait(Func<bool> done)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while(!done() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(done(), Is.True, "Adapter did not deliver the expected event-driven text.");
        }

        [UnityTest] public IEnumerator NamesLanguageDataFallbackAndSubscriptionLifecycle()
        {
            var definition = Own(ScriptableObject.CreateInstance<EasyResources.ResourceDefinition>()); definition.name = "Fallback";
            string key = "resource." + definition.Id;
            var catalog = Own(ScriptableObject.CreateInstance<LocalizedCatalog>());
            catalog.SetEntries(new[] { new CatalogTranslation { id = key, text = new LocalizedString("Adapter Test", 123) } });
            var go = Own(new GameObject("Independent adapter")); go.SetActive(false);
            var adapter = go.AddComponent<ResourceNames>(); var cache = go.GetComponent<LocalizedCatalogCache>();
            Assert.That(cache, Is.Not.Null, "Required cache should be added automatically.");
            cache.catalog = catalog; go.SetActive(true);
            yield return Wait(() => adapter.GetName(definition) == "Translated name");
            int changes = 0; adapter.Changed += () => changes++;
            definition.name = "Fallback renamed";
            Assert.That(adapter.GetName(definition), Is.EqualTo("Translated name"), "Renaming a definition must retain the stable translation key.");
            var language = Localization.SetLanguageAsync("ru"); yield return Wait(() => language.IsCompleted);
            Assert.That(language.IsCompletedSuccessfully, Is.True, language.Exception?.ToString());
            yield return Wait(() => adapter.GetName(definition) == "Переведённое имя");
            tables.Values["ru"].GetEntry(123).Value = "Updated data";
            catalog.NotifyChanged(); yield return Wait(() => adapter.GetName(definition) == "Updated data");
            Assert.That(changes, Is.EqualTo(2));
            adapter.enabled = false; int disabled = changes;
            catalog.NotifyChanged(); yield return null;
            Assert.That(changes, Is.EqualTo(disabled));
            adapter.enabled = true; Assert.That(changes, Is.EqualTo(disabled + 1));
            cache.Catalog = null;
            Assert.That(adapter.GetName(definition), Is.EqualTo("Fallback renamed"));
            Assert.That(adapter.GetName(null), Is.Empty);
            int detached = changes; catalog.NotifyChanged(); yield return null;
            Assert.That(changes, Is.EqualTo(detached), "Old catalog must be detached.");
        }
    }
}
