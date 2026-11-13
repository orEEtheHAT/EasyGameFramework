using EasyGameFramework.Editor;
using EasyInput.Integrations;
using EasyInteraction;
using EasyMovement.FirstPerson;
using EasyFootsteps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
namespace EasyInput.Tests
{
    public sealed class PlayerSetupTests
    {
        [Test] public void SelectedPlayerWiringIsIdempotentAndSharesOneSettingsOwner()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.oreethehat.easy-game-framework/Modules/EasyMovement/FirstPerson/Prefabs/FP_Player_Sample.prefab");
            Assert.That(prefab,Is.Not.Null);
            var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var character=root.GetComponentInChildren<FirstPersonCharacterController>(true);
                var player=InputPlayerSetup.Connect(character); Assert.That(InputPlayerSetup.Connect(character),Is.SameAs(player));
                Assert.That(root.GetComponentsInChildren<PlayerInputSettings>(true).Length,Is.EqualTo(1));
                Assert.That(character.GetComponent<EasyMovementInput>().Player,Is.SameAs(player));
                var actor=character.GetComponentInChildren<InteractionActor>(true); var input=actor.GetComponent<EasyInteractionInput>();
                Assert.That(input.Player,Is.SameAs(player));
                var body=character.gameObject.transform;
                Assert.That(body,Is.Not.Null);
                Assert.That(actor.PlayerRoot,Is.SameAs(body));
                // Inspect the authored field too: the getter can fall back to actor.transform and hide a null assignment.
                Assert.That(new SerializedObject(actor).FindProperty("playerRoot").objectReferenceValue,Is.SameAs(body));
                Assert.That(new SerializedObject(actor).FindProperty("view").objectReferenceValue,Is.SameAs(character.LookPivot ? character.LookPivot : body));
                Assert.That(actor.GetComponent<InteractionPresenter>().InputSource,Is.SameAs(input));
                Assert.That(player.GetComponent<PlayerFeedbackContext>().Player,Is.SameAs(player));
                var steps=character.GetComponentInChildren<CharacterStepController>(true);
                Assert.That(steps,Is.Not.Null); Assert.That(steps.ContextProvider,Is.SameAs(player.GetComponent<PlayerFeedbackContext>()));
                var contextObject=new GameObject("Explicit feedback context"); contextObject.transform.SetParent(root.transform);
                var explicitContext=contextObject.AddComponent<PlayerFeedbackContext>(); explicitContext.Player=player; steps.ContextProvider=explicitContext;
                InputPlayerSetup.Connect(character); Assert.That(steps.ContextProvider,Is.SameAs(explicitContext),"Do not overwrite an explicitly assigned provider");
            }
            finally
            {
                // Setup records Undo for this hierarchy. Remove those records while their native
                // targets are still alive so Test Runner cannot replay them after destruction.
                // Do not clear global history or revert a group range owned by the runner.
                Undo.FlushUndoRecordObjects();
                if (root)
                {
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        if (component) Undo.ClearUndo(component);
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        Undo.ClearUndo(child.gameObject);
                    Object.DestroyImmediate(root);
                }
                Undo.IncrementCurrentGroup();
            }
        }
    }
}
