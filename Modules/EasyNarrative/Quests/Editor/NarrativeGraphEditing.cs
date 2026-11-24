using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    public static class NarrativeGraphEditing
    {
        public static string[] Duplicate(QuestDefinition quest, ICollection<string> selection)
        {
            var copy=UnityEngine.Object.Instantiate(quest);
            try
            {
                var nodes=copy.steps.Where(s=>selection.Contains(s.id) && s.type!=QuestStepType.Start).ToArray();
                if(nodes.Length==0)return Array.Empty<string>();
                var map=nodes.ToDictionary(n=>n.id,n=>Guid.NewGuid().ToString("N"));
                foreach(var node in nodes)
                {
                    EasyNarrative.Editor.NarrativeTextCollector.PrepareCopy(node);
                    node.id=map[node.id];node.name+=" (copy)";node.position+=new Vector2(25,115);
                    foreach(var goal in node.goals)goal.id=Guid.NewGuid().ToString("N");
                    if(node.next!=null && map.TryGetValue(node.next,out var next))node.next=next;
                    foreach(var route in node.routes)if(route.next!=null && map.TryGetValue(route.next,out next))route.next=next;
                }
                Undo.RegisterCompleteObjectUndo(quest,"Duplicate quest branch");
                quest.steps=quest.steps.Concat(nodes).ToArray();EditorUtility.SetDirty(quest);
                return nodes.Select(n=>n.id).ToArray();
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static string[] Duplicate(DialogueDefinition dialogue, ICollection<string> selection)
        {
            var copy=UnityEngine.Object.Instantiate(dialogue);
            try
            {
                var lines=copy.lines.Where(l=>selection.Contains(l.id)).ToArray();
                if(lines.Length==0)return Array.Empty<string>();
                var map=lines.ToDictionary(l=>l.id,l=>Guid.NewGuid().ToString("N"));
                foreach(var line in lines)
                {
                    EasyNarrative.Editor.NarrativeTextCollector.PrepareCopy(line);
                    line.id=map[line.id];line.name+=" (copy)";line.position+=new Vector2(25,115);
                    if(line.next!=null && map.TryGetValue(line.next,out var next))line.next=next;
                    foreach(var reply in line.replies)
                    {
                        reply.id=Guid.NewGuid().ToString("N");
                        if(reply.next!=null && map.TryGetValue(reply.next,out next))reply.next=next;
                        if(reply.failedNext!=null && map.TryGetValue(reply.failedNext,out next))reply.failedNext=next;
                    }
                }
                Undo.RegisterCompleteObjectUndo(dialogue,"Duplicate dialogue branch");
                dialogue.lines=dialogue.lines.Concat(lines).ToArray();dialogue.Invalidate();EditorUtility.SetDirty(dialogue);
                return lines.Select(l=>l.id).ToArray();
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static bool Contains(string text,string search) => text?.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0;
        public static bool Matches(QuestStep s,string search)
        {
            if(new[]{s.id,s.name,s.fact,s.outcome,s.nameKey}.Any(v=>Contains(v,search)))return true;
            if(s.routes.Any(r=>Contains(r.when?.fact,search)))return true;
            return s.goals.Any(g=>new[]{g.title,g.target,g.signal,g.textKey,g.condition?.fact,g.resource?g.resource.DisplayName:null}.Any(v=>Contains(v,search)));
        }
        public static bool Matches(DialogueLine line,string search)
        {
            if (line == null) return false;
            bool Has(string value) => Contains(value,search);
            bool Rule(DialogueRequirement condition) => condition != null &&
                (Has(condition.fact?.fact) || Has(condition.fact?.subject) || Has(condition.stepId) ||
                 Has(condition.instanceId) || (condition.quest && Has(condition.quest.title)) ||
                 (condition.resource && Has(condition.resource.DisplayName)) || (condition.custom && Has(condition.custom.name)));
            bool Effect(DialogueAction action) => action != null && (Has(action.key) || (action.quest && Has(action.quest.title)) ||
                (action.custom && Has(action.custom.name)));
            if (Has(line.id) || Has(line.name) || Has(line.speaker) || Has(line.text) || Has(line.textKey) ||
                (line.profile && (Has(line.profile.displayName) || Has(line.profile.nameKey)))) return true;
            return line.replies.Any(r => r != null && (Has(r.text) || Has(r.textKey) || Has(r.lockedReason) ||
                Effect(r.action) || r.when.Any(Rule) || r.groups.Any(g => g != null && g.rules.Any(Rule)) ||
                r.additionalActions.Any(Effect) || r.failureActions.Any(Effect) || (r.skill && Has(r.skill.name))));
        }
        public static void Fit(IEnumerable<Vector2> positions, Vector2 card, Vector2 viewport, ref Vector2 pan, ref float zoom)
        {
            var points=positions.ToArray();if(points.Length==0)return;
            Vector2 low=points[0],high=points[0]+card;
            foreach(var p in points){low=Vector2.Min(low,p);high=Vector2.Max(high,p+card);}
            var span=high-low+Vector2.one*48;
            zoom=Mathf.Clamp(Mathf.Min(viewport.x/span.x,viewport.y/span.y),.12f,1.35f);
            pan=(low+high-viewport/zoom)*.5f;
        }
        public static void ZoomAt(Vector2 cursor,float wheel,ref Vector2 pan,ref float zoom)
        {
            float previous=zoom;
            zoom=Mathf.Clamp(zoom*Mathf.Exp(-wheel*.075f),.12f,1.8f);
            pan+=cursor/previous-cursor/zoom;
        }
        // Both graph editors reserve the same space for toolbars and inspector controls.
        public const float InspectorTop = 44f;
        public static Rect InspectorRect(Vector2 size)
        {
            float width = Mathf.Clamp(size.x * .42f, Mathf.Min(325f, size.x * .5f), 510f);
            return new Rect(size.x - width + 4f, InspectorTop, width - 8f, Mathf.Max(1f, size.y - InspectorTop - 2f));
        }

        public static bool Section(bool open, string caption, string tooltip = null)
        {
            // Read-only Play Mode inspectors still allow expanding their contents.
            bool enabled = GUI.enabled;
            GUI.enabled = true;
            try { return EditorGUILayout.Foldout(open, new GUIContent(caption, tooltip), true); }
            finally { GUI.enabled = enabled; }
        }

        public static string Short(string label) => string.IsNullOrEmpty(label)?"": label.Length<=28?label:label.Substring(0,25)+"…";
    }
}
