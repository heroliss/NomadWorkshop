using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class InteractionSpaceTopologyTests
    {
        [Test]
        public void AlternativeSlotsOfSameGroup_ShareCapacityWithoutConflictWarning()
        {
            InteractionSpaceSlot left = Slot("facility-a", "cook", "left", 0, 0);
            InteractionSpaceSlot right = Slot("facility-a", "cook", "right", 400, 0);
            var topology = new InteractionSpaceTopology(650);

            topology.Rebuild(new[] { left, right });

            Assert.That(topology.Spaces.Count, Is.EqualTo(1));
            Assert.That(topology.Spaces[0].IsShared, Is.True);
            Assert.That(topology.Spaces[0].SpansMultipleGroups, Is.False);
            Assert.That(topology.HasCrossGroupConflict(left.Address), Is.False);
        }

        [Test]
        public void NearbySlotsFromDifferentFunctions_ShareOneAtomicReservationSpace()
        {
            InteractionSpaceSlot tap = Slot("station-a", "drink", "center", 0, 0);
            InteractionSpaceSlot cupboard = Slot("kitchen-a", "storage", "center", 600, 0);
            var topology = new InteractionSpaceTopology(650);
            topology.Rebuild(new[] { tap, cupboard });
            Assert.That(topology.TryGetSpace(tap.Address, out InteractionSpace shared), Is.True);
            Assert.That(shared.SpansMultipleGroups, Is.True);
            Assert.That(topology.HasCrossGroupConflict(cupboard.Address), Is.True);

            var reservations = new ReservationLedger();
            Assert.That(
                reservations.TryAcquire(1UL, shared.ReservationKeys, out ReservationLease first),
                Is.True);
            Assert.That(topology.TryGetSpace(cupboard.Address, out InteractionSpace same), Is.True);
            Assert.That(
                reservations.TryAcquire(2UL, same.ReservationKeys, out ReservationLease blocked),
                Is.False,
                "从任一停靠位进入都必须占满同一个共享空间。 ");
            Assert.That(blocked, Is.Null);

            first.Dispose();
            Assert.That(
                reservations.TryAcquire(2UL, same.ReservationKeys, out ReservationLease second),
                Is.True,
                "空间释放后，所有成员停靠位应同时恢复可用。 ");
            second.Dispose();
        }

        [Test]
        public void ProximityClustering_IsTransitiveAndDeterministic()
        {
            InteractionSpaceSlot a = Slot("a", "g", "s", 0, 0);
            InteractionSpaceSlot b = Slot("b", "g", "s", 500, 0);
            InteractionSpaceSlot c = Slot("c", "g", "s", 1000, 0);
            var topology = new InteractionSpaceTopology(600);

            topology.Rebuild(new[] { c, a, b });

            Assert.That(topology.Spaces.Count, Is.EqualTo(1));
            Assert.That(topology.Spaces[0].Members.Count, Is.EqualTo(3));
            Assert.That(topology.Spaces[0].Members[0].Address.FacilityInstanceId, Is.EqualTo("a"));
        }

        [Test]
        public void SlotsOnDifferentDeckLevels_DoNotShareSpace()
        {
            InteractionSpaceSlot lower = Slot("a", "g", "s", 0, 0, 0);
            InteractionSpaceSlot upper = Slot("b", "g", "s", 0, 0, 1);
            var topology = new InteractionSpaceTopology(650);

            topology.Rebuild(new[] { lower, upper });

            Assert.That(topology.Spaces.Count, Is.EqualTo(2));
        }

        private static InteractionSpaceSlot Slot(
            string facility,
            string group,
            string slot,
            int x,
            int z,
            int deckLevel = 0) =>
            new(
                new InteractionSlotAddress(facility, group, slot),
                new DeckPose(x, z, 0, deckLevel));
    }
}
