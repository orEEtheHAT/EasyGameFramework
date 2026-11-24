using System;
using System.Collections.Generic;
using System.Linq;
using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasyQuests.UI
{
    [RequireComponent(typeof(QuestJournal))]
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Journal Browser")]
    public sealed class QuestJournalBrowser : MonoBehaviour
    {
        public enum Page { All, Main, Side, Completed }
        [Tooltip("Optional. Defaults to the current Quest Journal body scroll area.")] public RectTransform area;
        public Page page;
        private QuestJournal journal;
        private RectTransform host, list, details;
        private TMP_InputField query;
        private TextMeshProUGUI description;
        private Button filter, track, pin;
        private CanvasGroup original;
        private float alpha;
        private bool raycasts, interactable;
        private readonly List<Row> entries = new(), objectives = new();
        private string selected;
        private bool updating;

        private sealed class Row { public Button button; public TMP_Text label; public string id; }

        private void OnEnable()
        {
            journal = GetComponent<QuestJournal>();
            if (!journal.world || !journal.body) return;
            if (!host) Build();
            if (!area.TryGetComponent(out original)) original = area.gameObject.AddComponent<CanvasGroup>();
            alpha = original.alpha; interactable = original.interactable; raycasts = original.blocksRaycasts;
            original.alpha = 0; original.interactable = false; original.blocksRaycasts = false;
            host.gameObject.SetActive(true);
            journal.world.Session.Changed += Refresh;
            if (journal.translations) journal.translations.Changed += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (journal && journal.world) journal.world.Session.Changed -= Refresh;
            if (journal && journal.translations) journal.translations.Changed -= Refresh;
            if (original) { original.alpha = alpha; original.interactable = interactable; original.blocksRaycasts = raycasts; }
            if (host) host.gameObject.SetActive(false);
        }

        private string T(string key,string fallback) => journal.translations ? journal.translations.Get(key,fallback) : fallback;
        private string Title(QuestRun run) => string.IsNullOrEmpty(run.Title) ? T(run.Definition.titleKey,run.Definition.title) : T(run.TitleKey,run.Title);
        private void Build()
        {
            if (!area)
            {
                var scroll = journal.body.GetComponentInParent<ScrollRect>(true);
                area = scroll ? scroll.GetComponent<RectTransform>() : journal.body.rectTransform;
            }
            host = Rect("Quest list and details", area.parent, area.anchorMin, area.anchorMax, Vector2.zero, Vector2.zero);
            host.pivot = area.pivot; host.sizeDelta = area.sizeDelta; host.anchoredPosition = area.anchoredPosition;
            var searchRect = Rect("Search",host,new Vector2(0,1),new Vector2(.55f,1),new Vector2(0,-42),new Vector2(-3,0));
            Back(searchRect);
            query = searchRect.gameObject.AddComponent<TMP_InputField>();
            var clip = Rect("Viewport",searchRect,Vector2.zero,Vector2.one,new Vector2(6,0),new Vector2(-6,0));
            clip.gameObject.AddComponent<RectMask2D>();
            query.textViewport = clip;
            var edit = Rect("Text",clip,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            query.textComponent = Label(edit,"",UITextRole.Body);
            var placeholder = Rect("Placeholder",clip,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
            query.placeholder = Label(placeholder,"Search quests",UITextRole.Caption);
            query.onValueChanged.AddListener(_=>Refresh());
            filter = ButtonAt("Category",host,new Vector2(.55f,1),Vector2.one,new Vector2(3,-42),Vector2.zero);
            filter.onClick.AddListener(()=>{ page=(Page)(((int)page+1)%4); Refresh(); });
            list = Scroll("Quests",host,Vector2.zero,new Vector2(.4f,1),Vector2.zero,new Vector2(-4,-48));
            details = Scroll("Details",host,new Vector2(.4f,0),Vector2.one,new Vector2(4,47),new Vector2(0,-48));
            description = Label(Rect("History",details,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero),"",UITextRole.Body);
            description.alignment = TextAlignmentOptions.TopLeft;
            track = ButtonAt("Track",host,new Vector2(.4f,0),new Vector2(.7f,0),new Vector2(4,0),new Vector2(-2,42));
            track.onClick.AddListener(()=>journal.world.Session.Track(selected));
            pin = ButtonAt("Pin",host,new Vector2(.7f,0),new Vector2(1,0),new Vector2(2,0),new Vector2(0,42));
            pin.onClick.AddListener(()=>journal.world.Session.Pin(selected,!journal.world.Session.Pinned.Contains(selected)));
        }

        private static void Back(RectTransform r)
        {
            var image=r.gameObject.AddComponent<Image>(); image.color=new Color(.14f,.23f,.32f,.96f);
        }

        private static RectTransform Rect(string name,Transform parent,Vector2 min,Vector2 max,Vector2 offsetMin,Vector2 offsetMax)
        {
            var go=new GameObject(name,typeof(RectTransform)); go.layer=parent.gameObject.layer;
            var r=(RectTransform)go.transform; r.SetParent(parent,false);r.anchorMin=min;r.anchorMax=max;r.offsetMin=offsetMin;r.offsetMax=offsetMax;return r;
        }

        private static TextMeshProUGUI Label(RectTransform r,string text,UITextRole role)
        {
            var label=UIText.Add(r.gameObject,text,role,TextAlignmentOptions.MidlineLeft);
            label.raycastTarget=false;return label;
        }

        private static Button ButtonAt(string name,Transform parent,Vector2 min,Vector2 max,Vector2 lo,Vector2 hi)
        {
            var r=Rect(name,parent,min,max,lo,hi);Back(r);var b=r.gameObject.AddComponent<Button>();
            Label(Rect("Label",r,Vector2.zero,Vector2.one,new Vector2(6,0),new Vector2(-6,0)),name,UITextRole.Button);
            return b;
        }

        private static RectTransform Scroll(string name,Transform parent,Vector2 min,Vector2 max,Vector2 lo,Vector2 hi)
        {
            var view=Rect(name,parent,min,max,lo,hi);view.gameObject.AddComponent<RectMask2D>();
            var touch=view.gameObject.AddComponent<Image>();touch.color=Color.clear;
            var scroll=view.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.viewport=view;scroll.scrollSensitivity=30;
            var content=Rect("Content",view,new Vector2(0,1),Vector2.one,Vector2.zero,Vector2.zero);content.pivot=new Vector2(.5f,1);
            var layout=content.gameObject.AddComponent<VerticalLayoutGroup>();layout.spacing=4;
            layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
            scroll.content=content; return content;
        }

        private Row Get(List<Row> pool,RectTransform parent,int index,bool isGoal)
        {
            if (index==pool.Count)
            {
                var b=ButtonAt(isGoal?"Track objective":"Select quest",parent,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
                var row=new Row{button=b,label=b.GetComponentInChildren<TMP_Text>()};
                b.gameObject.AddComponent<LayoutElement>().minHeight=42;
                b.onClick.AddListener(()=>
                {
                    if(isGoal) journal.world.Session.TrackGoal(selected,row.id);
                    else { selected=row.id;Refresh(); }
                });pool.Add(row);
            }
            var result=pool[index];result.button.gameObject.SetActive(true);return result;
        }
        private void Refresh()
        {
            if (updating || !host || !isActiveAndEnabled) return;
            updating=true;
            try
            {
                var session=journal.world.Session;
                ((TMP_Text)query.placeholder).text=T("quests.search","Search quests");
                filter.GetComponentInChildren<TMP_Text>().text=T("quests.category."+page,page.ToString());
                var search=query.text?.Trim() ?? "";
                int count=0;QuestRun chosen=null,first=null,primary=null;
                foreach(var run in session.Journal)
                {
                    if(run.Definition.hidden || (page==Page.Completed && !run.Finished) ||
                       (page==Page.Main && (run.Finished || run.Definition.category!=QuestCategory.Main)) ||
                       (page==Page.Side && (run.Finished || run.Definition.category!=QuestCategory.Side))) continue;
                    string title=Title(run);
                    if(search.Length!=0 && title.IndexOf(search,StringComparison.OrdinalIgnoreCase)<0) continue;
                    first??=run;if(run.Id==selected)chosen=run;if(run.Id==session.Tracked)primary=run;
                    var row=Get(entries,list,count++,false);row.id=run.Id;
                    row.label.text=(run.Id==session.Tracked?"› ":session.Pinned.Contains(run.Id)?"• ":"")+title;
                }
                for(int i=count;i<entries.Count;i++)entries[i].button.gameObject.SetActive(false);
                chosen??=primary??first;selected=chosen?.Id;
                if(chosen!=null) {journal.Select(chosen.Id,false);description.text=journal.body.text;}
                else description.text=T("quests.noMatches","No matching quests");
                int goalCount=0;
                if(chosen!=null && !chosen.Finished && chosen.Current.type==QuestStepType.Objectives)
                    foreach(var goal in chosen.Current.goals)
                    {
                        if(goal.hidden || chosen.Satisfied(goal))continue;
                        var row=Get(objectives,details,goalCount++,true);row.id=goal.id;
                        row.label.text="› "+T(goal.textKey,goal.title);
                    }
                for(int i=goalCount;i<objectives.Count;i++)objectives[i].button.gameObject.SetActive(false);
                track.interactable=chosen!=null && !chosen.Finished;
                track.GetComponentInChildren<TMP_Text>().text=T("quests.trackSelected","Track");
                bool pinned=chosen!=null && session.Pinned.Contains(chosen.Id);
                pin.interactable=track.interactable && chosen.Id!=session.Tracked && (pinned || session.Pinned.Count<QuestSession.MaxPins);
                pin.GetComponentInChildren<TMP_Text>().text=pinned?T("quests.unpin","Unpin"):T("quests.pin","Pin");
            }
            finally {updating=false;}
        }
    }
}
