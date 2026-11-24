using EasyInput;
using EasyUI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyQuests.UI
{
    [AddComponentMenu("EasyGameFramework/Narrative/Quest Journal Shortcut")]
    public sealed class QuestJournalShortcut : MonoBehaviour
    {
        public PlayerInputSettings player;
        public WindowManager windows;
        public QuestJournal journal;
        public InputActionLink binding = new("Narrative/Journal");
        private InputAction action;
        private InputContextHandle context;

        private void Reset() => player = GetComponentInParent<PlayerInputSettings>();
        private void OnEnable()
        {
            if (!player || !player.isActiveAndEnabled || !windows || !journal) return;
            action = binding?.Resolve(player);
            if (action == null)
            {
                Debug.LogWarning("Choose a Journal binding from this player's actions.", this);
                return;
            }
            context = player.AcquireContext(this, new InputContextRequest { EnabledMaps = new[] { action.actionMap.name } });
            action.performed += Pressed;
        }
        private void OnDisable()
        {
            if (action != null) action.performed -= Pressed;
            action = null;
            context?.Dispose();
            context = null;
        }
        private void Pressed(InputAction.CallbackContext input)
        {
            if (!windows.InputReady || player.Rebind?.IsActive == true) return;
            var selection = windows.EventSystem ? windows.EventSystem.currentSelectedGameObject : null;
            if (selection && selection.TryGetComponent<TMP_InputField>(out var field) && field.isFocused) return;
            var window = journal.GetComponent<UIWindow>();
            if (window.Handle != null && windows.ActiveWindow == window.Handle) window.Close();
            else if (windows.Count == 0) windows.Open(window, openingAction: input.action);
        }
    }
}
