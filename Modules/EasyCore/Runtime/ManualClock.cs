using System;

namespace EasyCore
{
    /// <summary>
    /// Explicit, nondecreasing finite seconds for tests and manually advanced simulations.
    /// No Unity API, reset, automatic ticking or thread synchronization.
    /// </summary>
    public sealed class ManualClock : IClock
    {
        public double NowSeconds { get; private set; }

        public ManualClock(double startSeconds = 0)
        {
            if (!Numeric.IsNonNegative(startSeconds))
                throw new ArgumentOutOfRangeException(nameof(startSeconds), "Use finite, nonnegative seconds.");
            NowSeconds = startSeconds;
        }

        /// <summary>
        /// Advances by finite nonnegative seconds. Invalid input or overflow throws without
        /// changing the clock. Zero is allowed; tiny increments may round to the current value.
        /// </summary>
        public void Advance(double deltaSeconds)
        {
            if (!Numeric.IsNonNegative(deltaSeconds))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Use finite, nonnegative seconds.");
            double next = NowSeconds + deltaSeconds;
            if (!Numeric.IsFinite(next))
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "The resulting time must remain finite.");
            NowSeconds = next;
        }
    }
}
