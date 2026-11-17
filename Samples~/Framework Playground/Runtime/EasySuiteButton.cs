using EasyLocalization;
using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example
{
    [RequireComponent(typeof(Button))]
    public sealed class EasySuiteButton : MonoBehaviour
    {
        public EasySuiteCommand Command;
        private EasySuiteWindow window;
        private EasySuiteLab carryLabelOwner;
        private LocalizedText carryLabel;
        public void Bind(EasySuiteWindow owner)
        {
            DisconnectCarryLabel();
            window = owner;
            if (isActiveAndEnabled) ConnectCarryLabel();
        }
        private void OnEnable()
        {
            GetComponent<Button>().onClick.AddListener(Clicked);
            ConnectCarryLabel();
        }
        private void OnDisable()
        {
            GetComponent<Button>().onClick.RemoveListener(Clicked);
            DisconnectCarryLabel();
        }
        private void ConnectCarryLabel()
        {
            if ((Command != EasySuiteCommand.CycleCarryWalkingLag && Command != EasySuiteCommand.CycleCarryFollowSpeed) || !window || !window.Lab) return;
            carryLabelOwner = window.Lab;
            if (Command == EasySuiteCommand.CycleCarryWalkingLag) carryLabelOwner.CarryWalkingLagChanged += RefreshCarryLabel;
            else carryLabelOwner.CarryFollowSpeedChanged += RefreshCarryLabel;
            RefreshCarryLabel();
        }
        private void DisconnectCarryLabel()
        {
            if (carryLabelOwner)
            {
                carryLabelOwner.CarryWalkingLagChanged -= RefreshCarryLabel;
                carryLabelOwner.CarryFollowSpeedChanged -= RefreshCarryLabel;
            }
            carryLabelOwner = null;
        }
        private void RefreshCarryLabel()
        {
            if (!carryLabelOwner) return;
            if (!carryLabel) carryLabel = GetComponentInChildren<LocalizedText>(true);
            if (!carryLabel) return;
            bool followSpeed = Command == EasySuiteCommand.CycleCarryFollowSpeed;
            carryLabel.text.TableEntryReference = followSpeed ? carryLabelOwner.CarryFollowSpeedLabelKey : carryLabelOwner.CarryWalkingLagLabelKey;
            carryLabel.fallback = followSpeed ? carryLabelOwner.CarryFollowSpeedLabelFallback : carryLabelOwner.CarryWalkingLagLabelFallback;
            if (followSpeed) carryLabel.SetArguments(carryLabelOwner.CarryFollowSpeedValue);
            else carryLabel.Refresh();
        }
        private void Clicked() => window?.Invoke(Command);
    }
}
