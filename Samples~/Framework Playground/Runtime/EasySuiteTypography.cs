using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example
{
    public sealed class EasySuiteTypography : MonoBehaviour
    {
        public EasySuiteLab lab;
        private UIWindow page;
        private TMP_Text menuLabel, heading, speaker, body, caption, marker, scaleLabel, motionLabel, contrastLabel;
        private TMP_Text replayLabel;
        private UITextReveal subtitleReveal;
        private WindowManager Manager => lab.Windows;

        public void AddMenuEntry(UIWindow menu)
        {
            if (!lab || !menu) return;
            var existing = menu.transform.Find("Typography");
            if (existing) menuLabel = existing.GetComponentInChildren<TMP_Text>(true);
            else
            {
                var r = Rect("Typography", menu.transform);
                r.anchorMin = r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(1, 1);
                r.anchoredPosition = new Vector2(-18, -14); r.sizeDelta = new Vector2(88, 42);
                r.gameObject.AddComponent<Image>().color = new Color(.17f, .27f, .31f);
                var b = r.gameObject.AddComponent<Button>();
                b.onClick.AddListener(() => Open(menu.Handle));
                menuLabel = FillLabel(r, UITextRole.Caption);
                menu.RefreshNavigation();
            }
            Refresh();
        }

        public void Open(WindowHandle parent)
        {
            if (!page) Build();
            Refresh();
            Manager.Open(page, parent);
            subtitleReveal.Replay();
        }
        private void Start()
        {
            if (lab.Texts) lab.Texts.Changed += Refresh;
            Manager.TypographyChanged += Refresh;
        }
        private void OnDestroy()
        {
            if (lab && lab.Texts) lab.Texts.Changed -= Refresh;
            if (lab && Manager) Manager.TypographyChanged -= Refresh;
        }

        private void Refresh()
        {
            string T(string id, string fallback) => lab.T("type." + id, fallback);
            if (menuLabel) menuLabel.text = T("open", "Text");
            if (!page) return;
            var prefs = Manager.ReadingPreferences;
            heading.text = T("heading", "Typography");
            replayLabel.text=T("replay","Replay subtitle");
            speaker.text = T("speaker", "Ferryman");
            body.text = T("body", "The bridge is open. Bring the parcel to the far bank.");
            caption.text = T("caption", "Delivery №7  ·  25%  ·  5 ₽  ·  →");
            marker.text = T("marker", "●  Ferry crossing");
            scaleLabel.text = T("size", "Text size") + "  " + Mathf.RoundToInt(prefs.scale * 100) + "%";
            motionLabel.text = T("motion", "Motion") + "  " + (prefs.reducedMotion ? T("off", "Off") : T("on", "On"));
            contrastLabel.text = T("contrast", "Contrast") + "  " + (prefs.highContrast ? T("high", "High") : T("normal", "Normal"));
        }

        private void CycleScale()
        {
            var p = Manager.ReadingPreferences;
            p.scale = p.scale < 1.24f ? 1.25f : p.scale < 1.49f ? 1.5f : 1f;
            Manager.ReadingPreferences = p;
        }
        private void Motion() { var p = Manager.ReadingPreferences; p.reducedMotion = !p.reducedMotion; Manager.ReadingPreferences = p; }
        private void Contrast() { var p = Manager.ReadingPreferences; p.highContrast = !p.highContrast; Manager.ReadingPreferences = p; }

        private void Build()
        {
            var panel = Rect("Typography Studio", Manager.transform);
            panel.gameObject.SetActive(false);
            panel.anchorMin = new Vector2(.15f, .22f); panel.anchorMax = new Vector2(.85f, .78f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            panel.gameObject.AddComponent<Image>().color = new Color(.045f, .085f, .115f, 1f);
            page = panel.gameObject.AddComponent<UIWindow>();
            page.Policy = new WindowPolicy { BlockGameplay = true, Cursor = WindowCursor.Visible };
            var view = Rect("Viewport", panel); Stretch(view, 24, 24, 24, 24);
            view.gameObject.AddComponent<RectMask2D>();
            var scroll = panel.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.viewport = view; scroll.scrollSensitivity = 32;
            var column = Rect("Specimen", view); column.anchorMin = new Vector2(0, 1); column.anchorMax = Vector2.one;
            column.pivot = new Vector2(.5f, 1); column.sizeDelta = Vector2.zero; scroll.content = column;
            Vertical(column, 12, 0);
            column.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var top = Row("Heading", column, 10);
            heading = Label("Title", top, UITextRole.Title);
            heading.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var close = Button(top, "Close", UITextRole.Button, () => page.Close(), out var closeLabel);
            closeLabel.text = "×";
            var compact = close.gameObject.AddComponent<LayoutElement>(); compact.minWidth = 54; compact.preferredWidth = 54; compact.flexibleWidth = 0;
            var rule = Rect("Rule", column); rule.gameObject.AddComponent<Image>().color = new Color(.60f, .45f, .24f);
            rule.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;
            var card = Rect("Dialogue sample", column);
            card.gameObject.AddComponent<Image>().color = new Color(.07f, .14f, .18f);
            Vertical(card, 8, 18);
            speaker = Label("Speaker", card, UITextRole.Speaker);
            body = Label("Dialogue body", card, UITextRole.Body);
            body.raycastTarget=true;
            subtitleReveal=body.gameObject.AddComponent<UITextReveal>();
            subtitleReveal.duration=1.15f;subtitleReveal.playOnEnable=false;
            caption = Label("Caption", card, UITextRole.Caption);
            Button(card,"Replay subtitle",UITextRole.Caption,()=>subtitleReveal.Replay(),out replayLabel);
            marker = Label("Marker", column, UITextRole.Marker);
            var buttons = Row("Reading controls", column, 8);
            var size = Button(buttons, "Text size", UITextRole.Button, CycleScale, out scaleLabel);
            Button(buttons, "Motion", UITextRole.Button, Motion, out motionLabel);
            Button(buttons, "Contrast", UITextRole.Button, Contrast, out contrastLabel);
            foreach (Transform t in buttons)
            {
                var slot = t.gameObject.AddComponent<LayoutElement>();
                slot.minWidth = 0; slot.preferredWidth = 0; slot.flexibleWidth = 1;
            }
            page.FirstSelected = size;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var r = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            r.SetParent(parent, false); return r;
        }
        private static void Stretch(RectTransform r, float left, float bottom, float right, float top)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top); }
        private static void Vertical(RectTransform r, float spacing, int inset)
        {
            var group = r.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(inset, inset, inset, inset); group.spacing = spacing;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true; group.childForceExpandHeight = false;
        }
        private static RectTransform Row(string name, RectTransform parent, float gap)
        {
            var r = Rect(name, parent);
            var group = r.gameObject.AddComponent<HorizontalLayoutGroup>(); group.spacing = gap;
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft; return r;
        }
        private static TextMeshProUGUI Label(string name, RectTransform parent, UITextRole role)
        {
            var text = UIText.Add(Rect(name, parent).gameObject, "", role, TextAlignmentOptions.Left);
            text.overflowMode = TextOverflowModes.Overflow; return text;
        }
        private static TMP_Text FillLabel(RectTransform r, UITextRole role)
        {
            var label = Label("Label", r, role); Stretch(label.rectTransform, 3, 3, 3, 3);
            label.alignment = TextAlignmentOptions.Center; return label;
        }
        private static Button Button(RectTransform parent, string name, UITextRole role, UnityEngine.Events.UnityAction click, out TMP_Text label)
        {
            var root = Rect(name, parent); root.gameObject.AddComponent<Image>().color = new Color(.15f, .27f, .33f);
            var button = root.gameObject.AddComponent<Button>();
            root.gameObject.AddComponent<UIScrollFocus>();
            var background = root.GetComponent<Image>();
            background.color = Color.white;
            button.targetGraphic = background;
            var colors = button.colors;
            colors.normalColor = new Color(.13f, .24f, .29f);
            colors.highlightedColor = colors.selectedColor = new Color(.24f, .41f, .46f);
            colors.pressedColor = new Color(.11f, .20f, .25f);
            button.colors = colors;
            button.onClick.AddListener(click);
            Vertical(root, 0, 9); label = Label("Label", root, role); label.alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
