using NUnit.Framework;
using UnityEngine;

namespace EasyCore.Tests
{
    public sealed class NumericTests
    {
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteFloatsFailEveryPredicate(float value)
        {
            Assert.That(Numeric.IsFinite(value), Is.False);
            Assert.That(Numeric.IsPositive(value), Is.False);
            Assert.That(Numeric.IsNonNegative(value), Is.False);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonFiniteDoublesFailEveryPredicate(double value)
        {
            Assert.That(Numeric.IsFinite(value), Is.False);
            Assert.That(Numeric.IsPositive(value), Is.False);
            Assert.That(Numeric.IsNonNegative(value), Is.False);
        }

        [TestCase(float.MaxValue, true, true)]
        [TestCase(float.MinValue, false, false)]
        [TestCase(float.Epsilon, true, true)]
        [TestCase(-float.Epsilon, false, false)]
        [TestCase(0f, false, true)]
        public void FiniteFloatBoundariesHaveNoHiddenEpsilon(float value, bool positive, bool nonNegative)
        {
            Assert.That(Numeric.IsFinite(value), Is.True);
            Assert.That(Numeric.IsPositive(value), Is.EqualTo(positive));
            Assert.That(Numeric.IsNonNegative(value), Is.EqualTo(nonNegative));
        }

        [TestCase(double.MaxValue, true, true)]
        [TestCase(double.MinValue, false, false)]
        [TestCase(double.Epsilon, true, true)]
        [TestCase(-double.Epsilon, false, false)]
        [TestCase(0d, false, true)]
        public void FiniteDoubleBoundariesHaveNoHiddenEpsilon(double value, bool positive, bool nonNegative)
        {
            Assert.That(Numeric.IsFinite(value), Is.True);
            Assert.That(Numeric.IsPositive(value), Is.EqualTo(positive));
            Assert.That(Numeric.IsNonNegative(value), Is.EqualTo(nonNegative));
        }

        [Test]
        public void NegativeZeroIsNonNegativeButNotPositive()
        {
            float single = System.BitConverter.ToSingle(System.BitConverter.GetBytes(int.MinValue), 0);
            double twice = System.BitConverter.Int64BitsToDouble(long.MinValue);
            Assert.That(Numeric.IsFinite(single) && Numeric.IsNonNegative(single), Is.True);
            Assert.That(Numeric.IsFinite(twice) && Numeric.IsNonNegative(twice), Is.True);
            Assert.That(Numeric.IsPositive(single) || Numeric.IsPositive(twice), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EveryVectorComponentIsValidated(int axis)
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                var value = Vector3.one;
                value[axis] = invalid;
                Assert.That(Numeric.IsFinite(value), Is.False);
            }
        }

        [Test]
        public void LargeVectorWithOverflowingLengthStillHasFiniteComponents()
        {
            var value = new Vector3(float.MaxValue, -float.MaxValue, float.MaxValue);
            Assert.That(float.IsInfinity(value.sqrMagnitude), Is.True);
            Assert.That(Numeric.IsFinite(value), Is.True);
            Assert.That(Numeric.IsFinite(Vector3.zero), Is.True);
        }
    }
}
