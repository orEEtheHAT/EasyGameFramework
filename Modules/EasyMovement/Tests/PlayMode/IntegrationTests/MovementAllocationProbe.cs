using System;
using Unity.Profiling;

namespace EasyMovement.IntegrationTests
{
    internal static class MovementAllocationProbe
    {
        internal static long Count(Action operation, int iterations)
        {
            var options = ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
            using var events = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1, options);
            for (int i = 0; i < iterations; i++) operation();
            events.Stop();
            if (!events.Valid) throw new InvalidOperationException("The main-thread GC.Alloc recorder is unavailable.");
            return events.Count == 0 ? 0 : events.GetSample(0).Count;
        }
    }
}
