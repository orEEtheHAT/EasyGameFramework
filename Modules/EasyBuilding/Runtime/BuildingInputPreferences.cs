using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace EasyBuilding
{
    [Serializable]
    public sealed class BuildingInputPreferences
    {
        public List<BuildingBindingOverride> Bindings = new List<BuildingBindingOverride>();
        public static BuildingInputPreferences Capture(IBuildingPlacementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input)); var state = new BuildingInputPreferences();
            foreach (BuildingControl control in Enum.GetValues(typeof(BuildingControl)))
            {
                var action = input.InputActionFor(control); if (action == null) continue;
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    var binding = action.bindings[i]; if (!binding.hasOverrides) continue;
                    state.Bindings.Add(new BuildingBindingOverride { Control = control, Index = i, OriginalPath = binding.path, OverridePath = binding.overridePath,
                        OverrideInteractions = binding.overrideInteractions, OverrideProcessors = binding.overrideProcessors });
                }
            }
            return state;
        }
        public void Apply(IBuildingPlacementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (Bindings == null) return;
            foreach (var saved in Bindings)
            {
                if (saved == null) continue; var action = input.InputActionFor(saved.Control); if (action == null) continue;
                int index = saved.Index;
                if (index < 0 || index >= action.bindings.Count || action.bindings[index].path != saved.OriginalPath)
                {
                    index = -1;
                    for (int i = 0; i < action.bindings.Count; i++) if (action.bindings[i].path == saved.OriginalPath) { index = i; break; }
                }
                if (index < 0) continue;
                action.ApplyBindingOverride(index, new InputBinding { overridePath = saved.OverridePath, overrideInteractions = saved.OverrideInteractions, overrideProcessors = saved.OverrideProcessors });
            }
        }
    }
    [Serializable]
    public sealed class BuildingBindingOverride
    {
        public BuildingControl Control;
        public int Index;
        public string OriginalPath, OverridePath, OverrideInteractions, OverrideProcessors;
    }
    /// <summary>Interactive rebinding with explicit cancellation and restoration of input state.</summary>
    public sealed class BuildingRebinding : IDisposable
    {
        private readonly IBuildingPlacementInput input;
        private InputActionRebindingExtensions.RebindingOperation operation;
        private InputAction action;
        private bool wasEnabled;
        public bool IsRunning => operation != null;
        public event Action<bool> Finished;
        public BuildingRebinding(IBuildingPlacementInput input) => this.input = input ?? throw new ArgumentNullException(nameof(input));
        public void Begin(BuildingControl control, int bindingIndex = 0)
        {
            Cancel(); action = input.InputActionFor(control);
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count || action.bindings[bindingIndex].isComposite)
                throw new ArgumentException("Choose an existing non-composite binding or a specific composite part.");
            wasEnabled = action.enabled; action.Disable(); input.SetInputBlocked(this, true);
            try
            {
                operation = action.PerformInteractiveRebinding(bindingIndex).WithCancelingThrough("<Keyboard>/escape")
                    .WithControlsExcluding("<Pointer>/position").WithControlsExcluding("<Pointer>/delta")
                    .OnComplete(_ => Finish(true)).OnCancel(_ => Finish(false));
                operation.Start();
            }
            catch { Finish(false); throw; }
        }
        private void Finish(bool success)
        {
            var previous = operation; operation = null; previous?.Dispose();
            if (wasEnabled) action?.Enable(); input.SetInputBlocked(this, false); action = null;
            if (Finished != null) foreach (Action<bool> handler in Finished.GetInvocationList()) try { handler(success); } catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
        }
        public void Cancel() { if (operation != null) operation.Cancel(); }
        public void Dispose() { Cancel(); Finished = null; }
    }
}
