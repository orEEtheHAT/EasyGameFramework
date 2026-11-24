using System;
using System.Linq;
using System.Collections.Generic;
using EasyQuests;
using EasyQuests.UI;
using EasySuite.Example;
using TMPro;
using UnityEngine;
using Unity.Profiling;

using UnityEditor;
using System.Text;

namespace EasySuite.Example.Editor
{
    public static class MarkerHudBenchmark
    {
        private const string Menu = "Tools/EasyGameFramework/Examples/Narrative/Measure marker HUD";
        private static bool running;
        public sealed class Row
        {
            public int population, depth, visible, badges, represented, warmedFrames, measuredFrames;
            public bool cluster;
            public double meanMs, p50Ms, p95Ms, maxMs;
        }
        public sealed class Report
        {
            public string marker = "EasyNarrative.Markers";
            public string context = "Editor Play Mode, one HUD, LateUpdate CPU only, fixed projection camera, ten spatial groups";
            public Row[] results;
        }

        [MenuItem(Menu, true)]
        private static bool CanMeasure() => EditorApplication.isPlaying && !EditorApplication.isPaused && !running;

        [MenuItem(Menu)]
        private static async void FromMenu()
        {
            try { await Run(); }
            catch (OperationCanceledException) { Debug.Log("Marker measurement stopped."); }
            catch (Exception error) { Debug.LogException(error); }
        }

        public static async Awaitable<Report> Run()
        {
            if (!CanMeasure()) throw new InvalidOperationException("Enter unpaused Play Mode and wait for any previous run to finish.");
            var lab = UnityEngine.Object.FindFirstObjectByType<EasySuiteLab>();
            if (!lab) throw new InvalidOperationException("Open FrameworkPlayground to measure its HUD.");
            running = true;
            try
            {
                var until=Time.realtimeSinceStartup+40;
                while(lab && !lab.Ready && Time.realtimeSinceStartup<until) await Frames(1);
                if (!lab || !lab.Ready || !lab.Placer || !lab.Placer.ViewCamera)
                    throw new InvalidOperationException("Playground did not initialize its view camera.");
                var report = await Measure(lab);
                var table = new StringBuilder("Quest marker HUD · Editor CPU (ms)\nTargets | Cluster | Visible | Frames | Mean | p50 | p95 | Max\n");
                foreach (var row in report.results)
                    table.AppendLine($"{row.population,7} | {(row.cluster ? "on" : "off"),7} | {row.visible,7} | {row.measuredFrames,6} | {row.meanMs:F3} | {row.p50Ms:F3} | {row.p95Ms:F3} | {row.maxMs:F3}");
                Debug.Log(table.ToString());
                return report;
            }
            finally { running=false; }
        }

