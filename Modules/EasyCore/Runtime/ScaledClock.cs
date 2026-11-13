using UnityEngine;

namespace EasyCore
{
    /// <summary>
    /// Unity game time in seconds (Time.timeAsDouble). Affected by timeScale.
    /// Main thread, in Play Mode after Awake. In FixedUpdate reads fixed time;
    /// do not mix fixed and frame readings. Has no mutable global state.
    /// </summary>
    public sealed class ScaledClock : IClock
    {
        public double NowSeconds => Time.timeAsDouble;
    }
}
