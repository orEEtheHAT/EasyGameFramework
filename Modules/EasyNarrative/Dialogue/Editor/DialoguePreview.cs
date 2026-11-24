using System;
using System.Collections.Generic;
using System.Linq;
using EasyQuests;
using EasyResources;
using UnityEditor;
using UnityEngine;

namespace EasyNarrative.Editor
{
    public sealed class DialoguePreview : EditorWindow
    {
        [SerializeField] private DialogueDefinition dialogue;
        private DialogueRun run;
        private QuestSession world;
        private ResourceStore previewWallet;
        private readonly HashSet<ResourceDefinition> previewResources = new();
        private QuestDefinition[] quests;
        private WorldFactDatabase temporaryDatabase;
        private Vector2 scroll;
        private bool editWorld;
        private string error;
        private int contentVersion;

        private int ContentVersion()
        {
            int version = EditorUtility.GetDirtyCount(dialogue);
            if (dialogue.database) version = unchecked(version * 31 + EditorUtility.GetDirtyCount(dialogue.database));
            if (quests != null)
                foreach (var quest in quests)
                    version = unchecked(version * 31 + (quest ? EditorUtility.GetDirtyCount(quest) : -1));
            return version;
        }

        public static DialoguePreview Open(DialogueDefinition asset)
        {
            var window = GetWindow<DialoguePreview>("Conversation Preview");
            window.dialogue = asset;
            window.minSize = new Vector2(360, 310);
            window.ResetPreview();
            window.Show();
            return window;
        }

        private void OnDisable() => DisposePreview();

        private void DisposePreview()
        {
            run?.Dispose();
            run = null;
            if (previewWallet != null) { if (world != null) previewWallet.DetailedChanged -= world.ObserveWalletChange; previewWallet.Dispose(); previewWallet = null; }
            world = null;
            if (temporaryDatabase) DestroyImmediate(temporaryDatabase);
        }

        private void ResetPreview()
        {
            DisposePreview();
            error = null;
            try
            {
                dialogue.Invalidate();
                dialogue.ValidateGraph();
                previewResources.Clear();
                var used = new HashSet<QuestDefinition>();
                var signals = new HashSet<string>();
                foreach (var line in dialogue.lines)
                    foreach (var reply in line.replies)
                    {
                        void InspectAction(DialogueAction effect)
                        {
                            if (effect.kind == DialogueActionType.StartQuest) used.Add(effect.quest);
                            if (effect.kind == DialogueActionType.SendEvent) signals.Add(effect.key);
                        }
                        void InspectCondition(DialogueRequirement condition)
                        {
                            if (condition.quest && condition.kind != DialogueRequirementType.WorldFact) used.Add(condition.quest);
                            if (condition.kind == DialogueRequirementType.ResourceAvailable && condition.resource) previewResources.Add(condition.resource);
                        }
                        InspectAction(reply.action);
                        foreach (var effect in reply.additionalActions ?? Array.Empty<DialogueAction>()) InspectAction(effect);
                        foreach (var effect in reply.failureActions ?? Array.Empty<DialogueAction>()) InspectAction(effect);
                        foreach (var condition in reply.when) InspectCondition(condition);
                        if (reply.groups != null) foreach (var group in reply.groups) foreach (var rule in group.rules) InspectCondition(rule);
                        if (reply.price != null) foreach (var row in reply.price.Items) previewResources.Add(row.Resource);
                    }
                var db = dialogue.database ? dialogue.database : used.FirstOrDefault()?.database;
                if (db && signals.Count > 0)
                    foreach (var guid in AssetDatabase.FindAssets("t:QuestDefinition"))
                    {
                        var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                        if (quest && quest.database == db && quest.steps != null && quest.steps.Any(step =>
                            step?.type == QuestStepType.Objectives && step.goals != null && step.goals.Any(goal =>
                                goal?.mode == GoalMode.NewEvents && signals.Contains(goal.signal)))) used.Add(quest);
                    }
                if (!db)
                {
                    temporaryDatabase = CreateInstance<WorldFactDatabase>();
                    temporaryDatabase.hideFlags = HideFlags.HideAndDontSave;
                    db = temporaryDatabase;
                }
                quests = used.OrderBy(q => q.title).ToArray();
                previewWallet = new ResourceStore();
                world = new QuestSession(db, quests, previewWallet.GetAvailable);
                previewWallet.DetailedChanged += world.ObserveWalletChange;
                world.StartAvailable();
                Replay();
            }
            catch (Exception ex) { DisposePreview(); error = ex.Message; }
            contentVersion = ContentVersion();
        }

