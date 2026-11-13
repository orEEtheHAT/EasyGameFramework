using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilding
{
    /// <summary>Pure placement generators. Output is replaced; the world is changed only by TryPlaceBatch.</summary>
    public static class BuildingPatterns
    {
        public static void Line(BuildingDefinition definition, Vector3 start, Vector3 end, float spacing,
            List<BuildingPlacement> output, Quaternion? rotation = null, BuildingSpace space = null, int maximum = 10000)
        {
            Validate(definition, output, spacing, maximum);
            if (!BuildingMath.Finite(start) || !BuildingMath.Finite(end)) throw new ArgumentException("Line endpoints must be finite.");
            float distance = Vector3.Distance(start, end);
            double intervals = Math.Ceiling(distance / (double)spacing);
            if (intervals >= maximum) throw new ArgumentException("The line exceeds the placement budget.");
            int steps = (int)intervals;
            var facing = rotation ?? (distance > 0.0001f ? Quaternion.LookRotation((end - start).normalized, SafeUp(end - start)) : Quaternion.identity);
            output.Clear();
            for (int i = 0; i <= steps; i++) output.Add(new BuildingPlacement(definition,
                new PlacementRequest(steps == 0 ? start : Vector3.Lerp(start, end, i / (float)steps), facing, space: space)));
        }
        public static void Rectangle(BuildingDefinition definition, Pose origin, int columns, int rows, Vector2 spacing,
            List<BuildingPlacement> output, bool borderOnly = false, BuildingSpace space = null, int maximum = 10000)
        {
            Validate(definition, output, Mathf.Min(spacing.x, spacing.y), maximum);
            ValidatePose(origin);
            if (!BuildingMath.Finite(spacing.x) || !BuildingMath.Finite(spacing.y) || columns <= 0 || rows <= 0)
                throw new ArgumentException("Rows, columns and both spacing values must be positive.");
            long count = borderOnly && columns > 1 && rows > 1 ? 2L * columns + 2L * rows - 4 : (long)columns * rows;
            if (count > maximum) throw new ArgumentException("The rectangle exceeds the placement budget.");
            output.Clear();
            for (int y = 0; y < rows; y++) for (int x = 0; x < columns; x++)
            {
                if (borderOnly && x != 0 && y != 0 && x != columns - 1 && y != rows - 1) continue;
                Add(definition, origin, new Vector3(x * spacing.x, 0, y * spacing.y), output, space);
            }
        }
        public static void Brush(BuildingDefinition definition, Pose center, float radius, float spacing,
            List<BuildingPlacement> output, BuildingSpace space = null, int maximum = 10000)
        {
            Validate(definition, output, spacing, maximum); ValidatePose(center);
            if (!BuildingMath.Finite(radius) || radius < 0) throw new ArgumentException("Brush radius must be finite and nonnegative.");
            double extent = Math.Floor(radius / (double)spacing);
            if (extent * 2 + 1 > maximum) throw new ArgumentException("The brush exceeds the placement budget.");
            int cells = (int)extent; int count = 0;
            for (int z = -cells; z <= cells; z++) for (int x = -cells; x <= cells; x++)
                if ((double)x * x + (double)z * z <= (double)radius * radius / spacing / spacing && ++count > maximum)
                    throw new ArgumentException("The brush exceeds the placement budget.");
            output.Clear();
            for (int z = -cells; z <= cells; z++) for (int x = -cells; x <= cells; x++)
                if ((double)x * x + (double)z * z <= (double)radius * radius / spacing / spacing)
                    Add(definition, center, new Vector3(x * spacing, 0, z * spacing), output, space);
        }
        /// <summary>Places at regular arc-length intervals across a polyline, including its final endpoint once.</summary>
        public static void Path(BuildingDefinition definition, IReadOnlyList<Vector3> points, float spacing,
            List<BuildingPlacement> output, BuildingSpace space = null, int maximum = 10000)
        {
            Validate(definition, output, spacing, maximum);
            if (points == null || points.Count == 0) throw new ArgumentException("Supply at least one path point.");
            double length = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (!BuildingMath.Finite(points[i])) throw new ArgumentException("Path points must be finite.");
                if (i > 0) length += Vector3.Distance(points[i - 1], points[i]);
            }
            if (Math.Ceiling(length / spacing) + 1 > maximum) throw new ArgumentException("The path exceeds the placement budget.");
            output.Clear(); double next = 0, passed = 0; Quaternion facing = Quaternion.identity;
            for (int i = 1; i < points.Count; i++)
            {
                var delta = points[i] - points[i - 1]; double segment = delta.magnitude;
                if (segment < 0.000001) continue;
                facing = Quaternion.LookRotation(delta.normalized, SafeUp(delta));
                while (next < passed + segment - 0.000001)
                {
                    var position = Vector3.Lerp(points[i - 1], points[i], (float)((next - passed) / segment));
                    output.Add(new BuildingPlacement(definition, new PlacementRequest(position, facing, space: space))); next += spacing;
                }
                passed += segment;
            }
            output.Add(new BuildingPlacement(definition, new PlacementRequest(points[points.Count - 1], facing, space: space)));
        }
        private static Vector3 SafeUp(Vector3 direction) => Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
        private static void Add(BuildingDefinition definition, Pose pose, Vector3 point, List<BuildingPlacement> output, BuildingSpace space)
            => output.Add(new BuildingPlacement(definition, new PlacementRequest(pose.position + pose.rotation * point, pose.rotation, space: space)));
        private static void ValidatePose(Pose pose)
        { if (!BuildingMath.Finite(pose.position) || !BuildingMath.ValidRotation(pose.rotation)) throw new ArgumentException("Use a finite pose with a nonzero rotation."); }
        private static void Validate(BuildingDefinition definition, List<BuildingPlacement> output, float spacing, int maximum)
        {
            if (definition == null || output == null) throw new ArgumentNullException(definition == null ? nameof(definition) : nameof(output));
            if (!BuildingMath.Finite(spacing) || spacing <= 0 || maximum < 1) throw new ArgumentException("Spacing and maximum placements must be positive.");
        }
    }
}
