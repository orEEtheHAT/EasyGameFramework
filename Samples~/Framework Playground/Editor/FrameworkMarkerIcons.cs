using System;
using System.Linq;
using EasyQuests;
using UnityEditor;
using UnityEngine;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkMarkerIcons
    {
        internal static void Apply(EasySuiteLab lab)
        {
            const string path = EasySuiteSceneBuilder.Content + "/Quests/Marker Icons.asset";
            var library = AssetDatabase.LoadAssetAtPath<QuestIconLibrary>(path);
            if (!library)
            {
                const string presetPath = "Packages/com.oreethehat.easy-game-framework/Modules/EasyNarrative/Quests/Marker Icons/Quest Markers.asset";
                var preset = AssetDatabase.LoadAssetAtPath<QuestIconLibrary>(presetPath);
                if (!preset) throw new InvalidOperationException("Missing built-in marker library: " + presetPath);
                library = UnityEngine.Object.Instantiate(preset);
                library.name = "Marker Icons";
                AssetDatabase.CreateAsset(library, path);
            }
            var main = Array.Find(library.icons, e => e.name == "Main");
            var side = Array.Find(library.icons, e => e.name == "Side");
            foreach (var quest in lab.GetComponent<QuestWorld>().quests)
            {
                if (quest.iconLibrary) continue;
                if (quest.id == "suite.delivery")
                {
                    quest.iconLibrary = library;
                    quest.defaultIconId = main?.Id;
                    var alternate = quest.steps.SelectMany(s => s.goals).FirstOrDefault(g => g.id == "boat");
                    if (alternate != null) alternate.iconId = side?.Id;
                    EditorUtility.SetDirty(quest);
                }
                else if (quest.titleKey == "quest.survey")
                {
                    quest.iconLibrary = library;
                    quest.defaultIconId = side?.Id;
                    EditorUtility.SetDirty(quest);
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
