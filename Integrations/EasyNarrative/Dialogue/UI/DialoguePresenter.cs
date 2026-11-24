using System.Collections.Generic;
using System.Text;
using EasyFeedbacks;
using EasyCombat;
using EasyUI;
using EasyLocalization;
using EasyQuests;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasyNarrative.UI
{
    /// <summary>One presenter per player. The manager owns input contexts, selection and cursor.</summary>
    [AddComponentMenu("EasyGameFramework/Narrative/Dialogue Presenter")]
    public sealed partial class DialoguePresenter : MonoBehaviour
    {
        public WindowManager manager;
        public QuestWorld world;
        public LocalizedCatalogCache translations;
        [Tooltip("Localization key for the Continue button.")] public string continueKey = "dialogue.continue";
        [Tooltip("Seconds for the spoken line to appear. Zero shows it instantly; replies are always selectable.")]
        [Range(0, 3)] public float revealDuration = .85f;
        private UITextReveal reveal;
        public void ShowFullLine() { if (reveal) reveal.Complete(); }
        public DialogueRun Conversation { get; private set; }
        private UIWindow window;
        private WindowHandle handle;
        private DialogueLine displayedLine;
        private RectTransform content;
        private ScrollRect scroll;
        private TMP_Text speaker, line;
        private readonly List<Row> rows = new();

        private sealed class Row
        {
            public Button button;
            public TMP_Text label;
            public string reply;
            public int revision;
        }

        public bool Open(DialogueDefinition dialogue, WindowHandle parent = null) => OpenFrom(dialogue, null, null, parent);

        public bool OpenFrom(DialogueDefinition dialogue, Transform npc, DialogueInterruption policy, WindowHandle parent = null)
        {
            if (!isActiveAndEnabled || !manager || !dialogue || Conversation != null || (handle != null && handle.IsVisible)) return false;
            ObserveWorld();
            string entry = null;
            if (npc) bookmarks.TryGetValue((npc, dialogue), out entry);
            var run = new DialogueRun(dialogue, new DialogueContext(world ? world.Session : null, world && world.wallet ? world.wallet.Store : null,
                npc ? npc.gameObject : null, gameObject), entry);
            try
            {
                EnsureWindow();
                HideBark();
                if (npc) bookmarks.Remove((npc, dialogue));
                Observe(npc, policy);
                window.Policy.BlockGameplay = policy == null || !policy.allowMovement;
                Conversation = run;
                run.Changed += Refresh;
                if (translations) translations.Changed += Refresh;
                Refresh();
                var opened = manager.Open(window, parent);
                if (opened == null || !opened.IsOpen || Conversation != run || !isActiveAndEnabled)
                {
                    if (opened != null && opened.IsVisible) manager.Close(opened, true);
                    Detach();
                    return false;
                }
                handle = opened;
                handle.Closed += Closed;
                return true;
            }
            catch { Detach(); throw; }
        }

        public void Close()
        {
            Detach();
            handle?.Close();
        }
        private void Closed(WindowHandle closed)
        {
            closed.Closed -= Closed;
            if (handle != closed) return;
            handle = null;
            Detach();
        }
        private void Detach()
        {
            StopObserving();
            StopVoice();
            if (translations) translations.Changed -= Refresh;
            if (Conversation == null) return;
            var run = Conversation;
            Conversation = null;
            displayedLine = null;
            displayedVisit = -1;
            if (transcript) transcript.gameObject.SetActive(false);
            ShowFullLine();
            run.Changed -= Refresh;
            run.Dispose();
        }
        private void OnDisable() { Close(); HideBark(); if (observedSession != null) observedSession.Restored -= Restored; observedSession = null; }
        private void OnDestroy()
        {
            if (handle != null)
            {
                handle.Closed -= Closed;
                if (manager) manager.Close(handle, true);
            }
            if (window) Destroy(window.gameObject);
            if (barkText) Destroy(barkText.gameObject);
        }

        private string Text(string key, string fallback) => translations && !string.IsNullOrEmpty(key) ? translations.Get(key, fallback) : fallback;
        private void Refresh()
        {
            var run = Conversation;
            if (run == null || !run.IsOpen) { Close(); return; }
            string selected = null;
            var eventSystem = manager.EventSystem;
            if (eventSystem)
                foreach (var row in rows)
                    if (eventSystem.currentSelectedGameObject == row.button.gameObject) selected = row.reply;

            speaker.text = Text(run.Current.SpeakerKey, run.Current.SpeakerName);
            if (portrait)
            {
                portrait.sprite = run.Current.profile ? run.Current.profile.portrait : null;
                portrait.gameObject.SetActive(portrait.sprite);
            }
            speaker.color = run.Current.profile && !manager.ReadingPreferences.highContrast ? run.Current.profile.subtitleColor : Color.white;
            speaker.gameObject.SetActive(!string.IsNullOrWhiteSpace(speaker.text));
            bool newLine = displayedLine != run.Current || displayedVisit != run.Visit;
            string translated = Text(run.Current.textKey, run.Current.text) ?? string.Empty;
            if (newLine)
            {
                reveal.duration = revealDuration;
                reveal.Play(translated);
            }
            else if (line.text != translated)
            {
                // A language change updates the current line immediately, without restarting the conversation.
                reveal.Complete();
                line.text = translated;
            }
            string locale = EasyLocalization.Localization.LanguageCode;
            if (newLine || locale != voiceLocale)
            {
                PlayVoice(run.Current, locale);
                if (newLine) Remember(speaker.text, translated);
            }
            skipVoice.gameObject.SetActive(voice.IsPlaying);
            int count = run.Displayed.Count + (run.Replies.Count == 0 ? 1 : 0);
            Button focus = null;
            for (int i = 0; i < count; i++)
            {
                var row = i < rows.Count ? rows[i] : AddButton();
                var reply = i < run.Displayed.Count ? run.Displayed[i] : null;
                row.reply = reply?.id;
                row.revision = run.Revision;
                row.label.text = reply == null ? Text(continueKey, "Continue") : Text(reply.textKey, reply.text);
                row.button.interactable = reply == null || run.IsAvailable(reply);
                if (reply != null && reply.price != null && !reply.price.IsEmpty)
                    foreach (var cost in reply.price.Items)
                        row.label.text += "  ·  " + cost.Resource.Format(cost.Amount) + " " + cost.Resource.DisplayName;
                if (!row.button.interactable && reply.locked == LockedReply.Explain)
                    row.label.text += "\n" + Text(reply.lockedReasonKey, run.Explain(reply));
                row.button.gameObject.SetActive(true);
                if (row.button.interactable && (focus == null || selected == row.reply)) focus = row.button;
            }
            for (int i = count; i < rows.Count; i++) rows[i].button.gameObject.SetActive(false);
            window.FirstSelected = focus;
            window.RefreshNavigation();
            displayedLine = run.Current; displayedVisit = run.Visit;
            historyButton.gameObject.SetActive(history.Count > 1);
            historyButton.GetComponentInChildren<TMP_Text>().text = Text("dialogue.history", "History");
            skipVoice.GetComponentInChildren<TMP_Text>().text = Text("dialogue.skipVoice", "Skip voice");
            if (newLine) scroll.verticalNormalizedPosition = 1;
            if (handle != null && handle.IsActive && eventSystem) eventSystem.SetSelectedGameObject(focus.gameObject);
        }

        private Row AddButton()
        {
            var root = Rect("Reply", content);
            var image = root.gameObject.AddComponent<Image>();
            image.color = new Color(.18f, .32f, .40f);
            var button = root.gameObject.AddComponent<Button>();
            root.gameObject.AddComponent<DialogueReplyFocus>().Scroll = scroll;
            button.targetGraphic = image;
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 9, 9);
            layout.childForceExpandHeight = false;
            var label = Label("Answer", root, UITextRole.Button);
            var row = new Row { button = button, label = label };
            button.onClick.AddListener(() =>
            {
                if (handle == null || !handle.IsActive || !handle.IsOpen || Conversation == null) return;
                if (row.reply == null) Conversation.Continue(row.revision);
                else Conversation.Choose(row.reply, row.revision);
            });
            rows.Add(row);
            return row;
        }

        private void EnsureWindow()
        {
            if (window) return;
            var panel = Rect("Dialogue", manager.transform);
            panel.gameObject.SetActive(false);
            panel.anchorMin = new Vector2(.12f, .12f);
            panel.anchorMax = new Vector2(.88f, .66f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(.04f, .07f, .10f, .98f);
            window = panel.gameObject.AddComponent<UIWindow>();
            window.Policy = new WindowPolicy { BlockGameplay = true, Cursor = WindowCursor.Visible };
            var viewport = Rect("Viewport", panel);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(20, 16); viewport.offsetMax = new Vector2(-20, -16);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.viewport = viewport; scroll.scrollSensitivity = 30;
            content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            scroll.content = content;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlHeight = true; layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            portrait = Rect("Portrait", content).gameObject.AddComponent<Image>();
            portrait.preserveAspect = true; portrait.raycastTarget = false;
            portrait.gameObject.AddComponent<LayoutElement>().preferredHeight = 64;
            speaker = Label("Speaker", content, UITextRole.Speaker);
            line = Label("Line", content, UITextRole.Body);
            line.raycastTarget = true;
            reveal = line.gameObject.AddComponent<UITextReveal>();
            reveal.playOnEnable = false;
            skipVoice = ToolButton("Skip voice", () => { ShowFullLine(); StopVoice(); skipVoice.gameObject.SetActive(false); });
            historyButton = ToolButton("History", ToggleHistory);
            transcript = Label("Dialogue transcript", content, UITextRole.Caption);
            transcript.gameObject.SetActive(false);
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false);
            return r;
        }
        private static TMP_Text Label(string name, Transform parent, UITextRole role)
        {
            var t = UIText.Add(Rect(name, parent).gameObject, "", role, TextAlignmentOptions.Left);
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }
    }
}
