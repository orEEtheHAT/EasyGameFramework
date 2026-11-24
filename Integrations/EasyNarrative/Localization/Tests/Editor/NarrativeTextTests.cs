using System;
using System.Linq;
using EasyLocalization;
using EasyLocalization.Editor;
using EasyNarrative.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace EasyNarrative.Tests
{
    public sealed class NarrativeTextTests
    {
        [Test]
        public void PreparingCopiesKeepsSharedKeysAndTheOriginalSpeakerProfile()
        {
            var profile = ScriptableObject.CreateInstance<SpeakerProfile>();
            try
            {
                profile.nameKey = "narrative.auto.shared-profile";
                var line = new DialogueLine { textKey = "narrative.auto.private", speakerKey = "people.guard", profile = profile };
                var reply = new DialogueReply { textKey = "ui.goodbye", lockedReasonKey = "narrative.auto.private-reason" };
                line.replies = new[] { reply };
                NarrativeTextCollector.PrepareCopy(line);
                Assert.That(line.textKey, Is.Empty);
                Assert.That(reply.lockedReasonKey, Is.Empty);
                Assert.That(reply.textKey, Is.EqualTo("ui.goodbye"));
                Assert.That(line.speakerKey, Is.EqualTo("people.guard"));
                Assert.That(line.profile.nameKey, Is.EqualTo("narrative.auto.shared-profile"));
                var step = new EasyQuests.QuestStep { nameKey = "narrative.auto.stage", outcomeKey = "quests.done",
                    goals = new[] { new EasyQuests.QuestGoal { textKey = "narrative.auto.goal" } } };
                NarrativeTextCollector.PrepareCopy(step);
                Assert.That(step.nameKey, Is.Empty);
                Assert.That(step.goals[0].textKey, Is.Empty);
                Assert.That(step.outcomeKey, Is.EqualTo("quests.done"));
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void CollectionPreservesStableIdsReviewedCopyTranslationsAndContext()
        {
            string name = "NarrativeTextTest_" + Guid.NewGuid().ToString("N");
            string root = "Assets/" + name;
            var en = Locale.CreateLocale("en");
            var ru = Locale.CreateLocale("ru");
            try
            {
                AssetDatabase.CreateFolder("Assets", name);
                var tables = LocalizationEditorSettings.CreateStringTableCollection(name, root, new[] { en, ru });
                var catalog = ScriptableObject.CreateInstance<LocalizedCatalog>();
                AssetDatabase.CreateAsset(catalog, root + "/Catalog.asset");
                var graph = ScriptableObject.CreateInstance<DialogueDefinition>();
                var line = new DialogueLine { id = "intro", name = "At the gate", speaker = "Guard", text = "Welcome {player}." };
                var second = new DialogueLine { id = "bye", name = "Gate exit", text = "Bye." };
                line.replies = new[] { new DialogueReply { id = "door", text = "Open the gate.", lockedReason = "Key needed" } };
                graph.lines = new[] { line, second };
                AssetDatabase.CreateAsset(graph, root + "/Gate.asset");
                NarrativeTextCollector.Plan Preview() => NarrativeTextCollector.Preview(tables, catalog, "en", new[] { graph });
                var initial = Preview();
                Assert.That(initial.rows.Count, Is.EqualTo(5));
                Assert.That(line.textKey, Is.Null.Or.Empty);
                Assert.That(initial.rows.Any(r => r.error != null), Is.False);
                Assert.That(NarrativeTextCollector.Apply(initial), Is.EqualTo(5));
                string key = line.textKey;
                long id = tables.SharedData.GetId(key);
                Assert.That(id, Is.Positive);
                Assert.That(Preview().rows.All(r => !r.update), Is.True);
                var english = (StringTable)tables.GetTable("en");
                var russian = (StringTable)tables.GetTable("ru");
                russian.AddEntry(id, "Привет {player}.");
                var shared = tables.SharedData.GetEntry(id);
                TranslationComments.Write(shared, "Keep this note\n" + TranslationComments.Read(shared));
                NarrativePreviewLanguage.Configure(catalog, "ru");
                Assert.That(NarrativePreviewLanguage.Text(key, "fallback"), Is.EqualTo("Привет {player}."));

                graph.lines = new[] { second, line };
                line.name = "Renamed stage";
                line.text = "Halt {player}.";
                var renamed = Preview();
                var changed = renamed.rows.Single(r => r.key == key);
                Assert.That(changed.resolution, Is.EqualTo(NarrativeTextCollector.Resolution.UseGraph));
                Assert.That(NarrativeTextCollector.Apply(renamed), Is.Positive);
                Assert.That(line.textKey, Is.EqualTo(key));
                Assert.That(tables.SharedData.GetId(key), Is.EqualTo(id));
                Assert.That(english.GetEntry(id).Value, Is.EqualTo("Halt {player}."));
                Assert.That(russian.GetEntry(id).Value, Is.EqualTo("Привет {player}."));
                Assert.That(TranslationComments.Read(shared), Does.Contain("Keep this note").And.Contain("Renamed stage"));
                Assert.That(Preview().rows.Single(r => r.key == key).issues.Any(i => i.Contains("review translation")), Is.True);
                var export = PublishedCsvTransport.Parse(NarrativeTextCollector.ExportCsv(Preview()));
                var csvRow = export.Skip(1).Single(r => r[1] == key);
                Assert.That(csvRow[0], Is.EqualTo(id.ToString()));
                Assert.That(csvRow[3], Is.EqualTo("Привет {player}."));
                Assert.That(csvRow[4], Does.Contain("Keep this note\n"));
                Assert.That(csvRow[7], Does.Contain("Welcome {player}."));
                Assert.That(csvRow[8], Does.Contain("review translation"));

                line.text = "Good evening {name}.";
                english.GetEntry(id).Value = "Independent English edit {player}.";
                var conflict = Preview();
                changed = conflict.rows.Single(r => r.key == key);
                Assert.That(changed.conflict, Is.True);
                Assert.That(changed.resolution, Is.EqualTo(NarrativeTextCollector.Resolution.KeepTable));
                russian.GetEntry(id).Value = "Добрый вечер {name}.";
                Assert.Throws<InvalidOperationException>(() => NarrativeTextCollector.Apply(conflict));
                var current = Preview();
                Assert.That(current.rows.Single(r => r.key == key).issues.Any(i => i.Contains("placeholder")), Is.True);
                changed = current.rows.Single(r => r.key == key);
                changed.resolution = NarrativeTextCollector.Resolution.UseGraph;
                NarrativeTextCollector.UpdateIssues(changed);
                Assert.That(changed.issues.Any(i => i.Contains("placeholder")), Is.False);
                changed.resolution = NarrativeTextCollector.Resolution.KeepTable;
                NarrativeTextCollector.UpdateIssues(changed);
                Assert.That(changed.issues.Any(i => i.Contains("[player]") && i.Contains("[name]")), Is.True);
                NarrativeTextCollector.Apply(current);
                Assert.That(english.GetEntry(id).Value, Is.EqualTo("Independent English edit {player}."));
                graph.lines = new[] { line };
                var smaller = Preview();
                Assert.That(smaller.unusedKeys, Does.Contain(second.textKey));
                Assert.That(tables.SharedData.GetId(second.textKey), Is.Positive);
                var map = ScriptableObject.CreateInstance<SheetMapping>();
                try
                {
                    map.collection = tables;
                    map.contextColumn = 5;
                    map.languages.Add(new LanguageColumn { languageCode = "en", column = 3 });
                    Assert.That(UnityTableStore.Read(map).rows.Single(r => r.id == id).context, Does.Contain("Guard"));
                    var csv = PublishedCsvTransport.ReadCsv("ID,Key,en,Unused,Context\n123,line,Hello,,Guard", map);
                    Assert.That(csv.rows.Single().context, Is.EqualTo("Guard"));
                }
                finally { UnityEngine.Object.DestroyImmediate(map); }
            }
            finally
            {
                NarrativePreviewLanguage.Configure(null, null);
                if (AssetDatabase.IsValidFolder(root))
                {
                    foreach (string guid in AssetDatabase.FindAssets("", new[] { root }))
                        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                            if (o) Undo.ClearUndo(o);
                    AssetDatabase.DeleteAsset(root);
                }
                UnityEngine.Object.DestroyImmediate(en);
                UnityEngine.Object.DestroyImmediate(ru);
            }
        }
    }
}
