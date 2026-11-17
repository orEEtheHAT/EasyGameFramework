using System;
using UnityEngine;

namespace EasyInteraction
{
    /// <summary>Converts an authored hold pose without moving the item or modifying its hold frame.</summary>
    public static class GrabPoseUtility
    {
        /// <summary>The supplied frame ignores scale. View frames add HoldDistance; anchors do not.</summary>
        public static Pose GetRootPose(GrabAction grab, Pose frame, bool usesViewDistance)
        {
            if (!grab) throw new ArgumentNullException(nameof(grab));
            var rotation = frame.rotation * Quaternion.Euler(grab.LocalEulerAngles);
            var local = grab.LocalPosition;
            if (usesViewDistance) local.z += grab.HoldDistance;
            var point = frame.position + frame.rotation * local;
            var matrix = GetRootMatrix(grab, new Pose(Vector3.zero, rotation));
            return new Pose(point - matrix.MultiplyVector(LocalGrip(grab)), rotation);
        }

        /// <summary>Returns LocalPosition and local rotation for the supplied desired world root pose.</summary>
        public static Pose GetLocalPose(GrabAction grab, Pose rootPose, Pose frame, bool usesViewDistance)
        {
            if (!grab) throw new ArgumentNullException(nameof(grab));
            var point = rootPose.position + GetRootMatrix(grab, rootPose).MultiplyVector(LocalGrip(grab));
            var local = Quaternion.Inverse(frame.rotation) * (point - frame.position);
            if (usesViewDistance) local.z -= grab.HoldDistance;
            return new Pose(local, Quaternion.Inverse(frame.rotation) * rootPose.rotation);
        }

        /// <summary>Predicts the held root matrix, including root scale multiplier and its full parent matrix.</summary>
        public static Matrix4x4 GetRootMatrix(GrabAction grab, Pose rootPose)
        {
            if (!grab) throw new ArgumentNullException(nameof(grab));
            var root = grab.transform;
            var parent = root.parent;
            var scale = grab.UnheldLocalScale * grab.HeldScaleMultiplier;
            if (!parent) return Matrix4x4.TRS(rootPose.position, rootPose.rotation, scale);
            var localPosition = parent.InverseTransformPoint(rootPose.position);
            var localRotation = Quaternion.Inverse(parent.rotation) * rootPose.rotation;
            return parent.localToWorldMatrix * Matrix4x4.TRS(localPosition, localRotation, scale);
        }

        private static Vector3 LocalGrip(GrabAction grab) => grab.GripPoint
            ? grab.transform.InverseTransformPoint(grab.GripPoint.position)
            : Vector3.zero;
    }
}
