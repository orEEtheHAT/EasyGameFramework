using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using EasyNarrative.UI;
using EasyResources;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Editor
{
    internal sealed partial class NarrativeCatalog
    {
        internal enum PresetKind { All, Conditions, Skills, Effects, Rewards, Icons }
        private ScriptableObject[] presets;
        private NarrativeScenario[] scenarios;
        internal ScriptableObject[] Presets { get { if(presets==null)IndexPresets(); return presets; } }
        internal NarrativeScenario[] Scenarios => scenarios ??= Find<NarrativeScenario>().OrderBy(a=>a.name,StringComparer.OrdinalIgnoreCase).ToArray();
        private readonly Dictionary<Object,List<StoryLink>> presetSites = new();
        private Dictionary<(WorldFactDatabase,string,bool),List<StoryLink>> sceneFacts;

        internal static PresetKind Kind(Object asset) => asset switch {
            DialogueCondition _ => PresetKind.Conditions, DialogueSkillCheck _ => PresetKind.Skills,
            DialogueEffect _ => PresetKind.Effects, ResourceRewardProfile _ => PresetKind.Rewards,
            QuestIconLibrary _ => PresetKind.Icons, _ => PresetKind.All
        };

        private void IndexPresets()
        {
            var assets = new HashSet<ScriptableObject>();
            assets.UnionWith(Find<DialogueCondition>()); assets.UnionWith(Find<DialogueSkillCheck>());
            assets.UnionWith(Find<DialogueEffect>()); assets.UnionWith(Find<ResourceRewardProfile>());
            assets.UnionWith(Find<QuestIconLibrary>());
            void Use(ScriptableObject preset,Object owner,string node,string title,string reason,string reply=null,string icon=null)
            {
                if (!preset) return;
                assets.Add(preset);
                if (!presetSites.TryGetValue(preset,out var uses)) presetSites.Add(preset,uses=new List<StoryLink>());
                uses.Add(new StoryLink {asset=owner,node=node,title=title,reason=reason,reply=reply,icon=icon});
            }
            foreach (var quest in quests)
            {
                var library=quest.iconLibrary;
                if(library)Use(library,quest,null,quest.title,"Quest icon library",icon:library.Find(quest.defaultIconId)?.Id);
                foreach(var step in quest.steps??Array.Empty<QuestStep>())
                {
                    if(step==null)continue;
                    if(step.type==QuestStepType.End && step.rewardMode!=QuestRewardMode.None)
                        Use(step.rewardProfile,quest,step.id,step.name,step.rewardMode+" reward");
                    if(!library || step.type!=QuestStepType.Objectives)continue;
                    foreach(var goal in step.goals??Array.Empty<QuestGoal>())
                    {
                        if(goal==null || goal.marker==MarkerStyle.Hidden || goal.hidden)continue;
                        var icon=library.Find(goal.iconId)??library.Find(quest.defaultIconId);
                        if(icon!=null)Use(library,quest,step.id,step.name,goal.title+" · "+icon.name,icon:icon.Id);
                    }
                }
            }
            foreach(var graph in dialogues)
                foreach(var line in graph.lines??Array.Empty<DialogueLine>())
                {
                    if(line==null)continue;
                    foreach(var reply in line.replies??Array.Empty<DialogueReply>())
                    {
                        if(reply==null)continue;
                        void Add(ScriptableObject preset,string role)=>Use(preset,graph,line.id,line.name,reply.text+" · "+role,reply.id);
                        void Conditions(DialogueRequirement[] rules)
                        {
                            foreach(var rule in rules??Array.Empty<DialogueRequirement>())
                                if(rule?.kind==DialogueRequirementType.Custom)Add(rule.custom,"Requirement");
                        }
                        void Effect(DialogueAction action,string role)
                        {
                            if(action?.kind==DialogueActionType.Custom)Add(action.custom,role);
                        }
                        Conditions(reply.when);
                        foreach(var group in reply.groups??Array.Empty<DialogueRequirementGroup>())Conditions(group?.rules);
                        Add(reply.skill,"Skill check");
                        Effect(reply.action,"On selection");
                        foreach(var action in reply.additionalActions??Array.Empty<DialogueAction>())Effect(action,"Success");
                        if(reply.skill)foreach(var action in reply.failureActions??Array.Empty<DialogueAction>())Effect(action,"Failure");
                    }
                }
            presets=assets.OrderBy(a=>a.name,StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal IReadOnlyList<StoryLink> Uses(Object preset)
        {
            if(presets==null)IndexPresets();
            return presetSites.TryGetValue(preset,out var uses)?uses:Array.Empty<StoryLink>();
        }

        internal void InvalidateSceneFacts() => sceneFacts=null;
        internal IEnumerable<StoryLink> FactSites(WorldFactDatabase db,string id,bool write)
        {
            if(!db || string.IsNullOrEmpty(id))yield break;
            if(sites.TryGetValue((db,id,false,write),out var graphSites))
                foreach(var site in graphSites)yield return site;
            if(sceneFacts==null)IndexSceneFacts();
            if(sceneFacts.TryGetValue((db,id,write),out var sceneSites))
                foreach(var site in sceneSites)if(site.asset)yield return site;
        }
        private void IndexSceneFacts()
        {
            sceneFacts=new();
            foreach(var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,FindObjectsSortMode.None))
            {
                if(!component || !component.gameObject.scene.isLoaded)continue;
                void Add(WorldFactDatabase db,string id,FactAccess access)
                {
                    if(!db || string.IsNullOrEmpty(id))return;
                    void Record(bool write)
                    {
                        var key=(db,id,write);
                        if(!sceneFacts.TryGetValue(key,out var list))sceneFacts.Add(key,list=new List<StoryLink>());
                        list.Add(new StoryLink{asset=component,title=component.name,reason=component.gameObject.scene.name+" · "+component.GetType().Name});
                    }
                    if((access & FactAccess.Read)!=0)Record(false);
                    if((access & FactAccess.Write)!=0)Record(true);
                }
                if(component is INarrativeFactSource source)source.DescribeFacts(Add);
                if(component is WorldStateReaction reaction && reaction.world && reaction.source==WorldStateSource.Fact)
                    Add(reaction.world.database,reaction.condition?.fact,FactAccess.Read);
                if(component is DialogueBark bark && bark.listener && bark.listener.world)
                {
                    var db=bark.listener.world.database;
                    if(bark.once)Add(db,bark.playedFlag,FactAccess.Read|FactAccess.Write);
                    foreach(var condition in bark.conditions??Array.Empty<FactCondition>())Add(db,condition?.fact,FactAccess.Read);
                }
            }
        }
    }
}
