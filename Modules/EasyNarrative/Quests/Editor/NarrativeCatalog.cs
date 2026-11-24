using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Editor
{
    internal sealed partial class NarrativeCatalog
    {
        internal sealed class Speaker
        {
            internal SpeakerProfile profile;
            internal string key, name;
            internal readonly List<(DialogueDefinition dialogue, DialogueLine line)> lines = new();
        }

        internal readonly List<Speaker> speakers = new();
        internal readonly DialogueDefinition[] dialogues;
        internal readonly QuestDefinition[] quests;
        internal readonly WorldFactDatabase[] factDatabases;
        private readonly Dictionary<DialogueDefinition,List<Speaker>> cast = new();
        private readonly Dictionary<DialogueDefinition,HashSet<QuestDefinition>> references = new();

        internal NarrativeCatalog()
        {
            dialogues = Find<DialogueDefinition>().OrderBy(d=>d.name,StringComparer.OrdinalIgnoreCase).ToArray();
            quests = Find<QuestDefinition>().OrderBy(q=>q.title,StringComparer.OrdinalIgnoreCase).ToArray();
            factDatabases = Find<WorldFactDatabase>().OrderBy(db=>db.name,StringComparer.OrdinalIgnoreCase).ThenBy(db=>AssetDatabase.GetAssetPath(db),StringComparer.Ordinal).ToArray();
            var people = new Dictionary<string,Speaker>(StringComparer.Ordinal);
            Speaker Person(string key,string name,SpeakerProfile profile = null)
            {
                if (!people.TryGetValue(key,out var value))
                    people.Add(key,value = new Speaker {key=key,name=name,profile=profile});
                return value;
            }
            Speaker Profile(SpeakerProfile profile)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(profile,out string guid,out long localId);
                return Person("profile/"+guid+"/"+localId,
                    string.IsNullOrWhiteSpace(profile.displayName)?profile.name:profile.displayName,profile);
            }
            foreach (var profile in Find<SpeakerProfile>()) Profile(profile);
            foreach (var graph in dialogues)
            {
                var participants = new List<Speaker>();
                var links = new HashSet<QuestDefinition>();
                cast.Add(graph,participants);
                references.Add(graph,links);
                foreach (var line in graph.lines ?? Array.Empty<DialogueLine>())
                {
                    if (line == null) continue;
                    Speaker who;
                    if (line.profile) who=Profile(line.profile);
                    else if (!string.IsNullOrWhiteSpace(line.speakerKey))
                        who=Person("key/"+line.speakerKey,string.IsNullOrWhiteSpace(line.speaker)?line.speakerKey:line.speaker);
                    else if (!string.IsNullOrWhiteSpace(line.speaker)) who=Person("name/"+line.speaker,line.speaker);
                    else who=Person("narration","Unassigned speaker / narration");
                    void Include(Speaker person)
                    {
                        if (!participants.Contains(person)) participants.Add(person);
                        person.lines.Add((graph,line));
                    }
                    Include(who);
                    var replies = line.replies ?? Array.Empty<DialogueReply>();
                    if (replies.Any(r=>r!=null)) Include(Person("player","Player replies"));
                    void Conditions(DialogueRequirement[] rules)
                    {
                        foreach(var rule in rules ?? Array.Empty<DialogueRequirement>())
                            if(rule?.quest && (rule.kind == DialogueRequirementType.QuestNotStarted ||
                               rule.kind == DialogueRequirementType.QuestActive || rule.kind == DialogueRequirementType.QuestAtStep ||
                               rule.kind == DialogueRequirementType.QuestFinished)) links.Add(rule.quest);
                    }
                    void Action(DialogueAction action)
                    {
                        if (action?.kind == DialogueActionType.StartQuest && action.quest) links.Add(action.quest);
                    }
                    foreach(var reply in replies)
                    {
                        if(reply==null)continue;
                        Conditions(reply.when);
                        foreach(var group in reply.groups ?? Array.Empty<DialogueRequirementGroup>()) Conditions(group?.rules);
                        Action(reply.action);
                        foreach(var action in reply.additionalActions ?? Array.Empty<DialogueAction>()) Action(action);
                        if (reply.skill) foreach(var action in reply.failureActions ?? Array.Empty<DialogueAction>()) Action(action);
                    }
                }
            }
            IndexStoryKeys();
            speakers.AddRange(people.Values.OrderBy(p=>p.name,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.key,StringComparer.Ordinal));
        }

        internal static IEnumerable<T> Find<T>() where T : Object =>
            AssetDatabase.FindAssets("t:"+typeof(T).Name).Distinct()
                .SelectMany(id=>AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(id)).OfType<T>())
                .Where(a=>a).Distinct();

        internal IEnumerable<Speaker> Cast(DialogueDefinition graph) =>
            cast.TryGetValue(graph,out var people)?people:Enumerable.Empty<Speaker>();
        internal IEnumerable<QuestDefinition> Quests(DialogueDefinition graph) =>
            references.TryGetValue(graph,out var links)?links.OrderBy(q=>q.title):Enumerable.Empty<QuestDefinition>();
        internal IEnumerable<DialogueDefinition> Dialogues(QuestDefinition quest) =>
            dialogues.Where(d=>references[d].Contains(quest));
    }
}
