using System;
using System.Globalization;
using System.Linq;
using Health = EasyCombat.Health;
using EasyInteraction;
using EasyNarrative;
using EasyNarrative.Interaction;
using EasyQuests.Integrations;
using EasyResources;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

namespace EasyQuests.Editor
{
    public enum ObjectivePreset { Talk, Interact, EnterZone, Defeat, DefeatGroup, ReceiveResource, HaveResource, GiveResource, CustomEvent }

    public sealed class QuestObjectiveWizard : EditorWindow
    {
        private QuestDefinition quest;
        private QuestStep step;
        private QuestGoal existing;
        private QuestWorld world;
        private ResourceContainer wallet;
        private GameObject source;
        private InteractionActor actor;
        private InteractionAction interaction;
        private DialogueDefinition dialogue;
        [SerializeField] private ResourceDefinition resource;
        private ObjectivePreset preset;
        private string group = "", error;
        private int count = 1;
        private string quantity = "1";

        internal static void Open(QuestDefinition quest, QuestStep step, QuestGoal goal = null)
        {
            var w = CreateInstance<QuestObjectiveWizard>();
            w.titleContent = new GUIContent(step.type == QuestStepType.End ? "Quest reward" : "Quest objective");
            w.quest = quest; w.step = step; w.existing = goal;
            var worlds = FindObjectsByType<QuestWorld>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(x => x.database == quest.database).ToArray();
            var owners = worlds.Where(x => Array.IndexOf(x.quests, quest) >= 0).ToArray();
            w.world = owners.Length == 1 ? owners[0] : worlds.Length == 1 ? worlds[0] : null;
            w.wallet = w.world ? w.world.wallet : null;
            var actors = FindObjectsByType<InteractionActor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (actors.Length == 1) w.actor = actors[0];
            w.ShowUtility();
        }

        internal static void Quantity(SerializedProperty units, ResourceDefinition r)
        {
            if (!r) return;
            var text = EditorGUILayout.DelayedTextField("Quantity", r.FromUnits(units.intValue).ToString(CultureInfo.InvariantCulture));
            if (TryUnits(r, text, out int value)) units.intValue = value;
            else EditorGUILayout.HelpBox("Enter a positive quantity within this resource's precision.", MessageType.Warning);
        }

