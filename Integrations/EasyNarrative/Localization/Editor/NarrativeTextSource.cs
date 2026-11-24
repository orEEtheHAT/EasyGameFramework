using System;
using System.Collections.Generic;
using EasyQuests;
using UnityEditor;
using UnityEngine;

namespace EasyNarrative.Editor
{
    internal sealed class NarrativeTextSource
    {
        public ScriptableObject owner;
        public string ownerId, address, key, originalKey, text, context;
        public Action<string> bind;
        internal const string AutomaticPrefix = "narrative.auto.";

        internal static bool IsGenerated(string key) =>
            key != null && key.StartsWith(AutomaticPrefix, StringComparison.Ordinal);

        internal static string OwnerId(ScriptableObject asset)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                throw new ArgumentException("Save the graph or speaker profile as an asset before collecting its text.");
            return guid + "/" + localId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static List<NarrativeTextSource> Gather(IEnumerable<ScriptableObject> assets)
        {
            var result = new List<NarrativeTextSource>();
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets)
            {
                string owner = OwnerId(asset);
                void Add(string address, string source, string key, string context, Action<string> bind)
                {
                    if (string.IsNullOrWhiteSpace(source) && string.IsNullOrEmpty(key)) return;
                    if (!addresses.Add(owner + "/" + address))
                        throw new InvalidOperationException(asset.name + ": duplicate authored node ID at " + address + ". Validate this graph first.");
                    string generated = AutomaticPrefix + Hash128.Compute(owner + "/" + address);
                    // A cloned graph has a new asset GUID. Keeping its generated keys would couple edits to its original.
                    string expected = string.IsNullOrEmpty(key) || IsGenerated(key) ? generated : key;
                    result.Add(new NarrativeTextSource { owner = asset, ownerId = owner, address = address, key = expected,
                        originalKey = key ?? "", text = source ?? "", context = asset.name + " / " + context, bind = bind });
                }
                if (asset is QuestDefinition q)
                {
                    Add("title", q.title, q.titleKey, "Quest title", k => q.titleKey = k);
                    foreach (var s in q.steps ?? Array.Empty<QuestStep>())
                    {
                        if (s == null) continue;
                        if (s.type == QuestStepType.End) Add("ending/" + s.id, s.outcome, s.outcomeKey, s.name + " / Outcome", k => s.outcomeKey = k);
                        if (s.type != QuestStepType.Objectives) continue;
                        Add("stage/" + s.id, s.name, s.nameKey, s.name + " / Stage title", k => s.nameKey = k);
                        foreach (var g in s.goals ?? Array.Empty<QuestGoal>())
                            if (g != null) Add("goal/" + g.id, g.title, g.textKey, s.name + " / Objective", k => g.textKey = k);
                    }
                }
                else if (asset is DialogueDefinition d)
                {
                    foreach (var l in d.lines ?? Array.Empty<DialogueLine>())
                    {
                        if (l == null) continue;
                        string speaker = l.profile ? l.profile.displayName : l.speaker;
                        if (!l.profile) Add("speaker/" + l.id, l.speaker, l.speakerKey, l.name + " / Speaker", k => l.speakerKey = k);
                        Add("line/" + l.id, l.text, l.textKey, l.name + " / " + speaker + " / Spoken line", k => l.textKey = k);
                        foreach (var r in l.replies ?? Array.Empty<DialogueReply>())
                        {
                            if (r == null) continue;
                            Add("reply/" + r.id, r.text, r.textKey, l.name + " / Player reply", k => r.textKey = k);
                            Add("locked/" + r.id, r.lockedReason, r.lockedReasonKey, l.name + " / Locked reply", k => r.lockedReasonKey = k);
                        }
                    }
                }
                else if (asset is SpeakerProfile speaker)
                    Add("display-name", speaker.displayName, speaker.nameKey, "Speaker profile / Display name", k => speaker.nameKey = k);
            }
            return result;
        }
    }
}
