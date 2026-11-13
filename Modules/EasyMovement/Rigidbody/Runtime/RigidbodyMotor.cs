using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyMovement
{
    /// <summary>Bounded force propulsion along one local axis. Never replaces velocity, teleports or owns input devices.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody)), AddComponentMenu("EasyGameFramework/Movement/Rigidbody Motor")]
    public sealed class RigidbodyMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("Optional tuning. Empty uses forward 8 / reverse 4 m/s, acceleration 6 / braking 10 m/s², free coasting.")]
        private RigidbodyMotorProfile profile;
        [SerializeField, Tooltip("Local propulsion axis. Default +Z. A tilted axis deliberately controls part of vertical motion.")]
        private Vector3 localDirection = Vector3.forward;
        private Rigidbody body;
        private float throttle, brake;
        private readonly List<SpeedModifier> modifiers = new List<SpeedModifier>(4);

        public RigidbodyMotorProfile Profile
        {
            get => profile;
            set { if (profile == value) return; profile = value; ResetInput(); }
        }
        public Rigidbody Body { get { if (!body) body = GetComponent<Rigidbody>(); return body; } }
        public Vector3 LocalDirection
        {
            get => localDirection;
            set
            {
                if (!DirectionValid(value)) throw new ArgumentException("Use a finite, nonzero direction.", nameof(value));
                localDirection = value.normalized;
            }
        }
        public Vector3 WorldDirection => DirectionValid(localDirection) ? transform.TransformDirection(localDirection.normalized) : Vector3.zero;
        public float Throttle => throttle;
        public float BrakeInput => brake;
        public float CurrentSpeed => Body ? Vector3.Dot(Body.linearVelocity, WorldDirection) : 0;
        public float SpeedMultiplier
        {
            get
            {
                Prune();
                // Log composition keeps many tiny/large factors finite and order independent.
                double log = 0;
                for (int i = 0; i < modifiers.Count; ++i)
                {
                    float value = modifiers[i].Value;
                    if (value == 0) return 0;
                    if (value != 1) log += Math.Log(value);
                }
                return (float)Math.Min(100d, Math.Exp(log));
            }
        }
        public float EffectiveForwardSpeed => ValidProfile ? (profile ? profile.ForwardSpeed : 8) * SpeedMultiplier : 0;
        public float EffectiveReverseSpeed => ValidProfile ? (profile ? profile.ReverseSpeed : 4) * SpeedMultiplier : 0;
        public float TargetSpeed => brake > 0 ? 0 : throttle * (throttle >= 0 ? EffectiveForwardSpeed : EffectiveReverseSpeed);
        public string ConfigurationError => !DirectionValid(localDirection) ? "Use a finite nonzero Local Direction."
            : !ValidProfile ? "Profile speeds/rates must be finite, between 0 and 100000; Braking must be positive."
            : Body && Body.isKinematic ? "A kinematic Rigidbody does not accept motor forces." : string.Empty;
        private bool ValidProfile => !profile || profile.IsValid;

        public void SetThrottle(float value) => throttle = isActiveAndEnabled && Finite(value) ? Mathf.Clamp(value, -1, 1) : 0;
        public void SetBrake(float value) => brake = isActiveAndEnabled && Finite(value) ? Mathf.Clamp01(value) : 0;
        public void ResetInput() { throttle = 0; brake = 0; }

        public sealed class SpeedModifier : IDisposable
        {
            internal static readonly SpeedModifier Empty = new SpeedModifier(null, null, 1);
            internal RigidbodyMotor Host;
            internal UnityEngine.Object Owner;
            private float value;
            internal SpeedModifier(RigidbodyMotor host, UnityEngine.Object owner, float multiplier)
            { Host = host; Owner = owner; value = multiplier; }
            public float Value => value;
            public bool IsActive
            {
                get { if (Host) Host.Prune(); return Host && Host.isActiveAndEnabled && Active(Owner); }
            }
            public bool TrySet(float multiplier)
            {
                if (!Finite(multiplier) || multiplier < 0 || !IsActive) return false;
                value = Mathf.Min(100, multiplier); return true;
            }
            public void Dispose()
            {
                var host = Host; Host = null; Owner = null;
                if (host) host.modifiers.Remove(this);
            }
        }
        public SpeedModifier AcquireSpeedMultiplier(UnityEngine.Object owner, float multiplier = 1)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (!Finite(multiplier) || multiplier < 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
            Prune();
            if (!isActiveAndEnabled || !Active(owner)) return SpeedModifier.Empty;
            var handle = new SpeedModifier(this, owner, Mathf.Min(100, multiplier)); modifiers.Add(handle); return handle;
        }
        private void Awake() => body = GetComponent<Rigidbody>();
        private void OnDisable()
        {
            ResetInput();
            for (int i = 0; i < modifiers.Count; ++i) { modifiers[i].Host = null; modifiers[i].Owner = null; }
            modifiers.Clear();
        }
        private void FixedUpdate()
        {
            if (!isActiveAndEnabled || !Body || Body.isKinematic) { ResetInput(); return; }
            Prune();
            if (!ValidProfile || !DirectionValid(localDirection)) { ResetInput(); return; }
            float dt = Time.fixedDeltaTime;
            if (!Finite(dt) || dt <= 0) return;
            Vector3 direction = WorldDirection;
            float speed = Vector3.Dot(Body.linearVelocity, direction);
            if (!Finite(speed)) return;
            float factor = SpeedMultiplier;
            float forward = (profile ? profile.ForwardSpeed : 8) * factor;
            float reverse = (profile ? profile.ReverseSpeed : 4) * factor;
            float acceleration = profile ? profile.Acceleration : 6;
            float braking = profile ? profile.Braking : 10;
            float coast = profile ? profile.CoastingDeceleration : 0;
            float target, rate;
            if (brake > 0) { target = 0; rate = braking * brake; }
            else if (throttle != 0)
            {
                target = throttle * (throttle > 0 ? forward : reverse);
                if (speed * target < 0) { target = 0; rate = braking; }
                else rate = Mathf.Abs(target) > Mathf.Abs(speed) ? acceleration : braking;
            }
            else if (speed > forward) { target = forward; rate = braking; }
            else if (speed < -reverse) { target = -reverse; rate = braking; }
            else { target = 0; rate = coast; }
            if (rate <= 0 || speed == target) return;
            float next = Mathf.MoveTowards(speed, target, rate * dt);
            float force = (next - speed) / dt;
            if (Finite(force) && force != 0) Body.AddForce(direction * force, ForceMode.Acceleration);
        }
        private void Prune()
        {
            for (int i = modifiers.Count - 1; i >= 0; --i)
            {
                if (Active(modifiers[i].Owner)) continue;
                modifiers[i].Host = null; modifiers[i].Owner = null; modifiers.RemoveAt(i);
            }
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool DirectionValid(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z)
            && Finite(value.sqrMagnitude) && value.sqrMagnitude > .000001f;
        private static bool Active(UnityEngine.Object owner)
        {
            if (!owner) return false;
            if (owner is Behaviour behaviour) return behaviour.isActiveAndEnabled;
            if (owner is Component component) return component.gameObject.activeInHierarchy;
            if (owner is GameObject go) return go.activeInHierarchy;
            return true;
        }
    }
}
