using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EasyQuests.Editor
{
    public sealed class NarrativeAuditWindow : EditorWindow
    {
        private QuestWorld world;
        private string key="",report="";
        private Vector2 scroll;
        private int total=2000, active=500, iterations=100;
        [MenuItem("Tools/EasyGameFramework/Narrative/Dependencies, saves and benchmarks")]
        public static void Open()=>GetWindow<NarrativeAuditWindow>("Narrative audit");
        private void OnGUI()
        {
            world=(QuestWorld)EditorGUILayout.ObjectField("Quest world",world,typeof(QuestWorld),true);
            key=EditorGUILayout.TextField("Fact, signal or stable ID",key);
            if(GUILayout.Button("Find dependencies in project and loaded scenes")) {
                var rows=NarrativeContentDependencies.Describe(key);
                rows.AddRange(NarrativeInspection.References(key));
                report=rows.Count==0 ? "No matching serialized narrative references in this project or its loaded scenes." : string.Join("\n",rows);
            }
            using(new EditorGUI.DisabledScope(!world))
                if(GUILayout.Button("Check saved slot or snapshot…"))
                {
                    var path=EditorUtility.OpenFilePanel("Check narrative save","","");
                    if(!string.IsNullOrEmpty(path))Try(()=>ValidateFile(path,world.database,world.quests));
                }
            using(new EditorGUI.DisabledScope(!world))
                if(GUILayout.Button("Check a folder of regression slots…"))
                {
                    var path=EditorUtility.OpenFolderPanel("Saved slot fixtures","","");
                    if(!string.IsNullOrEmpty(path))Try(()=>string.Join("\n",Directory.EnumerateFiles(path,"*",SearchOption.AllDirectories)
                        .Where(f=>f.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||f.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)).Select(f=>{
                            try{return ValidateFile(f,world.database,world.quests);}catch(Exception ex){return Path.GetFileName(f)+" · "+ex.Message;}
                        })));
                }
            if(GUILayout.Button("Run all test scenarios"))report=string.Join("\n",NarrativeInspection.Assets<NarrativeScenario>().Select(s=>{
                try{return s.name+" · "+s.Run();}catch(Exception ex){return s.name+" · FAILED · "+ex.Message;}
            }));
            total=EditorGUILayout.IntSlider("Catalog size",total,1,5000);
            active=EditorGUILayout.IntSlider("Active quests",Math.Min(total,active),1,total);
            iterations=EditorGUILayout.IntSlider("Events",iterations,1,500);
            if(GUILayout.Button("Measure isolated quest session"))Try(()=>Benchmark(total,active,iterations));
            scroll=EditorGUILayout.BeginScrollView(scroll);EditorGUILayout.SelectableLabel(report,EditorStyles.wordWrappedLabel,GUILayout.MinHeight(Mathf.Max(90,report.Split('\n').Length*21)));EditorGUILayout.EndScrollView();
        }
        private void Try(Func<string> run){try{report=run();}catch(Exception e){report=e.Message;}}
        public static string ValidateFile(string path,WorldFactDatabase db,QuestDefinition[] catalog)
        {
            var session=new QuestSession(db,catalog);int count=0;
            void Read(JToken token,int depth)
            {
                if(depth>32)throw new InvalidDataException("Nested document exceeds 32 levels.");
                if(token is JObject data && data["quests"] is JArray && data["facts"] is JArray && data["version"]!=null)
                {
                    var snapshot=JsonUtility.FromJson<QuestSnapshot>(data.ToString());session.Validate(snapshot);count++;return;
                }
                if(token.Type==JTokenType.String)
                {
                    var str=((string)token)?.TrimStart();
                    if(str!=null && (str.StartsWith("{")||str.StartsWith("["))){JToken child;try{child=JToken.Parse(str);}catch(Newtonsoft.Json.JsonException){return;}Read(child,depth+1);}
                }
                else foreach(var child in token.Children())Read(child,depth+1);
            }
            if(new FileInfo(path).Length>new EasySave.SaveOptions().MaximumFileBytes)throw new InvalidDataException("File exceeds the save size budget.");
            if(path.EndsWith(".bin",StringComparison.OrdinalIgnoreCase))
            {
                var snapshot=EasySave.SaveSnapshot.InspectFile(File.ReadAllBytes(path));
                foreach(var payload in snapshot.ReadPayloads())
                {
                    var raw=payload.Value.TrimStart();if(!raw.StartsWith("{")&&!raw.StartsWith("["))continue;
                    Read(JToken.Parse(raw),0);
                }
            }
            else Read(JToken.Parse(File.ReadAllText(path)),0);
            if(count==0)throw new InvalidDataException("No JSON quest snapshot in this file. Export the decoded slot or quest participant.");
            return $"{Path.GetFileName(path)}: {count} valid quest snapshots.";
        }

        public static string Benchmark(int count,int active,int iterations)
        {
            if(count<1||active<1||active>count||iterations<1)throw new ArgumentOutOfRangeException();
            var db=CreateInstance<WorldFactDatabase>();var quests=new QuestDefinition[count];
            try
            {
                for(int i=0;i<count;i++)
                {
                    var q=quests[i]=CreateInstance<QuestDefinition>();q.id="bench"+i;q.database=db;
                    q.steps=new[]{new QuestStep{id="s",type=QuestStepType.Start,next="g"},
                        new QuestStep{id="g",type=QuestStepType.Objectives,next="e",goals=new[]{
                            new QuestGoal{id="goal",mode=GoalMode.NewEvents,signal=i==0?"one":"many",count=int.MaxValue}}},
                        new QuestStep{id="e",type=QuestStepType.End}};
                }
                var timer=Stopwatch.StartNew();var session=new QuestSession(db,quests);
                for(int i=0;i<active;i++)session.StartQuest(quests[i].id);
                double startup=timer.Elapsed.TotalMilliseconds;
                string Measure(string signal)
                {
                    session.Signal(signal);long evaluations=session.Evaluations;timer.Restart();
                    long bytes=GC.GetAllocatedBytesForCurrentThread();
                    for(int i=0;i<iterations;i++)session.Signal(signal);
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;timer.Stop();
                    return $"{signal}: {timer.Elapsed.TotalMilliseconds:F2} ms, {allocated} B, {session.Evaluations-evaluations} evaluations";
                }
                return $"{count} graphs, {active} active, {iterations} signals per row. Setup {startup:F2} ms.\n"+Measure("absent")+"\n"+Measure("one")+"\n"+Measure("many");
            }
            finally{foreach(var q in quests)if(q)Object.DestroyImmediate(q);Object.DestroyImmediate(db);}
        }
    }
}
