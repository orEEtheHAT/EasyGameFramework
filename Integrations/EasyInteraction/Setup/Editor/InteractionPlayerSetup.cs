using System;
using EasyInteraction;
using EasyInteraction.Integrations.Movement;
using EasyMovement.FirstPerson;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>Editor-only, idempotent interaction wiring for an existing EasyMovement FPS player.</summary>
    public static class InteractionPlayerSetup
    {
        [MenuItem("Tools/EasyGameFramework/Interaction/Set Up Selected Player")]
        public static void ConfigureSelected()
        {
            var selected = Selection.activeGameObject;
            var character = selected ? selected.GetComponentInParent<FirstPersonCharacterController>() : null;
            if (!character && selected)
                character = selected.GetComponentInChildren<FirstPersonCharacterController>(true);
            if (!character)
            {
                Debug.LogWarning("Select an EasyMovement First Person player or its camera. Create/configure that player through EasyGameFramework Setup first.");
                return;
            }

            var actor = Configure(character);
            Selection.activeGameObject = actor.gameObject;
            EditorSceneManager.MarkSceneDirty(character.gameObject.scene);
        }

        /// <summary>Used by the unified Setup. Keeps an existing single camera-hosted Actor and does not add a duplicate reader.</summary>
        public static InteractionActor Configure(FirstPersonCharacterController character)
        {
            if (!character)
                throw new ArgumentNullException(nameof(character));
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure interaction components before entering Play Mode.");
            if (EditorUtility.IsPersistent(character))
                throw new InvalidOperationException("Select a scene player or open its prefab in Prefab Mode before configuring interaction.");
            var actor = character.GetComponent<InteractionActor>();
            if (!actor)
            {
                var candidates = character.GetComponentsInChildren<InteractionActor>(true);
                if (candidates.Length > 1)
                    throw new InvalidOperationException("The selected character has multiple child Interaction Actors. Keep one FPS Actor or configure their explicit roles manually.");
                actor = candidates.Length == 1 ? candidates[0] : null;
            }

            // Reading Actor.View may resolve and write its serialized fallback. Capture authored state without mutation.
            var previousView = actor ? new SerializedObject(actor).FindProperty("view").objectReferenceValue as Transform : null;
            if (actor && !previousView)
            {
                var camera = actor.GetComponentInChildren<Camera>();
                previousView = camera ? camera.transform : actor.transform;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            const string undoName = "Configure player interaction";
            Undo.SetCurrentGroupName(undoName);
            try
            {
                // Input owns the shared reader, action/context policy and feedback context wiring.
                InputPlayerSetup.Connect(character);
                Undo.FlushUndoRecordObjects();
                if (!actor)
                    actor = character.GetComponentInChildren<InteractionActor>(true);
                var motor = Add<EasyMovementInteractionMotor>(actor.gameObject);
                // Input already records this Actor. Keep one kind of property recording when composing the command.
                Undo.RecordObject(actor, undoName);
                Undo.RegisterCompleteObjectUndo(motor, undoName);
                motor.character = character;
                actor.Motor = motor;
                actor.PlayerRoot = character.gameObject.transform;
                if (character.LookPivot)
                    actor.View = character.LookPivot;
                else if (previousView)
                    actor.View = previousView;
                var standalone = actor.GetComponent<InteractionInput>();
                if (standalone && standalone.enabled)
                {
                    Undo.RegisterCompleteObjectUndo(standalone, undoName);
                    standalone.enabled = false;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(standalone);
                }

                EditorUtility.SetDirty(actor);
                EditorUtility.SetDirty(motor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(motor);
                return actor;
            }
            finally
            {
                // The nested Input setup also creates a group. A caller gets one complete undoable command.
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                Undo.IncrementCurrentGroup();
            }
        }

        private static T Add<T>(GameObject go)
            where T : Component => go.TryGetComponent<T>(out var component) ? component : Undo.AddComponent<T>(go);
    }
}
