using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EasyQuests.Editor
{
    internal sealed partial class NarrativeCatalog
    {
        internal sealed class StoryLink
        {
            internal Object asset;
            internal string node, title, reason, reply, icon;
        }
        private readonly Dictionary<(WorldFactDatabase db,string key,bool signal,bool write),List<StoryLink>> sites=new();
        private readonly Dictionary<Object,List<StoryLink>> related=new();

        private void IndexStoryKeys()
        {
            void Add(Object owner,string node,string title,WorldFactDatabase db,string key,bool signal,bool write,string reply=null)
            {
                if(string.IsNullOrWhiteSpace(key)||(!signal && !db))return;
                var address=(signal ? null : db,key,signal,write);
                if(!sites.TryGetValue(address,out var entries))sites.Add(address,entries=new List<StoryLink>());
                entries.Add(new StoryLink{asset=owner,node=node,title=title,reply=reply,reason=(signal?(write?"Sends ":"Waits for "):(write?"Writes ":"Checks "))+key});
            }
            foreach(var q in quests)
            {
                void Test(FactCondition c,string node,string title)=>Add(q,node,title,q.database,c?.fact,false,false);
                if(q.autoStart)Test(q.startWhen,null,"Automatic start");
                foreach(var rule in q.rules??Array.Empty<QuestRule>())if(rule!=null)Test(rule.when,rule.destination,"Quest rule");
                foreach(var step in q.steps??Array.Empty<QuestStep>())
                {
                    if(step==null)continue;
                    if(step.type==QuestStepType.SetFact)Add(q,step.id,step.name,q.database,step.fact,false,true);
                    if(step.type==QuestStepType.Branch)
                        foreach(var route in step.routes??Array.Empty<QuestRoute>())Test(route?.when,step.id,step.name);
                    if(step.type!=QuestStepType.Objectives)continue;
                    foreach(var goal in step.goals??Array.Empty<QuestGoal>())
                    {
                        if(goal?.mode==GoalMode.NewEvents)Add(q,step.id,step.name,q.database,goal.signal,true,false);
                        if(goal?.mode==GoalMode.WorldState)Test(goal.condition,step.id,step.name);
                    }
                }
            }
            foreach(var dialogue in dialogues)
                foreach(var line in dialogue.lines??Array.Empty<DialogueLine>())
                {
                    if(line==null)continue;
                    string replyId=null;
                    void Condition(FactCondition fact)=>Add(dialogue,line.id,line.name,dialogue.database,fact?.fact,false,false,replyId);
                    void Rules(DialogueRequirement[] rules)
                    {
                        foreach(var rule in rules??Array.Empty<DialogueRequirement>())
                            if(rule?.kind==DialogueRequirementType.WorldFact)Condition(rule.fact);
                    }
                    void Consequence(DialogueAction action)
                    {
                        if(action?.kind==DialogueActionType.SendEvent)Add(dialogue,line.id,line.name,dialogue.database,action.key,true,true,replyId);
                        if(action?.kind==DialogueActionType.SetFact)Add(dialogue,line.id,line.name,dialogue.database,action.key,false,true,replyId);
                    }
                    foreach(var reply in line.replies??Array.Empty<DialogueReply>())
                    {
                        if(reply==null)continue;
                        replyId=reply.id;
                        Rules(reply.when);
                        foreach(var group in reply.groups??Array.Empty<DialogueRequirementGroup>())Rules(group?.rules);
                        if(reply.skill is FactSkillCheck skill)Condition(skill.threshold);
                        Consequence(reply.action);
                        foreach(var action in reply.additionalActions??Array.Empty<DialogueAction>())Consequence(action);
                        if(reply.skill)foreach(var action in reply.failureActions??Array.Empty<DialogueAction>())Consequence(action);
                    }
                }
        }

        internal IEnumerable<StoryLink> SharedKeys(Object asset)
        {
            if(related.TryGetValue(asset,out var result))return result;
            result=new List<StoryLink>();
            var unique=new HashSet<(Object,string,string)>();
            foreach(var pair in sites)
            {
                if(!pair.Value.Any(site=>site.asset==asset))continue;
                var (db,key,signal,write)=pair.Key;
                if(!sites.TryGetValue((db,key,signal,!write),out var opposites))continue;
                foreach(var target in opposites)
                    if(target.asset!=asset && unique.Add((target.asset,target.node,target.reason)))result.Add(target);
            }
            result.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.asset.name,b.asset.name));
            related.Add(asset,result);return result;
        }
    }
}
