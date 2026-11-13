using EasyCombat;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    public sealed partial class FrameworkSetupWindow
    {
        partial void DrawCombat()
        {
            try { DrawCombatChoice(); }
            catch (ExitGUIException) { throw; }
            catch (System.Exception e) { EditorGUILayout.HelpBox("Combat configuration: " + e.Message, MessageType.Error); }
        }
        private void DrawCombatChoice()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Combat projectile execution", EditorStyles.boldLabel);
                GUILayout.Label("New projects use Parallel. Single thread is useful for debugging and comparing measurements.", EditorStyles.wordWrappedLabel);
                using (new EditorGUI.DisabledScope(Busy))
                {
                    var current = CombatProjectileSetup.RequestedMode;
                    EditorGUI.BeginChangeCheck();
                    var selected = (ProjectileExecutionMode)EditorGUILayout.EnumPopup("Requested mode", current);
                    if (EditorGUI.EndChangeCheck() && selected != current)
                    {
                        try { CombatProjectileSetup.SetMode(selected); message = "Combat projectile mode saved."; }
                        catch (System.Exception e) { message = "Could not save Combat mode: " + e.Message; }
                    }
                }
                EditorGUILayout.LabelField("Effective mode", CombatProjectileSetup.EffectiveMode.ToString());
                EditorGUILayout.LabelField("Project choice", CombatProjectileSetup.HasExplicitSelection ? "Saved explicitly" : "Default (Parallel)");
                string diagnostic = CombatProjectileSetup.SupportDiagnostic;
                if (!string.IsNullOrEmpty(diagnostic)) EditorGUILayout.HelpBox(diagnostic, CombatProjectileSetup.ParallelAvailable ? MessageType.Info : MessageType.Warning);
            }
        }
    }
}
