using EasyLocalization;
using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasyQuests.UI
{
    internal sealed class QuestTrackerPanel
    {
        private readonly RectTransform parent, panel;
        private readonly TMP_Text title, stage;

        internal QuestTrackerPanel(RectTransform parent)
        {
            this.parent = parent;
            panel = Child("Tracked quest", parent);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
            var back = panel.gameObject.AddComponent<Image>();
            back.raycastTarget = false;
            back.color = new Color(.025f, .045f, .065f, .84f);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 8, 8);
            layout.spacing = 2;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            title = Label("Quest", UITextRole.Speaker);
            stage = Label("Stage", UITextRole.Caption);
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }
        private TMP_Text Label(string name, UITextRole role)
        {
            var text = UIText.Add(Child(name, panel).gameObject, "", role, TextAlignmentOptions.TopLeft);
            text.raycastTarget = false;
            return text;
        }
        internal void Position(Vector2 inset)
        {
            inset = Vector2.Max(inset, Vector2.zero);
            panel.anchoredPosition = new Vector2(inset.x, -inset.y);
            panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Clamp(parent.rect.width - inset.x * 2, 1, 320));
        }
        internal void Hide() => panel.gameObject.SetActive(false);
        internal void Show(QuestRun run, LocalizedCatalogCache translations, Vector2 inset, QuestSession session = null)
        {
            string T(string key, string fallback) => translations && !string.IsNullOrEmpty(key) ? translations.Get(key, fallback) : fallback;
            title.text = string.IsNullOrEmpty(run.Title) ? T(run.Definition.titleKey, run.Definition.title) : T(run.TitleKey,run.Title);
            string line = T(run.Current.nameKey, run.Current.name);
            if (run.Current.goals.Length == 1)
            {
                var goal = run.Current.goals[0];
                if (goal.mode != GoalMode.WorldState && goal.count > 1) line += $"  {goal.DisplayCount(run.Progress(goal))}/{goal.DisplayCount(goal.count)}";
            }
            if (session != null)
                foreach (string id in session.Pinned)
                {
                    var extra = session.Find(id);
                    if (extra == null || extra == run || extra.Definition.hidden || extra.Finished) continue;
                    line += "\n• " + (string.IsNullOrEmpty(extra.Title) ? T(extra.Definition.titleKey,extra.Definition.title) : T(extra.TitleKey,extra.Title));
                }
            stage.text = line;
            Position(inset);
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
        }
    }
}
