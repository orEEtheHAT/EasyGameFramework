using UnityEngine;

namespace EasyCore
{
    /// <summary>
    /// Unity unscaled frame time in seconds (Time.unscaledTimeAsDouble).
    /// Continues at timeScale zero; does not advance within a frame or during Editor pause.
    /// Main thread in Play Mode. In FixedUpdate reads fixed unscaled time.
    /// Not a realtime/UTC/authority clock; do not mix fixed and frame readings.
    /// </summary>
    public sealed class UnscaledClock : IClock
    {
        public double NowSeconds => Time.unscaledTimeAsDouble;
    }
}
