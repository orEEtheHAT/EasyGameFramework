using System;
using System.Collections.Generic;
using EasyUI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace EasySettings.UI
{
    public interface ISettingsTextProvider { string GetText(string key, string fallback); event Action Changed; }

    [RequireComponent(typeof(UIWindow)), AddComponentMenu("EasyGameFramework/Settings/Graphics Settings Window")]
    public sealed class SettingsWindow : MonoBehaviour, IWindowBackHandler
    {
        public SettingsController Controller;
        [Tooltip("Optional windows owned by Input, Feedbacks or Localization. Opened as children through EasyUI; their state stays with those modules.")]
        public WindowDefinition[] AdditionalWindows = Array.Empty<WindowDefinition>();
        ISettingsTextProvider textProvider;
        public ISettingsTextProvider TextProvider {
            get => textProvider;
            set { if (textProvider != null) textProvider.Changed -= Refresh; textProvider = value; if (isActiveAndEnabled && textProvider != null) textProvider.Changed += Refresh; Refresh(); }
        }
        SettingsController subscribedController;
        SettingsSession session;
        UIWindow window, confirmation;
        CanvasGroup body;
        Text title, status, effective, confirmationText;
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Selectable> editors = new List<Selectable>();
        Button apply, cancel, reset, keep, revertButton;
        RectTransform content;
        int lastSecond = -1;
        public RectTransform ExtensionsRoot => content;
        string T(string key, string fallback) => textProvider?.GetText("settings." + key, fallback) ?? fallback;
        void OnEnable()
        {
            window = GetComponent<UIWindow>();
            if (textProvider != null) { textProvider.Changed -= Refresh; textProvider.Changed += Refresh; }
            if (!title) Build();
            Bind(Controller);
        }
        void OnDisable()
        {
            if (textProvider != null) textProvider.Changed -= Refresh;
            if (subscribedController) subscribedController.SessionChanged -= BindSession;
            subscribedController = null;
            if (session != null) session.Changed -= Refresh;
            var closingSession = session; session = null;
            if (closingSession != null && !closingSession.IsDisposed && !closingSession.IsFaulted) closingSession.RequestCancel();
            if (confirmation && confirmation.Handle != null) confirmation.Close();
        }
        void OnDestroy() { if (confirmation) Destroy(confirmation.gameObject); }
        public void Bind(SettingsController controller)
        {
            if (subscribedController) subscribedController.SessionChanged -= BindSession;
            Controller = controller; subscribedController = isActiveAndEnabled ? controller : null;
            if (subscribedController) subscribedController.SessionChanged += BindSession;
            BindSession();
        }
        void BindSession()
        {
            if (session != null) session.Changed -= Refresh;
            session = Controller ? Controller.Session : null;
            if (Controller) Controller.Refresh();
            if (session != null && isActiveAndEnabled) session.Changed += Refresh;
            Refresh();
        }
        void Update()
        {
            if (session == null || !session.HasPreview || !keep) return;
            int seconds = Mathf.CeilToInt(session.RemainingSeconds);
            if (seconds != lastSecond || keep.interactable != session.CanConfirm) { lastSecond = seconds; RefreshConfirmation(); }
        }
        public void Apply() { if (session != null) session.Apply(); }
        public void Cancel() { if (session != null) session.Cancel(); }
        public void Reset() { if (session != null) session.Reset(); }
        public bool HandleBack() { if (session == null) return false; bool pending = session.HasPreview; session.Cancel(); return pending; }
        void Edit(Func<SettingsState, SettingsState> change) { if (session != null && !session.HasPreview && !session.IsFaulted) session.Draft = change(session.Draft); }

        void Build()
        {
            var root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f); root.pivot = new Vector2(.5f, .5f); root.sizeDelta = new Vector2(920, 690);
            var background = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>(); background.color = new Color(.055f, .075f, .105f, .99f);
            title = Label(root, "Graphics & display", 28); Place(title.rectTransform, new Vector2(24, -18), new Vector2(870, 40));
            effective = Label(root, "", 14); effective.color = new Color(.55f, .8f, .85f); Place(effective.rectTransform, new Vector2(24, -64), new Vector2(870, 45));
            var scroll = Rect("Settings", root); Place(scroll, new Vector2(20, -118), new Vector2(880, 425));
            body = scroll.gameObject.AddComponent<CanvasGroup>();
            scroll.gameObject.AddComponent<Image>().color = new Color(.08f, .1f, .14f);
            scroll.gameObject.AddComponent<RectMask2D>();
            var scroller = scroll.gameObject.AddComponent<ScrollRect>(); scroller.horizontal = false; scroller.viewport = scroll;
            scroll.gameObject.AddComponent<WindowScrollIntoView>();
            var rail = Rect("Scroll bar", root); Place(rail, new Vector2(884, -118), new Vector2(12, 425)); rail.gameObject.AddComponent<Image>().color = new Color(.06f, .1f, .13f);
            var handle = Rect("Scroll handle", rail); handle.sizeDelta = Vector2.zero; var handleImage = handle.gameObject.AddComponent<Image>(); handleImage.color = new Color(.35f, .6f, .68f);
            var bar = rail.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = handle; bar.targetGraphic = handleImage; scroller.verticalScrollbar = bar;
            content = Rect("Content", scroll); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(14, 14, 12, 12); layout.spacing = 8; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroller.content = content;
            Section("screen", "DISPLAY & FRAME PACING");
            Cycle("mode", "Window mode", () => session.Draft.Display.Mode.ToString(), () => Edit(s => { s.Display.Mode = Next(session.Capabilities.Modes, s.Display.Mode); return s; }), () => session.Capabilities.Display);
            Cycle("resolution", "Resolution / refresh", () => $"{session.Draft.Display.Width} x {session.Draft.Display.Height} @ {session.Draft.Display.RefreshHz:0.##}", () => Edit(s => {
                var values = session.Capabilities.Resolutions; if (values.Count == 0) return s;
                int selected = -1; for (int i = 0; i < values.Count; i++) if (values[i].Width == s.Display.Width && values[i].Height == s.Display.Height && Math.Abs(values[i].RefreshHz - s.Display.RefreshHz) < .001) selected = i;
                var mode = s.Display.Mode; s.Display = values[(selected + 1) % values.Count]; s.Display.Mode = mode; return s;
            }), () => session.Capabilities.Display);
            Cycle("vsync", "VSync", () => session.Draft.VSyncCount == 0 ? T("off", "Off") : session.Draft.VSyncCount.ToString(), () => Edit(s => { s.VSyncCount = (s.VSyncCount + 1) % 3; return s; }), () => session.Capabilities.VSync);
            Cycle("fps", "FPS limit (VSync off)", () => session.Draft.FrameRateLimit < 1 ? T("unlimited", "Unlimited") : session.Draft.FrameRateLimit.ToString(), () => Edit(s => { s.FrameRateLimit = Next(new[] {-1, 30, 60, 90, 120, 144, 240}, s.FrameRateLimit); return s; }), () => session.Draft.VSyncCount == 0);
            Section("graphics", "URP GRAPHICS");
            Cycle("profile", "Quality profile", () => T("next_profile", "Next preset") + ": " + (Controller && Controller.Defaults && Controller.Defaults.Profiles?.Length > 0 ? Controller.Defaults.Profiles[(profileIndex + 1) % Controller.Defaults.Profiles.Length]?.Name : "—"), () => { if (Controller.Defaults.Profiles?.Length > 0) { profileIndex = (profileIndex + 1) % Controller.Defaults.Profiles.Length; var p = Controller.Defaults.Profiles[profileIndex]; if (p != null) session.SelectProfile(p); } }, () => Controller && Controller.Defaults && Controller.Defaults.Profiles?.Length > 0);
            Cycle("scale", "Render scale", () => session.Draft.RenderScale.ToString("0.00"), () => Edit(s => { s.RenderScale = Next(new[] {.5f, .67f, .75f, 1f, 1.25f, 1.5f, 2f}, s.RenderScale); return s; }));
            Cycle("msaa", "MSAA", () => session.Draft.MsaaSamples + "x", () => Edit(s => { s.MsaaSamples = Next(new[] {1, 2, 4, 8}, s.MsaaSamples); return s; }), () => session.Capabilities.Msaa);
            Cycle("shadows", "Shadow distance", () => session.Draft.ShadowDistance.ToString("0"), () => Edit(s => { s.ShadowDistance = Next(new[] {0f, 25f, 50f, 100f, 150f, 200f}, s.ShadowDistance); return s; }), () => session.Capabilities.Shadows);
            Section("camera", "CAMERA & POST PROCESSING");
            Cycle("fov", "Base vertical field of view", () => session.Draft.FieldOfView.ToString("0"), () => Edit(s => { s.FieldOfView = Next(new[] {40f, 50f, 60f, 70f, 80f, 90f, 100f}, s.FieldOfView); return s; }), () => session.Capabilities.Camera);
            Cycle("post", "Post processing", () => session.Draft.PostProcessing ? T("on", "On") : T("off", "Off"), () => Edit(s => { s.PostProcessing = !s.PostProcessing; return s; }), () => session.Capabilities.PostProcessing);
            Cycle("aa", "Camera anti-aliasing", () => session.Draft.CameraAntialiasing.ToString(), () => Edit(s => { s.CameraAntialiasing = Next(new[] {AntialiasingMode.None, AntialiasingMode.FastApproximateAntialiasing, AntialiasingMode.SubpixelMorphologicalAntiAliasing}, s.CameraAntialiasing); return s; }), () => session.Capabilities.PostProcessing && session.Draft.PostProcessing);
            Cycle("exposure", "Exposure (EV)", () => session.Draft.PostExposure.ToString("0.0"), () => Edit(s => { s.PostExposure = Next(new[] {-2f, -1f, 0f, 1f, 2f}, s.PostExposure); return s; }), () => session.Capabilities.Volume && session.Draft.PostProcessing);
            Cycle("bloom", "Bloom intensity", () => session.Draft.BloomIntensity.ToString("0.0"), () => Edit(s => { s.BloomIntensity = Next(new[] {0f, .25f, .5f, 1f, 2f}, s.BloomIntensity); return s; }), () => session.Capabilities.Volume && session.Draft.PostProcessing);
            var hints = Label(content, "", 13); hints.gameObject.AddComponent<LayoutElement>().preferredHeight = 220;
            refreshers.Add(() => hints.text = session == null ? "" :
                (!session.Capabilities.Display ? T("display_unavailable", session.Capabilities.DisplayReason) + "\n" : "") +
                (!session.Capabilities.Msaa ? T("msaa_unavailable", session.Capabilities.MsaaReason) + "\n" : "") +
                (!session.Capabilities.Camera ? T("camera_unavailable", session.Capabilities.CameraReason) + "\n" : "") +
                (!session.Capabilities.PostProcessing ? T("post_unavailable", session.Capabilities.PostProcessingReason) + "\n" : "") +
                (!session.Capabilities.Volume ? T("volume_unavailable", session.Capabilities.VolumeReason) : ""));
            foreach (var definition in AdditionalWindows ?? Array.Empty<WindowDefinition>()) if (definition) {
                var d = definition; var link = Button(content, d.name, () => { if (window.Handle?.IsOpen == true) window.Handle.Manager.Open(d, window.Handle); }); link.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            }
            status = Label(root, "", 14); Place(status.rectTransform, new Vector2(24, -555), new Vector2(870, 58));
            apply = Button(root, "Apply", Apply); Place((RectTransform)apply.transform, new Vector2(24, -630), new Vector2(210, 42));
            cancel = Button(root, "Cancel edits", Cancel); Place((RectTransform)cancel.transform, new Vector2(246, -630), new Vector2(210, 42));
            reset = Button(root, "Reset", Reset); Place((RectTransform)reset.transform, new Vector2(468, -630), new Vector2(210, 42));
            var close = Button(root, "Close", () => window.Close()); Place((RectTransform)close.transform, new Vector2(690, -630), new Vector2(210, 42));
            refreshers.Add(() => { apply.GetComponentInChildren<Text>().text = T("apply", "Apply"); cancel.GetComponentInChildren<Text>().text = T("cancel", "Cancel edits"); reset.GetComponentInChildren<Text>().text = T("reset", "Reset"); close.GetComponentInChildren<Text>().text = T("close", "Close"); });
            window.FirstSelected = apply;

        }
        int profileIndex = -1;
        void Section(string key, string fallback) { var text = Label(content, fallback, 16); text.color = new Color(.5f, .82f, .9f); text.gameObject.AddComponent<LayoutElement>().preferredHeight = 32; refreshers.Add(() => text.text = T(key, fallback)); }
        void Cycle(string key, string fallback, Func<string> value, Action action, Func<bool> available = null)
        {
            var row = Rect(key, content); row.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
            var name = Label(row, fallback, 16); Place(name.rectTransform, Vector2.zero, new Vector2(390, 38));
            var button = Button(row, "", action); Place((RectTransform)button.transform, new Vector2(395, 0), new Vector2(430, 38)); editors.Add(button);
            refreshers.Add(() => { name.text = T(key, fallback); button.GetComponentInChildren<Text>().text = session == null ? "—" : value() + "  ›"; button.interactable = session != null && !session.HasPreview && !session.IsFaulted && (available?.Invoke() ?? true); });
        }
        static TItem Next<TItem>(IReadOnlyList<TItem> values, TItem current) { for (int i = 0; i < values.Count; i++) if (EqualityComparer<TItem>.Default.Equals(values[i], current)) return values[(i + 1) % values.Count]; return values.Count == 0 ? current : values[0]; }
        public void Refresh()
        {
            if (!title) return;
            title.text = T("title", "Graphics & display");
            bool ready = session != null && !session.IsDisposed && !session.IsFaulted;
            effective.text = ready ? T("observed", "Observed screen") + ": " + session.Observed.Display + "\n" + T("render", "Applied render scale") + $": {session.Observed.RenderScale:0.00}  ·  MSAA: {session.Observed.MsaaSamples}x" : Controller ? Controller.Error : T("assign", "Assign Settings Controller to this window.");
            apply.interactable = cancel.interactable = reset.interactable = ready && !session.HasPreview;
            body.interactable = ready && !session.HasPreview;
            foreach (var refresh in refreshers) refresh();
            status.text = session?.LastResult == null ? T("hint", "Edit values, then Apply. Screen changes require confirmation. Presets keep your display and frame pacing choices.") : session.LastResult.Messages.Count == 0 ? T("applied", "Settings applied.") : string.Join("\n", session.LastResult.Messages);
            if (ready && session.HasPreview && window.Handle?.IsOpen == true) {
                EnsureConfirmation();
                if (confirmation.Handle?.IsOpen != true) window.Handle.Manager.Open(confirmation, window.Handle);
                RefreshConfirmation();
            } else if (confirmation && confirmation.Handle?.IsOpen == true) confirmation.Close();
        }
        void EnsureConfirmation()
        {
            if (confirmation) return;
            var root = Rect("Confirm display", transform); root.gameObject.SetActive(false); root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f); root.sizeDelta = new Vector2(680, 270);
            root.gameObject.AddComponent<Image>().color = new Color(.08f, .12f, .17f);
            confirmation = root.gameObject.AddComponent<UIWindow>(); confirmation.Policy.FadeIn = confirmation.Policy.FadeOut = 0;
            confirmation.Policy.BlockGameplay = window.Policy.BlockGameplay; confirmation.Policy.Cursor = window.Policy.Cursor; confirmation.Policy.BlockedActions = window.Policy.BlockedActions;
            confirmation.Closed += _ => { if (session != null && session.HasPreview && !session.IsDisposed && !session.IsFaulted) session.RequestCancel(); };
            confirmationText = Label(root, "", 20); Place(confirmationText.rectTransform, new Vector2(24, -20), new Vector2(632, 160));
            keep = Button(root, "Keep", () => session?.Confirm()); Place((RectTransform)keep.transform, new Vector2(24, -205), new Vector2(300, 42));
            revertButton = Button(root, "Revert", () => session?.Revert()); Place((RectTransform)revertButton.transform, new Vector2(350, -205), new Vector2(300, 42));
            confirmation.FirstSelected = revertButton;
        }
        void RefreshConfirmation()
        {
            if (!confirmationText || session == null) return;
            confirmationText.text = T("keep_question", "Keep this display configuration?") + $"\n{Mathf.CeilToInt(session.RemainingSeconds)} s\n" + T("requested", "Requested") + ": " + session.Preview.Display + "\n" + T("observed", "Observed") + ": " + session.Observed.Display;
            keep.interactable = session.CanConfirm; keep.GetComponentInChildren<Text>().text = T("keep", "Keep");
            revertButton.GetComponentInChildren<Text>().text = T("revert", "Revert");
        }
        internal static RectTransform Rect(string name, Transform parent) { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return (RectTransform)go.transform; }
        internal static void Place(RectTransform rect, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = size; }
        internal static Text Label(Transform parent, string value, int size) { var rect = Rect("Label", parent); var text = rect.gameObject.AddComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value; text.fontSize = size; text.color = new Color(.9f, .94f, .97f); text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; return text; }
        internal static Button Button(Transform parent, string label, Action action) {
            var rect = Rect(label, parent); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.13f, .23f, .3f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var text = Label(rect, label, 16); text.alignment = TextAnchor.MiddleCenter; text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(6, 2); text.rectTransform.offsetMax = new Vector2(-6, -2);
            button.onClick.AddListener(() => action()); return button;
        }
    }
}
