using System;
using System.Linq;
using EasyResources;
using UnityEditor;
using UnityEngine;

namespace EasyQuests.Editor
{
    public sealed class QuestVariantWindow : EditorWindow
    {
        [SerializeField] private QuestDefinition template;
        [SerializeField] private ResourceDefinition replaceResource, withResource;
        [SerializeField] private string title, oldTarget, newTarget;
        private string status;

        public static void Open(QuestDefinition source)
        {
            var w=CreateInstance<QuestVariantWindow>();w.template=source;w.title=source.title+" variant";
            w.titleContent=new GUIContent("Quest from template");w.ShowUtility();
        }
        public static QuestDefinition Make(QuestDefinition source,string newTitle,ResourceDefinition from,ResourceDefinition to,string target,string replacement)
        {
            if(!source || string.IsNullOrWhiteSpace(newTitle) || from&&!to || !string.IsNullOrEmpty(target)&&string.IsNullOrWhiteSpace(replacement))
                throw new ArgumentException("Supply a title and the replacement for each selected field.");
            var q=Instantiate(source);
            try
            {
                EasyNarrative.Editor.NarrativeTextCollector.PrepareCopy(q);
                q.id=Guid.NewGuid().ToString("N");q.title=newTitle;q.titleKey="";
                foreach(var s in q.steps)
                    foreach(var g in s.goals)
                    {
                        if(from && g.resource==from)
                        {
                            int units=checked((int)to.ToUnits(from.FromUnits(g.count)));g.resource=to;g.count=units;
                            if(!string.IsNullOrEmpty(from.DisplayName) && g.title.Contains(from.DisplayName))
                            {g.title=g.title.Replace(from.DisplayName,to.DisplayName);g.textKey="";}
                        }
                        if(!string.IsNullOrEmpty(target)&&g.target==target)g.target=replacement;
                    }
                q.ValidateGraph();return q;
            }
            catch {DestroyImmediate(q);throw;}
        }
        private void OnGUI()
        {
            template=(QuestDefinition)EditorGUILayout.ObjectField("Template",template,typeof(QuestDefinition),false);
            title=EditorGUILayout.TextField("Quest title",title);
            replaceResource=(ResourceDefinition)EditorGUILayout.ObjectField("Replace resource",replaceResource,typeof(ResourceDefinition),false);
            if(replaceResource)withResource=(ResourceDefinition)EditorGUILayout.ObjectField("With resource",withResource,typeof(ResourceDefinition),false);
            var targets=template?template.steps.SelectMany(s=>s.goals).Select(g=>g.target).Where(t=>!string.IsNullOrEmpty(t)).Distinct().ToArray():Array.Empty<string>();
            int index=EditorGUILayout.Popup("Replace marker target",Array.IndexOf(targets,oldTarget)+1,new[]{"Keep markers"}.Concat(targets).ToArray());
            oldTarget=index==0?null:targets[index-1];
            if(index>0)newTarget=EditorGUILayout.TextField("New target",newTarget);
            EditorGUILayout.HelpBox("The new graph has its own quest identity. Shared world events, facts and translation keys keep their meaning. Bind quest-specific hand-ins to the new graph.",MessageType.Info);
            if(status!=null)EditorGUILayout.HelpBox(status,MessageType.Error);
            if(GUILayout.Button("Save and open"))
            {
                string path=EditorUtility.SaveFilePanelInProject("New quest",title,"asset","Choose a new asset.");
                if(string.IsNullOrEmpty(path))return;
                if(System.IO.File.Exists(path)){status="Choose an unused asset path.";return;}
                try
                {
                    var graph=Make(template,title,replaceResource,withResource,oldTarget,newTarget);
                    AssetDatabase.CreateAsset(graph,path);AssetDatabase.SaveAssetIfDirty(graph);QuestGraphWindow.Open(graph);Close();
                }
                catch(Exception e){status=e.Message;}
            }
        }
    }
}
