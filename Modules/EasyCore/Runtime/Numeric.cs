using UnityEngine;

namespace EasyCore
{
    /// <summary>
    /// Allocation-free numeric predicates. Every predicate rejects NaN and infinities.
    /// Domain limits, tolerances, clamping and fallback values belong to the caller.
    /// </summary>
    public static class Numeric
    {
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Tests components, not length; very large finite vectors are accepted.</summary>
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        /// <summary>Finite and strictly greater than zero; both signed zeros are rejected.</summary>
        public static bool IsPositive(float value) => IsFinite(value) && value > 0f;
        public static bool IsPositive(double value) => IsFinite(value) && value > 0d;

        /// <summary>Finite and at least zero; both signed zeros are accepted.</summary>
        public static bool IsNonNegative(float value) => IsFinite(value) && value >= 0f;
        public static bool IsNonNegative(double value) => IsFinite(value) && value >= 0d;
    }
}
