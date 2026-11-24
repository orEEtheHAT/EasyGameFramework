using System.Text;
using EasyLocalization;
using EasyUI;
using TMPro;
using UnityEngine;

namespace EasyQuests.UI
{
    [RequireComponent(typeof(UIWindow))]
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Journal")]
    public sealed class QuestJournal : MonoBehaviour
    {
        public QuestWorld world;
        public LocalizedCatalogCache translations;
        public TMP_Text body;
        public string SelectedId { get; private set; }
        public void Select(string id, bool track = true)
        {
            var run = world.Session.Find(id);
            if (run == null || run.Definition.hidden) return;
            SelectedId = id;
            if (track && !run.Finished) world.Session.Track(id);
            Refresh();
        }
        public void Track(QuestDefinition quest) => world.Session.Track(quest.id);
        public void TrackNext()
        {
            string first = null;
            bool next = false;
            foreach (var run in world.Session.Journal)
            {
                if (run.Definition.hidden || run.Finished) continue;
                first ??= run.Id;
                if (next) { world.Session.Track(run.Id); return; }
                next = run.Id == world.Session.Tracked;
            }
            world.Session.Track(first);
        }
        private void OnEnable()
        {
            if (!world) return;
            world.Session.Changed += Refresh;
            if (translations) translations.Changed += Refresh;
            Refresh();
        }
        private void OnDisable()
        {
            if (world) world.Session.Changed -= Refresh;
            if (translations) translations.Changed -= Refresh;
        }
        private string T(string key, string fallback) => translations && !string.IsNullOrEmpty(key) ? translations.Get(key, fallback) : fallback;
        public void Refresh()
        {
            if (!world || !body) return;
            var text = new StringBuilder();
            foreach (var run in world.Session.Journal)
            {
                var q = run.Definition;
                if (q.hidden || (SelectedId != null && world.Session.Find(SelectedId) != null && run.Id != SelectedId)) continue;
                text.Append(run.Id == world.Session.Tracked ? "› " : "").AppendLine(string.IsNullOrEmpty(run.Title) ? T(q.titleKey,q.title) : T(run.TitleKey,run.Title));
                foreach (var id in run.History)
                {
                    var step = world.Session.Step(run.Id, id);
                    if (step.type != QuestStepType.Objectives) continue;
                    foreach (var g in step.goals)
                    {
                        if (g.hidden) continue;
                        text.Append(run.Satisfied(g) ? "  ✓ " : step == run.Current ? "  • " : "  – ").Append(T(g.textKey, g.title));
                        if (g.mode != GoalMode.WorldState && g.count > 1) text.Append("  ").Append(g.DisplayCount(run.Progress(g))).Append('/').Append(g.DisplayCount(g.count));
                        var origin = run.Evidence(g);
                        if (!string.IsNullOrEmpty(origin)) text.Append("  ·  ").Append(T(origin,origin));
                        text.AppendLine();
                    }
                }
                if (run.Finished) text.AppendLine(T(run.Current.outcomeKey, run.Current.outcome));
                text.AppendLine();
            }
            body.text = text.Length > 0 ? text.ToString() : T("quests.empty", "No quests yet");
        }
    }
}
