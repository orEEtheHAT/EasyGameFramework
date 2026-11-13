using EasyCombat;
using UnityEngine;

namespace EasyMovement.Integrations
{
    /// <summary>Uses Combat's already-composed spatial/status multiplier once. No source assets are modified.</summary>
    [DefaultExecutionOrder(-100), DisallowMultipleComponent, RequireComponent(typeof(RigidbodyMotor), typeof(MovementInfluences))]
    [AddComponentMenu("EasyGameFramework/Movement/Rigidbody Combat Speed")]
    public sealed class RigidbodyCombatSpeed : MonoBehaviour
    {
        [SerializeField, Tooltip("Motor on the affected body. Resolves locally when empty; place this component once on that body, not on a zone.")]
        private RigidbodyMotor motor;
        [SerializeField, Tooltip("Combat's combined spatial/status speed factor. Resolves locally when empty. Do not apply StatusController's multiplier separately.")]
        private MovementInfluences influences;
        private RigidbodyMotor.SpeedModifier modifier;
        public RigidbodyMotor Motor
        {
            get => motor;
            set { if (motor == value) return; Release(); motor = value; Refresh(); }
        }
        public MovementInfluences Influences
        {
            get => influences;
            set { if (influences == value) return; influences = value; Refresh(); }
        }
        private void Reset() => ResolveLocalReferences();
        private void ResolveLocalReferences()
        {
            if (!motor) motor = GetComponent<RigidbodyMotor>();
            if (!influences) influences = GetComponent<MovementInfluences>();
        }
        private void OnEnable()
        {
            ResolveLocalReferences();
            Refresh();
        }
        private void FixedUpdate() => Refresh();
        private void OnDisable() => Release();
        private void Release() { modifier?.Dispose(); modifier = null; }
        public void Refresh()
        {
            if (!isActiveAndEnabled || !motor || !motor.isActiveAndEnabled) { Release(); return; }
            if (modifier == null || !modifier.IsActive) modifier = motor.AcquireSpeedMultiplier(this);
            float value = influences && influences.isActiveAndEnabled ? influences.Multiplier : 1;
            // Invalid foreign data must not leave the last valid slow/haste stuck on the motor.
            if (!modifier.TrySet(value)) modifier.TrySet(1);
        }
    }
}
