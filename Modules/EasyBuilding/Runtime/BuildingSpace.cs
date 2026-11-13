using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    public enum BuildingGravity { WorldUp, LocalUp, Radial }

    [Serializable]
    public struct BuildingWorldPosition
    {
        public double X, Y, Z;
        public BuildingWorldPosition(double x, double y, double z) { X = x; Y = y; Z = z; }
        public Vector3 RelativeTo(BuildingWorldPosition origin) => new Vector3((float)(X - origin.X), (float)(Y - origin.Y), (float)(Z - origin.Z));
        public BuildingWorldPosition Add(Vector3 value) => new BuildingWorldPosition(X + value.x, Y + value.y, Z + value.z);
    }

    /// <summary>Optional frame for a plot, floor, ship, planet or streamed region. Buildings use its local coordinates.</summary>
    [ExecuteAlways, DisallowMultipleComponent, AddComponentMenu("EasyGameFramework/Building/Building Space")]
    public sealed class BuildingSpace : MonoBehaviour
    {
        [SerializeField] private string spaceId = Guid.NewGuid().ToString("N");
        public BuildingSystem System;
        public BuildingGravity Gravity = BuildingGravity.LocalUp;
        [Tooltip("Optional spherical grid. Zero uses an ordinary local grid.")]
        [Min(0)] public float SphereRadius;
        [Min(0.01f)] public float SphereStepDegrees = 5;
        public BuildingWorldPosition OriginOffset;
        public string Id => spaceId;
        internal static readonly List<BuildingSpace> Active = new List<BuildingSpace>();
        public event Action<Vector3> OriginShifted;
        private void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        private void OnDisable() => Active.Remove(this);
        public void SetId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A space ID cannot be empty.", nameof(id));
            spaceId = id;
        }
        public Vector3 UpAt(Vector3 point)
        {
            if (Gravity == BuildingGravity.WorldUp) return Vector3.up;
            if (Gravity == BuildingGravity.Radial)
            {
                var delta = point - transform.position;
                return delta.sqrMagnitude > 0.000001f ? delta.normalized : transform.up;
            }
            return transform.up;
        }
        public bool IsValid
        {
            get
            {
                var scale = transform.lossyScale;
                return !string.IsNullOrWhiteSpace(spaceId) && BuildingMath.Finite(scale) && scale.x > 0 &&
                    Mathf.Abs(scale.x - scale.y) < 0.0001f && Mathf.Abs(scale.x - scale.z) < 0.0001f;
            }
        }
        public Pose ToLocal(Pose pose) => new Pose(transform.InverseTransformPoint(pose.position), Quaternion.Inverse(transform.rotation) * pose.rotation);
        public Pose ToWorld(Pose pose) => new Pose(transform.TransformPoint(pose.position), transform.rotation * pose.rotation);
        public Vector3 Snap(BuildingDefinition definition, Vector3 point)
        {
            var local = transform.InverseTransformPoint(point);
            if (SphereRadius > 0)
            {
                if (local.sqrMagnitude < 0.000001f || !BuildingMath.Finite(SphereStepDegrees) || SphereStepDegrees <= 0) return new Vector3(float.NaN, 0, 0);
                var direction = local.normalized;
                float latitude = Mathf.Asin(Mathf.Clamp(direction.y, -1, 1)) * Mathf.Rad2Deg;
                float longitude = Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg;
                latitude = Mathf.Clamp(Mathf.Round(latitude / SphereStepDegrees) * SphereStepDegrees, -90, 90) * Mathf.Deg2Rad;
                longitude = Mathf.Round(longitude / SphereStepDegrees) * SphereStepDegrees * Mathf.Deg2Rad;
                local = new Vector3(Mathf.Cos(latitude) * Mathf.Cos(longitude), Mathf.Sin(latitude), Mathf.Cos(latitude) * Mathf.Sin(longitude)) * SphereRadius;
            }
            else local = BuildingGeometry.Snap(local, definition.GridSize, definition.GridOrigin, definition.GridLayout, definition.GridPlane);
            return transform.TransformPoint(local);
        }
        public void ShiftOrigin(Vector3 shift)
        {
            if (!BuildingMath.Finite(shift)) throw new ArgumentException("Use a finite origin shift.", nameof(shift));
            transform.position -= shift; OriginOffset = OriginOffset.Add(shift);
            if (OriginShifted == null) return;
            foreach (Action<Vector3> handler in OriginShifted.GetInvocationList())
                try { handler(shift); } catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
