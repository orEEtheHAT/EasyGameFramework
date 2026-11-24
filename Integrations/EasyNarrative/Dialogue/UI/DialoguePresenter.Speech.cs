using System;
using System.Collections.Generic;
using EasyCombat;
using EasyFeedbacks;
using EasyQuests;
using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EasyNarrative.UI
{
    public sealed partial class DialoguePresenter
    {
        [Range(0, 64), Tooltip("Number of recent spoken lines retained for this player until a slot is restored.")] public int historyLimit = 20;
        private readonly List<string> history = new();
        public IReadOnlyList<string> History => history;
        private readonly Dictionary<(Transform npc, DialogueDefinition graph), string> bookmarks = new();
        private Transform contact;
        private bool watchContact;
        private Health actorHealth, npcHealth;
        private DialogueInterruption interruption;
        private QuestSession observedSession;
        private Image portrait;
        private Button historyButton, skipVoice;
        private TMP_Text transcript, barkText;
        private AudioHandle voice;
        private string voiceLocale;
        private int displayedVisit = -1, barkPriority;
        private float barkUntil;

        private void OnEnable() => ObserveWorld();

        private void ObserveWorld()
        {
            var session = world && world.database ? world.Session : null;
            if (observedSession == session) return;
            if (observedSession != null) observedSession.Restored -= Restored;
            observedSession = session;
            if (session != null) session.Restored += Restored;
        }

        private void Restored()
        {
            bookmarks.Clear(); history.Clear();
            HideBark(); Close();
        }

        private Button ToolButton(string name, UnityAction pressed)
        {
            var root = Rect(name, content);
            root.gameObject.AddComponent<Image>().color = new Color(.15f, .23f, .29f);
            var button = root.gameObject.AddComponent<Button>();
            var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 6, 6);
            layout.childForceExpandHeight = false;
            Label(name + " label", root, UITextRole.Caption).text = name;
            button.onClick.AddListener(pressed);
            return button;
        }

        private void Remember(string who, string text)
        {
            if (historyLimit <= 0) { history.Clear(); return; }
            while (history.Count >= historyLimit) history.RemoveAt(0);
            history.Add(string.IsNullOrWhiteSpace(who) ? text : who + " · " + text);
            if (transcript && transcript.gameObject.activeSelf) transcript.text = string.Join("\n", history);
        }

        private void ToggleHistory()
        {
            bool show = !transcript.gameObject.activeSelf;
            if (show) transcript.text = string.Join("\n", history);
            transcript.gameObject.SetActive(show);
            window.RefreshNavigation();
        }

        private void PlayVoice(DialogueLine spoken, string language)
        {
            StopVoice();
            voiceLocale = language;
            var clip = DialogueVoice.Find(spoken.voices, language);
            if (clip && AudioManager.Instance) voice = AudioManager.Instance.Play(clip);
        }

        private void StopVoice() { voice.Stop(0); voice = default; }

        private void Observe(Transform npc, DialogueInterruption policy)
        {
            StopObserving();
            contact = npc;
            watchContact = npc && policy != null;
            interruption = policy;
            if (!watchContact) return;
            actorHealth = GetComponentInParent<Health>();
            npcHealth = npc.GetComponentInParent<Health>();
            if (npcHealth) { npcHealth.Died += Died; npcHealth.Changed += HealthChanged; }
            if (actorHealth && actorHealth != npcHealth) { actorHealth.Died += Died; actorHealth.Changed += HealthChanged; }
            if (!policy.interruptOnDamage) return;
            if (npcHealth) npcHealth.Damaged += Damaged;
            if (actorHealth && actorHealth != npcHealth) actorHealth.Damaged += Damaged;
        }

        private void StopObserving()
        {
            if (npcHealth) { npcHealth.Died -= Died; npcHealth.Damaged -= Damaged; npcHealth.Changed -= HealthChanged; }
            if (actorHealth && actorHealth != npcHealth) { actorHealth.Died -= Died; actorHealth.Damaged -= Damaged; actorHealth.Changed -= HealthChanged; }
            actorHealth = npcHealth = null;
            watchContact = false;
            contact = null;
        }

        private void Died(Health _) { if (interruption != null) Interrupt(interruption.whenSpeakerLeaves); }
        private void Damaged(DamageResult _) { if (interruption != null) Interrupt(interruption.whenDamaged); }
        private void HealthChanged(HealthChange _) => Conversation?.RefreshConditions();
        private void Interrupt(DialogueInterruptMode mode)
        {
            var active = Conversation;
            if (active == null || !active.IsOpen || mode == DialogueInterruptMode.KeepTalking) return;
            if (mode == DialogueInterruptMode.Resume && contact) bookmarks[(contact, active.Definition)] = active.Current.id;
            var route = interruption.interruptLine;
            StopObserving();
            if (mode != DialogueInterruptMode.Redirect || !active.Redirect(route)) Close();
        }

        private void LateUpdate()
        {
            if (Conversation != null && skipVoice && skipVoice.gameObject.activeSelf && !voice.IsPlaying)
                skipVoice.gameObject.SetActive(false);
            if (barkText && barkText.gameObject.activeSelf && Time.unscaledTime >= barkUntil) HideBark();
            if (!watchContact || Conversation == null) return;
            if (!contact || !contact.gameObject.activeInHierarchy) Interrupt(interruption.whenSpeakerLeaves);
            else if (interruption.distance > 0 && (contact.position - transform.position).sqrMagnitude > interruption.distance * interruption.distance)
                Interrupt(interruption.whenFar);
        }

        public bool TryBark(DialogueLine spoken, int priority, float seconds)
        {
            if (!isActiveAndEnabled || !manager || spoken == null || Conversation != null ||
                (barkText && barkText.gameObject.activeSelf && Time.unscaledTime < barkUntil && priority <= barkPriority)) return false;
            ObserveWorld();
            if (!barkText)
            {
                barkText = Label("Ambient subtitle", (RectTransform)manager.transform, UITextRole.Body);
                var rect = barkText.rectTransform;
                rect.anchorMin = new Vector2(.12f, .12f); rect.anchorMax = new Vector2(.88f, .27f);
                rect.offsetMin = rect.offsetMax = Vector2.zero; barkText.alignment = TextAlignmentOptions.Center;
                barkText.raycastTarget = false;
            }
            string who = Text(spoken.SpeakerKey, spoken.SpeakerName), words = Text(spoken.textKey, spoken.text);
            barkText.text = string.IsNullOrWhiteSpace(who) ? words : who + " · " + words;
            barkText.color = spoken.profile && !manager.ReadingPreferences.highContrast ? spoken.profile.subtitleColor : Color.white;
            barkText.gameObject.SetActive(true);
            PlayVoice(spoken, EasyLocalization.Localization.LanguageCode);
            barkPriority = priority;
            barkUntil = Time.unscaledTime + Mathf.Max(.1f, seconds);
            Remember(who, words);
            return true;
        }

        private void HideBark()
        {
            if (!barkText || !barkText.gameObject.activeSelf) return;
            barkText.gameObject.SetActive(false); StopVoice();
        }
    }
}
