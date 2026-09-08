using System;
using Game.NomadWorkshop.Foundation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Editor.Tests
{
    public sealed class FoundationJourneyCameraBufferTests
    {
        [Test]
        public void MotionBuffer_RespondsWithoutJumping_AndIsFramePartitionInvariant()
        {
            var whole = new FoundationJourneyCameraBuffer(response: 8d);
            var chunked = new FoundationJourneyCameraBuffer(response: 8d);
            var ramp = new FoundationJourneyCameraBuffer(response: 8d);
            whole.Reset(0d);
            chunked.Reset(0d);
            ramp.Reset(0d);

            double first = ramp.Update(.45d, .016d);
            whole.Update(.45d, 1d);
            for (var i = 0; i < 60; i++) chunked.Update(.45d, 1d / 60d);

            Assert.That(first, Is.GreaterThan(0d).And.LessThan(.45d));
            Assert.That(whole.CurrentOffsetMeters, Is.EqualTo(chunked.CurrentOffsetMeters).Within(1e-12));
            Assert.That(whole.CurrentOffsetMeters, Is.LessThan(.45d));
        }

        [Test]
        public void MotionBuffer_ResetAndZeroDeltaPreserveExplicitRebuildBoundary()
        {
            var buffer = new FoundationJourneyCameraBuffer();
            buffer.Reset(.3d);
            Assert.That(buffer.Update(-.3d, 0d), Is.EqualTo(.3d));
            buffer.Reset(0d);
            Assert.That(buffer.CurrentOffsetMeters, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Update(double.NaN, .1d));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Update(.1d, -1d));
        }
    }
}
