using System;
using System.Collections.Generic;
using System.Linq;
using EasyNarrative;
using EasyResources;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace EasyQuests.Editor
{
    public sealed partial class NarrativeStudio
    {
        [SerializeField] private NarrativeCatalog.PresetKind presetFilter;
        [SerializeField] private bool includeUnusedRewards;
        private string selectedIcon;
        private readonly Dictionary<NarrativeScenario,(bool passed,string report)> testResults=new();

        public static void PresetField(SerializedProperty property,GUIContent label)
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(property,label);
                if(property.objectReferenceValue && GUILayout.Button(new GUIContent("Open","Open this preset in Narrative Studio"),GUILayout.Width(45)))
                    OpenAsset(property.objectReferenceValue);
            }
        }
        private void DrawPresetFilter()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                presetFilter=(NarrativeCatalog.PresetKind)EditorGUILayout.EnumPopup("Preset type",presetFilter,GUILayout.MaxWidth(340));
                if(presetFilter==NarrativeCatalog.PresetKind.All || presetFilter==NarrativeCatalog.PresetKind.Rewards)
                    includeUnusedRewards=GUILayout.Toggle(includeUnusedRewards,"Include rewards unused by quests");
                if(EditorGUI.EndChangeCheck()){listedCatalog=null;listScroll=Vector2.zero;}
            }
        }
        private void ListPresets(NarrativeCatalog source,Func<string,bool> matches)
        {
            foreach(var preset in source.Presets)
            {
                var kind=NarrativeCatalog.Kind(preset);
                if(presetFilter!=NarrativeCatalog.PresetKind.All && kind!=presetFilter)continue;
                var uses=source.Uses(preset);
                if(kind==NarrativeCatalog.PresetKind.Rewards && !includeUnusedRewards && uses.Count==0)continue;
                var path=AssetDatabase.GetAssetPath(preset);
                if(matches(preset.name)||matches(path))
                    libraryItems.Add(new LibraryItem{asset=preset,label=preset.name,detail=kind+"  ·  "+path});
            }
        }
        private void DrawWhereUsed()
        {
            if(current.asset is WorldFactDatabase db)
            {
                var facts=(db.facts??Array.Empty<WorldFact>()).Where(f=>f!=null && !string.IsNullOrEmpty(f.id)).ToArray();
                if(facts.Length==0){EditorGUILayout.HelpBox("Add a fact in Settings first.",MessageType.Info);return;}
                int index=Array.FindIndex(facts,f=>f.id==current.node);
                var names=new[]{"Select a fact"}.Concat(facts.Select(f=>f.name+"  ·  "+f.id)).ToArray();
                int choice=EditorGUILayout.Popup("World fact",index+1,names);
                if(choice!=index+1)current.node=choice==0?null:facts[choice-1].id;
                if(string.IsNullOrEmpty(current.node))return;
                DrawSites("Writes this fact",Catalog.FactSites(db,current.node,true));
                DrawSites("Reads this fact",Catalog.FactSites(db,current.node,false));
                return;
            }
            var uses=Catalog.Uses(current.asset);
            if(current.asset is QuestIconLibrary library)
            {
                var icons=(library.icons??Array.Empty<QuestIcon>()).Where(i=>i!=null).ToArray();
                int previous=Array.FindIndex(icons,i=>i.Id==selectedIcon);
                int choice=EditorGUILayout.Popup("Icon",previous+1,new[]{"All icons"}.Concat(icons.Select(i=>i.name)).ToArray());
                selectedIcon=choice==0?null:icons[choice-1].Id;
                foreach(var icon in icons)
                {
                    if(selectedIcon!=null && icon.Id!=selectedIcon)continue;
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        var rect=GUILayoutUtility.GetRect(36,36,GUILayout.Width(36));
                        if(icon.sprite)
                        {
                            var preview=AssetPreview.GetAssetPreview(icon.sprite)??AssetPreview.GetMiniThumbnail(icon.sprite);
                            if(preview)GUI.DrawTexture(rect,preview,ScaleMode.ScaleToFit);
                            if(AssetPreview.IsLoadingAssetPreview(icon.sprite.GetInstanceID()))Repaint();
                        }
                        if(GUILayout.Button(icon.name,EditorStyles.linkLabel))selectedIcon=icon.Id;
                    }
                }
                DrawSites("Quest and objective usages",uses.Where(link=>selectedIcon==null||link.icon==selectedIcon));
            }
            else DrawSites("Graph usages",uses);

            if(current.asset is FactSkillCheck skill && !string.IsNullOrEmpty(skill.threshold?.fact))
            {
                foreach(var database in uses.Select(u=>(u.asset as DialogueDefinition)?.database).Where(db=>db).Distinct())
                    if(Row(skill.threshold.fact,database.name+" · World fact"))
                        Queue(()=>Visit(new Location{asset=database,node=skill.threshold.fact,page=Page.Usages}));
            }
        }
        private void DrawSites(string heading,IEnumerable<NarrativeCatalog.StoryLink> sites)
        {
            GUILayout.Label(heading,EditorStyles.boldLabel);
            bool any=false;
            foreach(var site in sites)
            {
                any=true;
                if(!Row(site.asset is Component ? site.title : NameOf(site.asset)+" / "+site.title,site.reason))continue;
                if(site.asset is Component c){Selection.activeGameObject=c.gameObject;EditorGUIUtility.PingObject(c);}
                else Queue(()=>Visit(new Location{asset=site.asset,node=site.node,reply=site.reply,page=site.node==null?Page.Settings:Page.Graph}));
            }
            if(!any)GUILayout.Label("No indexed usages.",EditorStyles.miniLabel);
        }
        private void DrawTests(QuestDefinition quest)
        {
            var scenarios=Catalog.Scenarios.Where(s=>(s.catalog??Array.Empty<QuestDefinition>()).Contains(quest) ||
                (s.commands??Array.Empty<ScenarioCommand>()).Any(c=>c?.quest==quest)).ToArray();
            if(scenarios.Length==0)EditorGUILayout.HelpBox("No test scenarios reference this quest.",MessageType.Info);
            using(new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if(GUILayout.Button("Create scenario for this quest…"))Create(typeof(NarrativeScenario),"Test scenario");
                foreach(var scenario in scenarios)
                {
                    using(new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        if(GUILayout.Button(scenario.name,EditorStyles.linkLabel))Queue(()=>Browse(scenario));
                        if(GUILayout.Button("Run",GUILayout.Width(52)))RunTest(scenario);
                    }
                    if(testResults.TryGetValue(scenario,out var result))
                        EditorGUILayout.HelpBox(result.report,result.passed?MessageType.Info:MessageType.Error);
                }
            }
        }
        private void RunTest(NarrativeScenario scenario)
        {
            try { testResults[scenario]=(true,scenario.Run()); }
            catch(Exception ex){testResults[scenario]=(false,ex.Message);}
        }
        private void AddPresetMenu(GenericMenu menu)
        {
            void Add(Type type,string title)=>menu.AddItem(new GUIContent("Presets/"+title),false,()=>Create(type,ObjectNames.NicifyVariableName(type.Name)));
            foreach(var baseType in new[]{typeof(DialogueCondition),typeof(DialogueSkillCheck),typeof(DialogueEffect)})
                foreach(var type in TypeCache.GetTypesDerivedFrom(baseType).Where(t=>!t.IsAbstract&&!t.ContainsGenericParameters&&t.IsPublic).OrderBy(t=>t.FullName))
                    Add(type,ObjectNames.NicifyVariableName(baseType.Name)+"/"+ObjectNames.NicifyVariableName(type.Name));
            Add(typeof(ResourceRewardProfile),"Reward profile");
            Add(typeof(QuestIconLibrary),"Quest icon library");
        }
    }
}