        private void Replay()
        {
            run?.Dispose();
            run = new DialogueRun(dialogue, new DialogueContext(world, previewWallet, null, null, true));
        }

        private void OnGUI()
        {
            if (!dialogue) { EditorGUILayout.HelpBox("Open Preview conversation from a dialogue graph.", MessageType.Info); return; }
            if ((world == null && error == null) || contentVersion != ContentVersion()) ResetPreview();
            GUILayout.Label(dialogue.name, EditorStyles.boldLabel);
            NarrativePreviewLanguage.Draw();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset preview")) ResetPreview();
                if (world != null && GUILayout.Button("Replay with current facts")) Replay();
            }
            if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            editWorld = EditorGUILayout.Foldout(editWorld, "Test world · facts and quest progress", true);
            if (editWorld) DrawWorld();
            if (!run.IsOpen) EditorGUILayout.HelpBox("Conversation ended.", MessageType.Info);
            else
            {
                var line = run.Current;
                EditorGUILayout.LabelField(NarrativePreviewLanguage.Text(line.SpeakerKey, line.SpeakerName), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(NarrativePreviewLanguage.Text(line.textKey, line.text), EditorStyles.wordWrappedLabel);
                foreach (var reply in line.replies)
                {
                    bool available = run.Replies.Contains(reply);
                    using (new EditorGUI.DisabledScope(!available))
                        if (GUILayout.Button(NarrativePreviewLanguage.Text(reply.textKey, reply.text)))
                        {
                            run.Choose(reply.id, run.Revision);
                            GUIUtility.ExitGUI();
                        }
                    if (!available) EditorGUILayout.HelpBox(NarrativePreviewLanguage.Text(reply.lockedReasonKey, run.Explain(reply)), MessageType.None);
                }
                if (run.Replies.Count == 0 && GUILayout.Button("Continue")) run.Continue(run.Revision);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawWorld()
        {
            using var panel = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            foreach (var res in previewResources)
            {
                EditorGUILayout.LabelField(res.DisplayName, res.Format(previewWallet.GetAvailable(res)));
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add 1")) previewWallet.TryAdd(res, res.UnitsPerWhole, out _);
                    if (GUILayout.Button("Spend 1")) previewWallet.TrySpend(new ResourceCost(ResourceAmount.FromQuantity(res, 1)));
                }
            }
            foreach (var fact in world.Database.facts)
            {
                int old = world.GetFact(fact.id);
                int next = fact.type switch
                {
                    FactType.Flag => EditorGUILayout.Toggle(fact.name, old != 0) ? 1 : 0,
                    FactType.State => EditorGUILayout.IntPopup(fact.name, old, fact.states.Select(s => s.name).ToArray(), fact.states.Select(s => s.value).ToArray()),
                    _ => EditorGUILayout.DelayedIntField(fact.name, old)
                };
                if (next != old) world.SetFact(fact.id, next, "Preview");
            }
            foreach (var quest in quests)
            {
                var progress = world.Find(quest.id);
                EditorGUILayout.LabelField(quest.title, progress == null ? "Not started" : progress.Current.name);
                if (progress == null)
                {
                    if (GUILayout.Button("Start " + quest.title)) world.StartQuest(quest.id);
                }
                else if (!progress.Finished)
                    foreach (var goal in progress.Current.goals)
                    {
                        if (goal.mode == GoalMode.WorldState)
                            EditorGUILayout.LabelField(goal.title, "Change " + Array.Find(world.Database.facts, f => f.id == goal.condition.fact).name + " above");
                        else if (goal.UsesResource)
                        {
                            EditorGUILayout.LabelField(goal.title, "Wallet: " + goal.resource.Format(previewWallet.GetAvailable(goal.resource)));
                            if (GUILayout.Button("Add " + goal.DisplayCount(goal.count) + " " + goal.resource.DisplayName))
                                previewWallet.TryAdd(goal.resource, goal.count, out _);
                            if (goal.mode == GoalMode.TurnInResource && GUILayout.Button("Hand over resource"))
                                world.TryTurnIn(quest, goal, previewWallet);
                        }
                        else if (!progress.Satisfied(goal) && GUILayout.Button(goal.title + " · send event (" + progress.Progress(goal) + "/" + goal.count + ")"))
                            world.Signal(goal.signal);
                    }
            }
        }
    }
}
