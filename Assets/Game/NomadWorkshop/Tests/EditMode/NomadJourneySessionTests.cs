using System;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class NomadJourneySessionTests
    {
        private static NomadJourneyRoute Route(int speed = 1379) =>
            new("salt-road", "workshop", "well", 100_000L, speed, 150);
        private static NomadJourneySession Journey() => new(Route(), 100_000_000_000L);

        private static NomadJourneyRoute SmoothRoute() =>
            new("smooth-road", "workshop", "well", 100_000L, 4_000, 150,
                new[]
                {
                    new NomadJourneyRouteAnchor("midway", 500),
                });

        private static NomadJourneyMotionPolicy SmoothMotion() =>
            new(accelerationMillimetersPerSecondSquared: 1_000,
                brakingMillimetersPerSecondSquared: 2_000);

        [Test]
        public void TargetAloneCannotDrive_AndDriverIsExclusive()
        {
            using var journey = Journey();
            Assert.IsFalse(journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out _));
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            Assert.That(journey.Advance(10_000), Is.Zero);
            Assert.That(journey.FuelPicoliters, Is.EqualTo(100_000_000_000L));
            Assert.IsTrue(journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var driver));
            Assert.IsFalse(journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out _));
            Assert.IsFalse(journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _));
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            Assert.IsTrue(driver.IsActive, "重复玩家意图不应让已经到岗的居民离岗。");
            Assert.That(journey.Advance(1000), Is.EqualTo(1_379_000L));
            driver.Dispose();
            driver.Dispose();
            Assert.That(journey.Advance(1000), Is.Zero);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
        }

        [Test]
        public void SmoothedMotion_AcceleratesBrakesAndIsFramePartitionInvariant()
        {
            NomadJourneyRoute route = SmoothRoute();
            using var whole = new NomadJourneySession(route, 1_000_000_000_000L, SmoothMotion());
            using var chunked = new NomadJourneySession(route, 1_000_000_000_000L, SmoothMotion());
            whole.SetDestination(NomadJourneyEndpoint.Destination);
            chunked.SetDestination(NomadJourneyEndpoint.Destination);
            Assert.IsTrue(whole.TryAcquireDriver("resident-1", whole.DestinationRevision, out _));
            Assert.IsTrue(chunked.TryAcquireDriver("resident-1", chunked.DestinationRevision, out _));

            Assert.That(whole.CurrentSpeedNanometersPerMillisecond, Is.Zero,
                "平滑路线从静止起步，不能在接班瞬间跳到巡航速度。");
            whole.Advance(6_000);
            for (var i = 0; i < 6; i++) chunked.Advance(1_000);

            Assert.That(whole.Capture(), Is.EqualTo(chunked.Capture()),
                "同一模拟时长的不同输入分块必须得到同一速度、位置和燃料。");
            Assert.That(whole.PositionMicrometers, Is.GreaterThan(0L));
            Assert.That(whole.PositionMicrometers, Is.LessThan(24_000_000L),
                "起步加速阶段不应等同于旧恒速路线。");
            Assert.That(whole.CurrentSpeedNanometersPerMillisecond, Is.EqualTo(4_000_000L));

            var saved = whole.Capture();
            whole.Advance(30_000);
            Assert.That(whole.Status, Is.EqualTo(NomadJourneyStatus.Arrived));
            Assert.That(whole.CurrentSpeedNanometersPerMillisecond, Is.Zero,
                "到达目标前应完成制动，而不是把表现瞬移到终点。");
            whole.Restore(saved);
            Assert.IsTrue(whole.TryAcquireDriver("resident-2", whole.DestinationRevision, out _));
            whole.Advance(500);
            var restoredExpected = whole.Capture();

            using var replay = new NomadJourneySession(route, 1_000_000_000_000L, SmoothMotion());
            replay.Restore(saved);
            Assert.IsTrue(replay.TryAcquireDriver("resident-2", replay.DestinationRevision, out _));
            replay.Advance(500);
            Assert.That(replay.Capture(), Is.EqualTo(restoredExpected),
                "保存 / 读取必须保留速度与亚微米积分余量。");
        }

        [Test]
        public void RouteAnchors_ResolveStableProgressAndRejectUnorderedDuplicates()
        {
            var route = new NomadJourneyRoute(
                "anchored-road", "workshop", "well", 100_000L, 1_000, 10,
                new[]
                {
                    new NomadJourneyRouteAnchor("first-shelter", 100),
                    new NomadJourneyRouteAnchor("last-shelter", 900),
                });

            Assert.IsTrue(route.TryResolveAnchorPosition("first-shelter", out long first));
            Assert.That(first, Is.EqualTo(10_000_000L));
            Assert.IsFalse(route.TryResolveAnchorPosition("missing", out _));
            Assert.Throws<ArgumentException>(() => new NomadJourneyRoute(
                "duplicate-road", "workshop", "well", 100_000L, 1_000, 10,
                new[]
                {
                    new NomadJourneyRouteAnchor("same", 100),
                    new NomadJourneyRouteAnchor("same", 200),
                }));
            Assert.Throws<ArgumentException>(() => new NomadJourneyRoute(
                "unordered-road", "workshop", "well", 100_000L, 1_000, 10,
                new[]
                {
                    new NomadJourneyRouteAnchor("late", 900),
                    new NomadJourneyRouteAnchor("early", 100),
                }));
        }

        [Test]
        public void AnchorDestination_ChoosesRouteDirectionStopsAndSurvivesRestore()
        {
            NomadJourneyRoute route = SmoothRoute();
            using var journey = new NomadJourneySession(route, 1_000_000_000_000L, SmoothMotion());
            journey.SetAnchorDestination("midway");

            Assert.That(journey.Destination, Is.EqualTo(NomadJourneyEndpoint.Destination));
            Assert.That(journey.DestinationAnchorId, Is.EqualTo("midway"));
            Assert.That(journey.DestinationPositionMicrometers, Is.EqualTo(50_000_000L));
            Assert.IsTrue(journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out _));
            journey.Advance(1_000);
            var saved = journey.Capture();

            journey.SetDestination(NomadJourneyEndpoint.None);
            Assert.That(journey.PositionMicrometers, Is.EqualTo(saved.PositionMicrometers));
            Assert.That(journey.DestinationAnchorId, Is.Empty);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.NoDestination));

            journey.Restore(saved);
            Assert.That(journey.DestinationAnchorId, Is.EqualTo("midway"));
            Assert.That(journey.DestinationPositionMicrometers, Is.EqualTo(50_000_000L));
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
            Assert.IsTrue(journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _));
            journey.Advance(30_000);
            Assert.That(journey.PositionMicrometers, Is.EqualTo(50_000_000L));
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.Arrived));
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(137)]
        [TestCase(1000)]
        public void FramePartitionsPreserveExactDistanceFuelAndRestore(int chunk)
        {
            using var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out _);
            long remaining = 12_347;
            while (remaining > 0)
            {
                long step = Math.Min(chunk, remaining);
                journey.Advance(step);
                remaining -= step;
            }
            Assert.That(journey.PositionMicrometers, Is.EqualTo(1379L * 12_347));
            Assert.That(journey.FuelPicoliters, Is.EqualTo(100_000_000_000L - 1379L * 12_347 * 150));
            var saved = journey.Capture();
            journey.Advance(777);
            var expected = journey.Capture();
            journey.Restore(saved);
            Assert.That(journey.Advance(5000), Is.Zero, "恢复不得把旧岗位伪装成已重新到岗。");
            journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _);
            journey.Advance(777);
            Assert.That(journey.Capture(), Is.EqualTo(expected));
        }

        [Test]
        public void ChangeTargetAndCancelPreservePosition_StaleReleaseCannotStopNewDriver()
        {
            using var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var old);
            journey.Advance(137);
            long position = journey.PositionMicrometers;
            journey.SetDestination(NomadJourneyEndpoint.Origin);
            Assert.IsFalse(old.IsActive);
            Assert.That(journey.PositionMicrometers, Is.EqualTo(position));
            journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out var current);
            old.Dispose();
            Assert.IsTrue(current.IsActive);
            Assert.That(journey.Advance(1), Is.EqualTo(1379L));
            Assert.That(journey.PositionMicrometers, Is.EqualTo(position - 1379L));
            journey.SetDestination(NomadJourneyEndpoint.None);
            Assert.IsFalse(current.IsActive);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.NoDestination));
            Assert.That(journey.Advance(1000), Is.Zero);
            Assert.That(journey.PositionMicrometers, Is.EqualTo(position - 1379L));
        }

        [Test]
        public void LateArrivalForCancelledReissuedOrRestoredGoalCannotAcquireDriver()
        {
            using var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            long oldApproachRevision = journey.DestinationRevision;
            journey.SetDestination(NomadJourneyEndpoint.None);
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            Assert.IsFalse(journey.TryAcquireDriver("resident-1", oldApproachRevision, out _),
                "目标地点相同也不能把取消前的寻路完成当成新一次到岗。");
            long currentApproachRevision = journey.DestinationRevision;
            Assert.IsTrue(journey.TryAcquireDriver("resident-2", currentApproachRevision, out _));
            journey.Restore(journey.Capture());
            Assert.IsFalse(journey.TryAcquireDriver("resident-2", currentApproachRevision, out _),
                "读取同一快照也必须撤销恢复前尚未完成的到岗请求。");
            Assert.That(journey.Advance(1000), Is.Zero);
            Assert.IsTrue(journey.TryAcquireDriver("resident-3", journey.DestinationRevision, out _));
            Assert.That(journey.Advance(1), Is.GreaterThan(0));
        }

        [Test]
        public void ArrivalConsumesOnlyActualDistance_AndCanReturnWithoutTeleporting()
        {
            using var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var outward);
            Assert.That(journey.Advance(1_000_000), Is.EqualTo(100_000_000L));
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.Arrived));
            Assert.IsFalse(outward.IsActive);
            Assert.That(journey.FuelPicoliters, Is.EqualTo(85_000_000_000L));
            journey.SetDestination(NomadJourneyEndpoint.Origin);
            Assert.That(journey.Advance(1000), Is.Zero);
            journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _);
            journey.Advance(1_000_000);
            Assert.That(journey.PositionMicrometers, Is.Zero);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.Arrived));
            Assert.That(journey.FuelPicoliters, Is.EqualTo(70_000_000_000L));
        }

        [Test]
        public void FuelLimitStopsBeforeOverdraw_AndRetainsUnspendableRemainder()
        {
            using var journey = new NomadJourneySession(Route(), 150_001L);
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var driver);
            Assert.That(journey.Advance(1000), Is.EqualTo(1000L));
            Assert.That(journey.FuelPicoliters, Is.EqualTo(1L));
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.FuelExhausted));
            Assert.IsFalse(driver.IsActive);
            Assert.IsFalse(journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _));
            Assert.That(journey.Advance(1000), Is.Zero);
        }

        [Test]
        public void RestoreRevokesOldDriver_ButInvalidRestoreAndOverflowAreAtomic()
        {
            using var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var old);
            journey.Advance(10);
            var saved = journey.Capture();
            Assert.Throws<ArgumentException>(() => journey.Restore(new NomadJourneySnapshot(Route(2000), 0, 1, saved.Destination)));
            Assert.Throws<ArgumentOutOfRangeException>(() => journey.Restore(new NomadJourneySnapshot(Route(), -1, 1, saved.Destination)));
            Assert.Throws<ArgumentOutOfRangeException>(() => journey.SetDestination((NomadJourneyEndpoint)99));
            Assert.Throws<OverflowException>(() => journey.Advance(long.MaxValue));
            Assert.That(journey.Capture(), Is.EqualTo(saved));
            Assert.IsTrue(old.IsActive);
            journey.Restore(saved);
            Assert.IsFalse(old.IsActive);
            journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out var current);
            old.Dispose();
            Assert.IsTrue(current.IsActive);
        }

        [Test]
        public void DisposalRevokesDriver_RejectsWritesAndRetainsDiagnosticState()
        {
            var journey = Journey();
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var driver);
            journey.Dispose();
            journey.Dispose();
            driver.Dispose();
            Assert.IsFalse(driver.IsActive);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.Disposed));
            Assert.That(journey.DriverId, Is.Empty);
            Assert.That(journey.Capture().PositionMicrometers, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() => journey.Advance(1));
            Assert.Throws<ObjectDisposedException>(() => journey.TryAcquireDriver("resident-2", journey.DestinationRevision, out _));
            Assert.Throws<ObjectDisposedException>(() => journey.Restore(journey.Capture()));
        }
    }
}
