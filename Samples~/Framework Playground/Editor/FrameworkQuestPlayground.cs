using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyLocalization;
using EasyQuests;
using EasyQuests.Integrations;
using EasyQuests.UI;
using EasyResources;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.UI;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkQuestPlayground
    {
        private const string Folder = EasySuiteSceneBuilder.Content + "/Quests";
        private static readonly (string key, string en, string ru)[] Texts ={
            ("quest.delivery","Parcel delivery","Р”РѕСЃС‚Р°РІРєР° РїРѕСЃС‹Р»РєРё"),("quest.pickup","Pick up the carry crate","Р’РѕР·СЊРјРёС‚Рµ СЏС‰РёРє"),
            ("quest.bridge","Repair the bridge","РџРѕС‡РёРЅРёС‚Рµ РјРѕСЃС‚"),("quest.ferry","Use the ferry","Р’РѕСЃРїРѕР»СЊР·СѓР№С‚РµСЃСЊ РїРµСЂРµРїСЂР°РІРѕР№"),
            ("quest.hand","Bring the crate to the recipient","Р”РѕСЃС‚Р°РІСЊС‚Рµ СЏС‰РёРє РїРѕР»СѓС‡Р°С‚РµР»СЋ"),("quest.done","Delivered","Р”РѕСЃС‚Р°РІР»РµРЅРѕ"),
            ("quest.action.Board","Accept delivery / open journal","Р’Р·СЏС‚СЊ Р·Р°РєР°Р· / РѕС‚РєСЂС‹С‚СЊ Р¶СѓСЂРЅР°Р»"),("quest.action.Bridge","Repair bridge","РџРѕС‡РёРЅРёС‚СЊ РјРѕСЃС‚"),
            ("quest.action.Ferry","Cross by ferry","РџРµСЂРµРїСЂР°РІРёС‚СЊСЃСЏ"),("quest.action.Recipient","Hand over parcel / claim reward","РЎРґР°С‚СЊ РїРѕСЃС‹Р»РєСѓ / РїРѕР»СѓС‡РёС‚СЊ РЅР°РіСЂР°РґСѓ"),
            ("quest.bridge.done","Bridge repaired.","РњРѕСЃС‚ РїРѕС‡РёРЅРµРЅ."),("quest.lost","Bring the physical crate to the recipient.","РџСЂРёРЅРµСЃРёС‚Рµ СЃР°Рј СЏС‰РёРє Рє РїРѕР»СѓС‡Р°С‚РµР»СЋ."),
            ("quest.reward","Delivery complete. Reward received.","РџРѕСЃС‹Р»РєР° РґРѕСЃС‚Р°РІР»РµРЅР°. РќР°РіСЂР°РґР° РїРѕР»СѓС‡РµРЅР°."),("quests.empty","No quests yet","РџРѕРєР° РЅРµС‚ Р·Р°РґР°РЅРёР№"),
            ("quest.close","Close","Р—Р°РєСЂС‹С‚СЊ"),("quest.track","Track next","РћС‚СЃР»РµР¶РёРІР°С‚СЊ СЃР»РµРґСѓСЋС‰РµРµ")
        };
        private static QuestGoal Goal(string id, string textKey, string signal, string target)
        {
            return new QuestGoal
            {
                id = id,
                title = Texts.First(t => t.key == textKey).en,
                textKey = textKey,
                mode = GoalMode.NewEvents,
                signal = signal,
                target = target,
                marker = MarkerStyle.Target
            };
        }
        private static QuestStep Node(string id, QuestStepType type, string name, int x, int y, string next = null)
            => new QuestStep { id = id, type = type, name = name, position = new Vector2(x, y), next = next };
        private static QuestDefinition Graph(WorldFactDatabase db)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.id = "suite.delivery"; q.title = "Parcel delivery"; q.titleKey = "quest.delivery"; q.database = db;
            var a = Node("start", QuestStepType.Start, "Order accepted", 20, 100, "pickup");
            var b = Node("pickup", QuestStepType.Objectives, "Collect crate", 225, 100, "route");
            b.goals = new[] { Goal("picked", "quest.pickup", "parcel.collected", "crate") };
            var c = Node("route", QuestStepType.Objectives, "Choose a crossing", 430, 100, "which");
            var bridge = Goal("repair", "quest.bridge", "", "bridge");
            bridge.mode = GoalMode.WorldState; bridge.condition = new FactCondition { fact = "bridge.repaired", value = 1 };
            var ferry = Goal("boat", "quest.ferry", "ferry.crossed", "ferry-search");
            ferry.marker = MarkerStyle.SearchArea;
            ferry.searchRadius = 4;
            c.goals = new[] { bridge, ferry };
            c.join = GoalJoin.Any;
            var d = Node("which", QuestStepType.Branch, "Which route?", 635, 100, "ferry-used");
            d.routes = new[] { new QuestRoute { when = new FactCondition { fact = "bridge.repaired", value = 1 }, next = "bridge-used" } };
            var e = Node("bridge-used", QuestStepType.SetFact, "Via bridge", 840, 30, "handover"); e.fact = "last.crossing"; e.value = 1;
            var f = Node("ferry-used", QuestStepType.SetFact, "Via ferry", 840, 170, "handover"); f.fact = "last.crossing"; f.value = 2;
            var g = Node("handover", QuestStepType.Objectives, "Hand over parcel", 1045, 100, "end");
            g.goals = new[] { Goal("handed", "quest.hand", "parcel.delivered", "recipient") };
            var end = Node("end", QuestStepType.End, "Delivered", 1250, 100); end.outcome = "Delivered"; end.outcomeKey = "quest.done"; end.grantReward = true;
            q.steps = new[] { a, b, c, d, e, f, g, end }; q.ValidateGraph(); return q;
        }
        private static T Asset<T>(string name, Func<T> create) where T : ScriptableObject
        {
            var path = Folder + "/" + name + ".asset"; var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing;
            var asset = create(); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        internal static void Apply(EasySuiteLab lab)
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var db = Asset("World Facts", () =>
            {
                var d = ScriptableObject.CreateInstance<WorldFactDatabase>();
                d.facts = new[]{new WorldFact{id="bridge.repaired",name="Bridge repaired"},new WorldFact{id="last.crossing",
                    name="Last crossing",type=FactType.State,states=new[]{new FactState{name="Unknown",value=0},new FactState{name="Bridge",value=1},new FactState{name="Ferry",value=2}}}};
                return d;
            });
            var quest = Asset("Parcel Delivery", () => Graph(db));
            var profile = Asset("Delivery Reward", () => ResourceRewardProfile.Create("Delivery Reward", lab.ResourceDatabase,
                ResourceRewardRange.FromQuantity(lab.Coin, 5, 5)));
            var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
            var entries = new List<CatalogTranslation>(lab.Texts.catalog.entries);
            foreach (var row in Texts)
            {
                foreach (var lang in new[] { "en", "ru" })
                {
                    var table = (StringTable)collection.GetTable(new LocaleIdentifier(lang));
                    if (table.GetEntry(row.key) == null) { table.AddEntry(row.key, lang == "en" ? row.en : row.ru); EditorUtility.SetDirty(table); }
                }
                if (entries.All(e => e.id != row.key)) entries.Add(new CatalogTranslation { id = row.key, text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id) });
            }
            lab.Texts.catalog.SetEntries(entries.ToArray()); EditorUtility.SetDirty(lab.Texts.catalog); EditorUtility.SetDirty(collection.SharedData);
            var world = lab.gameObject.AddComponent<QuestWorld>(); world.database = db; world.quests = new[] { quest };
            var reward = lab.gameObject.AddComponent<QuestReward>(); reward.world = world; reward.quest = quest; reward.profile = profile; reward.wallet = lab.Wallet;
            Target(lab.Crate.gameObject, world, "crate");
            var hud = Rect("Quest Markers", lab.Windows.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var markers = hud.gameObject.AddComponent<QuestMarkerHud>(); markers.world = world; markers.view = lab.Placer.ViewCamera; markers.translations = lab.Texts;
            var journal = Journal(lab, world);
            var root = new GameObject("Delivery Quest Stations");
            var searchCentre = new GameObject("Ferry search area");
            searchCentre.transform.SetParent(root.transform, false);
            searchCentre.transform.position = new Vector3(-8, 0, 4.5f);
            Target(searchCentre, world, "ferry-search");
            foreach (var station in new[] { DeliveryStation.Board, DeliveryStation.Bridge, DeliveryStation.Ferry, DeliveryStation.Recipient })
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = station.ToString(); go.transform.SetParent(root.transform, false);
                go.transform.position = station switch { DeliveryStation.Board => new Vector3(-3, .6f, -7), DeliveryStation.Bridge => new Vector3(-9, .6f, 1), DeliveryStation.Ferry => new Vector3(-9, .6f, 6), _ => new Vector3(-5, .6f, 9) };
                go.GetComponent<Renderer>().sharedMaterial = lab.Crate.GetComponent<Renderer>().sharedMaterial;
                var act = go.AddComponent<EasySuiteQuestStation>(); act.station = station; act.lab = lab; act.world = world; act.reward = reward; act.delivery = quest; act.journal = journal;
                var row = Texts.First(t => t.key == "quest.action." + station);
                act.ActionId = "suite.quest." + station; act.ActionLabel = row.en; act.LabelKey = row.key; act.Target.DisplayName = station.ToString();
                Target(go, world, station.ToString().ToLowerInvariant());
                var sign = new GameObject("Station sign", typeof(RectTransform)); sign.transform.SetParent(go.transform, false); sign.transform.localPosition = new Vector3(0, 1.1f, 0);
                var text = sign.AddComponent<TextMeshPro>(); text.font = UIText.DefaultFont; text.alignment = TextAlignmentOptions.Center; text.fontSize = 3;
                text.rectTransform.sizeDelta = new Vector2(3.8f, 1); text.text = row.en;
                var loc = sign.AddComponent<LocalizedText>(); loc.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id); loc.fallback = row.en;
                sign.AddComponent<EasySuiteLabel>();
            }
            AssetDatabase.SaveAssets();
        }
        private static void Target(GameObject go, QuestWorld world, string id)
        { var t = go.AddComponent<QuestTarget>(); t.world = world; t.id = id; }
        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 size, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform)); var r = (RectTransform)go.transform; r.SetParent(parent, false);
            r.anchorMin = min; r.anchorMax = max; r.sizeDelta = size; r.anchoredPosition = offset; return r;
        }
        private static UIWindow Journal(EasySuiteLab lab, QuestWorld world)
        {
            var mid = new Vector2(.5f, .5f);
            var panel = Rect("Quest Journal", lab.Windows.transform, mid, mid, new Vector2(640, 480), Vector2.zero);
            panel.gameObject.SetActive(false); panel.gameObject.AddComponent<Image>().color = new Color(.04f, .08f, .12f, .98f);
            var window = panel.gameObject.AddComponent<UIWindow>(); window.Policy = new WindowPolicy { BlockGameplay = true, Cursor = WindowCursor.Visible };
            var view = panel.gameObject.AddComponent<QuestJournal>(); view.world = world; view.translations = lab.Texts;
            var viewport = Rect("Journal viewport", panel, mid, mid, new Vector2(580, 360), new Vector2(0, 25));
            viewport.gameObject.AddComponent<RectMask2D>();
            var touch = viewport.gameObject.AddComponent<Image>(); touch.color = Color.clear;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.viewport = viewport; scroll.scrollSensitivity = 30;
            var body = Rect("Quest entries", viewport, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            body.pivot = new Vector2(.5f, 1); scroll.content = body;
            view.body = UIText.Add(body.gameObject, "", 21, TextAlignmentOptions.TopLeft); view.body.overflowMode = TextOverflowModes.Overflow;
            body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var close in new[] { false, true })
            {
                var button = Rect(close ? "Close" : "Track next", panel, mid, mid, new Vector2(260, 44), new Vector2(close ? 145 : -145, -195));
                button.gameObject.AddComponent<Image>().color = new Color(.15f, .28f, .36f);
                var b = button.gameObject.AddComponent<Button>();
                if (close) UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, window.Close);
                else UnityEditor.Events.UnityEventTools.AddPersistentListener(b.onClick, view.TrackNext);
                var label = Rect("Label", button, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                UIText.Add(label.gameObject, button.name, 16, TextAlignmentOptions.Center);
                var row = Texts.First(t => t.key == (close ? "quest.close" : "quest.track"));
                var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
                var loc = label.gameObject.AddComponent<LocalizedText>(); loc.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(row.key).Id); loc.fallback = row.en;
                label.gameObject.AddComponent<EasySuiteLabel>();
            }
            return window;
        }
    }
}
