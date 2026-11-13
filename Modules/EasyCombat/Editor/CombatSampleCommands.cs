using System;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace EasyGameFramework.Editor
{
    public static class CombatSampleCommands
    {
        [MenuItem("Tools/EasyGameFramework/Combat/Samples/Open Attack Gallery")]
        public static void OpenAttackGallery()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            foreach (var guid in AssetDatabase.FindAssets("AttackGallery_Sample t:Scene", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/AttackGallery_Sample.unity", StringComparison.Ordinal)) continue;
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(path);
                return;
            }
            EditorUtility.DisplayDialog("Combat Attack Gallery", "Import Combat Attack Gallery from Package Manager > EasyGameFramework > Samples, then open this command again.", "OK");
        }
    }
}
