using System;
using System.IO;
using System.Linq;
using EasyQuests;
using EasyQuests.UI;
using EasyUI.Integrations.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkQuestHud
    {
        internal static void Apply(EasySuiteLab lab)
        {
            const string controls = EasySuiteSceneBuilder.Content + "/Input/EasySuite Controls.inputactions";
            var actions = JObject.Parse(File.ReadAllText(controls));
            var maps = (JArray)actions["maps"];
            var source = JObject.Parse(File.ReadAllText("Packages/com.oreethehat.easy-game-framework/Modules/EasyInput/Runtime/Resources/EasyInputDefaults.inputactions"));
            var narrative = ((JArray)source["maps"]).OfType<JObject>().Single(m => (string)m["name"] == "Narrative");
            var existing = maps.OfType<JObject>().FirstOrDefault(m => (string)m["name"] == "Narrative");
            if (existing == null || !((JArray)existing["actions"]).OfType<JObject>().Any(a => (string)a["name"] == "Journal"))
            {
                if (existing == null) maps.Add(narrative.DeepClone());
                else
                {
                    ((JArray)existing["actions"]).Add(narrative["actions"][0].DeepClone());
                    ((JArray)existing["bindings"]).Add(narrative["bindings"][0].DeepClone());
                }
                File.WriteAllText(controls, actions.ToString(Formatting.Indented));
                AssetDatabase.ImportAsset(controls, ImportAssetOptions.ForceSynchronousImport);
            }

            var shortcut = lab.Player.GetComponent<QuestJournalShortcut>();
            if (!shortcut) shortcut = lab.Player.gameObject.AddComponent<QuestJournalShortcut>();
            shortcut.player = lab.Player; shortcut.windows = lab.Windows;
            shortcut.journal = lab.Windows.GetComponentInChildren<QuestJournal>(true);
            var hud = lab.Windows.GetComponentInChildren<QuestMarkerHud>(true);
            hud.trackerInset = new Vector2(24, 118);
            EditorUtility.SetDirty(hud);
            var world = lab.GetComponent<QuestWorld>();
            var library = AssetDatabase.LoadAssetAtPath<QuestIconLibrary>(EasySuiteSceneBuilder.Content + "/Quests/Marker Icons.asset");
            foreach (var icon in library.icons)
                if ((icon.name == "Enemy" && icon.labelMode == MarkerLabelMode.OnScreenOnly) ||
                    ((icon.name == "Main" || icon.name == "Side") && icon.labelMode == MarkerLabelMode.Always))
                    icon.labelMode = MarkerLabelMode.Hidden;
            EditorUtility.SetDirty(library);
            foreach (var quest in world.quests)
            {
                foreach (var step in quest.steps.Where(s => s.type == QuestStepType.Objectives && string.IsNullOrEmpty(s.nameKey)))
                {
                    if (quest.id == "suite.delivery" && step.id == "route") step.nameKey = "quest.stage.route";
                    else if (step.goals.Length == 1) step.nameKey = step.goals[0].textKey;
                }
                EditorUtility.SetDirty(quest);
            }
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("quest.stage.route", "Choose a crossing", "Выберите переправу"),
                ("ui.settings.action.Narrative/Journal", "Quest journal", "Журнал заданий"),
                ("quest.journal.shortHint", "journal", "журнал")
            });
            string prefab = AssetDatabase.GetAssetPath(lab.ControlsWindow.Prefab);
            var controlsWindow = PrefabUtility.LoadPrefabContents(prefab);
            try
            {
                var settings = controlsWindow.GetComponent<InputSettingsWindow>();
                if (!settings.Actions.Contains("Narrative/Journal"))
                {
                    settings.Actions = settings.Actions.Append("Narrative/Journal").ToArray();
                    PrefabUtility.SaveAsPrefabAsset(controlsWindow, prefab);
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(controlsWindow); }
            AssetDatabase.SaveAssets();
        }
    }
}
