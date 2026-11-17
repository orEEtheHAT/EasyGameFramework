using EasyUI;
using UnityEngine;
using UnityEngine.UI;

namespace EasySuite.Example
{
    // Append new commands: existing prefab values 0..16 must keep their meaning.
    public enum EasySuiteCommand { Controls, Buildings, InteractionActions, Language, Save, Load, AddResources, DamagePlayer, KillPlayer, RevivePlayer, ResetTarget, Repair, Close, PaidAbility, SavePreferences, LoadPreferences, CycleCarryWalkingLag, CycleCarryFollowSpeed }

    [RequireComponent(typeof(UIWindow))]
    public sealed class EasySuiteWindow : MonoBehaviour
    {
        private EasySuiteLab lab;
        public EasySuiteLab Lab => lab;
        private WindowHandle handle;
        public void Bind(EasySuiteLab owner, WindowHandle window)
        {
            lab = owner; handle = window;
            foreach (var button in GetComponentsInChildren<EasySuiteButton>(true)) button.Bind(this);
        }
        public void Invoke(EasySuiteCommand command)
        {
            if (!lab || !lab.isActiveAndEnabled || !lab.Ready) return;
            switch (command)
            {
                case EasySuiteCommand.Controls: lab.OpenControls(handle); break;
                case EasySuiteCommand.Buildings: lab.OpenBuildings(handle); break;
                case EasySuiteCommand.InteractionActions: lab.OpenInteractions(handle); break;
                case EasySuiteCommand.Language: lab.ChangeLanguage(); break;
                case EasySuiteCommand.Save: lab.Save(); break;
                case EasySuiteCommand.Load: lab.Load(); break;
                case EasySuiteCommand.SavePreferences: lab.SavePreferences(); break;
                case EasySuiteCommand.LoadPreferences: lab.LoadPreferences(); break;
                case EasySuiteCommand.AddResources: lab.AddResources(); break;
                case EasySuiteCommand.DamagePlayer: lab.DamagePlayer(); break;
                case EasySuiteCommand.KillPlayer: lab.KillPlayer(); break;
                case EasySuiteCommand.RevivePlayer: lab.RevivePlayer(); break;
                case EasySuiteCommand.ResetTarget: lab.ResetTarget(); break;
                case EasySuiteCommand.Repair: lab.RepairNearest(); break;
                case EasySuiteCommand.PaidAbility: lab.CastPaidAbility(); break;
                case EasySuiteCommand.CycleCarryWalkingLag: lab.CycleCarryWalkingLag(); break;
                case EasySuiteCommand.CycleCarryFollowSpeed: lab.CycleCarryFollowSpeed(); break;
                case EasySuiteCommand.Close: handle?.Close(); break;
            }
        }
    }
}
