using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;
using EasyInput.Integrations;
using Object = UnityEngine.Object;

namespace EasyInput.IntegrationTests
{
    public sealed class LocalizationAdapterTests
    {
        private EnvironmentSnapshot previous;
        private sealed class EnvironmentSnapshot
        {
            public LocalizationSettings Settings;
            private EasyLocalization.LocalizationConfig configuration;
            private bool wasReady;
            public static EnvironmentSnapshot Capture()
            {
                var settings=LocalizationSettings.GetInstanceDontCreateDefault();
                bool ready=settings && EasyLocalization.Localization.IsReady;
                return new EnvironmentSnapshot { Settings=settings, wasReady=ready,
                    configuration=ready ? EasyLocalization.Localization.Configuration : null };
            }
            public Task Restore()
            {
                EasyLocalization.Localization.Reset();
                LocalizationSettings.Instance=Settings;
                // Never access Locales/GetInitializationOperation merely to decide whether to restore.
                // A cold or absent native environment must stay cold; only a previously ready service resumes.
                return wasReady ? EasyLocalization.Localization.ReinitializeAsync(configuration) : Task.CompletedTask;
            }
        }
        private sealed class ColdLocales : ILocalesProvider
        {
            public int Reads;
            public List<Locale> Locales { get { Reads++; throw new InvalidOperationException("Cold locale provider must not be loaded by fixture restoration."); } }
            public Locale GetLocale(LocaleIdentifier id) { Reads++; throw new InvalidOperationException("Cold locale lookup"); }
            public void AddLocale(Locale locale) => throw new NotSupportedException();
            public bool RemoveLocale(Locale locale) => throw new NotSupportedException();
        }
        private readonly List<Object> created = new List<Object>();
        private GameObject root;
        [SetUp] public void Setup()
        {
            previous = EnvironmentSnapshot.Capture();
            EasyLocalization.Localization.Reset();
        }
        [UnityTearDown] public IEnumerator Teardown()
        {
            try
            {
                // Views detach while their temporary localization environment is still current.
                if (root) Object.DestroyImmediate(root);
                yield return Wait(previous.Restore());
            }
            finally
            {
                // Restoration failures must not leak fixture assets. Never destroy the captured environment.
                foreach (var asset in created) if (asset) Object.DestroyImmediate(asset);
                created.Clear(); root = null;
            }
        }
        private sealed class TestLocales : ILocalesProvider
        {
            public List<Locale> Locales { get; } = new List<Locale>();
            public Locale GetLocale(LocaleIdentifier id) => Locales.FirstOrDefault(x => x.Identifier == id);
            public void AddLocale(Locale locale) => Locales.Add(locale);
            public bool RemoveLocale(Locale locale) => Locales.Remove(locale);
        }
        private sealed class TestTables : ITableProvider
        {
            public readonly Dictionary<string, StringTable> Values = new Dictionary<string, StringTable>();
            public AsyncOperationHandle<T> ProvideTableAsync<T>(string collection, Locale locale) where T : LocalizationTable =>
                Values.TryGetValue(locale.Identifier.Code, out var value) ? Addressables.ResourceManager.CreateCompletedOperation(value as T, (string)null) : default;
        }
        private static IEnumerator Wait(Task task)
        {
            float end = Time.realtimeSinceStartup + 15;
            while (!task.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(task.IsCompleted, Is.True); if (task.IsFaulted) throw task.Exception.GetBaseException(); Assert.That(task.IsCanceled, Is.False);
        }
        [UnityTest] public IEnumerator FixtureRestorePreservesColdNativeSettingsWithoutLoadingLocales()
        {
            var cold=ScriptableObject.CreateInstance<LocalizationSettings>(); created.Add(cold);
            var provider=new ColdLocales(); cold.SetAvailableLocales(provider);
            LocalizationSettings.Instance=cold;
            var snapshot=EnvironmentSnapshot.Capture();
            var temporary=ScriptableObject.CreateInstance<LocalizationSettings>(); created.Add(temporary);
            LocalizationSettings.Instance=temporary;
            yield return Wait(snapshot.Restore());
            Assert.That(LocalizationSettings.GetInstanceDontCreateDefault(),Is.SameAs(cold));
            Assert.That(provider.Reads,Is.Zero); Assert.That(EasyLocalization.Localization.IsReady,Is.False);
        }
        [UnityTest] public IEnumerator FixtureRestoreDoesNotCreateSettingsForAnUninitializedEnvironment()
        {
            LocalizationSettings.Instance = null;
            // In a clean project this is null. If a project config exists, retain its identity without initializing it.
            var original = LocalizationSettings.GetInstanceDontCreateDefault();
            var snapshot = EnvironmentSnapshot.Capture();
            var fixture = CreateFixtureSettings(); LocalizationSettings.Instance = fixture.settings;
            yield return Wait(EasyLocalization.Localization.ReinitializeAsync(fixture.config));
            Assert.That(EasyLocalization.Localization.IsReady, Is.True);
            yield return Wait(snapshot.Restore());
            Assert.That(LocalizationSettings.GetInstanceDontCreateDefault(), Is.SameAs(original));
            Assert.That(EasyLocalization.Localization.IsReady, Is.False);
        }
        [UnityTest] public IEnumerator FixtureRestoreResumesPreviouslyReadySettingsAndConfiguration()
        {
            var fixture = CreateFixtureSettings(); LocalizationSettings.Instance = fixture.settings;
            yield return Wait(EasyLocalization.Localization.ReinitializeAsync(fixture.config));
            yield return Wait(EasyLocalization.Localization.SetLanguageAsync("ru"));
            Assert.That(EasyLocalization.Localization.IsReady, Is.True);
            var snapshot = EnvironmentSnapshot.Capture();
            EasyLocalization.Localization.Reset();
            var temporary = ScriptableObject.CreateInstance<LocalizationSettings>(); created.Add(temporary);
            var cold = new ColdLocales(); temporary.SetAvailableLocales(cold); LocalizationSettings.Instance = temporary;
            yield return Wait(snapshot.Restore());
            Assert.That(LocalizationSettings.GetInstanceDontCreateDefault(), Is.SameAs(fixture.settings));
            Assert.That(EasyLocalization.Localization.IsReady, Is.True);
            Assert.That(EasyLocalization.Localization.Configuration, Is.SameAs(fixture.config));
            Assert.That(EasyLocalization.Localization.LanguageCode, Is.EqualTo("ru"));
            Assert.That(cold.Reads, Is.Zero);
        }
        private (LocalizationSettings settings, EasyLocalization.LocalizationConfig config) CreateFixtureSettings()
        {
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>(); created.Add(settings);
            var locales = new TestLocales(); var tables = new TestTables();
            foreach (string code in new[] { "en", "ru" })
            {
                var locale = Locale.CreateLocale(code); created.Add(locale); locales.AddLocale(locale);
                var shared = ScriptableObject.CreateInstance<SharedTableData>(); created.Add(shared); shared.TableCollectionName = "EasyInput Test"; shared.AddKey("use", 1001);
                var table = ScriptableObject.CreateInstance<StringTable>(); created.Add(table); table.SharedData = shared; table.LocaleIdentifier = code;
                table.AddEntry(1001, code == "en" ? "Use [{button}]" : "Использовать [{button}]"); tables.Values.Add(code, table);
            }
            settings.SetAvailableLocales(locales); settings.SetStringDatabase(new LocalizedStringDatabase { TableProvider = tables });
            settings.GetStartupLocaleSelectors().Clear(); settings.GetStartupLocaleSelectors().Add(new SpecificLocaleSelector { LocaleId = "en" });
            var config = ScriptableObject.CreateInstance<EasyLocalization.LocalizationConfig>(); created.Add(config); config.fallbackLanguage = "en";
            return (settings, config);
        }
        [UnityTest] public IEnumerator NativeLocalizedStringTracksLanguageRebindAndDisable()
        {
                var fixture = CreateFixtureSettings(); LocalizationSettings.Instance = fixture.settings;
                yield return Wait(EasyLocalization.Localization.ReinitializeAsync(fixture.config)); yield return Wait(EasyLocalization.Localization.SetLanguageAsync("en"));
                root = new GameObject("Localized input"); root.SetActive(false); var player = root.AddComponent<PlayerInputSettings>(); player.ControlsCursor = false;
                var prompt = root.AddComponent<LocalizedInputPrompt>(); prompt.Player = player; prompt.Text = new LocalizedString("EasyInput Test", 1001);
                root.SetActive(true); yield return null; yield return null;
                Assert.That(prompt.CurrentText, Does.StartWith("Use ["));
                Assert.That(player.TrySetBinding(player.Action("Gameplay/Interact"), 0, "<Keyboard>/k", out _), Is.True); yield return null;
                Assert.That(prompt.CurrentText, Does.Contain("K").IgnoreCase);
                var glyphs = ScriptableObject.CreateInstance<InputGlyphSet>(); created.Add(glyphs); glyphs.Family = player.DeviceFamily;
                var texture = new Texture2D(2,2); created.Add(texture);
                var sprite = Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f); created.Add(sprite);
                glyphs.Glyphs = new[] { new InputGlyph { ControlPath="<Keyboard>/k",Text="K",Sprite=sprite } };
                var profile = ScriptableObject.CreateInstance<InputProfile>(); created.Add(profile); profile.GlyphSets = new[] {glyphs};
                Sprite notified = null; prompt.SpriteChanged.AddListener(value=>notified=value); player.SetProfile(profile);
                Assert.That(prompt.CurrentSprite, Is.SameAs(sprite)); Assert.That(notified, Is.SameAs(sprite));
                yield return Wait(EasyLocalization.Localization.SetLanguageAsync("ru")); yield return null;
                Assert.That(prompt.CurrentText, Does.StartWith("Использовать [")); Assert.That(prompt.CurrentText, Does.Contain("K").IgnoreCase);
                prompt.enabled = false; int changes = 0; prompt.TextChanged.AddListener(_ => ++changes); int icons=0; prompt.SpriteChanged.AddListener(_=>icons++);
                player.TrySetBinding(player.Action("Gameplay/Interact"), 0, "<Keyboard>/l", out _);
                yield return Wait(EasyLocalization.Localization.SetLanguageAsync("en")); yield return null; Assert.That(changes, Is.Zero); Assert.That(icons, Is.Zero);
        }
    }
}
