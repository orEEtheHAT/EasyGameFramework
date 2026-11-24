using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EasyQuests.Editor
{
    public sealed class QuestInspectorWindow : EditorWindow
    {
        [SerializeField] private QuestWorld world;
        [SerializeField] private QuestDefinition quest;
        [SerializeField] private string stepId;
        [SerializeField] private string instanceId;
        private List<NarrativeInspection.Usage> usage = new();
        private Vector2 scroll;
        private GameObject focus;
        private string[] sceneReport=Array.Empty<string>();

        [MenuItem("Tools/EasyGameFramework/Narrative/Inspect scene")]
        public static void OpenScene() => Open(null,null);
        [MenuItem("GameObject/EasyGameFramework/Narrative/Find quests using object",false,23)]
        private static void Reverse() => FindObjectUses(Selection.activeGameObject);
        public static void FindObjectUses(GameObject go)
        {
            if(!go)return;
            var w=GetWindow<QuestInspectorWindow>("Inspect quest");
            w.world=go.GetComponentInParent<EasyQuests.Integrations.QuestHandInAction>()?.World ??
                go.GetComponentInParent<QuestOrder>()?.world ?? go.GetComponentInParent<QuestZone>()?.world ??
                go.GetComponentInParent<QuestEventReaction>()?.world ?? go.GetComponentInParent<QuestWorld>() ?? go.GetComponentsInChildren<QuestTarget>(true).FirstOrDefault()?.world ??
                go.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<QuestWorld>(true)).FirstOrDefault();
            w.focus=go; w.Index(); w.Show();
        }
        public static void Open(QuestDefinition q, string step)
        {
            var w=GetWindow<QuestInspectorWindow>("Inspect quest");
            w.quest=q;w.stepId=step;w.focus=null;
            if(!w.world || (q && !w.world.quests.Contains(q)))
                w.world=Object.FindObjectsByType<QuestWorld>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(x=>!q||x.quests.Contains(q));
            w.Index();w.Show();
        }
        private void Index() { usage=NarrativeInspection.Scan(world);sceneReport=Array.Empty<string>(); }
        private void OnHierarchyChange() { usage=null; }
        private void OnInspectorUpdate() { if(Application.isPlaying)Repaint(); }
        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            world=(QuestWorld)EditorGUILayout.ObjectField("Quest world",world,typeof(QuestWorld),true);
            if(EditorGUI.EndChangeCheck()||usage==null)Index();
            if(!world){EditorGUILayout.HelpBox("Select a Quest World in a loaded scene.",MessageType.Info);return;}
            if(GUILayout.Button("Check loaded scenes"))
            {
                Index();var report=new List<string>();
                foreach(var item in world.quests)
                {
                    if(!item){report.Add("Missing graph in world catalog");continue;}
                    try{item.ValidateGraph();}catch(Exception e){report.Add(e.Message);continue;}
                    foreach(var step in item.steps)
                        foreach(var row in NarrativeInspection.Explain(world,item,step,usage,null))
                            if(row.Contains("inactive hand-in")||row.Contains("no emitter")||row.Contains("missing")||row.Contains("assign ")||row.Contains("no hand-in")||row.Contains("No reward")||row.Contains("(0 loaded)"))report.Add(item.title+" / "+step.name+" · "+row);
                }
                var duplicates=Object.FindObjectsByType<QuestTarget>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                    .Where(t=>t.world==world&&!string.IsNullOrEmpty(t.id)).GroupBy(t=>t.id).Where(g=>g.Count()>1);
                report.AddRange(duplicates.Select(g=>"Duplicate loaded target ID: "+g.Key));
                sceneReport=report.Count==0?new[]{"Scene wiring checked."}:report.ToArray();
            }
            scroll=EditorGUILayout.BeginScrollView(scroll);
            foreach(var msg in sceneReport)EditorGUILayout.LabelField(msg,EditorStyles.wordWrappedLabel);
            focus=(GameObject)EditorGUILayout.ObjectField("Find usages of",focus,typeof(GameObject),true);
            if(focus)
            {
                foreach(var u in usage.Where(u=>u.component && (u.component.transform.IsChildOf(focus.transform)||focus.transform.IsChildOf(u.component.transform))))
                    if(u.step!=null && GUILayout.Button(u.quest.title+" / "+u.step.name+" · "+u.reason))QuestGraphWindow.OpenAt(u.quest,u.step.id);
            }
            quest=(QuestDefinition)EditorGUILayout.ObjectField("Graph",quest,typeof(QuestDefinition),false);
            if(quest && quest.steps.Length>0)
            {
                int index=Mathf.Max(0,Array.FindIndex(quest.steps,s=>s.id==stepId));
                var step=quest.steps[EditorGUILayout.Popup("Step",index,quest.steps.Select(s=>s.name).ToArray())];stepId=step.id;
                var runs=Application.isPlaying?world.Session.Journal.Where(r=>r.Definition==quest).ToArray():Array.Empty<QuestRun>();
                var run = runs.FirstOrDefault(r => r.Id == instanceId) ?? runs.FirstOrDefault(r => r.Id == world.Session.Tracked) ?? runs.FirstOrDefault();
                if (runs.Length > 1)
                {
                    int active = EditorGUILayout.Popup("Order instance", Array.IndexOf(runs,run), runs.Select(r => r.Id).ToArray());
                    run = runs[active]; instanceId = run.Id;
                }
                foreach(var row in NarrativeInspection.Explain(world,quest,step,usage,run))EditorGUILayout.LabelField(row,EditorStyles.wordWrappedLabel);
                foreach(var u in usage.Where(u=>u.quest==quest&&u.step==step&&u.component))
                    if(GUILayout.Button(u.reason+" · "+u.component.name)){Selection.activeGameObject=u.component.gameObject;EditorGUIUtility.PingObject(u.component.gameObject);}
                if(Application.isPlaying)
                {
                    var session=world.Session;
                    if(GUILayout.Button(session.IsTracing?"Stop and clear history":"Record recent events (128)"))
                    { if(session.IsTracing)session.DisableTrace();else session.EnableTrace(); }
                    var log=session.Trace;
                    for(int i=log.Count-1;i>=0;i--){var e=log[i]; if(!string.IsNullOrEmpty(e.Quest)&&e.Quest!=run?.Id)continue;
                        EditorGUILayout.LabelField($"#{e.Sequence} {e.Kind}  {e.Step}  {e.Key}  {e.Value}  {e.Source}",EditorStyles.wordWrappedLabel);}
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
