using UnityEditor;
using UnityEngine;

namespace EasyCombat.Editor
{
    internal sealed class ProjectileSetupWindow : EditorWindow
    {
        [MenuItem("Tools/EasyGameFramework/Combat/Projectile Settings")]
        private static void Open()
        {
            var window = GetWindow<ProjectileSetupWindow>(true, "EasyCombat | Projectile Settings");
            window.minSize = new Vector2(444, 285);
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Space(12);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(16, 16, 0, 12) });
            GUILayout.Label("Combat projectile settings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Choose how this project calculates projectile flight.", EditorStyles.wordWrappedLabel);
            GUILayout.Space(10);
            DrawOption(ProjectileExecutionMode.Parallel, "Parallel",
                "Process straight projectile movement in batches across available job workers. Scheduling adds overhead to small volleys. Players without worker support use a single thread.");
            GUILayout.Space(6);
            DrawOption(ProjectileExecutionMode.SingleThread, "Single Thread",
                "Run the same flight calculations on the main thread. Useful for debugging and comparing CPU costs. Large volleys share frame time with gameplay.");
            GUILayout.Space(8);
            EditorGUILayout.LabelField("Closing keeps the current mode. New projects default to Parallel. Also available in the EasyGameFramework Setup.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawOption(ProjectileExecutionMode mode, string title, string description)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool current = ProjectileExecutionSettings.RequestedMode == mode;
                    GUILayout.Label(current ? title + " (current)" : title, EditorStyles.boldLabel);
                    if (GUILayout.Button("Use " + title, GUILayout.Width(132)))
                    {
                        try { SaveChoice(mode); }
                        catch (System.InvalidOperationException error) { EditorUtility.DisplayDialog("Combat projectile settings", error.Message, "OK"); return; }
                        Close();
                        GUIUtility.ExitGUI();
                    }
                }
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            }
        }

        internal static void SaveChoice(ProjectileExecutionMode mode) => EasyGameFramework.Editor.CombatProjectileSetup.SetMode(mode);
    }
}