        private static async Awaitable Frames(int count)
        {
            var cancellation = Application.exitCancellationToken;
            while(count-- > 0) await Awaitable.NextFrameAsync(cancellation);
        }
        private static void Check(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
        private static void Release(UnityEngine.Object item)
        {
            if (!item) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }

        private static async Awaitable<Report> Measure(EasySuiteLab lab)
        {
            var original=UnityEngine.Object.FindObjectsByType<QuestMarkerHud>(FindObjectsSortMode.None).Where(h=>h.isActiveAndEnabled).ToArray();
            var fixture=new GameObject("Temporary isolated marker benchmark",typeof(RectTransform));fixture.SetActive(false);
            var db=ScriptableObject.CreateInstance<WorldFactDatabase>();var q=ScriptableObject.CreateInstance<QuestDefinition>();
            var result=new List<Row>();
            try
            {
                var cameraObject=new GameObject("Isolated projection camera");
                cameraObject.transform.SetParent(fixture.transform,false);
                var view=cameraObject.AddComponent<Camera>();view.CopyFrom(lab.Placer.ViewCamera);view.enabled=false;
                var source=lab.Placer.ViewCamera.transform;
                view.transform.SetPositionAndRotation(source.position,source.rotation);
                foreach(var h in original)h.enabled=false;
                var canvas=fixture.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
                var world=fixture.AddComponent<QuestWorld>();world.database=db;world.quests=new[]{q};db.facts=Array.Empty<WorldFact>();
                q.database=db;q.id="bench";q.steps=new[]{
                    new QuestStep{id="start",type=QuestStepType.Start,next="markers"},
                    new QuestStep{id="markers",type=QuestStepType.Objectives,next="end",goals=new[]{new QuestGoal{id="observe",mode=GoalMode.NewEvents,signal="finish",target="benchmark.members",targetGroup=true,marker=MarkerStyle.Target}}},
                    new QuestStep{id="end",type=QuestStepType.End}
                };
                var overlay=new GameObject("Fixture HUD",typeof(RectTransform));
                var rect=(RectTransform)overlay.transform;rect.SetParent(fixture.transform,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
                var hud=overlay.AddComponent<QuestMarkerHud>();hud.world=world;hud.view=view;hud.clusterRadius=24;hud.clusterDistance=12;hud.ShowQuestTracker=false;
                var group=new GameObject("Targets");group.transform.SetParent(fixture.transform,false);
                fixture.SetActive(true);world.StartQuest(q);await Frames(4);
                void Position(float depth)
                {
                    for(int i=0;i<group.transform.childCount;i++)
                    {
                        int cell=i%10;float u=.15f+(cell%5)*.17f,v=.30f+(cell/5)*.42f;
                        float jitter=(i%7-3)*.00025f;
                        group.transform.GetChild(i).position=view.ViewportToWorldPoint(new Vector3(u+jitter,v+jitter,depth));
                    }
                }
                (int visible,int members,int badges) Count()
                {
                    int shown=0,members=0,badges=0;
                    foreach(Transform t in overlay.transform)
                    {
                        if(t.name!="Quest marker" || !t.gameObject.activeInHierarchy)continue;
                        shown++;var badge=t.Find("Group count");int n=1;
                        if(badge!=null && badge.gameObject.activeInHierarchy)
                        {
                            var caption=badge.GetComponent<TMP_Text>().text;
                            Check(caption.StartsWith("×") && int.TryParse(caption.Substring(1),out n),"Invalid count: "+caption);badges++;
                        }
                        members+=n;
                    }
                    return(shown,members,badges);
                }
                foreach(int population in new[]{10,100,500})
                {
                    for(int i=group.transform.childCount;i<population;i++)
                    {
                        var go=new GameObject("Target "+i);go.transform.SetParent(group.transform,false);
                        var target=go.AddComponent<QuestTarget>();target.offset=Vector3.zero;target.Configure(world,null,"benchmark.members");
                    }
                    Position(50);await Frames(8);
                    foreach(bool cluster in new[]{false,true})
                    {
                        hud.clusterMarkers=cluster;await Frames(10);
                        var visible=Count();
                        Check(visible.members==population && (cluster ? visible.visible==10 : visible.visible==population),
                            "Incorrect warm group count: "+population+" / "+visible);
                        using var recorder=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"EasyNarrative.Markers",48,
                            ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
                        Check(recorder.Valid,"No recorder for EasyNarrative.Markers");
                        int first=Time.frameCount;await Frames(40);recorder.Stop();
                        var samples=recorder.ToArray().Where(s=>s.Count>0 && s.Value>0).ToArray();
                        Check(samples.Length>=30 && Time.frameCount-first>=40,"Insufficient recorded frames: "+samples.Length);
                        Check(samples.All(x=>x.Count==1),"A second HUD wrote into this benchmark");
                        var times=samples.Select(s=>(double)s.Value/1e6).OrderBy(x=>x).ToArray();
                        result.Add(new Row{population=population,cluster=cluster,depth=50,visible=visible.visible,badges=visible.badges,represented=visible.members,
                            warmedFrames=10,measuredFrames=times.Length,meanMs=times.Average(),p50Ms=times[(times.Length-1)/2],
                            p95Ms=times[(int)Math.Ceiling(times.Length*.95)-1],maxMs=times[times.Length-1]});
                    }
                    Position(4);await Frames(4);
                    var near=Count();Check(near.visible==population && near.badges==0,"Nearby members did not separate");
                }
            }
            finally
            {
                if(fixture)fixture.SetActive(false);
                Release(fixture); Release(q); Release(db);
                foreach(var h in original)if(h)h.enabled=true;
            }
            return new Report {results=result.ToArray()};
        }
    }
}
