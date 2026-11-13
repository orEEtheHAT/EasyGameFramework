using System;
using UnityEngine;

namespace EasyBuilding
{
    public enum BuildingGridLayout { Rectangular, HexPointy, HexFlat, Isometric }
    public enum BuildingGridPlane { XZ, XY, XYZ }
    [Flags] public enum BuildingSurface { Floor = 1, Wall = 2, Ceiling = 4, Any = 7 }

    [Serializable]
    public struct BuildingVolume
    {
        public Vector3 Center;
        public Vector3 Size;
        public Vector3 Rotation;
        public BuildingVolume(Vector3 center, Vector3 size, Vector3 rotation = default)
        { Center = center; Size = size; Rotation = rotation; }
        public bool IsValid => BuildingMath.Finite(Center) && BuildingMath.Finite(Size) && BuildingMath.Finite(Rotation)
            && Size.x > 0 && Size.y > 0 && Size.z > 0;
    }

    public readonly struct BuildingBox
    {
        public readonly Vector3 Center;
        public readonly Vector3 HalfSize;
        public readonly Quaternion Rotation;
        public BuildingBox(Vector3 center, Vector3 halfSize, Quaternion rotation)
        { Center = center; HalfSize = halfSize; Rotation = rotation; }
        public Vector3 Corner(int index) => Center + Rotation * new Vector3(
            (index & 1) == 0 ? -HalfSize.x : HalfSize.x,
            (index & 2) == 0 ? -HalfSize.y : HalfSize.y,
            (index & 4) == 0 ? -HalfSize.z : HalfSize.z);
        public Bounds Bounds
        {
            get
            {
                var x = Rotation * Vector3.right; var y = Rotation * Vector3.up; var z = Rotation * Vector3.forward;
                var extent = BuildingGeometry.Abs(x) * HalfSize.x + BuildingGeometry.Abs(y) * HalfSize.y + BuildingGeometry.Abs(z) * HalfSize.z;
                return new Bounds(Center, extent * 2);
            }
        }
    }

    /// <summary>Allocation-free placement math shared by validation, generators and tests.</summary>
    public static class BuildingGeometry
    {
        public static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        public static Vector3 Snap(Vector3 point, float cellSize, Vector3 origin, BuildingGridLayout layout, BuildingGridPlane plane)
        {
            if (!BuildingMath.Finite(point) || !BuildingMath.Finite(origin) || !BuildingMath.Finite(cellSize) || cellSize < 0.01f)
                return new Vector3(float.NaN, float.NaN, float.NaN);
            Vector3 p = (point - origin) / cellSize;
            float a = p.x, b = plane == BuildingGridPlane.XY ? p.y : p.z;
            if (layout == BuildingGridLayout.HexPointy || layout == BuildingGridLayout.HexFlat)
            {
                float q = layout == BuildingGridLayout.HexPointy ? Mathf.Sqrt(3) / 3 * a - b / 3 : 2f / 3 * a;
                float r = layout == BuildingGridLayout.HexPointy ? 2f / 3 * b : -a / 3 + Mathf.Sqrt(3) / 3 * b;
                float x = Mathf.Round(q), z = Mathf.Round(r), y = Mathf.Round(-q - r);
                float dx = Mathf.Abs(x - q), dy = Mathf.Abs(y + q + r), dz = Mathf.Abs(z - r);
                if (dx > dy && dx > dz) x = -y - z;
                else if (dz >= dy) z = -x - y;
                a = layout == BuildingGridLayout.HexPointy ? Mathf.Sqrt(3) * (x + z * 0.5f) : 1.5f * x;
                b = layout == BuildingGridLayout.HexPointy ? 1.5f * z : Mathf.Sqrt(3) * (z + x * 0.5f);
            }
            else if (layout == BuildingGridLayout.Isometric)
            {
                float u = Mathf.Round(a + b), v = Mathf.Round(b - a);
                a = (u - v) * 0.5f; b = (u + v) * 0.5f;
            }
            else { a = Mathf.Round(a); b = Mathf.Round(b); }
            p.x = a;
            if (plane == BuildingGridPlane.XY) p.y = b; else p.z = b;
            if (plane == BuildingGridPlane.XYZ) p.y = Mathf.Round(p.y);
            return origin + p * cellSize;
        }

        public static BuildingBox Transform(BuildingVolume volume, Pose pose, Vector3 scale)
        {
            // Rotated child volumes with nonuniform scale would create shear. Definitions reject that combination.
            return new BuildingBox(pose.position + pose.rotation * Vector3.Scale(volume.Center, scale),
                Vector3.Scale(volume.Size, Abs(scale)) * 0.5f, pose.rotation * Quaternion.Euler(volume.Rotation));
        }

        public static bool Intersects(in BuildingBox a, in BuildingBox b, float contactTolerance = 0.0001f)
        {
            var delta = b.Center - a.Center;
            for (int i = 0; i < 3; i++)
            {
                var axisA = Axis(a.Rotation, i); var axisB = Axis(b.Rotation, i);
                if (Separates(axisA, delta, a, b, contactTolerance) || Separates(axisB, delta, a, b, contactTolerance)) return false;
                for (int j = 0; j < 3; j++)
                {
                    var cross = Vector3.Cross(axisA, Axis(b.Rotation, j));
                    float length = cross.sqrMagnitude;
                    if (length > 0.00000001f && Separates(cross / Mathf.Sqrt(length), delta, a, b, contactTolerance)) return false;
                }
            }
            return true;
        }
        private static bool Separates(Vector3 axis, Vector3 delta, in BuildingBox a, in BuildingBox b, float tolerance)
        {
            float ra = Mathf.Abs(Vector3.Dot(axis, a.Rotation * Vector3.right)) * a.HalfSize.x
                + Mathf.Abs(Vector3.Dot(axis, a.Rotation * Vector3.up)) * a.HalfSize.y
                + Mathf.Abs(Vector3.Dot(axis, a.Rotation * Vector3.forward)) * a.HalfSize.z;
            float rb = Mathf.Abs(Vector3.Dot(axis, b.Rotation * Vector3.right)) * b.HalfSize.x
                + Mathf.Abs(Vector3.Dot(axis, b.Rotation * Vector3.up)) * b.HalfSize.y
                + Mathf.Abs(Vector3.Dot(axis, b.Rotation * Vector3.forward)) * b.HalfSize.z;
            return Mathf.Abs(Vector3.Dot(delta, axis)) >= Mathf.Max(0, ra + rb - tolerance);
        }
        private static Vector3 Axis(Quaternion rotation, int axis) => rotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
    }
}
