using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace EasyQuests.Editor
{
    [CustomEditor(typeof(WorldFactDatabase))]
    internal sealed class FactsInspector : UnityEditor.Editor
    {
        private string filter = "";
        internal void FocusFact(string id)
        {
            filter = id ?? "";
            if (filter.Length == 0) return;
            serializedObject.Update();
            var facts = serializedObject.FindProperty("facts");
            for (int i=0; i<facts.arraySize; i++)
            {
                var entry = facts.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("id").stringValue == id) { entry.isExpanded=true; break; }
            }
        }
        public override void OnInspectorGUI()
        {
            var db = (WorldFactDatabase)target;
            serializedObject.Update();
            filter = EditorGUILayout.TextField("Search", filter);
            var array = serializedObject.FindProperty("facts");
            for (int i = 0; i < array.arraySize; i++)
            {
                var p = array.GetArrayElementAtIndex(i);
                var name = p.FindPropertyRelative("name");
                var id = p.FindPropertyRelative("id");
                if (name.stringValue.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0 &&
                    id.stringValue.IndexOf(filter,StringComparison.OrdinalIgnoreCase)<0) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    p.isExpanded = EditorGUILayout.Foldout(p.isExpanded, name.stringValue, true);
                    if (!p.isExpanded) continue;
                    EditorGUILayout.PropertyField(name);
                    bool filteringById = filter.Length>0 && filter==id.stringValue;
                    EditorGUILayout.PropertyField(id, new GUIContent("Permanent ID", "Changing this key affects saved games and references. Rename the display name freely."));
                    if (filteringById) filter=id.stringValue;
                    var type = p.FindPropertyRelative("type");
                    EditorGUILayout.PropertyField(type);
                    if (type.enumValueIndex == (int)FactType.State) EditorGUILayout.PropertyField(p.FindPropertyRelative("states"), true);
                    EditorGUILayout.PropertyField(p.FindPropertyRelative("initial"));
                    if (GUILayout.Button("Remove fact") && NarrativeInspection.ConfirmRemoval(name.stringValue,p.FindPropertyRelative("id").stringValue,db,p.propertyPath)) { array.DeleteArrayElementAtIndex(i); break; }
                }
            }
            serializedObject.ApplyModifiedProperties();
            try { db.ValidateDatabase(); } catch (ArgumentException error) { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
            if (GUILayout.Button("Add fact")) { Undo.RecordObject(db, "Add world fact"); Array.Resize(ref db.facts, db.facts.Length + 1); db.facts[^1] = new WorldFact(); EditorUtility.SetDirty(db); filter = ""; }
        }
    }

    [CustomEditor(typeof(QuestDefinition))]
    internal sealed class QuestInspector : UnityEditor.Editor, INarrativeGraphInspector
    {
        public override void OnInspectorGUI()
        {
            DrawSettings();
            if (GUILayout.Button("Open in Narrative Studio", GUILayout.Height(30))) NarrativeStudio.OpenAsset(target);
        }
        public NarrativeGraphPanel CreatePanel(EditorWindow host) => new QuestGraphWindow(host,(QuestDefinition)target);
        public void DrawSettings()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "id", "steps", "startWhen", "rules", "defaultIconId", "iconLibrary");
            NarrativeStudio.PresetField(serializedObject.FindProperty("iconLibrary"),new GUIContent("Icon library"));
            QuestIconLibraryEditor.Pick(serializedObject.FindProperty("defaultIconId"), serializedObject.FindProperty("iconLibrary").objectReferenceValue as QuestIconLibrary, "Default icon", "Wayfinder");
            if (serializedObject.FindProperty("autoStart").boolValue)
                QuestGraphWindow.DrawCondition(serializedObject.FindProperty("startWhen"), ((QuestDefinition)target).database);
            serializedObject.ApplyModifiedProperties();
        }
    }

    public sealed class QuestGraphWindow : NarrativeGraphPanel
    {
        [SerializeField] private QuestDefinition quest;
        [SerializeField] private string selected;
        private Vector2 pan, inspectorScroll, ruleScroll, viewport = new Vector2(620,450);
        private float zoom = 1;
        private string search = "";
        private readonly HashSet<string> branch = new();
        private readonly HashSet<string> markerSections = new();
        public static void OpenAt(QuestDefinition quest, string step) => NarrativeStudio.OpenAsset(quest,step);
        public override string SelectedNode => selected;
        public override void FocusNode(string id)
        {
            var node = Array.Find(quest.steps, s => s.id == id);
            if (node != null) FocusStep(node);
        }
        private void FocusStep(QuestStep s)
        {
            selected=s.id;selectedRule=null;branch.Clear();branch.Add(s.id);
            pan=s.position+(new Vector2(CardWidth,CardHeight)-viewport/zoom)*.5f;Repaint();
        }
        private string selectedRule;
        private string dragged, linkSource;
        private Vector2 portDown;
        private bool draggingWire;
        private SerializedObject inspectorObject;
        private SerializedObject InspectQuest()
        {
            if (inspectorObject == null || inspectorObject.targetObject != quest)
            {
                inspectorObject?.Dispose();
                inspectorObject = new SerializedObject(quest);
            }
            inspectorObject.Update(); return inspectorObject;
        }
        private int linkRoute = -1;
        private int dragGroup;
        private QuestWorld observedWorld;
        private readonly Dictionary<string, QuestStep> destinations = new();
        private const float CardWidth = 175, CardHeight = 55;
        public static void Open(QuestDefinition quest) => NarrativeStudio.OpenAsset(quest);
        internal QuestGraphWindow(EditorWindow host, QuestDefinition graph) : base(host)
        {
            quest = graph;
            selected = graph.steps.FirstOrDefault()?.id;
        }
        public override void Dispose() { inspectorObject?.Dispose(); }
        private void Edit(string title, Action action)
        {
            Undo.RecordObject(quest, title); action(); EditorUtility.SetDirty(quest); Repaint();
        }
        public override void Draw(Vector2 size)
        {
            if (!quest) { EditorGUILayout.HelpBox("Open a Quest Graph asset to begin.", MessageType.Info); return; }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (!Application.isPlaying && GUILayout.Button("Add step", EditorStyles.toolbarDropDown, GUILayout.Width(95)))
                {
                    var menu = new GenericMenu();
                    foreach (QuestStepType kind in Enum.GetValues(typeof(QuestStepType)))
                        menu.AddItem(new GUIContent(kind.ToString()), false, () => Edit("Add quest step", () =>
                        {
                            var step = new QuestStep { name = kind.ToString(), type = kind, position = FreePosition() };
                            if (kind == QuestStepType.Objectives) step.goals = new[] { new QuestGoal { mode = GoalMode.NewEvents, signal = "completed" } };
                            quest.steps = quest.steps.Append(step).ToArray(); selected = step.id; selectedRule = null;
                        }));
                    menu.ShowAsContext();
                }
                if (!Application.isPlaying && quest.steps.Length == 0 && GUILayout.Button("Delivery template", EditorStyles.toolbarButton)) Edit("Create delivery template", Template);
                if (!Application.isPlaying && GUILayout.Button("Add quest rule", EditorStyles.toolbarButton))
                    Edit("Add quest rule", () =>
                    {
                        var rule = new QuestRule();
                        quest.rules = (quest.rules ?? Array.Empty<QuestRule>()).Append(rule).ToArray();
                        selectedRule = rule.id;
                    });
                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    try { quest.ValidateGraph(); ShowNotification(new GUIContent("Graph is valid")); }
                    catch (ArgumentException error) { EditorUtility.DisplayDialog("Quest Graph", error.Message, "OK"); }
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(linkSource == null ? "Middle mouse: pan" : "Click destination · Esc: cancel", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search=GUILayout.TextField(search,EditorStyles.toolbarSearchField,GUILayout.MinWidth(100));
                if(GUILayout.Button("Find ▾",EditorStyles.toolbarButton))
                {
                    var menu=new GenericMenu();foreach(var match in quest.steps.Where(s=>NarrativeGraphEditing.Matches(s,search)))
                    {var n=match;menu.AddItem(new GUIContent(n.name+"  ["+Array.IndexOf(quest.steps,n)+"]"),false,()=>FocusStep(n));}
                    if(menu.GetItemCount()==0)menu.AddDisabledItem(new GUIContent("No matches"));menu.ShowAsContext();
                }
                if(GUILayout.Button("Fit",EditorStyles.toolbarButton))NarrativeGraphEditing.Fit(quest.steps.Select(s=>s.position),new Vector2(CardWidth,CardHeight),viewport,ref pan,ref zoom);
                if (GUILayout.Button("Frame selection",EditorStyles.toolbarButton))
                {
                    var nodes=quest.steps.Where(n=>branch.Contains(n.id)||n.id==selected).Select(n=>n.position);
                    NarrativeGraphEditing.Fit(nodes,new Vector2(CardWidth,CardHeight),viewport,ref pan,ref zoom);
                }
                if(Application.isPlaying && GUILayout.Button("Active",EditorStyles.toolbarButton) && observedWorld)
                {
                    var current=observedWorld.Session.Journal.FirstOrDefault(r=>r.Definition==quest && r.Id==observedWorld.Session.Tracked) ??
                        observedWorld.Session.Journal.FirstOrDefault(r=>r.Definition==quest&&!r.Finished);
                    if(current!=null)FocusStep(current.Current);
                }
                if(GUILayout.Button("Inspect step",EditorStyles.toolbarButton))QuestInspectorWindow.Open(quest,selected);
                using(new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if(GUILayout.Button("Duplicate",EditorStyles.toolbarButton))
                    {
                        if(branch.Count==0 && selected!=null)branch.Add(selected);
                        var ids=NarrativeGraphEditing.Duplicate(quest,branch);
                        if(ids.Length>0){branch.Clear();branch.UnionWith(ids);selected=ids[0];}
                    }
                    if(GUILayout.Button("Use as template…",EditorStyles.toolbarButton))QuestVariantWindow.Open(quest);
                }
            }
            var sidebar = NarrativeGraphEditing.InspectorRect(size);
            float top = NarrativeGraphEditing.InspectorTop;
            if (quest.rules?.Length > 0)
            {
                float height = Mathf.Min(88, quest.rules.Length * 22);
                GUILayout.BeginArea(new Rect(2, top, sidebar.x - 7f, height));
                ruleScroll = EditorGUILayout.BeginScrollView(ruleScroll);
                foreach (var rule in quest.rules)
                {
                    var fact = quest.database ? Array.Find(quest.database.facts, f => f.id == rule.when.fact) : null;
                    var destination = Array.Find(quest.steps, s => s.id == rule.destination);
                    string label = $"Quest rule   ·   {fact?.name ?? "Choose fact"} {rule.when.comparison} {rule.when.value}   →   {destination?.name ?? "Choose step"}";
                    if (GUILayout.Button(label, EditorStyles.miniButton)) selectedRule = rule.id;
                }
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                top += height;
            }
            var canvas = new Rect(0, top, sidebar.x - 4f, Mathf.Max(1f,size.y - top));
            viewport=canvas.size;
            GUI.BeginGroup(canvas);
            var e = Event.current;
            var insideCanvas = new Rect(Vector2.zero,canvas.size).Contains(e.mousePosition);
            if (linkSource != null && e.type == EventType.MouseDrag && e.button == 0 && (e.mousePosition-portDown).sqrMagnitude > 16) { draggingWire = true; Repaint(); }
            bool finishWire = draggingWire && e.type == EventType.MouseUp && e.button == 0;
            if(e.type==EventType.ScrollWheel && insideCanvas){NarrativeGraphEditing.ZoomAt(e.mousePosition,e.delta.y,ref pan,ref zoom);e.Use();Repaint();}
            if (e.type == EventType.MouseDrag && e.button == 2) { pan -= e.delta/zoom; e.Use(); Repaint(); }
            if (Application.isPlaying && !observedWorld)
                observedWorld = UnityEngine.Object.FindObjectsByType<QuestWorld>(FindObjectsSortMode.None).FirstOrDefault(w => Array.IndexOf(w.quests, quest) >= 0);
            var session = Application.isPlaying && observedWorld ? observedWorld.Session : null;
            var run = session?.Find(session.Tracked);
            if (run?.Definition != quest) run = session?.Journal.FirstOrDefault(r => r.Definition == quest && !r.Finished);
            destinations.Clear();
            foreach (var card in quest.steps) if (!string.IsNullOrEmpty(card.id)) destinations[card.id] = card;
            Handles.BeginGUI();
            foreach (var s in quest.steps) foreach (var id in s.Outputs())
            {
                if (string.IsNullOrEmpty(id) || !destinations.TryGetValue(id, out var dest)) continue;
                var a = (s.position - pan + new Vector2(CardWidth, CardHeight / 2))*zoom;
                var b = (dest.position - pan + new Vector2(0, CardHeight / 2))*zoom;
                if (s.type == QuestStepType.Branch && zoom>.5f)
                {
                    var condition=s.routes.FirstOrDefault(r=>r.next==id)?.when;
                    var label=condition==null?"Otherwise":condition.fact+" "+condition.comparison+" "+condition.value;
                    GUI.Label(new Rect(Vector2.Lerp(a,b,.65f)-Vector2.up*17,new Vector2(200,20)),NarrativeGraphEditing.Short(label),EditorStyles.miniLabel);
                }
                Handles.DrawBezier(a, b, a + Vector2.right * 55, b + Vector2.left * 55, Color.gray, null, 2);
            }
            if (linkSource != null && destinations.TryGetValue(linkSource, out var from))
            {
                var a = (from.position - pan + new Vector2(CardWidth, CardHeight / 2))*zoom;
                Handles.DrawLine(a, e.mousePosition);
                if (e.type == EventType.MouseMove) Repaint();
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { linkSource = null; e.Use(); Repaint(); }
            }
            Handles.EndGUI();
            foreach (var s in quest.steps)
            {
                var rect = new Rect((s.position - pan)*zoom, new Vector2(CardWidth, CardHeight)*zoom);
                var old = GUI.backgroundColor;
                if (run?.Current == s) GUI.backgroundColor = Color.green;
                else if (selected == s.id || branch.Contains(s.id)) GUI.backgroundColor = Color.cyan;
                else if (run != null && run.History.Contains(s.id)) GUI.backgroundColor = new Color(.6f, .8f, .6f);
                GUI.Box(rect, s.name + "\n" + s.type);
                GUI.backgroundColor = old;
                if (!Application.isPlaying && linkSource == null && s.type != QuestStepType.End)
                {
                    var port = new Rect(rect.xMax - 14, rect.center.y - 9, 22, 18);
                    GUI.Box(port,new GUIContent("›","Click or drag to connect"),EditorStyles.miniButton);
                    if (e.type == EventType.MouseDown && e.button == 0 && port.Contains(e.mousePosition))
                    {
                        portDown = e.mousePosition; draggingWire = false; BeginConnection(s); e.Use();
                    }
                }
                if ((e.type == EventType.MouseDown || finishWire) && e.button == 0 && insideCanvas && rect.Contains(e.mousePosition))
                {
                    if (linkSource != null)
                    {
                        if (s.id != linkSource && s.type != QuestStepType.Start && destinations.TryGetValue(linkSource, out var source))
                            Edit("Connect quest steps", () => { if (linkRoute < 0) source.next = s.id; else source.routes[linkRoute].next = s.id; });
                        linkSource = null; e.Use(); Repaint(); break;
                    }
                    if(!e.shift)branch.Clear();
                    if(!branch.Add(s.id) && e.shift)branch.Remove(s.id);
                    selectedRule = null; selected = s.id; if (!Application.isPlaying) { dragged = s.id; Undo.IncrementCurrentGroup(); dragGroup = Undo.GetCurrentGroup(); }
                    e.Use(); Repaint();
                }
                if (dragged == s.id && e.type == EventType.MouseDrag && e.button == 0) { Undo.RecordObject(quest, "Move quest step"); s.position += e.delta/zoom; EditorUtility.SetDirty(quest); e.Use(); Repaint(); }
            }
            if (linkSource!=null && (e.type==EventType.MouseDown || finishWire) && e.type!=EventType.Used && e.button==0 && insideCanvas)
            {
                var origin=destinations[linkSource];int route=linkRoute;
                var point=pan+e.mousePosition/zoom;
                var menu=new GenericMenu();
                foreach(QuestStepType kind in Enum.GetValues(typeof(QuestStepType)))
                {
                    if(kind==QuestStepType.Start)continue;
                    menu.AddItem(new GUIContent("Create "+kind),false,()=>Edit("Connect new quest step",()=>{
                        var n=new QuestStep{name=kind.ToString(),type=kind,position=point};
                        if(kind==QuestStepType.Objectives)n.goals=new[]{new QuestGoal{mode=GoalMode.NewEvents,signal="completed"}};
                        if(route<0)origin.next=n.id;else origin.routes[route].next=n.id;
                        quest.steps=quest.steps.Append(n).ToArray();selected=n.id;branch.Clear();branch.Add(n.id);selectedRule=null;
                    }));
                }
                linkSource=null;menu.ShowAsContext();e.Use();
            }
            if (e.rawType == EventType.MouseUp) draggingWire = false;
            if (e.rawType == EventType.MouseUp && dragged != null) { Undo.CollapseUndoOperations(dragGroup); dragged = null; }
            GUI.EndGroup();
            GUILayout.BeginArea(sidebar);
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(sidebar.width * .37f, 100f, 155f);
            try
            {
                var rules = quest.rules ?? Array.Empty<QuestRule>();
                int ruleIndex = Array.FindIndex(rules, r => r.id == selectedRule);
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (ruleIndex >= 0) RuleInspector(ruleIndex, run);
                    else Inspector(run);
                }
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            EditorGUILayout.EndScrollView(); GUILayout.EndArea();
        }
        private void RuleInspector(int index, QuestRun run)
        {
            var so = InspectQuest();
            var item = so.FindProperty("rules").GetArrayElementAtIndex(index);
            EditorGUILayout.LabelField("Quest-wide rule", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Checked at quest start and when its fact changes. The first eligible match wins. A rule can only redirect forward.", MessageType.Info);
            DrawCondition(item.FindPropertyRelative("when"), quest.database);
            var targets = quest.steps.Where(step => step.type != QuestStepType.Start).ToArray();
            Choose(item.FindPropertyRelative("destination"), "Go to", targets.Select(s => s.id).ToArray(), targets.Select(s => s.name).ToArray());
            so.ApplyModifiedProperties();
            if (run != null) EditorGUILayout.LabelField("Taken", run.HasTakenRule(quest.rules[index].id) ? "Yes" : "No");
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(index == 0))
                    if (GUILayout.Button("Earlier")) Edit("Raise rule priority", () => Array.Reverse(quest.rules, index - 1, 2));
                using (new EditorGUI.DisabledScope(index == quest.rules.Length - 1))
                    if (GUILayout.Button("Later")) Edit("Lower rule priority", () => Array.Reverse(quest.rules, index, 2));
            }
            if (GUILayout.Button("Remove rule")) Edit("Remove quest rule", () =>
            {
                quest.rules = quest.rules.Where((_, i) => i != index).ToArray();
                selectedRule = null;
            });
        }

        private Vector2 FreePosition()
        {
            var pos = pan + new Vector2(30, 45);
            while (quest.steps.Any(s => new Rect(s.position, new Vector2(CardWidth + 10, CardHeight + 10)).Overlaps(new Rect(pos, new Vector2(CardWidth, CardHeight)))))
                pos.y += CardHeight + 18;
            return pos;
        }
        private void BeginConnection(QuestStep step)
        {
            void Pick(int route) { linkSource = step.id; linkRoute = route; Repaint(); }
            if (step.type != QuestStepType.Branch || step.routes.Length == 0) { Pick(-1); return; }
            var menu = new GenericMenu();
            for (int i = 0; i < step.routes.Length; i++)
            {
                int index = i;
                var condition = step.routes[i].when;
                var fact = quest.database ? quest.database.facts.FirstOrDefault(f => f.id == condition.fact) : null;
                menu.AddItem(new GUIContent($"Route {i + 1}: {fact?.name ?? "Fact"} {condition.comparison} {condition.value}"), false, () => Pick(index));
            }
            menu.AddItem(new GUIContent("Otherwise"), false, () => Pick(-1));
            menu.ShowAsContext();
        }
        private void Template()
        {
            var a = new QuestStep { type = QuestStepType.Start, name = "Start", position = new Vector2(15, 50) };
            var b = new QuestStep
            {
                type = QuestStepType.Objectives,
                name = "Deliver",
                position = new Vector2(220, 50),
                goals = new[] { new QuestGoal { title = "Deliver the parcel", mode = GoalMode.NewEvents, signal = "parcel.delivered" } }
            };
            var c = new QuestStep { type = QuestStepType.End, name = "Done", position = new Vector2(425, 50) };
            a.next = b.id; b.next = c.id; quest.steps = new[] { a, b, c }; selected = b.id;
        }
        private void Inspector(QuestRun run)
        {
            int index = Array.FindIndex(quest.steps, s => s.id == selected);
            if (index < 0) { GUILayout.Label("Select a step."); return; }
            var so = InspectQuest();
            var node = so.FindProperty("steps").GetArrayElementAtIndex(index);
            var step = quest.steps[index];
            EditorGUILayout.LabelField(step.type.ToString(), EditorStyles.boldLabel);
            Field(node, "name");
            if (step.type == QuestStepType.Objectives) Field(node, "nameKey");
            if (step.type == QuestStepType.Objectives)
            {
                Field(node, "join");
                for (int i = 0; i < step.goals.Length; i++)
                {
                    var g = node.FindPropertyRelative("goals").GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        Field(g, "title"); Field(g, "textKey"); Field(g, "mode");
                        if (g.FindPropertyRelative("mode").enumValueIndex == (int)GoalMode.WorldState) DrawCondition(g.FindPropertyRelative("condition"), quest.database);
                        else if (step.goals[i].UsesResource)
                        {
                            Field(g, "resource");
                            QuestObjectiveWizard.Quantity(g.FindPropertyRelative("count"), step.goals[i].resource);
                            if (step.goals[i].mode == GoalMode.TurnInResource && GUILayout.Button("Bind hand-in object…"))
                                QuestObjectiveWizard.Open(quest, step, step.goals[i]);
                        }
                        else { NarrativeKeyPicker.Draw(g.FindPropertyRelative("signal"), quest.database, false); Field(g, "count"); }
                        Field(g, "optional"); Field(g, "hidden");
                        if (!g.FindPropertyRelative("hidden").boolValue)
                        {
                            string key = step.goals[i].id;
                            bool opened = NarrativeGraphEditing.Section(markerSections.Contains(key), "Screen marker · " + step.goals[i].marker);
                            if (opened) markerSections.Add(key); else markerSections.Remove(key);
                            if (opened)
                            {
                                Field(g, "marker");
                                if (g.FindPropertyRelative("marker").enumValueIndex != 0)
                                {
                                    var grouped = g.FindPropertyRelative("targetGroup");
                                    grouped.boolValue = EditorGUILayout.Popup("Mark", grouped.boolValue ? 1 : 0, new[] { "One target", "All targets in group" }) == 1;
                                    NarrativeKeyPicker.Draw(g.FindPropertyRelative("target"), quest.database, true, grouped.boolValue);
                                    var library = so.FindProperty("iconLibrary");
                                    if (!library.objectReferenceValue) EditorGUILayout.PropertyField(library);
                                    QuestIconLibraryEditor.Pick(g.FindPropertyRelative("iconId"), library.objectReferenceValue as QuestIconLibrary, "Icon", "Quest default");
                                    Field(g, "labelMode");
                                    if (g.FindPropertyRelative("labelMode").enumValueIndex != (int)MarkerLabelMode.Hidden) Field(g, "labelMaxDistance");
                                    var edge = g.FindPropertyRelative("edgeMode");
                                    var legacy = g.FindPropertyRelative("edgeArrow");
                                    var selectedEdge = edge.enumValueIndex == 0 && !legacy.boolValue ? (int)MarkerEdgeMode.Hide : edge.enumValueIndex;
                                    int nextEdge = EditorGUILayout.Popup("Outside view", selectedEdge, new[] { "Icon default", "Pin to edge", "Hide" });
                                    if (selectedEdge != nextEdge) { edge.enumValueIndex = nextEdge; legacy.boolValue = true; }
                                    if (g.FindPropertyRelative("marker").enumValueIndex == 2) Field(g, "searchRadius");
                                }
                            }
                        }
                        if (run?.Current == step)
                        {
                            var goal = step.goals[i];
                            EditorGUILayout.LabelField("Progress", goal.DisplayCount(run.Progress(goal)));
                            if (goal.mode == GoalMode.WorldState)
                            {
                                var world = observedWorld;
                                if (world)
                                {
                                    var address = world.Session.ConditionAddress(goal.condition, run);
                                    EditorGUILayout.HelpBox(world.Session.GetFact(address) + " · " + world.Session.LastSource(address), MessageType.Info);
                                }
                            }
                        }
                        if (GUILayout.Button("Remove objective")) { node.FindPropertyRelative("goals").DeleteArrayElementAtIndex(i); break; }
                    }
                }
                if (GUILayout.Button("Add objective…")) { so.ApplyModifiedProperties(); QuestObjectiveWizard.Open(quest, step); return; }
            }
            if (step.type == QuestStepType.Branch)
            {
                EditorGUILayout.HelpBox("First matching route wins. Use Next for the fallback.", MessageType.Info);
                for (int i = 0; i < step.routes.Length; i++)
                {
                    var r = node.FindPropertyRelative("routes").GetArrayElementAtIndex(i);
                    DrawCondition(r.FindPropertyRelative("when"), quest.database); Link(r.FindPropertyRelative("next"));
                    if (GUILayout.Button("Remove route")) { node.FindPropertyRelative("routes").DeleteArrayElementAtIndex(i); break; }
                }
                if (GUILayout.Button("Add route")) { so.ApplyModifiedProperties(); Edit("Add route", () => step.routes = step.routes.Append(new QuestRoute()).ToArray()); return; }
            }
            if (step.type == QuestStepType.SetFact) { Fact(node.FindPropertyRelative("fact"), quest.database); Scope(node, "factScope", "factSubject"); Value(node, quest.database); }
            if (step.type == QuestStepType.End)
            {
                Field(node, "outcome"); Field(node, "outcomeKey");
                EditorGUI.BeginChangeCheck();
                Field(node, "rewardMode");
                if (EditorGUI.EndChangeCheck()) node.FindPropertyRelative("grantReward").boolValue = false;
                var mode = (QuestRewardMode)node.FindPropertyRelative("rewardMode").enumValueIndex;
                if (mode != QuestRewardMode.None)
                {
                    NarrativeStudio.PresetField(node.FindPropertyRelative("rewardProfile"),new GUIContent("Reward profile"));
                    var profile = node.FindPropertyRelative("rewardProfile").objectReferenceValue as EasyResources.ResourceRewardProfile;
                    if (profile)
                        foreach (var row in profile.Ranges)
                            if (row.Resource) EditorGUILayout.LabelField(row.Resource.DisplayName, row.Min == row.Max ? row.Resource.Format(row.Min) :
                                row.Resource.Format(row.Min) + " – " + row.Resource.Format(row.Max));
                    if (GUILayout.Button(mode == QuestRewardMode.AtInteraction ? "Bind reward recipient…" : "Set world wallet…"))
                    { so.ApplyModifiedProperties(); QuestObjectiveWizard.Open(quest, step); }
                }
                if (step.grantReward) Field(node, "grantReward");
            }
            else Link(node.FindPropertyRelative("next"));
            so.ApplyModifiedProperties();
            if (GUILayout.Button("Delete step") && NarrativeInspection.ConfirmRemoval(step.name,step.id,quest,node.propertyPath))
                Edit("Delete quest step", () => quest.steps = quest.steps.Where(s => s != step).ToArray());
        }
        private void Link(SerializedProperty p) => Choose(p, "Next", quest.steps.Select(s => s.id).ToArray(), quest.steps.Select(s => s.name).ToArray());
        private static void Field(SerializedProperty p, string name) => EditorGUILayout.PropertyField(p.FindPropertyRelative(name));
        internal static void DrawCondition(SerializedProperty c, WorldFactDatabase db)
        {
            Fact(c.FindPropertyRelative("fact"), db); Scope(c, "scope", "subject");
            Field(c, "comparison"); Value(c, db);
        }
        private static void Scope(SerializedProperty parent, string type, string subject)
        {
            var scope = parent.FindPropertyRelative(type);EditorGUILayout.PropertyField(scope);
            if (scope.enumValueIndex!=0) EditorGUILayout.PropertyField(parent.FindPropertyRelative(subject),new GUIContent("Owner / $role"));
        }
        private static void Fact(SerializedProperty p, WorldFactDatabase db)
        {
            if (!db) { EditorGUILayout.HelpBox("Assign a world fact database on the graph asset.", MessageType.Warning); return; }
            Choose(p, "Fact", db.facts.Select(f => f.id).ToArray(), db.facts.Select(f => f.name).ToArray());
        }
        private static void Value(SerializedProperty parent, WorldFactDatabase db)
        {
            var p = parent.FindPropertyRelative("value"); string id = parent.FindPropertyRelative("fact").stringValue;
            var f = db ? db.facts.FirstOrDefault(x => x.id == id) : null;
            if (f?.type == FactType.Flag) p.intValue = EditorGUILayout.Toggle("Value", p.intValue != 0) ? 1 : 0;
            else if (f?.type == FactType.State && f.states.Length > 0) p.intValue = EditorGUILayout.IntPopup("Value", p.intValue, f.states.Select(s => s.name).ToArray(), f.states.Select(s => s.value).ToArray());
            else EditorGUILayout.PropertyField(p);
        }
        private static void Choose(SerializedProperty p, string title, string[] keys, string[] names)
        {
            int before = Array.IndexOf(keys, p.stringValue) + 1;
            int after = EditorGUILayout.Popup(title, before, new[] { "вЂ” Select вЂ”" }.Concat(names).ToArray());
            if (before != after) p.stringValue = after == 0 ? "" : keys[after - 1];
        }
    }
}
