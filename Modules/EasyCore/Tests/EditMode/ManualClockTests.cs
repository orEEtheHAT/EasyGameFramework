using System;
using NUnit.Framework;

namespace EasyCore.Tests
{
    public sealed class ManualClockTests
    {
        [Test]
        public void ExplicitSecondsAndIndependentTimelines()
        {
            var first = new ManualClock(10);
            var second = new ManualClock();
            IClock source = first;
            first.Advance(.25);
            first.Advance(0);
            Assert.That(source.NowSeconds, Is.EqualTo(10.25));
            Assert.That(second.NowSeconds, Is.Zero);
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void RejectsInvalidStartingPoint(double seconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ManualClock(seconds));
        }

        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void RejectsInvalidAdvanceWithoutMutation(double seconds)
        {
            var clock = new ManualClock(42);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(seconds));
            Assert.That(clock.NowSeconds, Is.EqualTo(42));
            clock.Advance(1);
            Assert.That(clock.NowSeconds, Is.EqualTo(43));
        }

        [Test]
        public void OverflowDoesNotPoisonClockAndZeroStillWorks()
        {
            var clock = new ManualClock(double.MaxValue);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.MaxValue));
            clock.Advance(0);
            Assert.That(clock.NowSeconds, Is.EqualTo(double.MaxValue));
        }

        [Test]
        public void FinitePrecisionMayLeaveTimeUnchangedButNeverGoBack()
        {
            var clock = new ManualClock(1e30);
            clock.Advance(double.Epsilon);
            Assert.That(clock.NowSeconds, Is.EqualTo(1e30));
        }
    }
}
