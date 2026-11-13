using EasyInteraction;
using System;
using UnityEngine;

namespace EasySuite.Example
{
    public sealed class EasySuiteWorkAction : InteractionAction, IInteractionState
    {
        public EasySuiteLab Lab;
        public int Completions { get; private set; }
        [Serializable] private sealed class State { public int Version = 1; public int Completions; }
        public string CaptureState() => JsonUtility.ToJson(new State { Completions = Completions });
        public InteractionAvailability ValidateState(string json)
        {
            try { var state = JsonUtility.FromJson<State>(json); return state != null && state.Version == 1 && state.Completions >= 0
                ? InteractionAvailability.Available : InteractionAvailability.Unavailable("Invalid workbench state."); }
            catch { return InteractionAvailability.Unavailable("Invalid workbench JSON."); }
        }
        public void RestoreState(string json)
        {
            if (!ValidateState(json).IsAvailable) throw new ArgumentException("Invalid workbench state.");
            Completions = JsonUtility.FromJson<State>(json).Completions;
        }
        protected override bool OnExecute(InteractionActor actor)
        {
            Completions++;
            if (Lab) { Lab.AddResources(); Lab.Message(Lab.T("lab.workdone", "Calibration complete. Movement control returned to the player.")); }
            return false;
        }
    }
}
