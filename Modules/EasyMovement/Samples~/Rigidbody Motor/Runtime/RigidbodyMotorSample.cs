using UnityEngine;
using UnityEngine.InputSystem;

namespace EasyMovement.Samples
{
    /// <summary>Standalone New Input System example; the motor itself does not depend on these actions.</summary>
    [RequireComponent(typeof(RigidbodyMotor))]
    public sealed class RigidbodyMotorSample : MonoBehaviour
    {
        public RigidbodyMotor Motor;
        public bool ShowHelp = true;
        InputAction throttle, brake, impulse;
        void Awake()
        {
            if (!Motor) Motor = GetComponent<RigidbodyMotor>();
            throttle = new InputAction("Throttle", InputActionType.Value);
            throttle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
            throttle.AddBinding("<Gamepad>/leftStick/y");
            brake = new InputAction("Brake", InputActionType.Value, "<Keyboard>/space"); brake.AddBinding("<Gamepad>/leftTrigger");
            impulse = new InputAction("Side impulse", InputActionType.Button, "<Keyboard>/j"); impulse.AddBinding("<Gamepad>/buttonWest");
        }
        void OnEnable() { throttle.Enable(); brake.Enable(); impulse.Enable(); }
        void Update()
        {
            if (!Motor) return;
            Motor.SetThrottle(throttle.ReadValue<float>()); Motor.SetBrake(brake.ReadValue<float>());
            if (impulse.WasPressedThisFrame() && Motor.Body && !Motor.Body.isKinematic)
                Motor.Body.AddForce(transform.right * 3, ForceMode.VelocityChange);
        }
        void OnDisable()
        {
            throttle?.Disable(); brake?.Disable(); impulse?.Disable();
            if (Motor) Motor.ResetInput();
        }
        void OnDestroy() { throttle?.Dispose(); brake?.Dispose(); impulse?.Dispose(); }
        void OnGUI()
        {
            if (!ShowHelp || !Motor) return;
            GUI.Box(new Rect(12, 12, 500, 108), "Rigidbody Motor — straight-axis force example");
            GUI.Label(new Rect(24, 38, 470, 24), "W/S or left stick: throttle   Space/left trigger: brake");
            GUI.Label(new Rect(24, 61, 470, 24), "J / gamepad West: sideways impulse (the motor preserves it)");
            GUI.Label(new Rect(24, 84, 470, 24), $"Speed {Motor.CurrentSpeed:F2} m/s   Factor {Motor.SpeedMultiplier:F2}   Limits +{Motor.EffectiveForwardSpeed:F1} / -{Motor.EffectiveReverseSpeed:F1}");
        }
    }
}
