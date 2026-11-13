using EasyCombat;
using EasyGameFramework.Editor;
using EasyMovement.FirstPerson;
using EasyMovement.Integrations;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyMovement.Tests
{
    public sealed class MovementSetupTests
    {
        [Test]
        public void CombatSetupIsIdempotentUndoableAndKeepsAuthoredPhysics()
        {
            var go = new GameObject("Movement setup regression");
            var profile = ScriptableObject.CreateInstance<RigidbodyMotorProfile>();
            bool redoPending = false;
            try
            {
                var motor = go.AddComponent<RigidbodyMotor>();
                motor.Profile = profile; motor.Body.mass = 7;
                motor.Body.constraints = RigidbodyConstraints.FreezeRotationX;
                var adapter = MovementCombatSetup.AddCombatSupport(motor);
                Assert.AreSame(adapter, MovementCombatSetup.AddCombatSupport(motor));
                Assert.AreEqual(1, go.GetComponents<RigidbodyCombatSpeed>().Length);
                Assert.AreEqual(1, go.GetComponents<MovementInfluences>().Length);
                Assert.AreSame(motor, adapter.Motor);
                Assert.AreSame(go.GetComponent<MovementInfluences>(), adapter.Influences);
                Assert.AreSame(profile, motor.Profile); Assert.AreEqual(7, motor.Body.mass);
                Assert.AreEqual(RigidbodyConstraints.FreezeRotationX, motor.Body.constraints);
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo(); redoPending = true;
                Assert.IsNull(go.GetComponent<RigidbodyCombatSpeed>());
                Assert.IsNull(go.GetComponent<MovementInfluences>());
                Assert.IsNotNull(go.GetComponent<RigidbodyMotor>());
                // Restore live identifiers before clearing their history. Do not leave a structure
                // redo operation referring to components whose owner is destroyed by teardown.
                Undo.PerformRedo(); redoPending = false;
                adapter = go.GetComponent<RigidbodyCombatSpeed>();
                Assert.IsNotNull(adapter);
                Assert.AreSame(motor, adapter.Motor);
                Assert.AreSame(go.GetComponent<MovementInfluences>(), adapter.Influences);
                Assert.AreSame(profile, motor.Profile); Assert.AreEqual(7, motor.Body.mass);
            }
            finally
            {
                // The test is synchronous: if an assertion after our Undo failed, only our own
                // just-undone operation is pending. Recreate it before scoped cleanup as well.
                if (redoPending) Undo.PerformRedo();
                ClearFixtureUndo(go, profile);
                Object.DestroyImmediate(go); Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FixtureCleanupKeepsUnrelatedObjectUndoAvailable()
        {
            var go = new GameObject("Movement cleanup regression");
            var other = ScriptableObject.CreateInstance<RigidbodyMotorProfile>();
            try
            {
                Undo.IncrementCurrentGroup();
                Undo.RegisterCompleteObjectUndo(other, "Unrelated profile change");
                other.ForwardSpeed = 19;
                MovementCombatSetup.AddCombatSupport(go.AddComponent<RigidbodyMotor>());
                ClearFixtureUndo(go);
                Object.DestroyImmediate(go);
                // Leftover structure records would be replayed here; clearing the global history
                // would instead lose this separate object's edit. Neither is acceptable.
                Undo.PerformUndo();
                Assert.AreEqual(8, other.ForwardSpeed);
            }
            finally
            {
                ClearFixtureUndo(go, other);
                if (go) Object.DestroyImmediate(go);
                Object.DestroyImmediate(other);
            }
        }

        private static void ClearFixtureUndo(GameObject root, Object asset = null)
        {
            Undo.FlushUndoRecordObjects();
            if (root)
            {
                var hierarchy = root.GetComponentsInChildren<Transform>(true);
                for (int i = hierarchy.Length - 1; i >= 0; --i)
                {
                    var current = hierarchy[i];
                    foreach (var component in current.GetComponents<Component>())
                        if (component) Undo.ClearUndo(component);
                    Undo.ClearUndo(current.gameObject);
                }
            }
            if (asset) Undo.ClearUndo(asset);
        }

        [Test]
        public void ShippedFpsPrefabHasOneFovOwnerBoundToItsCharacter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab");
            Assert.IsNotNull(prefab);
            var character = prefab.GetComponentInChildren<FirstPersonCharacterController>(true);
            Assert.IsNotNull(character);
            var fovs = prefab.GetComponentsInChildren<FirstPersonFov>(true);
            Assert.AreEqual(1, fovs.Length);
            Assert.AreSame(character, fovs[0].Controller);
            Assert.IsNotNull(character.defaultCamera);
        }
    }
}
