using System;
using System.Collections.Generic;
using System.Linq;
using EasyLocalization;
using EasyQuests;
using EasyUI;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;

namespace EasySuite.Example.Editor
{
    internal static class FrameworkPatrolMarkers
    {
        internal static void Apply(EasySuiteLab lab)
        {
            const string folder = EasySuiteSceneBuilder.Content + "/Quests/";
            const string effects = "Packages/com.oreethehat.easy-game-framework/Modules/EasyUI/Runtime/IconEffects/";
            var library = AssetDatabase.LoadAssetAtPath<QuestIconLibrary>(folder + "Marker Icons.asset");
            var main = library.icons.FirstOrDefault(e => e.name == "Main");
            var enemy = library.icons.FirstOrDefault(e => e.name == "Enemy");
            if (main != null && !main.spriteMaterial) main.spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(effects + "Shimmer.mat");
            if (enemy != null)
            {
                if (!enemy.spriteMaterial) enemy.spriteMaterial = AssetDatabase.LoadAssetAtPath<Material>(effects + "Soft Pulse.mat");
                enemy.labelMode = MarkerLabelMode.OnScreenOnly;
                enemy.edgeMode = MarkerEdgeMode.Hide;
            }
            EditorUtility.SetDirty(library);
            var world = lab.GetComponent<QuestWorld>();
            var facts = new List<WorldFact>(world.database.facts);
            string Key(int i) => "patrol.inspected." + i;
            for (int i = 1; i <= 10; i++)
                if (!facts.Any(f => f.id == Key(i))) facts.Add(new WorldFact { id = Key(i), name = "Patrol inspected " + i });
            world.database.facts = facts.ToArray();
            EditorUtility.SetDirty(world.database);
            FrameworkDialoguePlayground.InstallStrings(lab, new[] {
                ("patrol.name", "Patrol exercise", "Учебный патруль"), ("patrol.goal", "Inspect the patrol targets", "Осмотрите мишени патруля"),
                ("patrol.start", "Track ten targets", "Отследить десять мишеней"), ("patrol.inspect", "Inspect target", "Осмотреть мишень"),
                ("patrol.inspected", "Already inspected", "Уже осмотрено"), ("patrol.done", "Patrol inspected", "Патруль осмотрен")
            });
            var path = folder + "Patrol Exercise.asset";
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            if (!quest)
            {
                quest = ScriptableObject.CreateInstance<QuestDefinition>();
                quest.id = "suite.patrol"; quest.title = "Patrol exercise"; quest.titleKey = "patrol.name";
                quest.database = world.database; quest.iconLibrary = library; quest.defaultIconId = enemy?.Id;
                quest.steps = new[] {
                    new QuestStep { id = "start", name = "Start", type = QuestStepType.Start, next = "inspect", position = new Vector2(20,60) },
                    new QuestStep { id = "inspect", name = "Inspect patrol", type = QuestStepType.Objectives, next = "done", position = new Vector2(230,60),
                        goals = new[] { new QuestGoal { id="inspect-ten", title="Inspect the patrol targets", textKey="patrol.goal",
                            mode=GoalMode.NewEvents, signal="patrol.inspected", count=10, marker=MarkerStyle.Target,
                            targetGroup=true, target="patrol", labelMaxDistance=6 } } },
                    new QuestStep { id="done", name="Done", type=QuestStepType.End, outcome="Patrol inspected", outcomeKey="patrol.done", position=new Vector2(440,60) }
                };
                quest.ValidateGraph(); AssetDatabase.CreateAsset(quest, path);
            }
            if (!world.quests.Contains(quest)) world.quests = world.quests.Append(quest).ToArray();
            EditorUtility.SetDirty(world);
            var root = new GameObject("Patrol marker exercise");
            var material = AssetDatabase.LoadAssetAtPath<Material>(EasySuiteSceneBuilder.Content + "/Materials/Coral.mat");
            for (int i = 0; i <= 10; i++)
            {
                bool board = i == 0;
                var go = GameObject.CreatePrimitive(board ? PrimitiveType.Cube : PrimitiveType.Capsule);
                go.name = board ? "Patrol board" : "Patrol target " + i;
                go.transform.SetParent(root.transform, false);
                go.transform.position = board ? new Vector3(-1.5f,.6f,-7f) : new Vector3(7f + ((i-1)%2)*2f,.55f,-7f + ((i-1)/2)*3.5f);
                go.transform.localScale = board ? Vector3.one : new Vector3(.55f,.55f,.55f);
                go.GetComponent<Renderer>().sharedMaterial = material;
                var action = go.AddComponent<PatrolMarkerAction>();
                action.world = world; action.quest = quest; action.lab = lab;
                action.ActionId = "suite.patrol." + i; action.Target.PersistentId = action.ActionId;
                action.ActionLabel = board ? "Track ten targets" : "Inspect target";
                action.LabelKey = board ? "patrol.start" : "patrol.inspect";
                if (board)
                {
                    var sign = new GameObject("Station sign", typeof(RectTransform)); sign.transform.SetParent(go.transform,false);
                    sign.transform.localPosition = Vector3.up * 1.2f;
                    var text = sign.AddComponent<TextMeshPro>(); text.font = UIText.DefaultFont; text.fontSize = 2;
                    text.alignment = TextAlignmentOptions.Center; text.rectTransform.sizeDelta = new Vector2(2.6f,1f); text.text = action.ActionLabel;
                    var collection = LocalizationEditorSettings.GetStringTableCollection("Framework Playground");
                    var loc = sign.AddComponent<LocalizedText>(); loc.fallback = action.ActionLabel;
                    loc.text = new LocalizedString(collection.SharedData.TableCollectionNameGuid, collection.SharedData.GetEntry(action.LabelKey).Id);
                    sign.AddComponent<EasySuiteLabel>();
                }
                else
                {
                    action.inspectedFact = Key(i);
                    var anchor = new GameObject("Patrol marker"); anchor.transform.SetParent(go.transform,false);
                    var target = anchor.AddComponent<QuestTarget>(); target.world = world; target.group = "patrol"; target.offset = Vector3.up * 1.4f;
                    var visibility = go.AddComponent<WorldStateReaction>(); visibility.world = world;
                    visibility.condition = new FactCondition { fact = Key(i), value = 1 }; visibility.showWhenFalse = anchor;
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
