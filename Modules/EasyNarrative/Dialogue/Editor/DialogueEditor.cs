using System;
using System.Collections.Generic;
using System.Linq;
using EasyQuests;
using EasyQuests.Editor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace EasyNarrative.Editor
{
    [CustomEditor(typeof(DialogueDefinition))]
    internal sealed class DialogueInspector : UnityEditor.Editor, INarrativeGraphInspector
    {
        public override void OnInspectorGUI()
        {
            DrawSettings();
            if (GUILayout.Button("Open in Narrative Studio", GUILayout.Height(30))) NarrativeStudio.OpenAsset(target);
        }
        public NarrativeGraphPanel CreatePanel(EditorWindow host) => new DialogueEditor(host,(DialogueDefinition)target);
        public void DrawSettings()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("database"));
            serializedObject.ApplyModifiedProperties();
        }
    }

    public sealed partial class DialogueEditor : NarrativeGraphPanel
    {
        [SerializeField] private DialogueDefinition asset;
        [SerializeField] private string selected;
        private string wiring, replyPort, dragging;
        private bool failurePort;
        private int dragGroup;
        private Vector2 pan, scroll, viewport = new Vector2(640,400);
        private float zoom=1;
        private string search="";
        private readonly HashSet<string> openReplies = new();
        private bool showLocalization, showVoices;
        private SerializedObject inspectorObject;
        private readonly HashSet<string> selection=new();
        private void FocusLine(DialogueLine l)
        {
            selected=l.id;selection.Clear();selection.Add(l.id);
            pan=l.position+(new Vector2(Width,Height)-viewport/zoom)*.5f;Repaint();
        }
        private const float Width = 200, Height = 66;
        public static void Open(DialogueDefinition dialogue) => NarrativeStudio.OpenAsset(dialogue);
        internal DialogueEditor(EditorWindow host, DialogueDefinition graph) : base(host)
        {
            asset = graph;
            selected = graph.entry;
        }
        public override void FocusReply(string replyId)
        {
            if (string.IsNullOrEmpty(replyId)) return;
            openReplies.Clear(); openReplies.Add(replyId);scroll=Vector2.zero;
        }
        public override string SelectedNode => selected;
        public override void FocusNode(string id)
        {
            var line = Array.Find(asset.lines,l => l.id == id);
            if (line != null) FocusLine(line);
        }
        public override void OnUndo() { asset.Invalidate(); CancelGesture(); }
        public override void Dispose() { ReleaseMouse(); inspectorObject?.Dispose(); }
        private void Edit(string operation, Action change)
        {
            Undo.RecordObject(asset, operation); change(); asset.Invalidate(); EditorUtility.SetDirty(asset); Repaint();
        }
        public override void Draw(Vector2 size)
        {
            if (!asset) { EditorGUILayout.HelpBox("Open a Dialogue asset.", MessageType.Info); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button("Add line", EditorStyles.toolbarButton)) Edit("Add dialogue line", AddLine);
                    if (asset.lines.Length == 0 && GUILayout.Button("Conversation template", EditorStyles.toolbarButton))
                        Edit("Create dialogue template", Template);
                }
                if (GUILayout.Button("Preview conversation", EditorStyles.toolbarButton)) DialoguePreview.Open(asset);
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton))
                {
                    try { asset.ValidateGraph(); ShowNotification(new GUIContent("Dialogue is valid")); }
                    catch (ArgumentException e) { EditorUtility.DisplayDialog("Dialogue", e.Message, "OK"); }
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(wiring == null ? "Drag a port to connect" : "Choose a line or empty space · Esc to cancel", EditorStyles.miniLabel);
            }
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search=GUILayout.TextField(search,EditorStyles.toolbarSearchField,GUILayout.MinWidth(100));
                if(GUILayout.Button("Find ▾",EditorStyles.toolbarButton))
                {
                    var menu=new GenericMenu();
                    foreach(var match in asset.lines.Where(l=>NarrativeGraphEditing.Matches(l,search)))
                    { var line=match;menu.AddItem(new GUIContent(line.name+"  ["+Array.IndexOf(asset.lines,line)+"]"),false,()=>FocusLine(line)); }
                    if(menu.GetItemCount()==0)menu.AddDisabledItem(new GUIContent("No matches"));menu.ShowAsContext();
                }
                if (GUILayout.Button("Frame selection", EditorStyles.toolbarButton)) FrameSelection();
                if (Application.isPlaying && GUILayout.Button("Active line", EditorStyles.toolbarButton)) FocusActive();
                if(GUILayout.Button("Fit",EditorStyles.toolbarButton))NarrativeGraphEditing.Fit(asset.lines.Select(l=>l.position),new Vector2(Width,Height),viewport,ref pan,ref zoom);
                using(new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if(GUILayout.Button("Duplicate",EditorStyles.toolbarButton))
                    {
                        if(selection.Count==0 && selected!=null)selection.Add(selected);
                        var keys=NarrativeGraphEditing.Duplicate(asset,selection);
                        if(keys.Length>0){selection.Clear();selection.UnionWith(keys);selected=keys[0];}
                    }
                    if(GUILayout.Button("Use as template…",EditorStyles.toolbarButton))
                    {
                        var path=EditorUtility.SaveFilePanelInProject("Dialogue from template",asset.name+" variant","asset","Choose a new asset.");
                        if(!string.IsNullOrEmpty(path)&&!System.IO.File.Exists(path)){var copy=UnityEngine.Object.Instantiate(asset); NarrativeTextCollector.PrepareCopy(copy); AssetDatabase.CreateAsset(copy,path); Open(copy);}
                    }
                }
            }
            var sidebar = NarrativeGraphEditing.InspectorRect(size);
            var canvas = new Rect(0, sidebar.y, sidebar.x - 4f, sidebar.height);
            DrawCanvas(canvas);
            GUILayout.BeginArea(sidebar);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(sidebar.width * .37f,100f,155f);
            try { using (new EditorGUI.DisabledScope(Application.isPlaying)) Inspect(); }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            EditorGUILayout.EndScrollView(); GUILayout.EndArea();
        }
        private void AddLine()
        {
            var pos = pan + new Vector2(25, 40);
            while (asset.lines.Any(l => new Rect(l.position, new Vector2(Width, Height)).Overlaps(new Rect(pos, new Vector2(Width, Height))))) pos.y += Height + 16;
            var l = new DialogueLine { position = pos }; asset.lines = asset.lines.Append(l).ToArray(); selected = l.id;
            if (asset.lines.Length == 1) asset.entry = l.id;
        }
        private void Template()
        {
            var a = new DialogueLine { name = "Greeting", speaker = "Villager", text = "What brings you here?", position = new Vector2(25, 50) };
            var b = new DialogueLine { name = "Directions", speaker = "Villager", text = "Follow the road east.", position = new Vector2(280, 50) };
            a.replies = new[] { new DialogueReply { text = "Where is the village?", next = b.id }, new DialogueReply { text = "Goodbye." } };
            asset.lines = new[] { a, b }; asset.entry = selected = a.id;
        }
        private void Inspect()
        {
            int index = Array.FindIndex(asset.lines, l => l.id == selected);
            if (index < 0) { EditorGUILayout.HelpBox("Select a line, or create a template.", MessageType.Info); return; }
            var line = asset.lines[index];
            if (inspectorObject == null || inspectorObject.targetObject != asset)
            {
                inspectorObject?.Dispose();
                inspectorObject = new SerializedObject(asset);
            }
            var so = inspectorObject;
            so.Update();
            var node = so.FindProperty("lines").GetArrayElementAtIndex(index);
            void Modify(string label, Action operation) { so.ApplyModifiedProperties(); Edit(label, operation); }
            Field(node, "name"); Field(node, "profile");
            if (!line.profile) Field(node, "speaker");
            Field(node, "text"); DrawVoices(node.FindPropertyRelative("voices"));
            if (GUILayout.Button(asset.entry == line.id ? "Opening line" : "Set as opening line")) Modify("Set opening line", () => asset.entry = line.id);
            showLocalization = NarrativeGraphEditing.Section(showLocalization, "Localization");
            if (showLocalization)
            {
                if (!line.profile) Field(node, "speakerKey");
                Field(node, "textKey");
            }
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Replies (" + line.replies.Length + ")", EditorStyles.boldLabel);
            for (int i = 0; i < line.replies.Length; i++)
            {
                var reply = line.replies[i]; var props = node.FindPropertyRelative("replies").GetArrayElementAtIndex(i);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string caption = string.IsNullOrWhiteSpace(reply.text) ? reply.textKey : reply.text;
                    bool open = NarrativeGraphEditing.Section(openReplies.Contains(reply.id), caption, caption);
                    if (!open) { openReplies.Remove(reply.id); continue; }
                    openReplies.Add(reply.id);
                    Field(props, "text");
                    if (showLocalization) Field(props, "textKey");
                    Link(props.FindPropertyRelative("next"), "Destination");
                    Field(props, "match", "Condition match");
                    for (int j = 0; j < reply.when.Length; j++)
                    {
                        var rule = props.FindPropertyRelative("when").GetArrayElementAtIndex(j);
                        Field(rule, "kind"); var kind = (DialogueRequirementType)rule.FindPropertyRelative("kind").enumValueIndex;
                        if (kind == DialogueRequirementType.WorldFact)
                        {
                            var fact = rule.FindPropertyRelative("fact");
                            Fact(fact.FindPropertyRelative("fact"), fact.FindPropertyRelative("value"));
                            Field(fact, "comparison"); Field(fact, "scope");
                            if (fact.FindPropertyRelative("scope").enumValueIndex != 0) Field(fact, "subject");
                        }
                        else if (kind == DialogueRequirementType.Custom) NarrativeStudio.PresetField(rule.FindPropertyRelative("custom"),new GUIContent("Condition"));
                        else if (kind == DialogueRequirementType.ResourceAvailable) { Field(rule, "resource"); Field(rule, "quantity"); }
                        else
                        {
                            Field(rule, "quest"); Field(rule, "instanceId", "Instance ID (optional)");
                            if (kind == DialogueRequirementType.QuestAtStep && rule.FindPropertyRelative("quest").objectReferenceValue is QuestDefinition quest)
                                Choose(rule.FindPropertyRelative("stepId"), "Step", quest.steps.Select(s => s.id).ToArray(), quest.steps.Select(s => s.name).ToArray(), "Select");
                        }
                        if (GUILayout.Button("Remove condition")) { int at = j; Modify("Remove condition", () => reply.when = reply.when.Where((_, k) => k != at).ToArray()); return; }
                    }
                    if (GUILayout.Button("Add condition")) { Modify("Add condition", () => reply.when = reply.when.Append(new DialogueRequirement()).ToArray()); return; }
                    Field(props, "groups", "Condition groups");
                    Field(props, "locked", "Unavailable reply");
                    if (reply.locked == LockedReply.Explain)
                    {
                        Field(props, "lockedReason", "Reason override");
                        if (showLocalization) Field(props, "lockedReasonKey", "Reason key");
                    }
                    Field(props, "price", "Attempt price");
                    NarrativeStudio.PresetField(props.FindPropertyRelative("skill"),new GUIContent("Skill check"));
                    if (reply.skill)
                    {
                        Link(props.FindPropertyRelative("failedNext"), "On failed check");
                        Field(props, "failureActions", "Failure consequences");
                    }
                    var effect = props.FindPropertyRelative("action"); Field(effect, "kind", "On selection");
                    switch ((DialogueActionType)effect.FindPropertyRelative("kind").enumValueIndex)
                    {
                        case DialogueActionType.SetFact: Fact(effect.FindPropertyRelative("key"), effect.FindPropertyRelative("value")); break;
                        case DialogueActionType.Custom: NarrativeStudio.PresetField(effect.FindPropertyRelative("custom"),new GUIContent("Effect")); break;
                        case DialogueActionType.StartQuest: Field(effect, "quest"); break;
                        case DialogueActionType.SendEvent: EasyQuests.Editor.NarrativeKeyPicker.Draw(effect.FindPropertyRelative("key"), asset.database, false); break;
                    }
                    Field(props, "additionalActions", "Success effects");
                    if (GUILayout.Button("Remove reply")) { Modify("Remove reply", () => line.replies = line.replies.Where(r => r != reply).ToArray()); return; }
                }
            }
            if (GUILayout.Button("Add reply")) { Modify("Add reply", () =>
            {
                var reply = new DialogueReply();
                line.replies = line.replies.Append(reply).ToArray();
                openReplies.Add(reply.id);
            }); return; }
            if (Array.TrueForAll(line.replies, r => r.CanBeLocked)) Link(node.FindPropertyRelative("next"), "Continue / fallback");
            if (so.ApplyModifiedProperties()) asset.Invalidate();
            if (GUILayout.Button("Delete line") && NarrativeInspection.ConfirmRemoval(line.name,line.id,asset,node.propertyPath))
                Edit("Delete line", () => RemoveLine(line));
        }
        private void RemoveLine(DialogueLine removed)
        {
            asset.lines=asset.lines.Where(line=>line!=removed).ToArray();
            foreach(var line in asset.lines)
            {
                if(line.next==removed.id)line.next="";
                foreach(var reply in line.replies)
                {
                    if(reply.next==removed.id)reply.next="";
                    if(reply.failedNext==removed.id)reply.failedNext="";
                }
            }
            if(asset.entry==removed.id)asset.entry=asset.lines.FirstOrDefault()?.id;
            selected=asset.entry;selection.Clear();
            if(selected!=null)selection.Add(selected);
        }
        private void DrawVoices(SerializedProperty voices)
        {
            showVoices = NarrativeGraphEditing.Section(showVoices, "Voices (" + voices.arraySize + ")");
            if (!showVoices) return;
            for (int i = 0; i < voices.arraySize; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var entry = voices.GetArrayElementAtIndex(i);
                    Field(entry, "locale", "Language");
                    Field(entry, "clip", "Audio clip");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(i == 0))
                            if (GUILayout.Button("Move up", EditorStyles.miniButton)) { voices.MoveArrayElement(i, i - 1); break; }
                        using (new EditorGUI.DisabledScope(i + 1 == voices.arraySize))
                            if (GUILayout.Button("Move down", EditorStyles.miniButton)) { voices.MoveArrayElement(i, i + 1); break; }
                        if (GUILayout.Button("Remove", EditorStyles.miniButton)) { voices.DeleteArrayElementAtIndex(i); break; }
                    }
                }
            }
            if (GUILayout.Button("Add voice"))
            {
                int at = voices.arraySize++;
                var entry = voices.GetArrayElementAtIndex(at);
                entry.FindPropertyRelative("locale").stringValue = "";
                entry.FindPropertyRelative("clip").objectReferenceValue = null;
            }
        }
        private static void Field(SerializedProperty p, string key, string label = null)
        {
            var field = p.FindPropertyRelative(key); EditorGUILayout.PropertyField(field, new GUIContent(label ?? field.displayName,field.tooltip),true);
        }
        private void Link(SerializedProperty p, string label) => Choose(p, label, asset.lines.Select(l => l.id).ToArray(), asset.lines.Select(l => l.name).ToArray(), "End conversation");
        private void Fact(SerializedProperty id, SerializedProperty value)
        {
            if (!asset.database) { EditorGUILayout.HelpBox("Assign the World Fact Database on the dialogue asset.", MessageType.Info); return; }
            var facts = asset.database.facts;
            Choose(id, "Fact", facts.Select(f => f.id).ToArray(), facts.Select(f => f.name).ToArray(), "Select");
            var f = Array.Find(facts, x => x.id == id.stringValue);
            if (f?.type == FactType.Flag) value.intValue = EditorGUILayout.Toggle("Value", value.intValue != 0) ? 1 : 0;
            else if (f?.type == FactType.State && f.states.Length > 0) value.intValue = EditorGUILayout.IntPopup("Value", value.intValue, f.states.Select(s => s.name).ToArray(), f.states.Select(s => s.value).ToArray());
            else EditorGUILayout.PropertyField(value);
        }
        private static void Choose(SerializedProperty p, string label, string[] keys, string[] names, string empty)
        {
            int current = Array.IndexOf(keys, p.stringValue) + 1;
            var choices = new[] { empty }.Concat(names).ToList();
            if (current == 0 && !string.IsNullOrEmpty(p.stringValue))
            {
                current=choices.Count;
                choices.Add("Missing · " + p.stringValue);
            }
            int result = EditorGUILayout.Popup(label, current, choices.ToArray());
            if (current != result && result <= keys.Length) p.stringValue = result == 0 ? "" : keys[result - 1];
        }
    }
}
