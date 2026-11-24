using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyQuests;
using EasyQuests.Editor;
using UnityEditor;
using UnityEngine;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkNarrativeSuite
    {
        public static void Apply(EasySuiteLab lab)
        {
            var world=lab.GetComponent<QuestWorld>();
            var workshop=world.quests.Single(q=>q.id=="suite.workshop");
            string directory=EasySuiteSceneBuilder.Content+"/Editor/Narrative Scenarios";
            if(!AssetDatabase.IsValidFolder(EasySuiteSceneBuilder.Content+"/Editor"))
                AssetDatabase.CreateFolder(EasySuiteSceneBuilder.Content,"Editor");
            if(!AssetDatabase.IsValidFolder(directory))AssetDatabase.CreateFolder(EasySuiteSceneBuilder.Content+"/Editor","Narrative Scenarios");
            int coins=checked((int)lab.Coin.UnitsPerWhole);
            int crystals=checked((int)lab.Crystal.UnitsPerWhole*3);
            ScenarioCommand Cmd(ScenarioAction act,string key=null,int value=1) => new ScenarioCommand{action=act,key=key,amount=value,quest=workshop};
            ScenarioCommand Currency(ScenarioAction act,EasyResources.ResourceDefinition resource,int amount) =>
                new ScenarioCommand{action=act,resource=resource,amount=amount};
            void Store(string name,params ScenarioCommand[] commands)
            {
                string path=directory+"/"+name+".asset";
                if(AssetDatabase.LoadAssetAtPath<NarrativeScenario>(path))return;
                var test=ScriptableObject.CreateInstance<NarrativeScenario>();
                test.database=world.database;test.catalog=new[]{workshop};test.commands=commands;
                test.Run();AssetDatabase.CreateAsset(test,path);
            }
            Store("Workshop - early crystals and reload",Currency(ScenarioAction.Receive,lab.Crystal,crystals),Cmd(ScenarioAction.Start),
                Cmd(ScenarioAction.ExpectStep,"give"),Cmd(ScenarioAction.SaveCheckpoint),Cmd(ScenarioAction.HandIn,"pay"),
                Currency(ScenarioAction.ExpectBalance,lab.Crystal,0),Currency(ScenarioAction.ExpectBalance,lab.Coin,5*coins),
                Cmd(ScenarioAction.LoadCheckpoint),Cmd(ScenarioAction.HandIn,"pay"),Currency(ScenarioAction.ExpectBalance,lab.Coin,5*coins),
                Cmd(ScenarioAction.ExpectFinished));
            Store("Workshop - alternative payout",Cmd(ScenarioAction.Start),Cmd(ScenarioAction.SetFact,"workshop.calibration"),Cmd(ScenarioAction.ExpectStep,"tune"),
                Cmd(ScenarioAction.Signal,"workshop.tuned"),Currency(ScenarioAction.ExpectBalance,lab.Coin,0),Cmd(ScenarioAction.SaveCheckpoint),
                Cmd(ScenarioAction.Collect),Currency(ScenarioAction.ExpectBalance,lab.Coin,8*coins),Cmd(ScenarioAction.LoadCheckpoint),
                Cmd(ScenarioAction.Collect),Currency(ScenarioAction.ExpectBalance,lab.Coin,8*coins));
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/EasyGameFramework/Examples/Narrative/Open Ferryman in Narrative Studio")]
        private static void OpenFerryman()
        {
            var dialogue=AssetDatabase.LoadAssetAtPath<EasyNarrative.DialogueDefinition>(EasySuiteSceneBuilder.Content+"/Dialogue/Ferryman.asset");
            if(dialogue)NarrativeStudio.OpenAsset(dialogue,"advice-offer");
            else EditorUtility.DisplayDialog("Narrative Studio","Create the Framework Playground to open the Ferryman example.","OK");
        }

        [MenuItem("Tools/EasyGameFramework/Examples/Narrative/Inspect crystal hand-in")]
        private static void Inspect()
        {
            var q=AssetDatabase.LoadAssetAtPath<QuestDefinition>(EasySuiteSceneBuilder.Content+"/Quests/Workshop Order.asset");
            if(!q){EditorUtility.DisplayDialog("Workshop","Create the Framework Playground first.","OK");return;}
            QuestGraphWindow.OpenAt(q,"give");QuestInspectorWindow.Open(q,"give");
        }
    }
}
