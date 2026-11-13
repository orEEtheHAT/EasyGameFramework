using UnityEditor;
using UnityEngine;
using EasyMovement.Integrations;
using EasyGameFramework.Editor;

namespace EasyMovement.Editor
{
    [CustomEditor(typeof(RigidbodyMotor))]
    public sealed class RigidbodyMotorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var motor = (RigidbodyMotor)target;
            EditorGUILayout.HelpBox("Speeds are m/s; rates are m/s² independent of mass. Empty Profile uses sensible defaults. The motor controls only Local Direction; input is supplied separately. Neutral coasts, but axial overspeed brakes gradually to the current limits.", MessageType.Info);
            if (!string.IsNullOrEmpty(motor.ConfigurationError)) EditorGUILayout.HelpBox(motor.ConfigurationError, MessageType.Warning);
            if (!motor.GetComponent<RigidbodyCombatSpeed>())
            {
                EditorGUILayout.HelpBox("To react to Combat slow volumes and timed speed effects, enable Combat support once on this body. The volume itself only needs a trigger collider and an effect asset.", MessageType.Info);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || EditorUtility.IsPersistent(motor)))
                    if (GUILayout.Button("Enable Combat Speed Effects")) MovementCombatSetup.AddCombatSupport(motor);
            }
            if (!EditorApplication.isPlaying) return;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("Signed Speed (m/s)", motor.CurrentSpeed);
                EditorGUILayout.FloatField("Combined Speed Factor", motor.SpeedMultiplier);
                EditorGUILayout.FloatField("Forward Limit (m/s)", motor.EffectiveForwardSpeed);
                EditorGUILayout.FloatField("Reverse Limit (m/s)", motor.EffectiveReverseSpeed);
            }
        }
    }
    [CustomEditor(typeof(RigidbodyMotorProfile))]
    public sealed class RigidbodyMotorProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!((RigidbodyMotorProfile)target).IsValid)
                EditorGUILayout.HelpBox("Use finite speeds/rates between 0 and 100000. Braking must be positive. Invalid profiles suspend motor forces without changing velocity.", MessageType.Error);
        }
    }
}
