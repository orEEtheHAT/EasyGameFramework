using UnityEngine;

namespace EasyMovement
{
    [CreateAssetMenu(menuName = "EasyGameFramework/Movement/Rigidbody Motor Profile", fileName = "RigidbodyMotorProfile")]
    public sealed class RigidbodyMotorProfile : ScriptableObject
    {
        [Min(0), Tooltip("Full forward throttle speed, metres/second.")] public float ForwardSpeed = 8;
        [Min(0), Tooltip("Full reverse throttle speed, metres/second.")] public float ReverseSpeed = 4;
        [Min(0), Tooltip("Propulsion acceleration, metres/second²; independent of Rigidbody mass.")] public float Acceleration = 6;
        [Min(.001f), Tooltip("Braking, reversal and soft-cap deceleration, metres/second². Combat speed modifiers do not scale it.")] public float Braking = 10;
        [Min(0), Tooltip("Neutral-input drag below the speed caps, metres/second². Zero coasts freely; soft caps still brake overspeed.")] public float CoastingDeceleration;

        public bool IsValid => Nonnegative(ForwardSpeed) && Nonnegative(ReverseSpeed) && Nonnegative(Acceleration)
            && Nonnegative(Braking) && Braking > 0 && Nonnegative(CoastingDeceleration);
        private static bool Nonnegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value <= 100000;
    }
}
