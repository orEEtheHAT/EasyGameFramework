using System;
using System.IO;
using System.Linq;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyNarrative.UI;
using EasyQuests;
using EasyResources;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace EasySuite.Example.Editor
{
    public static class FrameworkConversationSuite
    {
        private const string Folder = EasySuiteSceneBuilder.Content + "/Dialogue/";
        private const string Paid = "ferryman.advice.paid", Shared = "ferryman.advice.known", Heard = "ferryman.advice.heard";
        internal static void Apply(EasySuiteLab lab)
        {
            var world = lab.GetComponent<QuestWorld>();
            var graph = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(Folder + "Ferryman.asset");
            var talk = lab.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TalkAction>(true))
                .Single(action => action.dialogue == graph);
            var profile = AssetDatabase.LoadAssetAtPath<SpeakerProfile>(Folder + "Ferryman Speaker.asset");
            if (!profile)
            {
                profile = ScriptableObject.CreateInstance<SpeakerProfile>();
                profile.displayName = "Ferryman"; profile.nameKey = "dialogue.ferryman";
                profile.subtitleColor = new Color(.82f, .94f, .88f);
                AssetDatabase.CreateAsset(profile, Folder + "Ferryman Speaker.asset");
            }
            var db = world.database;
            foreach (var pair in new[] { (Paid, "Paid for river advice"), (Shared, "Knows the river landmark"), (Heard, "Heard bridge callout") })
                if (!db.facts.Any(f => f.id == pair.Item1))
                    db.facts = db.facts.Append(new WorldFact { id = pair.Item1, name = pair.Item2 }).ToArray();
            var skill = AssetDatabase.LoadAssetAtPath<FactSkillCheck>(Folder + "Ferryman Trust.asset");
            if (!skill)
            {
                skill = ScriptableObject.CreateInstance<FactSkillCheck>();
                skill.threshold = new FactCondition { fact = "ferryman.introduced", value = 1 };
                AssetDatabase.CreateAsset(skill, Folder + "Ferryman Trust.asset");
            }
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("dialogue.advice.topic", "Ask about the far bank.", "Спросить о дальнем берегу."),
                ("dialogue.advice.offer", "I can tell you what to look for across the river.", "Могу подсказать, что искать на другом берегу."),
                ("dialogue.advice.pay", "Pay for directions", "Заплатить за подсказку"),
                ("dialogue.advice.persuade", "Ask for a friendly favour.", "Попросить по знакомству."),
                ("dialogue.advice.pass", "Look for the blue post on the far bank.", "Ищите синий столб на другом берегу."),
                ("dialogue.advice.fail", "We barely know each other. Ask me who I am first.", "Мы едва знакомы. Сначала спросите, кто я."),
                ("dialogue.history", "History", "История"), ("dialogue.skipVoice", "Skip voice", "Пропустить озвучку")
            });
            if (!graph.lines.Any(l => l.id == "advice-offer"))
            {
                var offer = new DialogueLine { id = "advice-offer", name = "River advice", text = "I can tell you what to look for across the river.", textKey = "dialogue.advice.offer", profile = profile, position = new Vector2(290,390) };
                var tip = new DialogueLine { id = "advice-tip", name = "Landmark", text = "Look for the blue post on the far bank.", textKey = "dialogue.advice.pass", profile = profile, position = new Vector2(560,370) };
                var fail = new DialogueLine { id = "advice-fail", name = "Ask again", text = "We barely know each other. Ask me who I am first.", textKey = "dialogue.advice.fail", profile = profile, position = new Vector2(560,500), next = graph.entry };
                var dir = Path.GetDirectoryName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(lab)))).Replace('\\','/') + "/Art/";
                AudioClip Voice(string code) => AssetDatabase.LoadAssetAtPath<AudioClip>(dir + "FerrymanAdvice_" + code + ".wav");
                tip.voices = new[] { new DialogueVoice { locale = "ru", clip = Voice("ru") }, new DialogueVoice { locale = "en", clip = Voice("en") } };
                DialogueAction Set(string fact) => new DialogueAction { kind = DialogueActionType.SetFact, key = fact, value = 1 };
                var paid = new DialogueReply { id = "advice-pay", text = "Pay for directions", textKey = "dialogue.advice.pay", next = tip.id,
                    price = new ResourceCost(ResourceAmount.FromQuantity(lab.Coin, 2)), locked = LockedReply.Explain, action = Set(Paid),
                    when = new[] { new DialogueRequirement { fact = new FactCondition { fact = Paid, value = 0 } } },
                    additionalActions = new[] { Set(Shared), new DialogueAction { kind = DialogueActionType.SendEvent, key = "ferryman.advice" } } };
                var persuade = new DialogueReply { id = "advice-favour", text = "Ask for a friendly favour.", textKey = "dialogue.advice.persuade",
                    next = tip.id, failedNext = fail.id, skill = skill, action = Set(Shared) };
                offer.replies = new[] { paid, persuade, new DialogueReply { id = "advice-return", text = "Back to my question.", textKey = "dialogue.back", next = graph.entry } };
                var first = graph.lines.Single(l => l.id == graph.entry);
                first.replies = first.replies.Append(new DialogueReply { id = "advice", text = "Ask about the far bank.", textKey = "dialogue.advice.topic", next = offer.id }).ToArray();
                foreach (var line in graph.lines) if (!line.profile && line.speakerKey == "dialogue.ferryman") line.profile = profile;
                graph.lines = graph.lines.Concat(new[] { offer, tip, fail }).ToArray();
                graph.Invalidate(); graph.ValidateGraph();
            }
            EnsureCourierPass(lab);
            talk.interruption.distance = 7;
            talk.interruption.whenFar = DialogueInterruptMode.Resume;
            talk.interruption.interruptOnDamage = true;
            talk.interruption.whenDamaged = DialogueInterruptMode.Close;
            var reaction = lab.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<QuestEventReaction>(true))
                .FirstOrDefault(r => r.stepId == "bridge-used");
            if (reaction)
            {
                var bark = talk.GetComponent<DialogueBark>();
                if (!bark) bark = talk.gameObject.AddComponent<DialogueBark>();
                bark.script = graph; bark.lineId = "advice-tip"; bark.listener = lab.Actor.GetComponent<DialoguePresenter>();
                bark.priority = 1; bark.maxDistance = 16; bark.once = true; bark.playedFlag = Heard;
                bool wired = Enumerable.Range(0, reaction.onTriggered.GetPersistentEventCount()).Any(i => reaction.onTriggered.GetPersistentTarget(i) == bark);
                if (!wired) UnityEventTools.AddPersistentListener(reaction.onTriggered, bark.Play);
                EditorUtility.SetDirty(reaction); EditorUtility.SetDirty(bark);
            }
            EditorUtility.SetDirty(db); EditorUtility.SetDirty(graph); EditorUtility.SetDirty(talk);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Also upgrades a playground which already contains the river-advice branch.</summary>
        public static bool EnsureCourierPass(EasySuiteLab lab)
        {
            string flag = EasySuiteQuestStation.CourierPassFact;
            var db = lab.GetComponent<QuestWorld>().database;
            var graph = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(Folder + "Ferryman.asset");
            var offer = graph.lines.Single(l => l.id == "advice-offer");
            var rows = new[] {
                (key: "dialogue.advice.permit", en: "Show the courier pass.", ru: "Предъявить пропуск курьера."),
                (key: "dialogue.advice.permit.reason", en: "Collect your courier pass at the delivery board.", ru: "Получите пропуск курьера у доски заказов."),
                (key: "dialogue.advice.permit.issued", en: "Courier pass issued. Show it to the ferryman for directions.", ru: "Вы получили пропуск курьера. Паромщик подскажет дорогу по пропуску.")
            };
            bool changed = false;
            if (!db.facts.Any(f => f.id == flag))
            {
                db.facts = db.facts.Append(new WorldFact { id = flag, name = "Holds the courier pass" }).ToArray();
                EditorUtility.SetDirty(db);
                changed = true;
            }
            if (!offer.replies.Any(r => r.id == "advice-permit"))
            {
                var permit = new DialogueReply {
                    id = "advice-permit", text = rows[0].en, textKey = rows[0].key, next = "advice-tip",
                    when = new[] { new DialogueRequirement { fact = new FactCondition { fact = flag, value = 1 } } },
                    locked = LockedReply.Explain, lockedReason = rows[1].en, lockedReasonKey = rows[1].key,
                    action = new DialogueAction { kind = DialogueActionType.SetFact, key = Shared, value = 1 }
                };
                var replies = offer.replies.ToList();
                int back = replies.FindIndex(r => r.id == "advice-return");
                replies.Insert(back < 0 ? replies.Count : back, permit);
                offer.replies = replies.ToArray();
                graph.Invalidate();
                graph.ValidateGraph();
                EditorUtility.SetDirty(graph);
                changed = true;
            }
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var english = (StringTable)collection.GetTable("en");
            var russian = (StringTable)collection.GetTable("ru");
            if (rows.Any(row => english.GetEntry(row.key) == null || russian.GetEntry(row.key) == null ||
                                !lab.Texts.catalog.entries.Any(e => e.id == row.key)))
            {
                FrameworkDialoguePlayground.InstallStrings(lab, rows);
                changed = true;
            }
            if (changed) AssetDatabase.SaveAssets();
            return changed;
        }
    }
}
