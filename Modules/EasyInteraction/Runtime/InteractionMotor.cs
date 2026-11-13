using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Approach motor contract. Standard EasyMovement/ECM2 and NavMesh implementations are included in framework Integrations.</summary>
    public abstract class InteractionMotor : MonoBehaviour
    {
        public abstract void Begin(Transform destination);
        public abstract void Tick(float deltaTime);
        public abstract void Stop();
    }
}
