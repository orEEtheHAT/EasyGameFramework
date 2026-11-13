using System;
using UnityEngine;

namespace EasyCore.Samples
{
    /// <summary>Add to an empty GameObject and enter Play Mode. Results appear in the Console.</summary>
    [AddComponentMenu("EasyGameFramework/Examples/Core First Run")]
    public sealed class CoreFirstRun : MonoBehaviour
    {
        public bool Completed { get; private set; }
        public int Checks { get; private set; }
        public int Failures { get; private set; }

        private sealed class Lease : IDisposable
        {
            public bool Released;
            public void Dispose() => Released = true;
        }

        private void Start()
        {
            Check(!Numeric.IsNonNegative(float.NaN), "NaN is rejected");
            Check(Numeric.IsPositive(.25f), "Positive finite value is accepted");
            Check(!Numeric.IsFinite(new Vector3(0, float.PositiveInfinity, 0)), "All vector components are checked");

            var clock = new ManualClock(10);
            IClock source = clock;
            double deadline = source.NowSeconds + 2;
            clock.Advance(1);
            Check(source.NowSeconds < deadline, "Manual clock stays before deadline");
            clock.Advance(1);
            Check(source.NowSeconds >= deadline, "Manual clock reaches deadline without sleeping");

            var scope = new DisposableScope();
            var lease = scope.Own(new Lease());
            int callbacks = 0;
            scope.Defer(() => callbacks++);
            scope.Dispose();
            scope.Dispose();
            Check(lease.Released && callbacks == 1, "Owned resources and cleanup release once");
            Check(Numeric.IsNonNegative(new ScaledClock().NowSeconds), "Scaled game clock is available");
            Check(Numeric.IsNonNegative(new UnscaledClock().NowSeconds), "Unscaled frame clock is available");
            Completed = true;
            Debug.Log($"EasyCore first run: {Checks - Failures}/{Checks} checks passed.", this);
        }

        private void Check(bool passed, string label)
        {
            Checks++;
            if (passed) return;
            Failures++;
            Debug.LogError("EasyCore first run failed: " + label, this);
        }
    }
}
