using EasyNarrative.Editor;
using EasyQuests;
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;
using Object = UnityEngine.Object;

namespace EasySuite.Example.Editor
{
    public static class FrameworkNarrativeText
    {
        private static readonly (string asset, string objectiveKey, string title, string translation)[] Stages =
        {
            ("Parcel Delivery", "quest.pickup", "Collect crate", "Получить ящик"),
            ("Parcel Delivery", "quest.hand", "Hand over parcel", "Передать посылку"),
            ("Parcel Delivery", "quest.assistant.goal", "Hand over to assistant", "Передать посылку помощнику"),
            ("Patrol Exercise", "patrol.goal", "Inspect patrol", "Проверить патруль"),
            ("Survey Post", "quest.survey.inspect", "Interact", "Осмотреть пост")
        };

        public static void Apply(EasySuiteLab lab)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            if (!collection || !lab || !lab.Texts || !lab.Texts.catalog) return;
            var assets = NarrativeTextCollector.Assets(EasySuiteSceneBuilder.Content);
            var quests = assets.OfType<QuestDefinition>().ToArray();
            foreach (var stage in Stages)
            {
                var quest = quests.FirstOrDefault(q => q.name == stage.asset);
                var step = quest?.steps?.FirstOrDefault(s => s?.name == stage.title && s.nameKey == stage.objectiveKey);
                if (step == null) continue;
                // Older samples used one key for both the short stage title and the objective sentence.
                Undo.RecordObject(quest, "Separate Playground stage translation");
                step.nameKey = stage.objectiveKey + ".stage";
                EditorUtility.SetDirty(quest);
                AssetDatabase.SaveAssetIfDirty(quest);
            }
            var plan = NarrativeTextCollector.Preview(collection, lab.Texts.catalog, "en",
                assets);
            // Keep the English copy and the translations already authored for this demo.
            foreach (var row in plan.rows)
                if (row.tableId != 0 && !string.IsNullOrEmpty(row.primary))
                    row.resolution = NarrativeTextCollector.Resolution.KeepTable;
            NarrativeTextCollector.Apply(plan);
            var russian = collection.GetTable("ru") as StringTable;
            if (!russian) return;
            bool changed = false;
            void Seed(string key, string value)
            {
                if (string.IsNullOrEmpty(key)) return;
                var shared = collection.SharedData.GetEntry(key);
                if (shared == null) return;
                var entry = russian.GetEntry(shared.Id);
                if (!string.IsNullOrEmpty(entry?.Value)) return;
                if (!changed) Undo.RegisterCompleteObjectUndo(russian, "Fill missing Playground translations");
                changed = true;
                if (entry == null) russian.AddEntry(shared.Id, value);
                else entry.Value = value;
            }
            foreach (var stage in Stages)
            {
                string key = stage.objectiveKey + ".stage";
                var quest = quests.FirstOrDefault(q => q.name == stage.asset);
                if (quest && quest.steps.Any(s => s?.nameKey == key && s.name == stage.title))
                    Seed(key, stage.translation);
            }
            var order = quests.FirstOrDefault(q => q.id == "suite.orders" && q.title == "Parcel order");
            if (order && order.titleKey != null && order.titleKey.StartsWith("narrative.auto.", StringComparison.Ordinal))
                Seed(order.titleKey, "Заказ на доставку");
            if (changed) { EditorUtility.SetDirty(russian); AssetDatabase.SaveAssetIfDirty(russian); }
        }

        [MenuItem("Tools/EasyGameFramework/Examples/Preview Playground narrative translations")]
        private static void Preview()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var lab = Object.FindFirstObjectByType<EasySuiteLab>();
            if (!collection || !lab || !lab.Texts || !lab.Texts.catalog)
            {
                EditorUtility.DisplayDialog("Playground translations", "Open FrameworkPlayground in Edit Mode first.", "OK");
                return;
            }
            NarrativePreviewLanguage.Configure(lab.Texts.catalog, "ru");
            NarrativeTextWindow.OpenFor(collection, lab.Texts.catalog, "en", EasySuiteSceneBuilder.Content);
        }
    }
}
