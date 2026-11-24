using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyNarrative.UI;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyQuests.Editor
{
    public sealed partial class NarrativeStudio : EditorWindow
    {
        private enum Page { Library, Graph, Settings, Connections, Character, Usages, Tests }
        private enum Category { Characters, Dialogues, Quests, WorldFacts, Presets }
        [Serializable] private struct Location
        {
            public Page page;
            public Object asset;
            public string person, node, reply;
        }
        [SerializeField] private Location current;
        [SerializeField] private Category category = Category.Dialogues;
        [SerializeField] private string search = "";
        private readonly Stack<Location> history = new();
        private NarrativeCatalog catalog;
        private UnityEditor.Editor inspector, factsInspector;
        private NarrativeGraphPanel graph;
        private Vector2 listScroll, detailScroll;
        private bool showFacts;
        private QuestWorld world;
        private QuestWorld[] sceneWorlds;
        private SpeakerProfile bindProfile;
        private TalkAction npc;
        private TalkAction[] sceneSpeakers;
        private DialogueBark[] ambientSpeakers;
        private const float Header = 23;

        [MenuItem("Tools/EasyGameFramework/Narrative/Narrative Studio", priority=0)]
        private static void ShowLibrary()
        {
            var studio=Open();
            if(studio.current.page!=Page.Library)studio.Visit(default);
        }
        public static NarrativeStudio Open() => GetWindow<NarrativeStudio>("Narrative Studio");
        public static void OpenAsset(Object asset,string node = null)
        {
            if (!asset) return;
            var studio=Open();
            var location=new Location {asset=asset,node=node,page=IsGraph(asset)?Page.Graph:Page.Settings};
            if(Event.current!=null)studio.Queue(()=>studio.Visit(location));
            else studio.Visit(location);
        }
        [OnOpenAsset]
        private static bool OpenFromProject(int id,int line)
        {
            var asset=EditorUtility.InstanceIDToObject(id);
            if (!IsGraph(asset) && !(asset is SpeakerProfile) && !(asset is WorldFactDatabase) && !(asset is NarrativeScenario) &&
                (NarrativeCatalog.Kind(asset)==NarrativeCatalog.PresetKind.All || asset is EasyResources.ResourceRewardProfile)) return false;
            OpenAsset(asset); return true;
        }
        private static bool IsGraph(Object asset) => asset is QuestDefinition || asset is DialogueDefinition;
        private static string NameOf(Object asset) => asset switch {
            QuestDefinition q when !string.IsNullOrWhiteSpace(q.title) => q.title,
            SpeakerProfile p when !string.IsNullOrWhiteSpace(p.displayName) => p.displayName,
            _ => asset ? asset.name : ""
        };
        private void OnEnable()
        {
            minSize=new Vector2(800,440);
            wantsMouseMove=true;
            Undo.undoRedoPerformed+=OnUndo;
            EditorApplication.projectChanged+=Invalidate;
            EditorApplication.hierarchyChanged+=SceneChanged;
        }
        private void OnDisable()
        {
            RememberNode();
            Undo.undoRedoPerformed-=OnUndo;
            EditorApplication.projectChanged-=Invalidate;
            EditorApplication.hierarchyChanged-=SceneChanged;
            Release();
        }
        private void OnFocus() { Invalidate(); }
        private void OnLostFocus() { graph?.LostFocus(); }
        private void OnInspectorUpdate() { if(Application.isPlaying)Repaint(); }
        private void SceneChanged() { catalog?.InvalidateSceneFacts(); sceneSpeakers=null;sceneWorlds=null;ambientSpeakers=null; Repaint(); }
        private void OnUndo() { graph?.OnUndo(); Invalidate(); }
        private void Invalidate() { catalog=null; sceneSpeakers=null;sceneWorlds=null;ambientSpeakers=null; Repaint(); }
        private NarrativeCatalog Catalog => catalog ??= new NarrativeCatalog();
        private void Release()
        {
            graph?.Dispose(); graph=null;
            if(inspector)DestroyImmediate(inspector);
            if(factsInspector)DestroyImmediate(factsInspector);
            inspector=null;factsInspector=null; showFacts=false; npc=null; world=null;bindProfile=null;selectedIcon=null;
        }
        private void RememberNode()
        {
            if(current.page==Page.Graph && graph!=null)current.node=graph.SelectedNode;
        }
        private void Visit(Location destination,bool remember=true)
        {
            RememberNode();
            bool same = current.asset == destination.asset && current.person==destination.person;
            if (remember && (!same || current.page!=destination.page || destination.node!=null && current.node!=destination.node || destination.reply!=null && destination.reply!=current.reply))
                history.Push(current);
            graph?.LostFocus();
            if (!same) Release();
            else if (destination.node==null && IsGraph(destination.asset)) destination.node=current.node;
            if(destination.page==Page.Library || destination.page==Page.Connections || destination.page==Page.Character || destination.page==Page.Usages || destination.page==Page.Tests)catalog=null;
            current=destination;detailScroll=Vector2.zero;
            if (inspector is FactsInspector facts) facts.FocusFact(current.node);
            if (graph!=null && current.page==Page.Graph && current.node!=null){graph.FocusNode(current.node);graph.FocusReply(current.reply);}
            Repaint();
        }
        private void ChangePage(Page page) => Visit(new Location{asset=current.asset,page=page,person=current.person,node=current.asset is WorldFactDatabase?current.node:null});
        private void Back()
        {
            Visit(history.Count>0?history.Pop():new Location(),false);
        }
        private void Browse(Object asset,string node=null)
        {
            Visit(new Location{asset=asset,node=node,page=IsGraph(asset)?Page.Graph:Page.Settings});
        }
        private void Queue(Action action)
        {
            // Switching documents can destroy a serialized inspector or change its control tree.
            EditorApplication.delayCall+=()=>{if(this)action();};
        }
        private void OnGUI()
        {
            if(current.page!=Page.Library && !current.asset && current.page!=Page.Character)
            { Release();current=default; }
            using(new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                using(new EditorGUI.DisabledScope(history.Count==0))
                    if(GUILayout.Button("‹ Back",EditorStyles.toolbarButton,GUILayout.Width(58)))Queue(Back);
                if(GUILayout.Toggle(current.page==Page.Library,"Library",EditorStyles.toolbarButton,GUILayout.Width(62)) && current.page!=Page.Library)
                    Queue(()=>Visit(default));
                if(current.asset)
                {
                    GUILayout.Label(NameOf(current.asset),EditorStyles.boldLabel,GUILayout.MaxWidth(position.width*.32f));
                    if(IsGraph(current.asset))Tab(Page.Graph,"Graph");
                    Tab(Page.Settings,"Settings");
                    if(current.asset is WorldFactDatabase || NarrativeCatalog.Kind(current.asset)!=NarrativeCatalog.PresetKind.All)Tab(Page.Usages,"Where used");
                    if(current.asset is QuestDefinition)Tab(Page.Tests,"Tests");
                    if(IsGraph(current.asset)||current.asset is SpeakerProfile)Tab(Page.Connections,"Connections");
                }
                GUILayout.FlexibleSpace();
                using(new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if(GUILayout.Button("Create ▾",EditorStyles.toolbarDropDown,GUILayout.Width(73)))CreateMenu();
                    if(current.asset && AssetDatabase.Contains(current.asset) && GUILayout.Button("Save",EditorStyles.toolbarButton,GUILayout.Width(45)))
                    {
                        AssetDatabase.SaveAssetIfDirty(current.asset);
                        if(current.page==Page.Settings && showFacts && factsInspector)
                            AssetDatabase.SaveAssetIfDirty(factsInspector.target);
                    }
                }
            }
            var area=new Rect(0,Header,position.width,Mathf.Max(1f,position.height-Header));
            GUILayout.BeginArea(area);
            try
            {
                if(current.page==Page.Library)DrawLibrary();
                else if(current.page==Page.Character)DrawCharacter(Catalog.speakers.FirstOrDefault(p=>p.key==current.person));
                else
                {
                    if(!inspector)
                    {
                        inspector=UnityEditor.Editor.CreateEditor(current.asset);
                        if(inspector is FactsInspector facts)facts.FocusFact(current.node);
                    }
                    var adapter=inspector as INarrativeGraphInspector;
                    if(current.page==Page.Graph && adapter!=null)
                    {
                        if(graph==null)
                        {
                            graph=adapter.CreatePanel(this);
                            if(!string.IsNullOrEmpty(current.node))graph.FocusNode(current.node);
                            graph.FocusReply(current.reply);
                        }
                        using(new EditorGUI.DisabledScope(!CanEdit(current.asset) && !Application.isPlaying))graph.Draw(area.size);
                        if(GUI.changed)catalog=null;
                    }
                    else
                    {
                        detailScroll=EditorGUILayout.BeginScrollView(detailScroll);
                        if(current.page==Page.Settings)DrawSettings(adapter);
                        else if(current.page==Page.Usages)DrawWhereUsed();
                        else if(current.page==Page.Tests)DrawTests((QuestDefinition)current.asset);
                        else DrawConnections();
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
            finally{GUILayout.EndArea();}
        }
        private void Tab(Page page,string label)
        {
            if(GUILayout.Toggle(current.page==page,label,EditorStyles.toolbarButton,GUILayout.Width(84)) && current.page!=page)
                Queue(()=>ChangePage(page));
        }
        private static bool CanEdit(Object asset) => asset && AssetDatabase.IsOpenForEdit(asset);
        private sealed class LibraryItem
        {
            internal Object asset;
            internal string person, factId, label, detail;
        }
        private readonly List<LibraryItem> libraryItems = new();
        private NarrativeCatalog listedCatalog;
        private Category listedCategory;
        private string listedSearch;

        private void FilterLibrary()
        {
            var source = Catalog;
            if (listedCatalog == source && listedCategory == category && listedSearch == search) return;
            libraryItems.Clear();
            bool Matches(string value) => string.IsNullOrEmpty(search) ||
                (value ?? "").IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0;
            if (category == Category.Characters)
            {
                foreach (var person in source.speakers)
                {
                    if (!Matches(person.name) && !Matches(person.profile ? person.profile.name : person.key)) continue;
                    string kind = person.profile ? person.profile.name : person.key switch {
                        "player" => "Player choices", "narration" => "Lines without a speaker", _ => "Inline speaker" };
                    string count = person.key == "player" ? person.lines.Sum(use=>use.line.replies.Count(r=>r!=null))+" replies" :
                        person.lines.Count+(person.lines.Count==1 ? " line" : " lines");
                    libraryItems.Add(new LibraryItem {asset=person.profile,person=person.key,label=person.name,detail=kind+"  ·  "+count});
                }
            }
            else if(category==Category.Presets)ListPresets(source,Matches);
            else if (category == Category.WorldFacts)
            {
                foreach (var db in source.factDatabases)
                {
                    string path = AssetDatabase.GetAssetPath(db);
                    bool databaseMatches = Matches(db.name) || Matches(path);
                    if (databaseMatches)
                        libraryItems.Add(new LibraryItem { asset=db, label=db.name, detail="Fact database  ·  "+path });
                    foreach (var fact in db.facts ?? Array.Empty<WorldFact>())
                    {
                        if (fact == null || !(databaseMatches || Matches(fact.name) || Matches(fact.id))) continue;
                        libraryItems.Add(new LibraryItem {
                            asset=db, factId=fact.id, label=string.IsNullOrWhiteSpace(fact.name)?fact.id:fact.name,
                            detail=db.name+"  ·  "+fact.type+"  ·  "+fact.id
                        });
                    }
                }
            }
            else
            {
                IEnumerable<Object> assets = category == Category.Dialogues ? source.dialogues.Cast<Object>() : source.quests.Cast<Object>();
                foreach (var asset in assets)
                {
                    string path = AssetDatabase.GetAssetPath(asset);
                    if (Matches(NameOf(asset)) || Matches(path))
                        libraryItems.Add(new LibraryItem {asset=asset,label=NameOf(asset),detail=path});
                }
            }
            if (listedCategory!=category || listedSearch!=search) listScroll=Vector2.zero;
            listedCatalog=source;listedCategory=category;listedSearch=search;
        }
        private void DrawLibrary()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                category=(Category)GUILayout.Toolbar((int)category,new[]{"Characters","Dialogues","Quests","World Facts","Presets"},GUILayout.MaxWidth(550));
                search=EditorGUILayout.TextField(search,EditorStyles.toolbarSearchField,GUILayout.MinWidth(130));
            }
            if(category==Category.Presets)DrawPresetFilter();
            FilterLibrary();
            if (libraryItems.Count==0)
            {
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(search)?"No entries yet. Use Create to add the first asset.":"No matching entries.",MessageType.Info);
                return;
            }
            const float pitch=26f;
            float top=category==Category.Presets?49:25;
            var frame=new Rect(2,top,position.width-4,Mathf.Max(1,position.height-Header-top));
            float height=pitch*libraryItems.Count;
            float width=frame.width-(height>frame.height?16:0);
            listScroll=GUI.BeginScrollView(frame,listScroll,new Rect(0,0,width,height));
            int first=Mathf.Max(0,Mathf.FloorToInt(listScroll.y/pitch));
            int last=Mathf.Min(libraryItems.Count,first+Mathf.CeilToInt(frame.height/pitch)+1);
            for(int i=first;i<last;i++)
            {
                var item=libraryItems[i];
                if(Row(new Rect(0,i*pitch,width,pitch-2),item.label,item.detail))
                    Queue(()=>{if(item.asset)Browse(item.asset,item.factId);else Visit(new Location{page=Page.Character,person=item.person});});
            }
            GUI.EndScrollView();
        }
        private static bool Row(string name,string detail) => Row(EditorGUILayout.GetControlRect(false,24),name,detail);
        private static bool Row(Rect row,string name,string detail)
        {
            bool clicked=GUI.Button(row,new GUIContent("",detail),EditorStyles.helpBox);
            float column=Mathf.Min(295f,row.width*.39f);
            var title=new Rect(row.x+7,row.y+2,column-12,row.height-4);
            GUI.Label(title,new GUIContent(name,name),EditorStyles.linkLabel);
            GUI.Label(new Rect(row.x+column,row.y+2,row.width-column-6,row.height-4),new GUIContent(detail,detail),EditorStyles.miniLabel);
            EditorGUIUtility.AddCursorRect(row,MouseCursor.Link);
            return clicked;
        }
        private void DrawSettings(INarrativeGraphInspector adapter)
        {
            using(new EditorGUI.DisabledScope(Application.isPlaying || !CanEdit(current.asset)))
            {
                EditorGUI.BeginChangeCheck();
                if(AssetDatabase.IsMainAsset(current.asset))
                {
                    string name=EditorGUILayout.DelayedTextField("Asset name",current.asset.name);
                    if(name!=current.asset.name && !string.IsNullOrWhiteSpace(name))
                    {
                        var asset=current.asset;
                        Queue(()=>{string error=AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(asset),name.Trim());
                            if(!string.IsNullOrEmpty(error))ShowNotification(new GUIContent(error));
                            Invalidate();});
                    }
                }
                if(adapter!=null)adapter.DrawSettings();else inspector.OnInspectorGUI();
                if(EditorGUI.EndChangeCheck()){catalog=null;if(current.asset is DialogueDefinition d)d.Invalidate();}
            }
            if(current.asset is SpeakerProfile profile)
            {
                var person=Catalog.speakers.FirstOrDefault(p=>p.profile==profile);
                if(person!=null)DrawLines(person);
            }
            if(!IsGraph(current.asset))return;
            var db=current.asset is QuestDefinition q?q.database:((DialogueDefinition)current.asset).database;
            if(db)
            {
                showFacts=EditorGUILayout.Foldout(showFacts,"Edit world facts · "+db.name,true);
                if(showFacts)
                {
                    UnityEditor.Editor.CreateCachedEditor(db,null,ref factsInspector);
                    using(new EditorGUI.DisabledScope(Application.isPlaying || !CanEdit(db)))factsInspector.OnInspectorGUI();
                }
            }
            else
            {
                if(current.asset is QuestDefinition)
                    EditorGUILayout.HelpBox("Assign a fact database before validating or starting this quest.",MessageType.Info);
                using(new EditorGUI.DisabledScope(Application.isPlaying || !CanEdit(current.asset)))
                    if(GUILayout.Button("Create and assign a fact database…"))
                    {
                        var owner=current.asset;
                        string path=EditorUtility.SaveFilePanelInProject("Create fact database","World facts","asset","Choose a new fact database.");
                        if(!string.IsNullOrEmpty(path) && !File.Exists(path))
                        {
                            var created=(WorldFactDatabase)CreateDefinition(typeof(WorldFactDatabase),path,null,null);
                            Undo.RecordObject(owner,"Assign new fact database");
                            if(owner is QuestDefinition quest)quest.database=created;
                            else { var dialogue=(DialogueDefinition)owner; dialogue.database=created;dialogue.Invalidate(); }
                            EditorUtility.SetDirty(owner);AssetDatabase.SaveAssetIfDirty(owner);showFacts=true;Invalidate();
                        }
                    }
            }
        }
        private void DrawCharacter(NarrativeCatalog.Speaker person)
        {
            detailScroll=EditorGUILayout.BeginScrollView(detailScroll);
            if(person==null)EditorGUILayout.HelpBox("This speaker has no remaining lines.",MessageType.Info);
            else
            {
                GUILayout.Label(person.name,EditorStyles.boldLabel);
                if(person.key.StartsWith("name/",StringComparison.Ordinal)||person.key.StartsWith("key/",StringComparison.Ordinal))
                {
                    using(new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        bindProfile=(SpeakerProfile)EditorGUILayout.ObjectField("Speaker profile",bindProfile,typeof(SpeakerProfile),false);
                        using(new EditorGUI.DisabledScope(!bindProfile || person.lines.Any(use=>!CanEdit(use.dialogue))))
                            if(GUILayout.Button("Assign profile to these "+person.lines.Count+" lines"))
                            {
                                var profile=bindProfile;
                                var affected=person.lines.Select(use=>use.dialogue).Distinct().ToArray();
                                Undo.RecordObjects(affected,"Assign speaker profile to dialogue lines");
                                foreach(var use in person.lines)use.line.profile=profile;
                                foreach(var dialogue in affected){dialogue.Invalidate();EditorUtility.SetDirty(dialogue);AssetDatabase.SaveAssetIfDirty(dialogue);}
                                Queue(()=>{Invalidate();Browse(profile);});
                            }
                    }
                }
                DrawLines(person);
            }
            EditorGUILayout.EndScrollView();
        }
        private void DrawLines(NarrativeCatalog.Speaker person)
        {
            GUILayout.Label("Dialogue lines",EditorStyles.boldLabel);
            if(person.lines.Count==0)EditorGUILayout.LabelField("No dialogue lines use this profile yet.");
            foreach(var (dialogue,line) in person.lines)
                if(Row(dialogue.name+" / "+line.name,person.key=="player"?string.Join("  ·  ",line.replies.Where(r=>r!=null).Select(r=>r.text)):line.text))
                    Queue(()=>Browse(dialogue,line.id));
        }
        private void DrawConnections()
        {
            if(current.asset is SpeakerProfile profile)
            {
                var person=Catalog.speakers.FirstOrDefault(p=>p.profile==profile);
                if(person!=null)DrawLines(person);
                return;
            }
            if(current.asset is DialogueDefinition dialogue)
            {
                GUILayout.Label("Participants",EditorStyles.boldLabel);
                foreach(var person in Catalog.Cast(dialogue))
                    if(Row(person.name,person.profile?"Speaker profile":"Dialogue lines"))
                        Queue(()=>{if(person.profile)Browse(person.profile);else Visit(new Location{page=Page.Character,person=person.key});});
                LinkedAssets("Referenced quests",Catalog.Quests(dialogue));
                GUILayout.Label("NPCs in loaded scenes",EditorStyles.boldLabel);
                sceneSpeakers??=Object.FindObjectsByType<TalkAction>(FindObjectsInactive.Include,FindObjectsSortMode.None);
                foreach(var talk in sceneSpeakers)
                    if(talk && talk.dialogue==dialogue && Row(talk.name,talk.gameObject.scene.name))EditorGUIUtility.PingObject(talk);
                ambientSpeakers??=Object.FindObjectsByType<DialogueBark>(FindObjectsInactive.Include,FindObjectsSortMode.None);
                foreach(var bark in ambientSpeakers)
                    if(bark && bark.script==dialogue && Row(bark.name,bark.gameObject.scene.name+" · ambient line"))EditorGUIUtility.PingObject(bark);
                using(new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    npc=(TalkAction)EditorGUILayout.ObjectField("Assign to Talk Action",npc,typeof(TalkAction),true);
                    using(new EditorGUI.DisabledScope(!npc || !npc.gameObject.scene.IsValid() || npc.dialogue==dialogue))
                        if(GUILayout.Button("Assign dialogue to selected NPC"))
                        {
                            Undo.RecordObject(npc,"Assign NPC dialogue");npc.dialogue=dialogue;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(npc);EditorSceneManager.MarkSceneDirty(npc.gameObject.scene);
                        }
                }
            }
            else if(current.asset is QuestDefinition quest)
            {
                LinkedAssets("Dialogues referencing this quest",Catalog.Dialogues(quest));
                GUILayout.Label("Quest worlds in loaded scenes",EditorStyles.boldLabel);
                sceneWorlds??=Object.FindObjectsByType<QuestWorld>(FindObjectsInactive.Include,FindObjectsSortMode.None);
                foreach(var owner in sceneWorlds)
                    if(owner && owner.quests!=null && Array.IndexOf(owner.quests,quest)>=0 && Row(owner.name,owner.gameObject.scene.name))EditorGUIUtility.PingObject(owner);
                using(new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    world=(QuestWorld)EditorGUILayout.ObjectField("Register in quest world",world,typeof(QuestWorld),true);
                    bool compatible=world && world.gameObject.scene.IsValid() && quest.database && world.database==quest.database;
                    if(world && !compatible)EditorGUILayout.HelpBox("The quest and its world must share a fact database.",MessageType.Info);
                    var registered=world ? world.quests??Array.Empty<QuestDefinition>() : Array.Empty<QuestDefinition>();
                    bool duplicateId=registered.Any(existing=>existing && existing!=quest && existing.id==quest.id);
                    if(duplicateId)EditorGUILayout.HelpBox("Another quest in this world has the same permanent ID.",MessageType.Warning);
                    using(new EditorGUI.DisabledScope(!compatible || duplicateId || registered.Contains(quest)))
                        if(GUILayout.Button("Register quest"))
                        {
                            Undo.RecordObject(world,"Register quest");
                            world.quests=(world.quests??Array.Empty<QuestDefinition>()).Append(quest).ToArray();
                            PrefabUtility.RecordPrefabInstancePropertyModifications(world);EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
                        }
                }
            }
            var related=Catalog.SharedKeys(current.asset);
            if(related.Any())GUILayout.Label("Shared fact and event keys",EditorStyles.boldLabel);
            foreach(var link in related)
            {
                if (Row(NameOf(link.asset)+" / "+link.title,link.reason))Queue(()=>Browse(link.asset,link.node));
            }
        }
        private void LinkedAssets<T>(string caption,IEnumerable<T> assets) where T:Object
        {
            GUILayout.Label(caption,EditorStyles.boldLabel);
            int count=0;
            foreach(var asset in assets){count++;if(Row(NameOf(asset),AssetDatabase.GetAssetPath(asset)))Queue(()=>Browse(asset));}
            if(count==0)EditorGUILayout.LabelField("No direct quest references.");
        }
        private void CreateMenu()
        {
            var menu=new GenericMenu();
            void Add<T>(string label) where T:ScriptableObject =>
                menu.AddItem(new GUIContent(label),false,()=>Create(typeof(T),label));
            Add<QuestDefinition>("Quest");Add<DialogueDefinition>("Dialogue");Add<SpeakerProfile>("Speaker profile");Add<WorldFactDatabase>("World fact database");
            AddPresetMenu(menu);
            menu.ShowAsContext();
        }
        private void Create(Type type,string label)
        {
            string start=current.asset?Path.GetDirectoryName(AssetDatabase.GetAssetPath(current.asset)):"Assets";
            if(string.IsNullOrEmpty(start)||!(start=="Assets"||start.StartsWith("Assets/")))start="Assets";
            string path=EditorUtility.SaveFilePanelInProject("Create "+label,"New "+label,"asset","Choose a new asset path.",start);
            if(string.IsNullOrEmpty(path))return;
            if(File.Exists(path)){ShowNotification(new GUIContent("Choose an unused asset name."));return;}
            WorldFactDatabase db=current.asset is QuestDefinition q?q.database:current.asset is DialogueDefinition d?d.database:current.asset as WorldFactDatabase;
            if(!db)
            {
                var available=NarrativeCatalog.Find<WorldFactDatabase>().Take(2).ToArray();
                if(available.Length==1)db=available[0];
            }
            var asset=CreateDefinition(type,path,db,current.asset as SpeakerProfile);
            if(asset is NarrativeScenario scenario && current.asset is QuestDefinition tested)
            {
                scenario.catalog=new[]{tested};scenario.database=tested.database;
                scenario.commands=new[]{new ScenarioCommand{action=ScenarioAction.Start,quest=tested}};
                EditorUtility.SetDirty(scenario);AssetDatabase.SaveAssetIfDirty(scenario);
            }
            Invalidate();
            if(asset is QuestDefinition created && !created.database)Visit(new Location{asset=asset,page=Page.Settings});
            else Browse(asset);
        }
        internal static ScriptableObject CreateDefinition(Type type,string path,WorldFactDatabase database,SpeakerProfile speaker)
        {
            if(string.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||Path.GetExtension(path)!=".asset"||File.Exists(path))
                throw new ArgumentException("Choose an unused .asset path under Assets.");
            var asset=CreateInstance(type);
            try
            {
                string title=Path.GetFileNameWithoutExtension(path);
                if(asset is QuestDefinition q)
                {
                    q.title=title;q.database=database;
                    var finish=new QuestStep{name="Complete",type=QuestStepType.End,position=new Vector2(490,60)};
                    var goal=new QuestStep{name="Objective",type=QuestStepType.Objectives,next=finish.id,position=new Vector2(260,60),
                        goals=new[]{new QuestGoal{title="Complete the objective",mode=GoalMode.NewEvents,signal="quest."+q.id+".complete"}}};
                    q.steps=new[]{new QuestStep{name="Start",type=QuestStepType.Start,next=goal.id,position=new Vector2(30,60)},goal,finish};
                }
                else if(asset is DialogueDefinition d)
                {
                    d.database=database;
                    var line=new DialogueLine{name="Greeting",profile=speaker,text="Hello.",position=new Vector2(30,60)};
                    d.entry=line.id;d.lines=new[]{line};
                }
                else if(asset is SpeakerProfile p)p.displayName=title;
                else if(!(asset is WorldFactDatabase) && !(asset is NarrativeScenario) && NarrativeCatalog.Kind(asset)==NarrativeCatalog.PresetKind.All)throw new ArgumentException("Unsupported narrative asset type.");
                AssetDatabase.CreateAsset(asset,path);AssetDatabase.SaveAssetIfDirty(asset);return asset;
            }
            catch{if(!AssetDatabase.Contains(asset))DestroyImmediate(asset);throw;}
        }
    }
}
