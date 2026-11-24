using System;
using System.Linq;
using EasyLocalization;
using EasyQuests;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using Object = UnityEngine.Object;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkDeliveryRule
    {
        internal static void Apply(EasySuiteLab lab)
        {
            var recipient = lab.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EasySuiteQuestStation>(true))
                .Single(s => s.station == DeliveryStation.Recipient);
            var world = recipient.world;
            var db = world.database;
            const string away = "recipient.away";
            if (!db.facts.Any(f => f.id == away))
            {
                db.facts = db.facts.Append(new WorldFact { id = away, name = "Recipient has left" }).ToArray();
                EditorUtility.SetDirty(db);
            }
            var quest = recipient.delivery;
            const string stepId = "assistant-handover";
            if (!quest.steps.Any(s => s.id == stepId))
            {
                var step = new QuestStep { id = stepId, name = "Hand over to assistant", position = new Vector2(1045, 260),
                    type = QuestStepType.Objectives, next = "end", goals = new[] { new QuestGoal {
                        id = "assistant-received", title = "Bring the crate to the assistant", textKey = "quest.assistant.goal",
                        mode = GoalMode.NewEvents, signal = "parcel.delivered.assistant", target = "assistant", marker = MarkerStyle.Target } } };
                quest.steps = quest.steps.Append(step).ToArray();
            }
            var rules = quest.rules ?? Array.Empty<QuestRule>();
            if (!rules.Any(r => r.id == "recipient-left"))
                quest.rules = rules.Append(new QuestRule { id = "recipient-left", when = new FactCondition { fact = away }, destination = stepId }).ToArray();
            quest.ValidateGraph();
            EditorUtility.SetDirty(quest);
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("quest.action.Dispatch", "Send recipient away", "Отправить получателя в отъезд"),
                ("quest.action.Assistant", "Give crate to assistant", "Передать ящик помощнику"),
                ("quest.away", "The recipient has left. Bring the crate to the assistant.", "Получатель уехал. Доставьте ящик помощнику."),
                ("quest.assistant.goal", "Bring the crate to the assistant", "Доставьте ящик помощнику")
            });
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            GameObject Station(DeliveryStation kind, Vector3 position, string caption, string material)
            {
                var action = Object.Instantiate(recipient, recipient.transform.parent);
                action.name = kind.ToString();
                action.transform.position = position;
                action.station = kind;
                action.ActionId = "suite.quest." + kind;
                action.ActionLabel = caption;
                action.LabelKey = "quest.action." + kind;
                action.Target.DisplayName = kind.ToString();
                action.Target.PersistentId = "suite.delivery." + kind.ToString().ToLowerInvariant();
                action.GetComponent<QuestTarget>().id = kind.ToString().ToLowerInvariant();
                action.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(EasySuiteSceneBuilder.Content + "/Materials/" + material + ".mat");
                var sign = action.transform.Find("Station sign");
                var localized = sign.GetComponent<LocalizedText>();
                localized.fallback = caption;
                localized.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(action.LabelKey).Id);
                sign.GetComponent<TMP_Text>().text = caption;
                return action.gameObject;
            }
            var dispatch = Station(DeliveryStation.Dispatch, new Vector3(-6.8f, .6f, -7), "Send recipient away", "Coral");
            var assistant = Station(DeliveryStation.Assistant, new Vector3(-8, .6f, 9), "Give crate to assistant", "Blue");
            assistant.SetActive(false);
            var reaction = dispatch.AddComponent<WorldStateReaction>();
            reaction.world = world;
            reaction.condition = new FactCondition { fact = away };
            reaction.showWhenTrue = assistant;
            reaction.showWhenFalse = recipient.gameObject;
            AssetDatabase.SaveAssets();
        }
    }
}
