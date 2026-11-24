using System;
using System.Collections.Generic;
using System.Linq;
using EasyResources;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    public enum ScenarioAction { Start, StartInstance, Signal, SetFact, Receive, Spend, HandIn, Collect,
        SaveCheckpoint, LoadCheckpoint, ExpectStep, ExpectBalance, ExpectFact, ExpectFinished }
    [Serializable] public sealed class ScenarioCommand
    {
        public ScenarioAction action;
        public QuestDefinition quest;
        public string instance, key;
        public QuestRoleBinding[] roles;
        public FactScope scope;
        public string subject;
        public ResourceDefinition resource;
        [Tooltip("Resource quantities use the smallest storage unit, as in EasyResources.")] public int amount=1;
    }

    [CreateAssetMenu(menuName="EasyGameFramework/Narrative/Test scenario")]
    public sealed class NarrativeScenario : ScriptableObject
    {
        public WorldFactDatabase database;
        public QuestDefinition[] catalog=Array.Empty<QuestDefinition>();
        public ScenarioCommand[] commands=Array.Empty<ScenarioCommand>();
        public string Run() => RunCommands(commands);

        internal string RunCommands(IEnumerable<ScenarioCommand> script)
        {
            var commands=script.ToArray();
            using var wallet=new ResourceStore();
            var session=new QuestSession(database,catalog,wallet.GetAvailable);
            wallet.DetailedChanged+=session.ObserveWalletChange;
            var resources=new HashSet<ResourceDefinition>(commands.Where(c=>c?.resource).Select(c=>c.resource));
            foreach(var q in catalog)foreach(var s in q.steps)
            {
                foreach(var g in s.goals)if(g.resource)resources.Add(g.resource);
                if(s.rewardProfile)foreach(var row in s.rewardProfile.Ranges)resources.Add(row.Resource);
            }
            QuestSnapshot checkpoint=null;
            ResourceContainerState ledger=null;
            bool Grant(QuestRun r) => r!=null && r.RewardPending && r.Current.rewardProfile &&
                session.TryClaimReward(r.Id,()=>wallet.TryGrant(r.Current.rewardProfile.Roll(new System.Random(r.RewardSeed)),out _));
            session.StartAvailable();
            for(int i=0;i<commands.Length;i++)
            {
                var c=commands[i];
                try
                {
                    var id=string.IsNullOrEmpty(c.instance)?c.quest?.id:c.instance;
                    var run=session.Find(id);
                    void Require(bool ok,string reason){if(!ok)throw new InvalidOperationException(reason);}
                    switch(c.action)
                    {
                        case ScenarioAction.Start: session.StartQuest(c.quest.id);break;
                        case ScenarioAction.StartInstance:session.StartInstance(c.quest,c.instance,c.roles);break;
                        case ScenarioAction.Signal:
                            if (string.IsNullOrEmpty(c.instance)) session.SignalFrom(c.key,c.amount,"Scenario");
                            else session.SignalFor(c.instance,c.key,c.amount,"Scenario");
                            break;
                        case ScenarioAction.SetFact:session.SetFact(new FactAddress(c.scope,c.subject,c.key),c.amount,"Scenario");break;
                        case ScenarioAction.Receive:Require(wallet.TryAdd(c.resource,c.amount,out _),"Resource grant refused");break;
                        case ScenarioAction.Spend:Require(wallet.TrySpend(new ResourceCost(new ResourceAmount(c.resource,c.amount))),"Resource spend refused");break;
                        case ScenarioAction.HandIn:
                            var g=run?.Current.goals.FirstOrDefault(x=>x.id==c.key);
                            Require(run!=null && g!=null && session.TryTurnIn(run,g,wallet),"Hand-in refused");break;
                        case ScenarioAction.Collect:Require(run?.Current.rewardMode==QuestRewardMode.AtInteraction && Grant(run),"Manual reward refused");break;
                        case ScenarioAction.SaveCheckpoint:checkpoint=session.Capture();ledger=wallet.CaptureState();break;
                        case ScenarioAction.LoadCheckpoint:
                            Require(checkpoint!=null,"Save a checkpoint first");wallet.RestoreState(ledger,resources);session.Restore(checkpoint);break;
                        case ScenarioAction.ExpectStep:Require(run?.Current.id==c.key,"Expected "+c.key+", current "+(run?.Current.id??"not started"));break;
                        case ScenarioAction.ExpectBalance:Require(wallet.GetTotal(c.resource)==c.amount,"Balance is "+wallet.GetTotal(c.resource));break;
                        case ScenarioAction.ExpectFact:Require(session.GetFact(new FactAddress(c.scope,c.subject,c.key))==c.amount,"Unexpected fact value");break;
                        case ScenarioAction.ExpectFinished:Require(run?.Finished==true,"Quest has not finished");break;
                    }
                    // A scenario command represents a settled frame, including queued automatic payouts.
                    var pending=session.Journal.Where(r=>r.RewardPending && r.Current.rewardMode==QuestRewardMode.Automatic).ToArray();
                    foreach(var r in pending)Grant(r);
                }
                catch(Exception ex){throw new InvalidOperationException($"Command {i+1} ({c?.action}): {ex.Message}",ex);}
            }
            return $"Passed {commands.Length} commands; {session.Journal.Count()} quest runs.";
        }
    }

    [CustomEditor(typeof(NarrativeScenario))]
    internal sealed class ScenarioInspector : UnityEditor.Editor
    {
        private string report;
        private bool passed,recording;
        private ScenarioCommand next=new();
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var scenario=(NarrativeScenario)target;
            if(GUILayout.Button("Run scenario"))Try(()=>scenario.Run());
            recording=EditorGUILayout.Foldout(recording,"Record a walkthrough",true);
            if(recording)
            {
                next.action=(ScenarioAction)EditorGUILayout.EnumPopup("Next command",next.action);
                next.quest=(QuestDefinition)EditorGUILayout.ObjectField("Quest",next.quest,typeof(QuestDefinition),false);
                next.instance=EditorGUILayout.TextField("Instance (optional)",next.instance);
                if(next.action==ScenarioAction.Signal||next.action==ScenarioAction.ExpectStep||next.action==ScenarioAction.SetFact||next.action==ScenarioAction.ExpectFact||next.action==ScenarioAction.HandIn)
                    next.key=EditorGUILayout.TextField("Step, goal, signal or fact",next.key);
                if(next.action==ScenarioAction.SetFact||next.action==ScenarioAction.ExpectFact)
                {next.scope=(FactScope)EditorGUILayout.EnumPopup("Scope",next.scope);next.subject=EditorGUILayout.TextField("Owner",next.subject);}
                if(next.action==ScenarioAction.Receive||next.action==ScenarioAction.Spend||next.action==ScenarioAction.ExpectBalance)
                    next.resource=(ResourceDefinition)EditorGUILayout.ObjectField("Resource",next.resource,typeof(ResourceDefinition),false);
                next.amount=EditorGUILayout.IntField("Value / units",next.amount);
                if(GUILayout.Button("Run and append"))Try(()=>{
                    var list=scenario.commands.Append(next).ToArray();var result=scenario.RunCommands(list);
                    Undo.RecordObject(scenario,"Record narrative command");scenario.commands=list;next=new ScenarioCommand();EditorUtility.SetDirty(scenario);return result;
                });
            }
            if(report!=null)EditorGUILayout.HelpBox(report,passed?MessageType.Info:MessageType.Error);
        }
        private void Try(Func<string> action){try{report=action();passed=true;}catch(Exception e){report=e.Message;passed=false;}}
    }
}
