using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace EasyBuilding.Editor
{
    /// <summary>Persist asset GUIDs before a player build; copies get new IDs and moves preserve existing IDs.</summary>
    public sealed class BuildingDefinitionIds : AssetPostprocessor, IPreprocessBuildWithReport
    {
        private static readonly HashSet<BuildingDefinition> Pending = new HashSet<BuildingDefinition>();
        public int callbackOrder => -1000;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (!path.EndsWith(".asset", System.StringComparison.OrdinalIgnoreCase)) continue;
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                if (definition != null && Synchronize(definition)) Pending.Add(definition);
            }
            if (Pending.Count > 0)
            {
                EditorApplication.delayCall -= SavePending;
                EditorApplication.delayCall += SavePending;
            }
        }

        private static bool Synchronize(BuildingDefinition definition)
        {
            var serialized = new SerializedObject(definition);
            var id = serialized.FindProperty("stableId");
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition));
            if (string.IsNullOrEmpty(guid) || id.stringValue == guid) return false;
            id.stringValue = guid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return true;
        }

        private static void SavePending()
        {
            var pending = new List<BuildingDefinition>(Pending);
            Pending.Clear();
            foreach (var definition in pending) if (definition != null) AssetDatabase.SaveAssetIfDirty(definition);
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (definition != null && Synchronize(definition)) Pending.Add(definition);
            }
            SavePending();
        }
    }
}
