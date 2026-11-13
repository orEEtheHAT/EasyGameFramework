using System;
using EasyInput;
using EasyInput.Integrations;
using EasyInteraction;
using EasyMovement.FirstPerson;
using EasyFootsteps;
using UnityEditor;
using UnityEngine;

namespace EasyGameFramework.Editor
{
    /// <summary>Explicit editor composition for one selected FPS character. Does not search or mutate other players.</summary>
    public static class InputPlayerSetup
    {
        private static FirstPersonCharacterController SelectedCharacter()
        {
            var selected=Selection.activeGameObject;
            if (!selected) return null;
            var character=selected.GetComponentInParent<FirstPersonCharacterController>();
            return character ? character : selected.GetComponentInChildren<FirstPersonCharacterController>(true);
        }
        [MenuItem("Tools/EasyGameFramework/Input/Connect Selected Player",true)]
        private static bool CanConnect() => !EditorApplication.isPlayingOrWillChangePlaymode && SelectedCharacter();
        [MenuItem("Tools/EasyGameFramework/Input/Connect Selected Player")]
        private static void ConnectSelected() { Selection.activeGameObject=Connect(SelectedCharacter()).gameObject; }
        public static PlayerInputSettings Connect(FirstPersonCharacterController character)
        {
            if (!character) throw new ArgumentNullException(nameof(character));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Connect the player before Play Mode.");
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Connect framework player input");
            try
            {
                var player=character.GetComponentInParent<PlayerInputSettings>();
                if (!player) player=Undo.AddComponent<PlayerInputSettings>(character.gameObject);
                var movement=GetOrAdd<EasyMovementInput>(character.gameObject); Undo.RecordObject(movement,"Configure input"); movement.Player=player; movement.Character=character;
                var actor=character.GetComponentInChildren<InteractionActor>(true);
                if (!actor) actor=Undo.AddComponent<InteractionActor>(character.gameObject);
                // ECM2's Character.transform is an Awake-time cache; editor authoring uses the native Transform.
                var body=character.gameObject.transform;
                Undo.RecordObject(actor,"Configure actor"); actor.PlayerRoot=body; actor.View=character.LookPivot ? character.LookPivot : body;
                var interaction=GetOrAdd<EasyInteractionInput>(actor.gameObject); Undo.RecordObject(interaction,"Configure input"); interaction.Player=player; interaction.Actor=actor;
                var presenter=GetOrAdd<InteractionPresenter>(actor.gameObject); Undo.RecordObject(presenter,"Configure prompt"); presenter.InputSource=interaction;
                var feedback=GetOrAdd<PlayerFeedbackContext>(player.gameObject); Undo.RecordObject(feedback,"Configure feedback"); feedback.Player=player;
                foreach (var steps in character.GetComponentsInChildren<CharacterStepController>(true))
                {
                    if (steps.ContextProvider) continue;
                    Undo.RecordObject(steps,"Configure player footsteps"); steps.ContextProvider=feedback;
                    EditorUtility.SetDirty(steps); PrefabUtility.RecordPrefabInstancePropertyModifications(steps);
                }
                foreach (var value in new UnityEngine.Object[] {player,movement,actor,interaction,presenter,feedback})
                { EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
                return player;
            }
            finally {Undo.CollapseUndoOperations(group);}
        }
        private static T GetOrAdd<T>(GameObject target) where T:Component => target.TryGetComponent<T>(out var value) ? value : Undo.AddComponent<T>(target);
    }
}
