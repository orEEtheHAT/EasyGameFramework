using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyBuilding;
using EasyBuilding.Integrations;
using EasyInput;
using EasyLocalization;
using EasyResources;
using EasyUI.Integrations;
using EasyUI.Integrations.Building;
using EasyUI.Integrations.Input;
using EasyUI.Integrations.Resources;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using LocalizationApi = EasyLocalization.Localization;

namespace EasyUI.IntegrationWave1.Tests
{
    public sealed class LocalizedIntegrationTests
    {
        private sealed class TestLocales : ILocalesProvider
        {
            public List<Locale> Locales { get; } = new List<Locale>();
            public Locale GetLocale(LocaleIdentifier id) => Locales.FirstOrDefault(x => x.Identifier == id);
            public void AddLocale(Locale value) => Locales.Add(value);
            public bool RemoveLocale(Locale value) => Locales.Remove(value);
        }
        private sealed class Tables : ITableProvider
        {
            internal readonly Dictionary<string, StringTable> Values = new Dictionary<string, StringTable>();
            public AsyncOperationHandle<T> ProvideTableAsync<T>(string name, Locale locale) where T : LocalizationTable =>
                Values.TryGetValue(locale.Identifier.Code, out var table) ? Addressables.ResourceManager.CreateCompletedOperation(table as T, (string)null) : default;
        }
        private readonly List<Object> created = new List<Object>();
        private LocalizationSettings previous;
        private LocalizationConfig previousConfig;
        private bool previousReady;
        private LocalizedCatalogCache cache;
        private ResourceDefinition wood;
        private ResourceContainer wallet;
        private WindowManager manager;
        private T Asset<T>() where T : ScriptableObject { var v = ScriptableObject.CreateInstance<T>(); created.Add(v); return v; }
        private GameObject Go(string name, bool active = true, params System.Type[] types)
        { var go = new GameObject(name, types); go.SetActive(active); created.Add(go); return go; }
        private static IEnumerator Wait(Task task)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "Localization operation timeout."); if (task.IsFaulted) throw task.Exception.GetBaseException(); Assert.IsFalse(task.IsCanceled);
        }
        [UnitySetUp] public IEnumerator Setup()
        {
            previous = LocalizationSettings.GetInstanceDontCreateDefault();
            previousReady = previous && LocalizationApi.IsReady;
            previousConfig = previousReady ? LocalizationApi.Configuration : null;
            LocalizationApi.Reset();
            var settings = Asset<LocalizationSettings>(); var locales = new TestLocales(); var tables = new Tables(); var shared = Asset<SharedTableData>(); shared.TableCollectionName = "EasyUI Wave";
            shared.AddKey("wood", 8101); shared.AddKey("controls", 8102); shared.AddKey("wall", 8103);
            foreach (var code in new[] { "en", "ru" })
            {
                var locale = Locale.CreateLocale(code); created.Add(locale); locales.AddLocale(locale);
                var table = Asset<StringTable>(); table.SharedData = shared; table.LocaleIdentifier = code;
                table.AddEntry(8101, code == "en" ? "Wood" : "Дерево"); table.AddEntry(8102, code == "en" ? "Controls" : "Управление"); table.AddEntry(8103, code == "en" ? "Wall" : "Стена"); tables.Values.Add(code, table);
            }
            settings.SetAvailableLocales(locales); settings.SetStringDatabase(new LocalizedStringDatabase { TableProvider = tables });
            LocalizationSettings.Instance = settings; LocalizationSettings.StartupLocaleSelectors.Clear(); LocalizationSettings.StartupLocaleSelectors.Add(new SpecificLocaleSelector { LocaleId = "en" });
            var config = Asset<LocalizationConfig>(); config.fallbackLanguage = "en"; yield return Wait(LocalizationApi.ReinitializeAsync(config));
            wood = ResourceDefinition.Create("Wood", "ui-wave-wood"); created.Add(wood); wallet = Go("Wallet").AddComponent<ResourceContainer>(); wallet.Add(wood, 10);
            var catalog = Asset<LocalizedCatalog>(); catalog.SetEntries(new[] {
                new CatalogTranslation { id = "resource." + wood.Id, text = new LocalizedString("EasyUI Wave", 8101) },
                new CatalogTranslation { id = "ui.settings.title", text = new LocalizedString("EasyUI Wave", 8102) } });
            var cacheGo = Go("Cache", false); cache = cacheGo.AddComponent<LocalizedCatalogCache>(); cache.Catalog = catalog; cacheGo.SetActive(true); yield return Wait(cache.RefreshAsync());
            manager = Go("UI", true, typeof(RectTransform), typeof(WindowManager)).GetComponent<WindowManager>(); manager.EventSystem = Go("Events", true, typeof(EventSystem)).GetComponent<EventSystem>();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            try
            {
                if (manager) manager.CloseAll();
                for (int i = created.Count - 1; i >= 0; i--) if (created[i] is GameObject go && go) Object.DestroyImmediate(go);
                LocalizationApi.Reset(); LocalizationSettings.Instance = previous;
                if (previousReady) yield return Wait(LocalizationApi.ReinitializeAsync(previousConfig));
            }
            finally
            {
                for (int i = created.Count - 1; i >= 0; i--) if (created[i]) Object.DestroyImmediate(created[i]); created.Clear();
                manager = null; cache = null; wallet = null; wood = null;
            }
            Assert.AreSame(previous, LocalizationSettings.GetInstanceDontCreateDefault());
            Assert.AreEqual(previousReady, LocalizationApi.IsReady);
        }
        private UIWindow Window(string name)
        { var go = Go(name, false, typeof(RectTransform)); var w = go.AddComponent<UIWindow>(); w.Policy = new WindowPolicy { FadeIn = 0, FadeOut = 0 }; return w; }
        [UnityTest] public IEnumerator ResourceAndCatalogDataUseOneTextWriterAcrossLanguageAndReservations()
        {
            var labelGo = Go("Balance", false, typeof(RectTransform), typeof(Text)); var source = labelGo.AddComponent<LocalizedResourceLabelProvider>(); source.Cache = cache;
            var balance = labelGo.AddComponent<ResourceBalanceLabel>(); balance.Container = wallet; balance.Resource = wood; balance.Presentation = source; labelGo.SetActive(true);
            var world = Go("World", false); world.AddComponent<BuildingSystem>(); var placer = world.AddComponent<BuildingPlacer>(); placer.ExternalTargeting = true; placer.ShowHelp = false; world.SetActive(true);
            var economy = world.AddComponent<BuildingResources>(); economy.Resources = new[] { wood }; economy.Containers = new[] { wallet };
            var definition = Asset<BuildingDefinition>(); definition.name = "Wall"; definition.Prefab = Go("Wall prefab", false); definition.Payment = BuildingPayment.Paid; definition.Costs = new[] { new BuildingCost(wood.Id, 3) };
            definition.CheckObstacles = false; definition.RequireSurface = false;
            cache.Catalog.SetEntries(cache.Catalog.entries.Concat(new[] { new CatalogTranslation { id = "building." + definition.Id, text = new LocalizedString("EasyUI Wave", 8103) } }).ToArray()); yield return Wait(cache.RefreshAsync());
            var catalog = Asset<BuildingCatalog>(); catalog.Buildings = new[] { definition };
            var window = Window("Building"); var presentation = window.gameObject.AddComponent<BuildingCatalogPresentation>(); presentation.Bind(economy, cache);
            var view = window.gameObject.AddComponent<BuildingCatalogWindow>(); view.Presentation = presentation; view.Bind(placer, catalog); manager.Open(window); yield return null; yield return null;
            Assert.That(labelGo.GetComponent<Text>().text, Does.Contain("Wood").And.Contain("10"));
            Assert.IsTrue(wallet.TryReserve(new ResourceCost(new ResourceAmount(wood, 3)), out var reservation)); yield return Wait(LocalizationApi.SetLanguageAsync("ru")); yield return Wait(cache.RefreshAsync()); yield return null; yield return null;
            Assert.That(labelGo.GetComponent<Text>().text, Does.Contain("Дерево").And.Contain("7"));
            Assert.IsTrue(window.GetComponentsInChildren<Text>().Any(x => x.text.Contains("Стена") && x.text.Contains("Дерево") && x.text.Contains("7 / 3")));
            int refreshes = balance.RefreshCount; labelGo.SetActive(false); reservation.Cancel(); yield return Wait(LocalizationApi.SetLanguageAsync("en")); yield return Wait(cache.RefreshAsync()); yield return null;
            Assert.AreEqual(refreshes, balance.RefreshCount); labelGo.SetActive(true); yield return null; yield return null;
            Assert.That(labelGo.GetComponent<Text>().text, Does.Contain("Wood").And.Contain("10"));
        }
        [UnityTest] public IEnumerator OpenSettingsObserveExplicitLocalizationInitializationAndThenEnableLanguage()
        {
            var config = LocalizationApi.Configuration; // This fixture's service is already ready.
            var settings = LocalizationSettings.GetInstanceDontCreateDefault();
            LocalizationApi.Reset(); Assert.IsFalse(LocalizationApi.IsReady);
            var root = Go("Player", false); var player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false; root.SetActive(true);
            var provider = manager.gameObject.AddComponent<EasyInputWindowProvider>(); provider.Player = player; manager.InputProvider = provider;
            var window = Window("Cold localization controls"); window.Policy.BlockGameplay = true;
            window.gameObject.AddComponent<InputSettingsWindow>(); var combined = window.gameObject.AddComponent<IntegratedSettingsWindow>(); combined.Bind(cache);
            manager.Open(window); yield return null; yield return null;
            Assert.IsFalse(LocalizationApi.IsReady); Assert.IsFalse(combined.CanChangeLanguage); Assert.IsNotEmpty(combined.LanguageStatus);
            combined.NextLanguage(); yield return null;
            Assert.AreSame(settings, LocalizationSettings.GetInstanceDontCreateDefault()); Assert.IsFalse(LocalizationApi.IsReady);
            // Explicit bootstrap, not an implicit side effect of opening the window.
            yield return Wait(LocalizationApi.InitializeAsync(config)); yield return Wait(cache.RefreshAsync()); yield return null; yield return null;
            Assert.IsTrue(combined.CanChangeLanguage); Assert.IsNull(combined.LanguageStatus);
            var languageButton = window.GetComponentsInChildren<Button>().Single(b => b.name == "Language"); Assert.IsTrue(languageButton.interactable);
            combined.NextLanguage(); float deadline = Time.realtimeSinceStartup + 10;
            while (LocalizationApi.LanguageCode != "ru" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual("ru", LocalizationApi.LanguageCode);
        }
        [UnityTest] public IEnumerator CombinedControlsLanguageUsesExistingControlViewAndPublicLanguageApi()
        {
            var root = Go("Player", false); var player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false; root.SetActive(true);
            var provider = manager.gameObject.AddComponent<EasyInputWindowProvider>(); provider.Player = player; manager.InputProvider = provider;
            var window = Window("Settings"); window.Policy.BlockGameplay = true; var controls = window.gameObject.AddComponent<InputSettingsWindow>(); var combined = window.gameObject.AddComponent<IntegratedSettingsWindow>(); combined.Bind(cache);
            manager.Open(window); yield return null; yield return null; Assert.AreEqual(1, window.GetComponentsInChildren<ScrollRect>().Length); Assert.IsNotNull(controls.ExtrasRoot);
            combined.NextLanguage(); float deadline = Time.realtimeSinceStartup + 10; while (LocalizationApi.LanguageCode != "ru" && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual("ru", LocalizationApi.LanguageCode); yield return Wait(cache.RefreshAsync()); yield return null; yield return null;
            Assert.IsTrue(window.GetComponentsInChildren<Text>().Any(x => x.text == "Управление"));
            manager.CloseAll(); Assert.AreEqual(0, player.ContextRequestCount);
        }
    }
}
