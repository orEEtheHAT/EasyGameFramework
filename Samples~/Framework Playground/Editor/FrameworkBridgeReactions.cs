using System.Linq;
using EasyFeedbacks;
using EasyQuests;
using EasyQuests.Editor;
using EasyQuests.Integrations;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkBridgeReactions
    {
        internal static void UpgradeAuthoring(EasySuiteLab lab)
        {
            var bridge = lab.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EasySuiteQuestStation>(true))
                .Single(station => station.station == DeliveryStation.Bridge);
            var state = bridge.transform.Find("Bridge reactions")?.GetComponent<WorldStateReaction>();
            if (state && !state.showWhenTrue && !state.showWhenFalse)
            {
                var deck = state.transform.Find("Repaired deck");
                var barrier = state.transform.Find("Closed crossing");
                var data = new SerializedObject(state);
                bool Original(string field, GameObject first, GameObject second)
                {
                    var calls = data.FindProperty(field).FindPropertyRelative("m_PersistentCalls.m_Calls");
                    if (calls.arraySize != 2) return false;
                    for (int i = 0; i < 2; i++)
                    {
                        var call = calls.GetArrayElementAtIndex(i);
                        if (call.FindPropertyRelative("m_Target").objectReferenceValue != (i == 0 ? first : second) ||
                            call.FindPropertyRelative("m_MethodName").stringValue != "SetActive" ||
                            call.FindPropertyRelative("m_Mode").intValue != (int)UnityEngine.Events.PersistentListenerMode.Bool ||
                            call.FindPropertyRelative("m_CallState").intValue != (int)UnityEngine.Events.UnityEventCallState.RuntimeOnly ||
                            call.FindPropertyRelative("m_Arguments.m_BoolArgument").boolValue != (i == 1)) return false;
                    }
                    return true;
                }
                if (deck && barrier && Original("whenTrue", barrier.gameObject, deck.gameObject) &&
                    Original("whenFalse", deck.gameObject, barrier.gameObject))
                {
                    for (int i = 1; i >= 0; i--)
                    {
                        UnityEventTools.RemovePersistentListener(state.whenTrue, i);
                        UnityEventTools.RemovePersistentListener(state.whenFalse, i);
                    }
                    state.showWhenTrue = deck.gameObject;
                    state.showWhenFalse = barrier.gameObject;
                    EditorUtility.SetDirty(state);
                }
            }

            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "Survey post";
            post.transform.SetParent(bridge.transform.parent, false);
            post.transform.position = new Vector3(-4.8f, .6f, -6.8f);
            post.transform.localScale = new Vector3(.65f, .6f, .65f);
            post.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                EasySuiteSceneBuilder.Content + "/Materials/Blue.mat");
            string path = EasySuiteSceneBuilder.Content + "/Quests/Survey Post.asset";
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            QuestInteractionAction inspect;
            if (!quest)
            {
                inspect = QuestQuickStart.Create(post, path);
                quest = inspect.quest;
                quest.titleKey = "quest.survey";
                quest.steps[1].goals[0].textKey = "quest.survey.inspect";
                quest.steps[2].outcomeKey = "quest.survey.done";
                EditorUtility.SetDirty(quest);
            }
            else
            {
                var goal = quest.steps.SelectMany(step => step.goals).First(goal => goal.mode == GoalMode.NewEvents);
                var marker = post.AddComponent<QuestTarget>(); marker.world = bridge.world; marker.id = goal.target;
                inspect = post.AddComponent<QuestInteractionAction>(); inspect.world = bridge.world; inspect.quest = quest;
                inspect.signal = goal.signal; inspect.ActionId = goal.signal;
                if (!bridge.world.quests.Contains(quest)) bridge.world.quests = bridge.world.quests.Append(quest).ToArray();
            }
            inspect.ActionLabel = "Inspect survey post";
            inspect.LabelKey = "quest.survey.inspect";
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("quest.survey", "Survey", "Осмотр местности"),
                ("quest.survey.inspect", "Inspect survey post", "Осмотреть синий столбик"),
                ("quest.survey.done", "Survey complete", "Осмотр завершён")
            });
            AssetDatabase.SaveAssets();
        }

        internal static void Apply(EasySuiteLab lab)
        {
            var bridge = lab.gameObject.scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EasySuiteQuestStation>(true))
                .Single(station => station.station == DeliveryStation.Bridge);
            var model = new GameObject("Bridge reactions");
            model.transform.SetParent(bridge.transform, false);
            model.transform.localPosition = new Vector3(-1.65f, -.6f, .5f);
            Material Finish(string name) => AssetDatabase.LoadAssetAtPath<Material>(
                EasySuiteSceneBuilder.Content + "/Materials/" + name + ".mat");
            GameObject Part(Transform parent, string name, Vector3 position, Vector3 size, Material material)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = name;
                part.transform.SetParent(parent, false);
                part.transform.localPosition = position;
                part.transform.localScale = size;
                part.GetComponent<Renderer>().sharedMaterial = material;
                return part;
            }
            var steel = Finish("Slate");
            var wood = Finish("Gold");
            var red = Finish("Coral");
            foreach (float side in new[] { -1f, 1f })
                Part(model.transform, "Abutment", new Vector3(0, .12f, side * 1.45f), new Vector3(1.65f, .24f, .55f), steel);
            var broken = new GameObject("Closed crossing");
            broken.transform.SetParent(model.transform, false);
            foreach (float side in new[] { -1f, 1f })
                Part(broken.transform, "Post", new Vector3(side * .65f, .5f, -1.25f), new Vector3(.12f, 1f, .12f), steel);
            Part(broken.transform, "Barrier", new Vector3(0, .78f, -1.25f), new Vector3(1.55f, .23f, .13f), red);
            var deck = new GameObject("Repaired deck");
            deck.transform.SetParent(model.transform, false);
            for (int i = -3; i <= 3; i++)
                Part(deck.transform, "Deck plank", new Vector3(0, .2f, i * .4f), new Vector3(1.55f, .16f, .38f), wood);
            deck.SetActive(false);
            var state = model.AddComponent<WorldStateReaction>();
            state.world = bridge.world;
            state.condition = new FactCondition { fact = "bridge.repaired", value = 1 };
            state.showWhenTrue = deck;
            state.showWhenFalse = broken;

            var beacon = new GameObject("Bridge route confirmation");
            beacon.transform.SetParent(bridge.transform, false);
            beacon.transform.localPosition = Vector3.up * .85f;
            var emitter = beacon.AddComponent<FeedbackEmitter>();
            emitter.Preset = lab.UiFeedback;
            emitter.ContextProvider = lab.FeedbackContextProvider;
            emitter.VisualScale = Vector3.one * 3f;
            var once = beacon.AddComponent<QuestEventReaction>();
            once.world = bridge.world;
            once.quest = bridge.delivery;
            once.trigger = QuestTransitionKind.StepCompleted;
            once.stepId = "bridge-used";
            UnityEventTools.AddPersistentListener(once.onTriggered, emitter.Play);
        }
    }
}
