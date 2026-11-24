using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyLocalization;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyNarrative.UI;
using EasyQuests;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkDialoguePlayground
    {
        private const string Folder = EasySuiteSceneBuilder.Content + "/Dialogue";
        private const string Met = "ferryman.introduced";
        private static readonly (string key, string en, string ru)[] Strings ={
            ("dialogue.continue","Continue","Продолжить"),
            ("dialogue.ferryman","Ferryman","Паромщик"),
            ("dialogue.talk","Talk to the ferryman","Поговорить с паромщиком"),
            ("dialogue.greeting","The ferry is ready. What brings you here?","Паром готов. С чем пожаловали?"),
            ("dialogue.ask","Who runs this crossing?","Кто заведует переправой?"),
            ("dialogue.story","I've kept this ferry running for twenty years. Bring your parcel and I'll take you across.","Я здесь уже двадцать лет. Принесёте посылку — переправлю на тот берег."),
            ("dialogue.again","Tell me about the crossing again.","Напомни, как работает переправа."),
            ("dialogue.order","I'll take the delivery order.","Я возьму заказ на доставку."),
            ("dialogue.cross","The parcel is ready. Let's cross.","Посылка со мной. Переправляемся."),
            ("dialogue.sailed","Across we go. The recipient is waiting.","Мы на другом берегу. Получатель уже ждёт."),
            ("dialogue.bridge","I repaired the bridge.","Я починил мост."),
            ("dialogue.bridgeLine","Then the road is open again. Thank you.","Значит, дорога снова открыта. Спасибо."),
            ("dialogue.back","Back to my question.","Вернёмся к моему вопросу."),
            ("dialogue.leave","Goodbye.","До встречи.")
        };
        private static string En(string key) => Strings.First(x => x.key == key).en;
        internal static void Apply(EasySuiteLab lab)
        {
            var world = lab.GetComponent<QuestWorld>();
            var stations = lab.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EasySuiteQuestStation>(true));
            var station = stations.Single(s => s.station == DeliveryStation.Ferry);
            var db = world.database;
            if (!Array.Exists(db.facts, f => f.id == Met))
            {
                db.facts = db.facts.Append(new WorldFact { id = Met, name = "Met the ferryman", type = FactType.Flag }).ToArray();
                EditorUtility.SetDirty(db);
            }
            string path = Folder + "/Ferryman.asset";
            var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path);
            if (!dialogue)
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
                dialogue = Create(db, station.delivery);
                AssetDatabase.CreateAsset(dialogue, path);
            }
            InstallStrings(lab, Strings);
            var presenter = lab.Actor.GetComponent<DialoguePresenter>();
            if (!presenter) presenter = lab.Actor.gameObject.AddComponent<DialoguePresenter>();
            presenter.manager = lab.Windows; presenter.world = world; presenter.translations = lab.Texts;
            var talk = station.GetComponent<TalkAction>();
            if (!talk) talk = station.gameObject.AddComponent<TalkAction>();
            talk.dialogue = dialogue; talk.ActionId = "suite.talk.ferryman"; talk.ActionLabel = En("dialogue.talk"); talk.LabelKey = "dialogue.talk";
            // Keep the station identity for existing scenes; the interaction list now exposes Talk.
            station.enabled = false;
            var sign = station.transform.Find("Station sign")?.GetComponent<LocalizedText>();
            if (sign)
            {
                var table = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
                sign.text = new LocalizedString(table.SharedData.TableCollectionNameGuid, table.SharedData.GetEntry("dialogue.talk").Id);
                sign.fallback = talk.ActionLabel;
                EditorUtility.SetDirty(sign);
            }
            EditorUtility.SetDirty(station); EditorUtility.SetDirty(talk); EditorUtility.SetDirty(presenter);
            AssetDatabase.SaveAssets();
        }
        private static DialogueDefinition Create(WorldFactDatabase db, QuestDefinition quest)
        {
            var dialogue = ScriptableObject.CreateInstance<DialogueDefinition>();
            dialogue.database = db;
            DialogueLine Line(string id, string key, int x, int y) => new DialogueLine { id = id, name = En(key), speaker = "Ferryman", speakerKey = "dialogue.ferryman", text = En(key), textKey = key, position = new Vector2(x, y) };
            DialogueReply Reply(string id, string key, string next = null) => new DialogueReply { id = id, text = En(key), textKey = key, next = next };
            DialogueRequirement Fact(string key, int value) => new DialogueRequirement { kind = DialogueRequirementType.WorldFact, fact = new FactCondition { fact = key, value = value } };
            var hello = Line("hello", "dialogue.greeting", 45, 110);
            var story = Line("story", "dialogue.story", 320, 25);
            var trip = Line("arrival", "dialogue.sailed", 320, 135);
            var bridge = Line("road", "dialogue.bridgeLine", 320, 245);
            var ask = Reply("ask", "dialogue.ask", "story"); ask.when = new[] { Fact(Met, 0) };
            ask.action = new DialogueAction { kind = DialogueActionType.SetFact, key = Met, value = 1 };
            var again = Reply("again", "dialogue.again", "story"); again.when = new[] { Fact(Met, 1) };
            var order = Reply("order", "dialogue.order"); order.when = new[] { new DialogueRequirement { kind = DialogueRequirementType.QuestNotStarted, quest = quest } };
            order.action = new DialogueAction { kind = DialogueActionType.StartQuest, quest = quest };
            var sail = Reply("cross", "dialogue.cross", "arrival"); sail.when = new[] { new DialogueRequirement { kind = DialogueRequirementType.QuestAtStep, quest = quest, stepId = "route" } };
            sail.action = new DialogueAction { kind = DialogueActionType.SendEvent, key = "ferry.crossed" };
            var road = Reply("bridge", "dialogue.bridge", "road"); road.when = new[] { Fact("bridge.repaired", 1) };
            hello.replies = new[] { ask, again, order, sail, road, Reply("leave", "dialogue.leave") };
            story.replies = new[] { Reply("return", "dialogue.back", "hello") };
            dialogue.entry = hello.id; dialogue.lines = new[] { hello, story, trip, bridge }; dialogue.ValidateGraph(); return dialogue;
        }
        internal static void InstallStrings(EasySuiteLab lab, (string key, string en, string ru)[] strings)
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries);
            foreach (var row in strings)
            {
                foreach (var language in new[] { "en", "ru" })
                {
                    var table = (StringTable)collection.GetTable(new LocaleIdentifier(language));
                    if (table.GetEntry(row.key) == null) { table.AddEntry(row.key, language == "en" ? row.en : row.ru); EditorUtility.SetDirty(table); }
                }
                if (entries.All(e => e.id != row.key))
                    entries.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id) });
            }
            lab.Texts.catalog.SetEntries(entries.ToArray()); EditorUtility.SetDirty(lab.Texts.catalog); EditorUtility.SetDirty(collection.SharedData);
        }
    }
}
