using System;
using System.Threading;
using EasyLocalization;
using EasyUI.Integrations.Input;
using EasyUI.Integrations.Feedbacks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LocalizationApi = EasyLocalization.Localization;

namespace EasyUI.Integrations
{
    [RequireComponent(typeof(InputSettingsWindow)), AddComponentMenu("EasyUI/Integrations/Integrated Settings Window")]
    public sealed class IntegratedSettingsWindow : MonoBehaviour, IInputSettingsTextProvider, EasySettings.UI.ISettingsTextProvider
    {
        public LocalizedCatalogCache Cache;
        public WindowDefinition AudioWindow;
        public WindowDefinition GraphicsWindow;
        public EasySettings.SettingsController GraphicsController;
        public bool CanChangeLanguage { get; private set; }
        public string LanguageStatus { get; private set; }
        public event Action Changed;
        private LocalizedCatalogCache subscribed;
        private InputSettingsWindow controls;
        private Button language, audioButton, graphicsButton;
        private RectTransform extension;
        private CancellationTokenSource lifetime;
        private bool loading;
        private string failure;
        private readonly PreparedChildWindow audioChild = new PreparedChildWindow();
        private readonly PreparedChildWindow graphicsChild = new PreparedChildWindow();
        string EasySettings.UI.ISettingsTextProvider.GetText(string key, string fallback) => Cache ? Cache.Get(key, fallback) : fallback;
        public void BindGraphics(EasySettings.SettingsController controller) { GraphicsController = controller; Refresh(); }
        public string GetText(string key, string fallback)
        {
            // InputSettingsWindow remains the sole writer of its status TMP_Text.
            if (key == "status.help" && !string.IsNullOrEmpty(LanguageStatus)) return LanguageStatus;
            return Cache ? Cache.Get("ui.settings." + key, fallback) : fallback;
        }
        private void OnEnable()
        {
            if (!GraphicsController) GraphicsController = GetComponentInParent<EasySettings.SettingsController>();
            lifetime = new CancellationTokenSource(); controls = GetComponent<InputSettingsWindow>();
            failure = null;
            LocalizationApi.Changed += Refresh;
            controls.ViewBuilt += Build; controls.TextProvider = this;
            Bind(Cache); if (controls.ExtrasRoot) Build(controls.ExtrasRoot);
        }
        private void OnDisable()
        {
            lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
            LocalizationApi.Changed -= Refresh;
            if (controls) controls.ViewBuilt -= Build;
            if (subscribed) subscribed.Changed -= Refresh;
            subscribed = null; loading = false;
            DisposeChildren();
        }
        private void OnDestroy() => DisposeChildren();
        public void Bind(LocalizedCatalogCache cache)
        {
            if (subscribed) subscribed.Changed -= Refresh;
            Cache = cache; subscribed = isActiveAndEnabled ? Cache : null;
            if (subscribed) subscribed.Changed += Refresh;
            Refresh();
        }
        private void Build(RectTransform root)
        {
            if (extension == root && language) return;
            extension = root;
            language = SettingsView.Button(root, "Language", new Vector2(-205, 0), new Vector2(200, 30), NextLanguage);
            var languageText = language.GetComponentInChildren<TMP_Text>(); languageText.enableAutoSizing = true;
            languageText.fontSizeMin = 12;
            languageText.fontSizeMax = 18;
            audioButton = SettingsView.Button(root, "Audio", new Vector2(0, 0), new Vector2(200, 30), OpenAudio);
            graphicsButton = SettingsView.Button(root, "Graphics", new Vector2(205, 0), new Vector2(200, 30), OpenGraphics);
            Refresh(); GetComponent<UIWindow>().RefreshNavigation();
        }
        public void Refresh()
        {
            // Localization is configured and initialized explicitly by the game bootstrap.
            // Reading LanguageCode/Languages before IsReady would create/access an invalid session.
            bool ready = LocalizationApi.IsReady;
            CanChangeLanguage = ready && !loading && LocalizationApi.Languages.Count > 1;
            LanguageStatus = !ready
                ? GetText("language.unavailable", "Language unavailable. Configure and initialize localization to enable it.")
                : failure;
            if (language)
            {
                string caption;
                if (!ready) caption = GetText("language.unavailableButton", "Language unavailable");
                else if (failure != null) caption = GetText("language.failed", "Language change failed");
                else
                {
                    string code = LocalizationApi.LanguageCode;
                    string name = code;
                    foreach (var locale in LocalizationApi.Languages) if (locale.Identifier.Code == code) { name = locale.LocaleName; break; }
                    caption = GetText("language", "Language") + ": " + (Cache ? Cache.Get("language." + code, name) : name);
                }
                language.GetComponentInChildren<TMP_Text>().text = caption;
                language.interactable = CanChangeLanguage;
            }
            if (audioButton) { audioButton.GetComponentInChildren<TMP_Text>().text = GetText("audio.title", "Audio & vibration"); audioButton.interactable = AudioWindow; }
            if (graphicsButton) { graphicsButton.GetComponentInChildren<TMP_Text>().text = GetText("graphics.title", "Graphics & display"); graphicsButton.interactable = GraphicsWindow && GraphicsController; }
            Changed?.Invoke();
        }
        public async void NextLanguage()
        {
            if (loading || lifetime == null) return;
            if (!LocalizationApi.IsReady) { Refresh(); return; }
            var languages = LocalizationApi.Languages; if (languages.Count < 2) return;
            int current = -1; for (int i = 0; i < languages.Count; i++) if (languages[i].Identifier.Code == LocalizationApi.LanguageCode) current = i;
            var token = lifetime.Token; loading = true; failure = null; Refresh();
            try { await LocalizationApi.SetLanguageAsync(languages[(current + 1) % languages.Count].Identifier.Code, token); }
            catch (OperationCanceledException) { }
            catch (Exception e) { if (!token.IsCancellationRequested && this) failure = e.Message; }
            finally { if (!token.IsCancellationRequested && this) { loading = false; Refresh(); } }
        }
        public void OpenAudio()
        {
            var parent = GetComponent<UIWindow>(); var handle = parent.Handle;
            if (!AudioWindow || handle?.IsOpen != true) return;
            if (!AudioWindow.Prefab || !AudioWindow.Prefab.GetComponent<FeedbackSettingsWindow>())
                throw new InvalidOperationException("Audio Window must have Feedback Settings Window on its prefab.");
            var child = audioChild.Prepare(AudioWindow, parent); if (!child) return;
            child.GetComponent<FeedbackSettingsWindow>().TextProvider = this;
            if (parent && parent.Handle == handle && handle.IsOpen) handle.Manager.Open(child, handle);
        }
        public void OpenGraphics()
        {
            var parent = GetComponent<UIWindow>(); var handle = parent.Handle;
            if (!GraphicsWindow || !GraphicsController || handle?.IsOpen != true) return;
            if (!GraphicsWindow.Prefab || !GraphicsWindow.Prefab.GetComponent<EasySettings.UI.SettingsWindow>())
                throw new InvalidOperationException("Graphics Window must have EasySettings Settings Window on its prefab.");
            var child = graphicsChild.Prepare(GraphicsWindow, parent); if (!child) return;
            var view = child.GetComponent<EasySettings.UI.SettingsWindow>();
            view.TextProvider = this; view.Bind(GraphicsController);
            if (parent && parent.Handle == handle && handle.IsOpen) handle.Manager.Open(child, handle);
        }
        private void DisposeChildren() { audioChild.Dispose(); graphicsChild.Dispose(); }
    }
}
