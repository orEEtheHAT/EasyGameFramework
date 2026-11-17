using Unity.Profiling;
using UnityEngine;

namespace EasyInteraction
{
    public enum GrabRotation
    {
        Free = 0,
        KeepInitial = 1,
        FollowView = 2,
        Stabilized = 3
    }

    public enum GrabPoseMode
    {
        PreservePickupPose = 0,
        AuthoredPose = 1
    }

    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("EasyGameFramework/Interaction/Grab Action")]
    public sealed class GrabAction : InteractionAction
    {
        [SerializeField, Min(.1f)]
        private float grabRange = 3;
        [SerializeField, Min(.01f)]
        private float maximumMass = 20;
        [SerializeField, Min(.1f)]
        private float holdDistance = 2;
        [SerializeField, Tooltip("Ignore the owner's physical colliders and supported character queries. Release restores each owned exclusion after separation.")]
        private bool ignoreOwnerCollisions = true;
        [SerializeField, Tooltip("Metres in Hold Point, Hand Anchor or View local axes (without scale). With View, added to the point Hold Distance in front of the camera.")]
        private Vector3 holdOffset;
        [SerializeField, Tooltip("New components default to Authored Pose: Local Position/Local Euler Angles with stabilized physics, independent of pickup rotation. Preserve Pickup Pose uses Rotation Mode/Hold Offset. Existing serialized choices are retained.")]
        private GrabPoseMode poseMode = GrabPoseMode.AuthoredPose;
        [SerializeField, Tooltip("Authored Pose only. Metres along Hold Point > Actor Hand Anchor > View axes, ignoring frame scale. View adds Hold Distance (or inspection distance) on Z. Positions the Rigidbody root, or Grip Point when assigned.")]
        private Vector3 localPosition;
        [SerializeField, Tooltip("Authored Pose only. Rigidbody root rotation in degrees relative to Hold Point > Actor Hand Anchor > View. Grip Point rotation is ignored. Manual rotation is added during this hold only.")]
        private Vector3 localEulerAngles;
        [SerializeField, Range(0, 1), Tooltip("Physical follow response for position and driven rotation. Lower is softer/slower; higher is firmer/faster. Contacts and body limits still apply.")]
        private float followSpeed = .5f;
        [SerializeField, Tooltip("Walking position lag along the movement reference's local X (side), Y (up), Z (forward). 1 retains velocity-damping drift; 0 compensates that walking source in damping. Contacts and the physical acceleration limit still apply.")]
        private Vector3 movementLag = Vector3.one * .5f;
        [SerializeField, Tooltip("Optional movement source and local walking axes. By default uses Actor Player Root for a descendant hold frame, otherwise the external hold frame itself. Custom rigs should assign a source that actually carries their hold frame.")]
        private Transform movementReference;
        [SerializeField, Range(.1f, 1), Tooltip("Uniform multiplier of the Rigidbody root's captured local scale while held. Scales visuals, colliders and grip geometry together. Original scale and mass properties return on final release. Mass is unchanged; transfer/additional holders do not multiply again.")]
        private float heldScaleMultiplier = 1;
        [SerializeField, Tooltip("Optional point on this Rigidbody to position at the hold destination; its rotation is ignored. Without it, Authored Pose positions the Rigidbody root and Preserve Pickup Pose positions the centre of mass.")]
        private Transform gripPoint;
        [SerializeField, Tooltip("Optional external hold frame. Overrides Actor Hand Anchor and View, including distance/inspection positioning. Keep it outside the held Rigidbody hierarchy.")]
        private Transform holdPoint;
        [SerializeField, Min(.1f)]
        private float breakDistance = 5;
        [SerializeField]
        private GrabRotation rotationMode = GrabRotation.Stabilized;
        [SerializeField, Min(.1f)]
        private float minimumDistance = .5f;
        [SerializeField, Min(.1f)]
        private float maximumDistance = 4;
        [SerializeField, Min(0)]
        private float throwSpeed = 6;
        [SerializeField, Min(.1f)]
        private float inspectionDistance = .65f;
        private Rigidbody body;
        private bool captured, oldGravity, inspecting;
        private RigidbodyInterpolation oldInterpolation;
        private CollisionDetectionMode oldCollisionMode;
        private float currentDistance;
        private Quaternion initialRotation, viewOffset, manualRotation = Quaternion.identity, twoHandRotation;
        private Vector3 initialHandAxis;
        private Vector3 oldLocalScale, oldCenterOfMass, oldInertiaTensor;
        private Quaternion oldInertiaTensorRotation;
        private bool oldAutomaticCenterOfMass, oldAutomaticInertiaTensor, scaleChanged;
        private struct MotionSample
        {
            public InteractionActor actor;
            public Transform frame, frameParent, playerRoot, reference, referenceOverride;
            public Vector3 referencePosition;
            public bool valid;
        }
        private MotionSample[] motionSamples = System.Array.Empty<MotionSample>();
        private bool motionHistoryValid;
        private const float MaxCorrectiveAcceleration = 80;
        private static readonly ProfilerMarker marker = new ProfilerMarker("EasyInteraction.HoldPhysics");
        protected override string DefaultChannel => "carry";
        public override bool CanPause => false;
        public Rigidbody Body => body ? body : (body = GetComponent<Rigidbody>());
        public bool IsHeld => captured && Owner;
        public bool IsInspecting => inspecting;
        public float GrabRange { get => grabRange; set => grabRange = Mathf.Max(.1f, value); }
        public float MaximumMass { get => maximumMass; set => maximumMass = Mathf.Max(.01f, value); }

        public float HoldDistance
        {
            get => holdDistance;
            set
            {
                float next = Mathf.Max(.1f, value);
                holdDistance = currentDistance = next;
            }
        }

        public Transform GripPoint { get => gripPoint; set => gripPoint = value; }
        public Transform HoldPoint { get => holdPoint; set { if (holdPoint == value) return; holdPoint = value; InvalidateMotionHistory(); } }
        public bool IgnoreOwnerCollisions
        {
            get => ignoreOwnerCollisions;
            set
            {
                ignoreOwnerCollisions = value;
                if (!value) GrabCollisionExclusions.Release(this);
                else if (captured)
                    for (int i = 0; i < OwnerCount; i++) GrabCollisionExclusions.Acquire(this, GetOwner(i));
            }
        }
        public Vector3 HoldOffset { get => holdOffset; set => holdOffset = value; }
        public GrabPoseMode PoseMode { get => poseMode; set { if (poseMode == value) return; poseMode = value; InvalidateMotionHistory(); } }
        public Vector3 LocalPosition { get => localPosition; set => localPosition = value; }
        public Vector3 LocalEulerAngles { get => localEulerAngles; set => localEulerAngles = value; }
        public float FollowSpeed { get => FollowSpeedValue(followSpeed); set => followSpeed = FollowSpeedValue(value); }
        public Vector3 MovementLag
        {
            get => movementLag;
            set
            {
                var next = new Vector3(LagCoefficient(value.x), LagCoefficient(value.y), LagCoefficient(value.z));
                if (movementLag == next) return;
                movementLag = next;
                ResetMotionTracking();
            }
        }
        public Transform MovementReference { get => movementReference; set { if (movementReference == value) return; movementReference = value; ResetMotionTracking(); } }

        private static float LagCoefficient(float value) => float.IsFinite(value) ? Mathf.Clamp01(value) : 1;
        private static float FollowSpeedValue(float value) => float.IsFinite(value) ? Mathf.Clamp01(value) : .5f;

        /// <summary>Rebase walking-source history after moving the rig and held body. Does not change the body's pose or velocity.</summary>
        public void ResetMotionTracking() => InvalidateMotionHistory();

        private void InvalidateMotionHistory()
        {
            motionHistoryValid = false;
            // Keep capacity for pooling, without retaining previous holders or rig hierarchies.
            System.Array.Clear(motionSamples, 0, motionSamples.Length);
        }
        public float HeldScaleMultiplier
        {
            get => heldScaleMultiplier;
            set
            {
                heldScaleMultiplier = float.IsFinite(value) ? Mathf.Clamp(value, .1f, 1) : 1;
                if (captured) ApplyHeldScale();
            }
        }
        public Vector3 UnheldLocalScale => captured ? oldLocalScale : transform.localScale;
        public float BreakDistance { get => breakDistance; set => breakDistance = Mathf.Max(.1f, value); }
        public GrabRotation RotationMode { get => rotationMode; set { if (rotationMode == value) return; rotationMode = value; InvalidateMotionHistory(); } }
        public float ThrowSpeed { get => throwSpeed; set => throwSpeed = Mathf.Max(0, value); }
        public Vector3 GripPosition
        {
            get
            {
                if (gripPoint) return gripPoint.position;
                var heldBody = Body;
                if (!heldBody) return transform.position;
                return poseMode == GrabPoseMode.AuthoredPose ? heldBody.position : heldBody.worldCenterOfMass;
            }
        }

        public override string GetLabel(InteractionActor actor) => IsHeld && Owns(actor) ? "Release" : "Grab";
        public bool Owns(InteractionActor actor)
        {
            for (int i = 0; i < OwnerCount; i++)
                if (GetOwner(i) == actor)
                    return true;
            return false;
        }

        public override InteractionAvailability GetAvailability(InteractionActor actor)
        {
            if (!Body)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object cannot be picked up.", diagnostic: "Add a Rigidbody.");
            if (Body.isKinematic)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object is fixed in place.", diagnostic: "Turn off Rigidbody > Is Kinematic, or remove it from its socket first.");
            if (!Body.detectCollisions)
                return InteractionAvailability.Unavailable(InteractionFailure.InvalidConfiguration, "This object cannot be picked up.", diagnostic: "Enable Rigidbody Detect Collisions.");
            if (Body.mass > maximumMass)
                return InteractionAvailability.Unavailable(InteractionFailure.TooHeavy, "Too heavy.", "interaction.tooHeavy", "Reduce Rigidbody Mass or increase Maximum Mass.");
            if ((GripPosition - actor.View.position).sqrMagnitude > grabRange * grabRange)
                return InteractionAvailability.Unavailable(InteractionFailure.OutOfRange, "Move closer to grab this object.", "interaction.outOfRange");
            return InteractionAvailability.Available;
        }

        public override InteractionAvailability GetContinuationAvailability(InteractionActor actor) => captured ? InteractionAvailability.Available : GetAvailability(actor);
        protected override bool OnExecute(InteractionActor actor)
        {
            if (!captured)
            {
                body = Body;
                oldGravity = body.useGravity;
                oldInterpolation = body.interpolation;
                oldCollisionMode = body.collisionDetectionMode;
                oldLocalScale = transform.localScale;
                oldAutomaticCenterOfMass = body.automaticCenterOfMass;
                oldAutomaticInertiaTensor = body.automaticInertiaTensor;
                oldCenterOfMass = body.centerOfMass;
                oldInertiaTensor = body.inertiaTensor;
                oldInertiaTensorRotation = body.inertiaTensorRotation;
                captured = true;
                ApplyHeldScale();
                currentDistance = holdDistance;
                inspecting = false;
                manualRotation = Quaternion.identity;
                initialRotation = body.rotation;
                viewOffset = Quaternion.Inverse(RotationFrame(actor).rotation) * body.rotation;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.WakeUp();
            }

            GrabCollisionExclusions.Acquire(this, actor);
            // Acquisition is the only place this array can grow; the physics loop never allocates.
            if (motionSamples.Length < OwnerCount) System.Array.Resize(ref motionSamples, OwnerCount);
            InvalidateMotionHistory();
            if (OwnerCount == 2)
            {
                initialHandAxis = Destination(GetOwner(1)) - Destination(GetOwner(0));
                twoHandRotation = poseMode == GrabPoseMode.AuthoredPose ? AuthoredRotation(GetOwner(0)) : body.rotation;
            }

            return true;
        }

        private Transform PositionFrame(InteractionActor actor) => holdPoint ? holdPoint : actor.HandAnchor ? actor.HandAnchor : actor.View;
        private Transform RotationFrame(InteractionActor actor) => rotationMode == GrabRotation.Stabilized ? PositionFrame(actor) : actor.View;
        private Quaternion AuthoredRotation(InteractionActor actor) => PositionFrame(actor).rotation * Quaternion.Euler(localEulerAngles);
        public Transform GetMovementReference(InteractionActor actor)
        {
            if (movementReference) return movementReference;
            if (!actor) return null;
            var frame = PositionFrame(actor);
            var root = actor.PlayerRoot;
            return frame == root || frame.IsChildOf(root) ? root : frame;
        }

        private Vector3 LocalDestination(InteractionActor actor)
        {
            var local = poseMode == GrabPoseMode.AuthoredPose ? localPosition : holdOffset;
            if (!holdPoint && !actor.HandAnchor)
                local.z += inspecting ? inspectionDistance : currentDistance;
            return local;
        }
        private Vector3 Destination(InteractionActor actor)
        {
            var frame = PositionFrame(actor);
            return frame.position + frame.rotation * LocalDestination(actor);
        }

        private bool SampleMotion(int index, InteractionActor actor, Transform frame, out Vector3 compensation)
        {
            ref var sample = ref motionSamples[index];
            var root = actor.PlayerRoot;
            bool sameSource = motionHistoryValid && sample.valid && sample.actor == actor && sample.frame == frame && sample.frameParent == frame.parent && sample.playerRoot == root
                && sample.referenceOverride == movementReference && sample.reference;
            // Hierarchy resolution happens only when the source changes, not every warmed physics step.
            var reference = sameSource ? sample.reference : GetMovementReference(actor);
            var referencePosition = reference.position;
            var referenceRotation = reference.rotation;
            compensation = Vector3.zero;
            bool discontinuity = false;
            if (motionHistoryValid && sameSource && sample.valid)
            {
                var movement = referencePosition - sample.referencePosition;
                discontinuity = movement.sqrMagnitude > breakDistance * breakDistance;
                if (!discontinuity)
                {
                    var cancelledLocal = Vector3.Scale(Quaternion.Inverse(referenceRotation) * movement, Vector3.one - movementLag);
                    compensation = referenceRotation * cancelledLocal;
                }
            }
            sample.actor = actor;
            sample.frame = frame;
            sample.frameParent = frame.parent;
            sample.playerRoot = root;
            sample.referenceOverride = movementReference;
            sample.reference = reference;
            sample.referencePosition = referencePosition;
            sample.valid = true;
            return discontinuity;
        }

        private static Vector3 AngularDisplacement(Quaternion delta)
        {
            if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            var imaginary = new Vector3(delta.x, delta.y, delta.z);
            float length = imaginary.magnitude;
            return length > .000001f ? imaginary * (2 * Mathf.Atan2(length, Mathf.Max(0, delta.w)) / length) : Vector3.zero;
        }
        public void AdjustDistance(float delta)
        {
            if (IsHeld)
            {
                float next = Mathf.Clamp(currentDistance + delta, minimumDistance, maximumDistance);
                currentDistance = next;
            }
        }

        public void Rotate(Vector2 degrees)
        {
            if (IsHeld)
                manualRotation = Quaternion.Euler(-degrees.y, degrees.x, 0) * manualRotation;
        }

        public void SetInspection(bool value)
        {
            if (IsHeld && inspecting != value)
            {
                inspecting = value;
            }
        }

        public bool Throw(InteractionActor actor)
        {
            if (!Owns(actor) || OwnerCount != 1 || !body)
                return false;
            Vector3 velocity = Vector3.ClampMagnitude(body.linearVelocity + actor.View.forward * throwSpeed, 30);
            actor.Cancel(this, "Thrown.");
            if (body)
                body.linearVelocity = velocity;
            return true;
        }

        public bool TryTransferTo(InteractionActor receiver)
        {
            if (!IsHeld || OwnerCount != 1 || !receiver || receiver == Owner)
                return false;
            return Owner.TransferGrab(this, receiver);
        }

        private void FixedUpdate()
        {
            if (!captured)
                return;
            using (marker.Auto())
            {
                if (!Owner || !body || body.isKinematic || !Target.isActiveAndEnabled)
                {
                    Cancel("Holding interrupted.");
                    Restore();
                    return;
                }

                Vector3 destination = Vector3.zero;
                Vector3 compensation = Vector3.zero;
                bool discontinuity = false;
                bool ownersChanged = false;
                int count = 0;
                for (int i = OwnerCount - 1; i >= 0; i--)
                {
                    var actor = GetOwner(i);
                    if (!actor || !actor.isActiveAndEnabled)
                    {
                        ownersChanged = true;
                        if (actor)
                            actor.Cancel(this);
                        continue;
                    }

                    var frame = PositionFrame(actor);
                    var local = LocalDestination(actor);
                    destination += frame.position + frame.rotation * local;
                    discontinuity |= SampleMotion(i, actor, frame, out var actorCompensation);
                    compensation += actorCompensation;
                    GrabCollisionExclusions.Acquire(this, actor);
                    count++;
                }

                if (count == 0)
                {
                    Restore();
                    return;
                }

                destination /= count;
                compensation /= count;
                motionHistoryValid = true;
                if (discontinuity || ownersChanged)
                    compensation = Vector3.zero;
                var point = GripPosition;
                var error = destination - point;
                if (error.sqrMagnitude > breakDistance * breakDistance)
                {
                    Cancel("Object is too far from its hold point.");
                    return;
                }

                bool authored = poseMode == GrabPoseMode.AuthoredPose;
                bool stabilized = authored || rotationMode == GrabRotation.Stabilized;
                float dt = Time.fixedDeltaTime;
                float omega = 2 * Mathf.PI * Mathf.Lerp(1, 5, FollowSpeed), kp = omega * omega, kd = 2 * omega;
                float denominator = 1 + kd * dt + kp * dt * dt;
                var trackingVelocity = compensation / dt;
                var force = Vector3.ClampMagnitude((kp * error - kd * (body.GetPointVelocity(point) - trackingVelocity)) / denominator, MaxCorrectiveAcceleration);
                if (stabilized)
                    body.AddForce(force, ForceMode.Acceleration);
                else
                    body.AddForceAtPosition(force, point, ForceMode.Acceleration);
                bool rotated = authored || rotationMode != GrabRotation.Free || inspecting || manualRotation != Quaternion.identity || count > 1;
                if (rotated)
                {
                    var desired = authored ? AuthoredRotation(Owner) : rotationMode == GrabRotation.KeepInitial ? initialRotation : RotationFrame(Owner).rotation * viewOffset;
                    if (count > 1 && initialHandAxis.sqrMagnitude > .001f)
                        desired = Quaternion.FromToRotation(initialHandAxis, Destination(GetOwner(1)) - Destination(GetOwner(0))) * twoHandRotation;
                    desired *= manualRotation;
                    var angularError = AngularDisplacement(desired * Quaternion.Inverse(body.rotation));
                    // Always damp angular velocity, including an exactly aligned orientation.
                    var acceleration = (kp * angularError - kd * body.angularVelocity) / denominator;
                    body.AddTorque(Vector3.ClampMagnitude(acceleration, MaxCorrectiveAcceleration), ForceMode.Acceleration);
                }
            }
        }

        protected override void OnCancel(InteractionActor actor)
        {
            if (OwnerCount == 0) RestoreScale();
            GrabCollisionExclusions.Release(this, actor);
            if (OwnerCount == 0)
                Restore();
            else
                RebaseRotation();
        }

        protected override void OnComplete(InteractionActor actor)
        {
            if (OwnerCount == 0) RestoreScale();
            GrabCollisionExclusions.Release(this, actor);
            if (OwnerCount == 0)
                Restore();
            else
                RebaseRotation();
        }

        private void RebaseRotation()
        {
            if (!body || !Owner)
                return;
            viewOffset = Quaternion.Inverse(RotationFrame(Owner).rotation) * body.rotation * Quaternion.Inverse(manualRotation);
            initialHandAxis = OwnerCount > 1 ? Destination(GetOwner(1)) - Destination(GetOwner(0)) : Vector3.zero;
            twoHandRotation = poseMode == GrabPoseMode.AuthoredPose ? AuthoredRotation(Owner) : body.rotation * Quaternion.Inverse(manualRotation);
            InvalidateMotionHistory();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (OwnerCount == 0)
                Restore();
        }

        private void OnDestroy() => Restore();
        private void ApplyHeldScale()
        {
            if (!body) return;
            float multiplier = float.IsFinite(heldScaleMultiplier) ? Mathf.Clamp(heldScaleMultiplier, .1f, 1) : 1;
            var requested = oldLocalScale * multiplier;
            if (!scaleChanged && transform.localScale == requested) return;
            transform.localScale = requested;
            Physics.SyncTransforms();
            if (oldAutomaticCenterOfMass) body.ResetCenterOfMass();
            else body.centerOfMass = oldCenterOfMass * multiplier;
            if (oldAutomaticInertiaTensor) body.ResetInertiaTensor();
            else
            {
                body.inertiaTensor = oldInertiaTensor * (multiplier * multiplier);
                body.inertiaTensorRotation = oldInertiaTensorRotation;
            }
            scaleChanged = true;
        }

        private void RestoreScale()
        {
            if (!scaleChanged) return;
            transform.localScale = oldLocalScale;
            Physics.SyncTransforms();
            if (body)
            {
                if (oldAutomaticCenterOfMass) body.ResetCenterOfMass();
                else body.centerOfMass = oldCenterOfMass;
                if (oldAutomaticInertiaTensor) body.ResetInertiaTensor();
                else
                {
                    body.inertiaTensor = oldInertiaTensor;
                    body.inertiaTensorRotation = oldInertiaTensorRotation;
                }
            }
            scaleChanged = false;
        }

        private void OnValidate()
        {
            FollowSpeed = followSpeed;
            MovementLag = movementLag;
            ResetMotionTracking();
            // Scale edits apply on the next hold; validation never writes scene physics.
            heldScaleMultiplier = float.IsFinite(heldScaleMultiplier) ? Mathf.Clamp(heldScaleMultiplier, .1f, 1) : 1;
        }

        private void Restore()
        {
            RestoreScale();
            GrabCollisionExclusions.Release(this);
            if (!captured)
                return;
            captured = false;
            inspecting = false;
            InvalidateMotionHistory();
            if (!body)
                return;
            body.useGravity = oldGravity;
            body.interpolation = oldInterpolation;
            body.collisionDetectionMode = oldCollisionMode;
        }
    }
}
