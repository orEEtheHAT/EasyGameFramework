using System;
using EasyCombat;
using EasyMovement;
using EasyMovement.Integrations;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>One-time authoring helper. Does not change profiles, physics settings or existing bindings.</summary>
    public static class MovementCombatSetup
    {
        public static RigidbodyCombatSpeed AddCombatSupport(RigidbodyMotor motor)
        {
            if (!motor) throw new ArgumentNullException(nameof(motor));
            if (Application.isPlaying || EditorUtility.IsPersistent(motor))
                throw new InvalidOperationException("Configure a scene object or open its prefab in Prefab Mode, outside Play Mode.");
            var existing = motor.GetComponent<RigidbodyCombatSpeed>();
            if (existing) return existing;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Enable Combat Speed Effects");
            try
            {
                // Explicit Undo for both additions makes the complete setup reversible in one operation.
                var influences = motor.GetComponent<MovementInfluences>();
                if (!influences) influences = Undo.AddComponent<MovementInfluences>(motor.gameObject);
                var adapter = Undo.AddComponent<RigidbodyCombatSpeed>(motor.gameObject);
                Undo.RecordObject(adapter, "Connect Combat Speed");
                adapter.Motor = motor;
                adapter.Influences = influences;
                PrefabUtility.RecordPrefabInstancePropertyModifications(adapter);
                return adapter;
            }
            finally { Undo.CollapseUndoOperations(group); }
        }
    }

    [CustomEditor(typeof(RigidbodyCombatSpeed))]
    public sealed class RigidbodyCombatSpeedEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var adapter = (RigidbodyCombatSpeed)target;
            var motor = adapter.Motor ? adapter.Motor : adapter.GetComponent<RigidbodyMotor>();
            var influences = adapter.Influences ? adapter.Influences : adapter.GetComponent<MovementInfluences>();
            EditorGUILayout.HelpBox("Keep one Combat Speed component on the moving body. On each area, add a trigger collider and Combat Slow Volume, then assign a Movement Influence asset. Combat combines the strongest spatial slow and timed status once; only forward/reverse speed limits change.", MessageType.Info);
            if (!motor || !influences)
                EditorGUILayout.HelpBox("Assign a Rigidbody Motor and Movement Influences, or keep them on this object for automatic linking.", MessageType.Error);
            else
            {
                if (motor.gameObject != adapter.gameObject || influences.gameObject != adapter.gameObject)
                    EditorGUILayout.HelpBox("These references target another object. Ensure that body's motor has only one Combat Speed adapter; two adapters would apply the same multiplier twice.", MessageType.Warning);
                if (!motor.enabled || !influences.enabled)
                    EditorGUILayout.HelpBox("Enable both Rigidbody Motor and Movement Influences for Combat speed effects.", MessageType.Warning);
            }
        }
    }
}
