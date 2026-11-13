using EasyCombat;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyInput.Integrations
{
    [AddComponentMenu("EasyGameFramework/Input/EasyCombat Input")]
    public sealed class EasyCombatInput : MonoBehaviour
    {
        public PlayerInputSettings Player;
        public DamageEmitter Emitter;
        public Transform Aim;
        public bool Automatic = true;
        public InputActionLink Fire = new InputActionLink("Gameplay/Fire");
        private InputAction fire;
        private void OnEnable()
        {
            if (!Player) Player = GetComponentInParent<PlayerInputSettings>();
            if (!Emitter) Emitter = GetComponent<DamageEmitter>();
            if (!Player || !Emitter) { Debug.LogError("EasyCombat Input: assign Player Input Settings and Damage Emitter.", this); enabled = false; return; }
            try { fire = Fire.Resolve(Player, true); }
            catch (System.ArgumentException exception) { Debug.LogError("Combat Input: " + exception.Message, this); enabled = false; }
        }
        private void Update()
        {
            if (!Player || !Player.isActiveAndEnabled || fire == null || !fire.enabled || !Emitter || !Emitter.isActiveAndEnabled || (Automatic ? !fire.IsPressed() : !fire.WasPressedThisFrame())) return;
            var aim = Aim ? Aim : transform; Emitter.TryFire(aim.position, aim.forward);
        }
        private void OnDisable() => fire = null;
    }
}
