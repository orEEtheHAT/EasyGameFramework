using EasyMovement.FirstPerson;

namespace EasyMovement.IntegrationTests
{
    public sealed class ProbeCharacter : FirstPersonCharacterController
    {
        public void InitializeEditorDefaults() => base.Reset();
        public void TickBlockCleanup() => base.Update();
        public void TickGait() => base.OnBeforeSimulationUpdate(0.02f);
        public void ApplyCrouch() => base.CheckCrouchInput();
    }
}
