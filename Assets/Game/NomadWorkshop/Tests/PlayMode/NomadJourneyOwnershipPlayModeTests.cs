using Game.Framework;
using Game.Framework.Context;
using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.PlayMode.Tests
{
    /// <summary>旅途规则消费真实 Context/Bag 生命周期；不替正式居民模拟到岗、导航或需求仲裁。</summary>
    public sealed class NomadJourneyOwnershipPlayModeTests
    {
        private static NomadJourneySession CreateJourney()
        {
            var journey = new NomadJourneySession(
                new NomadJourneyRoute("salt-road", "workshop", "well", 100_000, 1379, 150),
                100_000_000_000L);
            journey.SetDestination(NomadJourneyEndpoint.Destination);
            return journey;
        }

        [Test]
        public void OwnedResidentBag_ContextDisposalStopsDrivingWithoutDestroyingWorld()
        {
            using var journey = CreateJourney();
            using var builder = new ContainerBuilder();
            using var resident = new DisposableBag();
            builder.RegisterOwned(resident, typeof(DisposableBag));
            using var context = new GameContext(builder.Build(), inheritFromGlobal: false);
            var action = resident.CreateChild();
            Assert.IsTrue(journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var lease));
            action.Add(lease);
            journey.Advance(10);
            var before = journey.Capture();
            context.Dispose();
            Assert.IsTrue(resident.IsDisposed);
            Assert.IsTrue(action.IsDisposed);
            Assert.IsFalse(lease.IsActive);
            Assert.That(journey.Advance(1000), Is.Zero);
            Assert.That(journey.Capture(), Is.EqualTo(before));
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
        }

        [Test]
        public void OldActionBagDisposedAfterRestoreCannotReleaseNewDriversLease()
        {
            using var journey = CreateJourney();
            using var resident = new DisposableBag();
            var oldAction = resident.CreateChild();
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var old);
            oldAction.Add(old);
            journey.Advance(137);
            journey.Restore(journey.Capture());
            var currentAction = resident.CreateChild();
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var current);
            currentAction.Add(current);
            oldAction.Dispose();
            Assert.IsTrue(current.IsActive);
            Assert.That(journey.Advance(1), Is.GreaterThan(0));
            currentAction.Dispose();
            Assert.That(journey.Advance(1), Is.Zero);
        }

        [Test]
        public void LateLeaseAddedToDisposedActionIsReleasedImmediately()
        {
            using var journey = CreateJourney();
            using var action = new DisposableBag();
            action.Dispose();
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var late);
            action.Add(late);
            Assert.IsFalse(late.IsActive);
            Assert.That(journey.Advance(1000), Is.Zero);
            Assert.That(journey.Status, Is.EqualTo(NomadJourneyStatus.AwaitingDriver));
        }

        [Test]
        public void DelayedApproachAfterRestoreIsRejectedBeforeRegisteringAnyNewLease()
        {
            using var journey = CreateJourney();
            using var resident = new DisposableBag();
            var oldApproach = resident.CreateChild();
            long approachRevision = journey.DestinationRevision;
            journey.Restore(journey.Capture());
            Assert.IsFalse(journey.TryAcquireDriver("resident-1", approachRevision, out var stale));
            oldApproach.Add(stale);
            Assert.That(journey.Advance(1000), Is.Zero);
            var newApproach = resident.CreateChild();
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var current);
            newApproach.Add(current);
            oldApproach.Dispose();
            Assert.IsTrue(current.IsActive);
            Assert.That(journey.Advance(1), Is.GreaterThan(0));
        }

        [Test]
        public void CreateBagAssociatesCapabilities_CallerStillOwnsItsDisposal()
        {
            using var journey = CreateJourney();
            using var builder = new ContainerBuilder();
            using var context = new GameContext(builder.Build(), inheritFromGlobal: false);
            using var callerBag = context.CreateBag();
            journey.TryAcquireDriver("resident-1", journey.DestinationRevision, out var driver);
            callerBag.Add(driver);
            context.Dispose();
            Assert.IsFalse(callerBag.IsDisposed, "CreateBag 关联 Context，不会把自身隐式注册为 Context owned。");
            Assert.IsTrue(driver.IsActive);
            callerBag.Dispose();
            Assert.IsFalse(driver.IsActive);
            Assert.That(journey.Advance(1), Is.Zero);
        }
    }
}
