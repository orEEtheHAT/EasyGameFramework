using System;
using System.IO;
using EasyMovement.FirstPerson;
using UnityEditor;
using UnityEngine;

namespace EasyCombat.Integrations.Editor
{
    public static class CombatPlayerPrefabBuilder
    {
        [MenuItem("Tools/EasyGameFramework/Combat/Integrations/Create EasyMovement Combat Player")]
        public static void Create() => CreatePrefab();
        public static string CreatePrefab(CombatStatDefinition speedStat=null)
        {
            const string source="Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab";
            var template=AssetDatabase.LoadAssetAtPath<GameObject>(source);
            var original=template ? template.GetComponentInChildren<FirstPersonCharacterController>(true) : null;
            if(!original)
                throw new InvalidOperationException("Install EasyMovement with its first-person sample prefab.");
            string originGuid=AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(original)));
            string scriptGuid=null;
            foreach(var guid in AssetDatabase.FindAssets("CombatFirstPersonController t:MonoScript"))
                if(AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid)).GetClass()==typeof(CombatFirstPersonController)) { scriptGuid=guid; break; }
            if(scriptGuid==null) throw new InvalidOperationException("Combat FPS adapter script is missing from EasyGameFramework.");
            string root=UnityEditor.PackageManager.PackageInfo.FindForAssetPath(source).resolvedPath;
            string yaml=File.ReadAllText(Path.Combine(root,"Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab"));
            // Keep file IDs so input, cameras and serialized private gait settings still reference the same component.
            yaml=yaml.Replace("\r\n","\n");
            string from="m_Script: {fileID: 11500000, guid: "+originGuid+", type: 3}";
            if(yaml.IndexOf(from,StringComparison.Ordinal)<0 || yaml.IndexOf(from,StringComparison.Ordinal)!=yaml.LastIndexOf(from,StringComparison.Ordinal))
                throw new InvalidOperationException("Expected exactly one FPS script in the text prefab.");
            yaml=yaml.Replace(from,from.Replace(originGuid,scriptGuid)).Replace("EasyMovement.FirstPerson::EasyMovement.FirstPerson.FirstPersonCharacterController\n","EasyCombat.EasyMovement::EasyCombat.Integrations.CombatFirstPersonController\n");
            if(!AssetDatabase.IsValidFolder("Assets/EasyCombatPlayers")) AssetDatabase.CreateFolder("Assets","EasyCombatPlayers");
            if(!speedStat)
            {
                const string statPath="Assets/EasyCombatPlayers/MovementSpeed.asset";
                var existing=AssetDatabase.LoadMainAssetAtPath(statPath);
                if(existing && !(existing is CombatStatDefinition)) throw new InvalidOperationException("Movement stat path is occupied: "+statPath);
                speedStat=existing as CombatStatDefinition;
                if(!speedStat) { speedStat=ScriptableObject.CreateInstance<CombatStatDefinition>(); speedStat.DisplayName="Movement speed"; AssetDatabase.CreateAsset(speedStat,statPath); }
            }
            var path=AssetDatabase.GenerateUniqueAssetPath("Assets/EasyCombatPlayers/CombatFPS.prefab");
            File.WriteAllText(path,yaml); AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var local=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var player=local.GetComponentInChildren<CombatFirstPersonController>(true);
                if(!player || !player.LookPivot || !player.defaultCamera)
                    throw new InvalidOperationException("Failed to preserve the player or its camera links.");
                var actor=player.gameObject;
                if(!actor.GetComponent<Health>()) actor.AddComponent<Health>();
                actor.GetComponent<Health>().StatusesEnabled=true;
                var influences=actor.GetComponent<MovementInfluences>() ?? actor.AddComponent<MovementInfluences>();
                influences.MovementSpeedStat=speedStat;
                PrefabUtility.SaveAsPrefabAsset(local,path);
            }
            finally { PrefabUtility.UnloadPrefabContents(local); }
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(path); return path;
        }
    }
}
