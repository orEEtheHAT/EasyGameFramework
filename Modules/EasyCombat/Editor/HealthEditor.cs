using UnityEditor;
using UnityEngine;

namespace EasyCombat.Editor
{
    [CustomEditor(typeof(Health))]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "GamesFactory.Combat.Editor", "GamesFactory.Combat.Editor")]
    public sealed class HealthEditor : UnityEditor.Editor
    {
        private StatusDefinition selectedStatus;
        private bool showStatusBudgets;
        public override void OnInspectorGUI()
        {
            var health = (Health)target;
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawPropertiesExcluding(serializedObject, "rewardEnabled", "rewardProfile", "rewardRecipient",
                    "statusesEnabled", "maximumActiveStatuses", "maximumCatchUpTicksPerStatus", "maximumIndependentStackRecords",
                    "feedbacksEnabled", "damageFeedback", "deathFeedback", "healingFeedback", "feedbackContextProvider");
                EditorGUILayout.Space();
                var enabled = serializedObject.FindProperty("rewardEnabled");
                EditorGUILayout.PropertyField(enabled, new GUIContent("Enable Death Reward"));
                if (enabled.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("rewardProfile"), new GUIContent("Resource Reward"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("rewardRecipient"), new GUIContent("Recipient Wallet"));
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUILayout.Space();
            if (Application.isPlaying)
            {
                health.StatusesEnabled = EditorGUILayout.Toggle("Enable Statuses", health.StatusesEnabled);
                health.FeedbacksEnabled = EditorGUILayout.Toggle("Enable Health Feedbacks", health.FeedbacksEnabled);
            }
            else
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("statusesEnabled"), new GUIContent("Enable Statuses"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("feedbacksEnabled"), new GUIContent("Enable Health Feedbacks"));
            }
            bool statusEnabled = Application.isPlaying ? health.StatusesEnabled : serializedObject.FindProperty("statusesEnabled").boolValue;
            bool feedbackEnabled = Application.isPlaying ? health.FeedbacksEnabled : serializedObject.FindProperty("feedbacksEnabled").boolValue;
            if (statusEnabled)
            {
                showStatusBudgets = EditorGUILayout.Foldout(showStatusBudgets, "Advanced status limits", true);
                if (showStatusBudgets)
                    using (new EditorGUI.DisabledScope(Application.isPlaying))
                    {
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumActiveStatuses"), new GUIContent("Status Groups"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumCatchUpTicksPerStatus"), new GUIContent("Catch-up Ticks"));
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("maximumIndependentStackRecords"), new GUIContent("Independent Records"));
                    }
            }
            if (feedbackEnabled)
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("damageFeedback"), new GUIContent("Damage"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("deathFeedback"), new GUIContent("Death"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("healingFeedback"), new GUIContent("Healing"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("feedbackContextProvider"), new GUIContent("Context Provider"));
                    EditorGUI.indentLevel--;
                }
            serializedObject.ApplyModifiedProperties();
            if (feedbackEnabled && health.FeedbackContextProvider && !(health.FeedbackContextProvider is EasyFeedbacks.IFeedbackContextProvider))
                EditorGUILayout.HelpBox("Context Provider must implement IFeedbackContextProvider. Assign a player feedback context or leave this empty.", MessageType.Error);
            if (feedbackEnabled && !health.DamageFeedback && !health.DeathFeedback && !health.HealingFeedback)
                EditorGUILayout.HelpBox("Assign only the feedback presets you need. Empty slots do no work; no extra component is required.", MessageType.Info);
            if (health.RewardEnabled)
            {
                if (!health.RewardProfile)
                    EditorGUILayout.HelpBox("Assign a Resource Reward profile. Its Min/Max rows use resources from your Resource Database.", MessageType.Error);
                else
                    try { health.RewardProfile.ValidateConfiguration(); }
                    catch (System.Exception error) when (error is System.ArgumentException || error is System.InvalidOperationException || error is System.OverflowException)
                    { EditorGUILayout.HelpBox(error.Message, MessageType.Error); }
                if (!health.RewardRecipient) EditorGUILayout.HelpBox("Assign the recipient ResourceContainer, for example the player wallet.", MessageType.Error);
                EditorGUILayout.HelpBox("Confirmed death grants all resources once. Downed and restoring a corpse do not pay. A capacity failure rejects the whole reward and is not retried. Completion is available through Health.RewardCompleted / LastRewardResult.", MessageType.Info);
                if (Application.isPlaying && health.LastRewardResult.Status != DeathResourceRewardStatus.None)
                {
                    EditorGUILayout.LabelField("Last reward", health.LastRewardResult.Status.ToString());
                    if (!string.IsNullOrEmpty(health.LastRewardResult.Error)) EditorGUILayout.HelpBox(health.LastRewardResult.Error, MessageType.Warning);
                }
            }
            if (health.Definition != null)
                EditorGUILayout.HelpBox("Начальное здоровье и резисты берутся из Health Profile. Runtime-изменения здоровья не изменяют SO.", MessageType.Info);
            if (!Application.isPlaying) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", $"{health.Current:0.##} / {health.Maximum:0.##}");
            EditorGUILayout.LabelField("State", health.IsAlive ? "Alive" : "Dead");
            EditorGUILayout.LabelField("Неуязвимость (с)", health.InvulnerabilityRemaining.ToString("0.##"));
            health.Invulnerable = EditorGUILayout.Toggle("Принудительная неуязвимость", health.Invulnerable);
            if (health.Defense != null)
            {
                EditorGUILayout.LabelField("Щиты", $"{health.ShieldCurrent:0.##} / {health.ShieldMaximum:0.##}");
                for (var i = 0; i < health.Defense.Count; i++)
                {
                    var layer = health.Defense.GetLayer(i); var state = health.Defense.GetState(i);
                    if (layer == null) continue;
                    var enabled = EditorGUILayout.ToggleLeft($"{i}: {layer.name}" + (layer.IsShield ? $" · {state.Current:0.##}/{state.Maximum:0.##} · задержка {state.RechargeDelay:0.##} с" : ""), state.Enabled);
                    if (enabled != state.Enabled) health.SetDefenseLayerEnabled(i, enabled);
                    if (layer.IsShield && GUILayout.Button($"Восстановить щит {i}")) health.RestoreShield(i, state.Maximum);
                }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Damage 25")) health.ApplyDamage(new DamageInfo(25, null, health.transform.position));
            if (GUILayout.Button("Heal 25")) health.Heal(25);
            if (GUILayout.Button("Reset")) health.ResetToFull();
            EditorGUILayout.EndHorizontal();
            if (health.StatusesEnabled) HealthStatusesInspector.Draw(health, ref selectedStatus);
            Repaint();
        }
    }
}
