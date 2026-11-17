using UnityEditor;
using UnityEngine;

namespace EasyCombat.Editor
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "GamesFactory.Combat.Editor", "GamesFactory.Combat.Editor")]
    internal static class HealthStatusesInspector
    {
        public static void Draw(Health health, ref StatusDefinition selectedStatus)
        {
            if (!Application.isPlaying) return;
            selectedStatus = (StatusDefinition)EditorGUILayout.ObjectField("Статус для проверки", selectedStatus, typeof(StatusDefinition), false);
            using (new EditorGUI.DisabledScope(selectedStatus == null))
                if (GUILayout.Button("Наложить статус")) health.ApplyStatus(selectedStatus);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Активные статусы", health.StatusCount.ToString());
            if (health.StatusCount == 0) return;
            var controller = health.Statuses;
            for (var i = 0; i < controller.Count; i++)
            {
                var status = controller.GetStatus(i);
                EditorGUILayout.BeginHorizontal();
                if (status.Definition == null) { EditorGUILayout.EndHorizontal(); continue; }
                EditorGUILayout.LabelField(status.Independent
                    ? $"{status.Definition.Label} ×{status.Stacks} · спад через {status.NextStackExpiresIn:0.0} с · конец через {status.Remaining:0.0} с"
                    : $"{status.Definition.Label} ×{status.Stacks}  ·  {status.Remaining:0.0} с");
                var remove = GUILayout.Button("Снять", GUILayout.Width(55));
                EditorGUILayout.EndHorizontal();
                if (remove) { controller.Remove(status.Definition); break; }
                if (status.Independent)
                {
                    EditorGUI.indentLevel++;
                    for (var stackIndex = 0; stackIndex < status.Stacks; stackIndex++)
                    {
                        var stack = controller.GetStack(status.Definition, stackIndex);
                        EditorGUILayout.LabelField($"#{stack.Id} · {stack.Remaining:0.00} с · {stack.Origin.Actor.Name ?? "Окружение"}",
                            double.IsPositiveInfinity(stack.NextTickIn) ? "Без тиков" : $"Тик через {stack.NextTickIn:0.00} с");
                        if (status.Definition.TickEffects != null)
                            for (var effectIndex = 0; effectIndex < status.Definition.TickEffects.Length; effectIndex++)
                                if (controller.TryGetStackDamage(status.Definition, stackIndex, effectIndex, out var amount))
                                    EditorGUILayout.LabelField($"    Урон тика: {amount:0.###} до резистов", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }
                if (status.PendingStacks > 0) EditorGUILayout.LabelField($"Завершение догоняющих тиков: {status.PendingStacks}", EditorStyles.miniLabel);
            }
            if (GUILayout.Button("Снять все")) controller.Clear();
        }
    }
}