        private static bool TryUnits(ResourceDefinition r, string text, out int value)
        {
            value = 0;
            if (!r || !decimal.TryParse(text.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount)) return false;
            try { value = checked((int)r.ToUnits(amount)); return value > 0; }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        private void OnGUI()
        {
            if (!quest || step == null) { Close(); return; }
            EditorGUILayout.LabelField(quest.title + " / " + step.name, EditorStyles.boldLabel);
            var owner = (QuestWorld)EditorGUILayout.ObjectField("Quest world", world, typeof(QuestWorld), true);
            if (owner != world) { world = owner; wallet = world ? world.wallet : null; }
            bool end = step.type == QuestStepType.End;
            if (!end && existing == null) preset = (ObjectivePreset)EditorGUILayout.EnumPopup("Objective", preset);
            bool usesWallet = end || existing != null || preset == ObjectivePreset.GiveResource ||
                preset == ObjectivePreset.ReceiveResource || preset == ObjectivePreset.HaveResource;
            if (usesWallet) wallet = (ResourceContainer)EditorGUILayout.ObjectField("Player wallet", wallet, typeof(ResourceContainer), true);
            bool needsObject = end ? step.rewardMode == QuestRewardMode.AtInteraction : existing != null ||
                preset == ObjectivePreset.GiveResource || preset == ObjectivePreset.Talk || preset == ObjectivePreset.Interact ||
                preset == ObjectivePreset.Defeat || preset == ObjectivePreset.EnterZone;
            if (needsObject)
            {
                var pick = (GameObject)EditorGUILayout.ObjectField("Scene object", source, typeof(GameObject), true);
                if (pick != source) { source = pick; interaction = null; }
            }
            if (!end && existing == null)
            {
                if (usesWallet)
                {
                    string help = preset == ObjectivePreset.HaveResource ? "Counts available stock, including resources owned before accepting the quest." :
                        preset == ObjectivePreset.GiveResource ? "Requires explicit hand-in at the scene object and spends the full amount once." :
                        "Counts new incoming resources while this step is active. Loading a save does not count as income.";
                    EditorGUILayout.HelpBox(help, MessageType.Info);
                    var so = new SerializedObject(this);
                    EditorGUILayout.PropertyField(so.FindProperty("resource")); so.ApplyModifiedProperties();
                    quantity = EditorGUILayout.TextField("Quantity", quantity);
                }
                else if (preset != ObjectivePreset.CustomEvent) count = Mathf.Max(1, EditorGUILayout.IntField("Count", count));
                if (preset == ObjectivePreset.EnterZone) actor = (InteractionActor)EditorGUILayout.ObjectField("Entering player", actor, typeof(InteractionActor), true);
                if (preset == ObjectivePreset.DefeatGroup)
                {
                    var keys = NarrativeKeyPicker.KnownKeys(quest.database,true,true);
                    int index = Array.IndexOf(keys,group) + 1;
                    int next = EditorGUILayout.Popup("Loaded groups",index,new[]{"Choose group"}.Concat(keys).ToArray());
                    if (index != next) group = next == 0 ? "" : keys[next-1];
                    group = EditorGUILayout.TextField("Group",group);
                }
                if (preset == ObjectivePreset.Talk && source && (!source.TryGetComponent<TalkAction>(out var talk) || !talk.dialogue))
                    dialogue = (DialogueDefinition)EditorGUILayout.ObjectField("Dialogue", dialogue, typeof(DialogueDefinition), false);
                if (preset == ObjectivePreset.Interact && source)
                {
                    var options = source.GetComponents<InteractionAction>();
                    if (options.Length > 0)
                    {
                        int index = Mathf.Max(0, Array.IndexOf(options, interaction));
                        interaction = options[EditorGUILayout.Popup("Action", index, options.Select(a => a.ActionLabel + " (" + a.GetType().Name + ")").ToArray())];
                    }
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button(end || existing != null ? "Connect" : "Add objective"))
            {
                Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
                try
                {
                    if (Application.isPlaying || Array.IndexOf(quest.steps,step)<0) throw new InvalidOperationException("Reopen this step in Edit Mode.");
                    if (preset != ObjectivePreset.CustomEvent || end || existing != null)
                    {
                        if (!world || world.database != quest.database || !world.gameObject.scene.IsValid()) throw new ArgumentException("Choose the scene world with this quest's database.");
                        if (needsObject && (!source || source.scene != world.gameObject.scene)) throw new ArgumentException("Choose an object in the quest world's scene.");
                        Undo.RegisterCompleteObjectUndo(world,"Connect quest world");
                        if (usesWallet) world.wallet = wallet ? wallet : throw new ArgumentException("Choose the player wallet.");
                        if (!world.quests.Contains(quest)) world.quests = world.quests.Append(quest).ToArray();
                        PrefabUtility.RecordPrefabInstancePropertyModifications(world);
                    }
                    if (end || existing != null)
                    {
                        if (needsObject) BindHandIn(source,world,quest,step,existing);
                    }
                    else
                    {
                        int amount=count;
                        if (usesWallet && !TryUnits(resource,quantity,out amount)) throw new ArgumentException("Select a resource and enter a valid positive quantity.");
                        Add(quest,step,preset,world,source,actor,group,resource,amount,interaction,dialogue);
                    }
                    Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(undo);Close();
                }
                catch (Exception ex) { Undo.RevertAllDownToGroup(undo);error=ex.Message; }
            }
        }

        public static QuestHandInAction BindHandIn(GameObject source, QuestWorld world, QuestDefinition quest, QuestStep step, QuestGoal goal)
        {
            var key=goal?.id ?? "";
            var action=source.GetComponents<QuestHandInAction>().FirstOrDefault(a=>a.world==world && a.quest==quest &&
                a.stepId==step.id && (a.goalId??"")==key);
            if (!action) action=Undo.AddComponent<QuestHandInAction>(source);
            Undo.RecordObject(action,"Bind quest hand-in");
            action.world=world;action.quest=quest;action.stepId=step.id;action.goalId=key;
            action.ActionId="quest."+quest.id+"."+step.id+"."+key;
            action.ActionLabel=goal==null?"Collect reward":goal.title;
            action.HideWhenUnavailable=false;
            if (!source.GetComponentInChildren<Collider>()) Undo.AddComponent<BoxCollider>(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(action);EditorUtility.SetDirty(action);
            return action;
        }

        public static QuestGoal Add(QuestDefinition quest, QuestStep step, ObjectivePreset preset, QuestWorld world, GameObject source,
            InteractionActor actor, string group, ResourceDefinition resource, int count, InteractionAction interaction=null, DialogueDefinition dialogue=null)
        {
            if (step.type!=QuestStepType.Objectives || count<1) throw new ArgumentException("Select an Objectives step and positive count.");
            var goal=new QuestGoal { mode=GoalMode.NewEvents, title=ObjectNames.NicifyVariableName(preset.ToString()),count=count };
            goal.signal="objective."+quest.id+"."+goal.id;
            switch(preset)
            {
                case ObjectivePreset.Talk:
                case ObjectivePreset.Interact:
                    if (preset==ObjectivePreset.Talk)
                    {
                        var talk=source.GetComponent<TalkAction>();
                        if (!talk || !talk.dialogue)
                        {
                            if (!dialogue) throw new ArgumentException("Assign a dialogue for this NPC.");
                            if (!talk) talk=Undo.AddComponent<TalkAction>(source);
                            Undo.RecordObject(talk,"Assign dialogue");talk.dialogue=dialogue;
                            PrefabUtility.RecordPrefabInstancePropertyModifications(talk);
                        }
                        interaction=talk;
                    }
                    if (interaction && interaction.gameObject != source) throw new ArgumentException("Choose an action on the selected scene object.");
                    if (!interaction) interaction=source.GetComponent<InteractionAction>();
                    if (!interaction) interaction=Undo.AddComponent<EventAction>(source);
                    Undo.RecordObject(interaction,"Bind objective");
                    UnityEventTools.AddStringPersistentListener(interaction.OnCompleted, world.Signal, goal.signal);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(interaction);
                    if (!source.GetComponentInChildren<Collider>()) Undo.AddComponent<BoxCollider>(source);
                    goal.title=(preset==ObjectivePreset.Talk?"Talk to ":"Interact with ")+source.name;
                    break;
                case ObjectivePreset.EnterZone:
                    if (!actor || !source.GetComponents<Collider>().Any(c=>c.isTrigger))
                        throw new ArgumentException("Choose the player and an object with a trigger collider.");
                    var zone=Undo.AddComponent<QuestZone>(source);zone.world=world;zone.actor=actor.transform;zone.signal=goal.signal;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(zone);goal.title="Enter "+source.name;
                    break;
                case ObjectivePreset.Defeat:
                    var health=source.GetComponentInParent<Health>();
                    if (!health) throw new ArgumentException("Choose an object with Health.");
                    var death=Undo.AddComponent<QuestEventBinding>(health.gameObject);
                    death.world=world;death.signal=goal.signal;death.source=QuestEventBinding.EventSource.Death;death.health=health;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(death);goal.title="Defeat "+source.name;
                    break;
                case ObjectivePreset.DefeatGroup:
                    if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Choose a Quest Target group.");
                    var binding=Undo.AddComponent<QuestGroupDeaths>(world.gameObject);
                    binding.world=world;binding.group=group;binding.signal=goal.signal;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(binding);
                    goal.targetGroup=true;goal.target=group;goal.marker=MarkerStyle.Target;goal.title="Defeat "+group;
                    break;
                case ObjectivePreset.ReceiveResource:
                case ObjectivePreset.HaveResource:
                case ObjectivePreset.GiveResource:
                    if (!resource) throw new ArgumentException("Choose a resource.");
                    goal.resource=resource;
                    goal.mode=preset==ObjectivePreset.ReceiveResource?GoalMode.ReceiveResource:preset==ObjectivePreset.HaveResource?GoalMode.HaveResource:GoalMode.TurnInResource;
                    goal.title=ObjectNames.NicifyVariableName(preset.ToString()).Replace(" Resource","")+" "+resource.Format(count)+" "+resource.DisplayName;
                    if (preset==ObjectivePreset.GiveResource) BindHandIn(source,world,quest,step,goal);
                    break;
            }
            if (source && preset!=ObjectivePreset.CustomEvent && preset!=ObjectivePreset.HaveResource && preset!=ObjectivePreset.ReceiveResource && !goal.targetGroup)
            {
                var marker=source.GetComponents<QuestTarget>().FirstOrDefault(t=>t.world==world && !string.IsNullOrEmpty(t.id));
                if (!marker)
                {
                    marker=Undo.AddComponent<QuestTarget>(source);
                    marker.world=world;marker.id="target."+goal.id;PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
                }
                goal.marker=MarkerStyle.Target;goal.target=marker.id;
            }
            Undo.RecordObject(quest,"Add quest objective");
            var goals=step.goals ?? Array.Empty<QuestGoal>();
            bool placeholder=goals.Length==1 && goals[0].title=="New objective" && goals[0].signal=="completed" && goals[0].count==1 && goals[0].marker==MarkerStyle.Hidden;
            step.goals=placeholder?new[]{goal}:goals.Append(goal).ToArray();
            EditorUtility.SetDirty(quest);return goal;
        }
    }
}
