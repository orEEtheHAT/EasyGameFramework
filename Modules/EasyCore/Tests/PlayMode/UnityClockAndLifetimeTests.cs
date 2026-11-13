using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyCore.Tests
{
    public sealed class UnityClockAndLifetimeTests
    {
        [UnityTest]
        public IEnumerator ScaledClockPausesWhileUnscaledClockContinues()
        {
            float previousScale = Time.timeScale;
            var scaled = new ScaledClock();
            var unscaled = new UnscaledClock();
            try
            {
                Time.timeScale = 0;
                yield return null;
                double gameStart = scaled.NowSeconds;
                double uiStart = unscaled.NowSeconds;
                yield return null;
                yield return null;
                Assert.That(scaled.NowSeconds, Is.EqualTo(gameStart));
                Assert.That(unscaled.NowSeconds, Is.GreaterThan(uiStart));
                Assert.That(scaled.NowSeconds, Is.EqualTo(Time.timeAsDouble));
                Assert.That(unscaled.NowSeconds, Is.EqualTo(Time.unscaledTimeAsDouble));
            }
            finally { Time.timeScale = previousScale; }
        }

        [UnityTest]
        public IEnumerator ClockReadsAreStableWithinAFrameAndProgressAfterResume()
        {
            float previousScale = Time.timeScale;
            var scaled = new ScaledClock();
            var unscaled = new UnscaledClock();
            try
            {
                Time.timeScale = 1;
                yield return null;
                double gameStart = scaled.NowSeconds, uiStart = unscaled.NowSeconds;
                Assert.That(new ScaledClock().NowSeconds, Is.EqualTo(gameStart));
                Assert.That(new UnscaledClock().NowSeconds, Is.EqualTo(uiStart));
                yield return null;
                yield return null;
                Assert.That(scaled.NowSeconds, Is.GreaterThan(gameStart));
                Assert.That(unscaled.NowSeconds, Is.GreaterThan(uiStart));
            }
            finally { Time.timeScale = previousScale; }
        }

        [UnityTest]
        public IEnumerator DisableReenableAndDestroyReleaseResourcesAndSubscriptions()
        {
            var go = new GameObject("EasyCore lifetime probe");
            var probe = go.AddComponent<ScopeOwnerProbe>();
            try
            {
                probe.Raise();
                Assert.That(probe.Notifications, Is.EqualTo(1));
                go.SetActive(false);
                probe.Raise();
                Assert.That(probe.Released, Is.EqualTo(1));
                Assert.That(probe.Notifications, Is.EqualTo(1));
                go.SetActive(true);
                probe.Raise();
                Assert.That(probe.Acquired, Is.EqualTo(2));
                Assert.That(probe.Notifications, Is.EqualTo(2));
                Object.Destroy(go);
                yield return null;
                // A managed reference remains inspectable after the Unity object is destroyed.
                Assert.That(probe.Released, Is.EqualTo(2));
                probe.Raise();
                Assert.That(probe.Notifications, Is.EqualTo(2));
            }
            finally { if (go) Object.DestroyImmediate(go); }
        }
    }
}
