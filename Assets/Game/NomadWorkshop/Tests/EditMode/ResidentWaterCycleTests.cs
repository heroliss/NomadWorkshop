using Game.NomadWorkshop.Simulation;
using NUnit.Framework;

namespace Game.NomadWorkshop.Tests
{
    public sealed class ResidentWaterCycleTests
    {
        private const ulong Ada = 0xADA01UL;

        [Test]
        public void DrinkCancellation_DoesNotConsumeWaterOrRelieveThirst()
        {
            var ledger = new ResourceFlowLedger();
            ResourceInventory cup = Inventory("drinking-station", 1, NomadResourceIds.Water, 1);
            ResidentWaterCycle cycle = Cycle(initialThirst: 0.8f);

            Assert.IsTrue(cycle.TryReserveDrink(
                ledger,
                cup,
                "ada:drink:1",
                "station:drinking",
                1,
                out ResidentWaterActionLease action,
                out ResourceFlowBlocker blocker));
            Assert.IsFalse(blocker.IsBlocked);

            action.Dispose();

            Assert.AreEqual(1, cup.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, cycle.BodyWater.TotalAmount);
            Assert.That(cycle.Thirst, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void DrinkCommit_AtomicallyMovesWaterAndRelievesThirst()
        {
            var ledger = new ResourceFlowLedger();
            ResourceInventory cup = Inventory("drinking-station", 1, NomadResourceIds.Water, 1);
            ResidentWaterCycle cycle = Cycle(initialThirst: 0.8f);

            Assert.IsTrue(cycle.TryReserveDrink(
                ledger,
                cup,
                "ada:drink:1",
                "station:drinking",
                1,
                out ResidentWaterActionLease action,
                out _));
            action.Commit();

            Assert.IsTrue(action.IsCommitted);
            Assert.AreEqual(0, cup.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(1, cycle.BodyWater.GetAmount(NomadResourceIds.Water));
            Assert.That(cycle.Thirst, Is.EqualTo(0.1f).Within(0.0001f));
        }

        [Test]
        public void Metabolism_IsDelayedAndPressureRisesBeforeDiscreteConversion()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = DrankOneUnit(ledger);

            ResidentWaterCycleTick firstHalf = cycle.Advance(2f, ledger);

            Assert.AreEqual(0, firstHalf.MetabolizedUnits);
            Assert.AreEqual(1, cycle.BodyWater.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, cycle.Bladder.TotalAmount);
            Assert.That(cycle.ExcretionPressure, Is.EqualTo(0.25f).Within(0.0001f));

            ResidentWaterCycleTick secondHalf = cycle.Advance(2f, ledger);

            Assert.AreEqual(1, secondHalf.MetabolizedUnits);
            Assert.AreEqual(0, cycle.BodyWater.TotalAmount);
            Assert.AreEqual(1, cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            Assert.That(cycle.ExcretionPressure, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void FullBladder_BlocksMetabolismAndKeepsBodyWater()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = Cycle(bladderCapacity: 1, bodyWaterCapacity: 2);
            Drink(cycle, ledger, Inventory("cup-1", 1, NomadResourceIds.Water, 1), "drink-1");
            Assert.AreEqual(1, cycle.Advance(4f, ledger).MetabolizedUnits);
            Drink(cycle, ledger, Inventory("cup-2", 1, NomadResourceIds.Water, 1), "drink-2");

            ResidentWaterCycleTick blocked = cycle.Advance(4f, ledger);

            Assert.AreEqual(0, blocked.MetabolizedUnits);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, blocked.Blocker.Reason);
            Assert.AreEqual("ada:bladder", blocked.Blocker.InventoryId);
            Assert.AreEqual(1, cycle.BodyWater.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(1, cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
        }

        [Test]
        public void FullToilet_BlocksUseWithoutReducingPressureOrContent()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = DrankAndMetabolizedOneUnit(ledger);
            ResourceInventory toilet = Inventory("toilet-holding", 1, NomadResourceIds.HumanWaste, 1);
            float pressure = cycle.ExcretionPressure;

            Assert.IsFalse(cycle.TryReserveToiletUse(
                ledger,
                toilet,
                "ada:toilet:1",
                "station:toilet",
                1,
                out ResidentWaterActionLease action,
                out ResourceFlowBlocker blocker));

            Assert.IsNull(action);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, blocker.Reason);
            Assert.AreEqual("toilet-holding", blocker.InventoryId);
            Assert.AreEqual(1, cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(1, toilet.GetAmount(NomadResourceIds.HumanWaste));
            Assert.That(cycle.ExcretionPressure, Is.EqualTo(pressure).Within(0.0001f));
        }

        [Test]
        public void ToiletThenPhysicalHaul_PreservesHumanWasteAcrossEveryInventory()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = DrankAndMetabolizedOneUnit(ledger);
            var toilet = new ResourceInventory("toilet-holding", 1);
            var hands = new ResourceInventory("ada-hands", 1);
            var vehicleWasteTank = new ResourceInventory("vehicle-waste-tank", 2);

            Assert.IsTrue(cycle.TryReserveToiletUse(
                ledger,
                toilet,
                "ada:toilet:1",
                "station:toilet",
                1,
                out ResidentWaterActionLease toiletAction,
                out _));
            toiletAction.Commit();

            Assert.AreEqual(0, cycle.Bladder.TotalAmount);
            Assert.AreEqual(1, toilet.GetAmount(NomadResourceIds.HumanWaste));
            Assert.That(cycle.ExcretionPressure, Is.EqualTo(0f).Within(0.0001f));

            var request = new HaulTaskRequest(
                "haul-toilet-canister-1",
                Ada,
                toilet,
                hands,
                vehicleWasteTank,
                NomadResourceIds.HumanWaste,
                1,
                "把厕所暂存桶清运到车辆废物罐",
                new[] { "station:toilet-canister", "station:vehicle-waste-tank" });
            Assert.IsTrue(ledger.TryReserveHaul(request, out HaulTaskLease haul, out _));
            Assert.AreEqual(1, TotalHumanWaste(cycle, toilet, hands, vehicleWasteTank));

            haul.PickUp();
            Assert.AreEqual(1, hands.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(1, TotalHumanWaste(cycle, toilet, hands, vehicleWasteTank));

            haul.Deliver();
            Assert.AreEqual(1, vehicleWasteTank.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(1, TotalHumanWaste(cycle, toilet, hands, vehicleWasteTank));
        }

        private static ResidentWaterCycle Cycle(
            float initialThirst = 0.8f,
            int bodyWaterCapacity = 2,
            int bladderCapacity = 2)
            => new(
                "ada",
                Ada,
                metabolismSecondsPerUnit: 4f,
                initialThirst: initialThirst,
                thirstIncreasePerSecond: 0f,
                thirstReliefPerUnit: 0.7f,
                bodyWaterCapacity: bodyWaterCapacity,
                bladderCapacity: bladderCapacity);

        private static ResidentWaterCycle DrankOneUnit(ResourceFlowLedger ledger)
        {
            ResidentWaterCycle cycle = Cycle();
            Drink(cycle, ledger, Inventory("cup", 1, NomadResourceIds.Water, 1), "drink");
            return cycle;
        }

        private static ResidentWaterCycle DrankAndMetabolizedOneUnit(ResourceFlowLedger ledger)
        {
            ResidentWaterCycle cycle = DrankOneUnit(ledger);
            Assert.AreEqual(1, cycle.Advance(4f, ledger).MetabolizedUnits);
            return cycle;
        }

        private static void Drink(
            ResidentWaterCycle cycle,
            ResourceFlowLedger ledger,
            ResourceInventory source,
            string taskId)
        {
            Assert.IsTrue(cycle.TryReserveDrink(
                ledger,
                source,
                taskId,
                "station:drinking",
                1,
                out ResidentWaterActionLease action,
                out _));
            action.Commit();
        }

        private static ResourceInventory Inventory(
            string id,
            int capacity,
            ResourceId resource,
            int amount)
            => new(id, capacity, new ResourceQuantity(resource, amount));

        private static int TotalHumanWaste(
            ResidentWaterCycle cycle,
            params ResourceInventory[] inventories)
        {
            int total = cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste);
            for (int i = 0; i < inventories.Length; i++)
                total += inventories[i].GetAmount(NomadResourceIds.HumanWaste);
            return total;
        }
    }
}
