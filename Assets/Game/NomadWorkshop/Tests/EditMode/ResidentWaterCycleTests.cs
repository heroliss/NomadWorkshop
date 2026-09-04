using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定 mL 水循环的连续代谢、容量背压、取消语义与全程物质守恒。</summary>
    public sealed class ResidentWaterCycleTests
    {
        private const int ServingMilliliters =
            ResidentWaterCycle.DefaultDrinkServingMilliliters;

        [Test]
        public void DrinkCancellation_DoesNotConsumeWaterOrRelieveThirst()
        {
            var ledger = new ResourceFlowLedger();
            ResourceInventory cup = Inventory(
                "drinking-station",
                ServingMilliliters,
                NomadResourceIds.Water,
                ServingMilliliters);
            ResidentWaterCycle cycle = Cycle(initialThirst: 0.8f);

            Assert.IsTrue(cycle.TryReserveDrink(
                ledger,
                cup,
                "drink-1",
                "station:drink",
                ServingMilliliters,
                out ResidentWaterActionLease action,
                out _));

            action.Dispose();

            Assert.AreEqual(ServingMilliliters, cup.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, cycle.BodyWater.TotalAmount);
            Assert.AreEqual(0.8f, cycle.Thirst, 0.0001f);
        }

        [Test]
        public void DrinkCommit_AtomicallyMovesMillilitersAndRelievesThirst()
        {
            var ledger = new ResourceFlowLedger();
            ResourceInventory cup = Inventory(
                "drinking-station",
                ServingMilliliters,
                NomadResourceIds.Water,
                ServingMilliliters);
            ResidentWaterCycle cycle = Cycle(initialThirst: 0.8f);

            Assert.IsTrue(cycle.TryReserveDrink(
                ledger,
                cup,
                "drink-1",
                "station:drink",
                ServingMilliliters,
                out ResidentWaterActionLease action,
                out _));
            action.Commit();

            Assert.AreEqual(0, cup.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(
                ServingMilliliters,
                cycle.BodyWater.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0.1f, cycle.Thirst, 0.0001f);
        }

        [Test]
        public void Advance_ConvertsMillilitersContinuouslyAndPreservesSplitStepResult()
        {
            var splitLedger = new ResourceFlowLedger();
            ResidentWaterCycle split = DrankOneServing(splitLedger);
            var combinedLedger = new ResourceFlowLedger();
            ResidentWaterCycle combined = DrankOneServing(combinedLedger);

            ResidentWaterCycleTick firstHalf = split.Advance(2f, splitLedger);
            ResidentWaterCycleTick secondHalf = split.Advance(2f, splitLedger);
            ResidentWaterCycleTick whole = combined.Advance(4f, combinedLedger);

            Assert.AreEqual(150, firstHalf.MetabolizedMilliliters);
            Assert.AreEqual(150, secondHalf.MetabolizedMilliliters);
            Assert.AreEqual(ServingMilliliters, whole.MetabolizedMilliliters);
            Assert.AreEqual(0, split.BodyWater.TotalAmount);
            Assert.AreEqual(
                ServingMilliliters,
                split.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(split.BodyWater.TotalAmount, combined.BodyWater.TotalAmount);
            Assert.AreEqual(split.Bladder.TotalAmount, combined.Bladder.TotalAmount);
        }

        [Test]
        public void FullBladder_BlocksMetabolismAndKeepsBodyWater()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = Cycle(
                bodyWaterCapacityMilliliters: ServingMilliliters * 2,
                bladderCapacityMilliliters: ServingMilliliters);
            Drink(
                cycle,
                ledger,
                Inventory("cup-1", ServingMilliliters, NomadResourceIds.Water, ServingMilliliters),
                "drink-1");
            Assert.AreEqual(
                ServingMilliliters,
                cycle.Advance(4f, ledger).MetabolizedMilliliters);
            Drink(
                cycle,
                ledger,
                Inventory("cup-2", ServingMilliliters, NomadResourceIds.Water, ServingMilliliters),
                "drink-2");

            ResidentWaterCycleTick blocked = cycle.Advance(4f, ledger);

            Assert.AreEqual(0, blocked.MetabolizedMilliliters);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, blocked.Blocker.Reason);
            Assert.AreEqual(cycle.Bladder.Id, blocked.Blocker.InventoryId);
            Assert.AreEqual(
                ServingMilliliters,
                cycle.BodyWater.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(
                ServingMilliliters,
                cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(1f, cycle.ExcretionPressure, 0.0001f);
        }

        [Test]
        public void Checkpoint_RestoresSubMilliliterMetabolismAndFollowingTransfer()
        {
            var originalLedger = new ResourceFlowLedger();
            ResidentWaterCycle original = DrankOneServing(originalLedger);
            Assert.AreEqual(0, original.Advance(0.005f, originalLedger).MetabolizedMilliliters);

            ResidentWaterCycleCheckpoint checkpoint = original.CaptureCheckpoint();
            Assert.AreEqual(375_000L, checkpoint.PendingMetabolismNanoliters);

            var restoredLedger = new ResourceFlowLedger();
            var restored = new ResidentWaterCycle(
                "ada",
                0xADA01UL,
                metabolismMillilitersPerSecond: 75f,
                drinkServingMilliliters: ServingMilliliters,
                initialThirst: 0f,
                thirstIncreasePerSecond: 0f,
                thirstReliefPerServing: 0.7f,
                checkpoint: checkpoint);

            ResidentWaterCycleTick originalNext = original.Advance(0.01f, originalLedger);
            ResidentWaterCycleTick restoredNext = restored.Advance(0.01f, restoredLedger);

            Assert.AreEqual(1, originalNext.MetabolizedMilliliters);
            Assert.AreEqual(originalNext.MetabolizedMilliliters, restoredNext.MetabolizedMilliliters);
            Assert.AreEqual(original.BodyWater.TotalAmount, restored.BodyWater.TotalAmount);
            Assert.AreEqual(original.Bladder.TotalAmount, restored.Bladder.TotalAmount);
            Assert.AreEqual(
                original.CaptureCheckpoint().PendingMetabolismNanoliters,
                restored.CaptureCheckpoint().PendingMetabolismNanoliters);
        }

        [Test]
        public void ToiletCancellation_KeepsBladderAndToiletUnchanged()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = DrankAndMetabolizedOneServing(ledger);
            ResourceInventory toilet = Inventory(
                "toilet-holding",
                ServingMilliliters,
                NomadResourceIds.HumanWaste,
                ServingMilliliters);

            Assert.IsFalse(cycle.TryReserveToiletUse(
                ledger,
                toilet,
                "toilet-full",
                "station:toilet",
                ServingMilliliters,
                out _,
                out ResourceFlowBlocker full));
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, full.Reason);

            toilet = new ResourceInventory(
                "toilet-holding-empty",
                ResourceMeasure.Milliliter,
                ServingMilliliters);
            Assert.IsTrue(cycle.TryReserveToiletUse(
                ledger,
                toilet,
                "toilet-cancel",
                "station:toilet",
                ServingMilliliters,
                out ResidentWaterActionLease action,
                out _));
            action.Dispose();

            Assert.AreEqual(
                ServingMilliliters,
                cycle.Bladder.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(0, toilet.GetAmount(NomadResourceIds.HumanWaste));
        }

        [Test]
        public void ToiletThenPhysicalHaul_PreservesHumanWasteAcrossEveryInventory()
        {
            var ledger = new ResourceFlowLedger();
            ResidentWaterCycle cycle = DrankAndMetabolizedOneServing(ledger);
            var toilet = new ResourceInventory(
                "toilet-holding",
                ResourceMeasure.Milliliter,
                ServingMilliliters);
            var liquidCarrier = new ResourceInventory(
                "ada-liquid-container",
                ResourceMeasure.Milliliter,
                ServingMilliliters);
            var vehicleWasteTank = new ResourceInventory(
                "vehicle-waste-tank",
                ResourceMeasure.Milliliter,
                ServingMilliliters * 2);

            Assert.IsTrue(cycle.TryReserveToiletUse(
                ledger,
                toilet,
                "use-toilet",
                "station:toilet",
                ServingMilliliters,
                out ResidentWaterActionLease toiletAction,
                out _));
            toiletAction.Commit();

            Assert.AreEqual(0, cycle.Bladder.TotalAmount);
            Assert.AreEqual(
                ServingMilliliters,
                toilet.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(
                ServingMilliliters,
                TotalHumanWaste(cycle, toilet, liquidCarrier, vehicleWasteTank));

            var request = new HaulTaskRequest(
                "empty-toilet",
                0xADA01UL,
                toilet,
                liquidCarrier,
                vehicleWasteTank,
                NomadResourceIds.HumanWaste,
                ServingMilliliters,
                "把厕所内容物实体清运到车辆废物罐",
                new[] { "station:toilet", "station:waste-tank" });
            Assert.IsTrue(ledger.TryReserveHaul(request, out HaulTaskLease haul, out _));
            haul.PickUp();

            Assert.AreEqual(0, toilet.TotalAmount);
            Assert.AreEqual(
                ServingMilliliters,
                liquidCarrier.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(
                ServingMilliliters,
                TotalHumanWaste(cycle, toilet, liquidCarrier, vehicleWasteTank));

            haul.Deliver();

            Assert.AreEqual(0, liquidCarrier.TotalAmount);
            Assert.AreEqual(
                ServingMilliliters,
                vehicleWasteTank.GetAmount(NomadResourceIds.HumanWaste));
            Assert.AreEqual(
                ServingMilliliters,
                TotalHumanWaste(cycle, toilet, liquidCarrier, vehicleWasteTank));
        }

        private static ResidentWaterCycle Cycle(
            float initialThirst = 0.8f,
            int bodyWaterCapacityMilliliters =
                ResidentWaterCycle.DefaultBodyWaterCapacityMilliliters,
            int bladderCapacityMilliliters =
                ResidentWaterCycle.DefaultBladderCapacityMilliliters)
            => new(
                "ada",
                0xADA01UL,
                metabolismMillilitersPerSecond: 75f,
                drinkServingMilliliters: ServingMilliliters,
                initialThirst: initialThirst,
                thirstIncreasePerSecond: 0f,
                thirstReliefPerServing: 0.7f,
                bodyWaterCapacityMilliliters: bodyWaterCapacityMilliliters,
                bladderCapacityMilliliters: bladderCapacityMilliliters);

        private static ResidentWaterCycle DrankOneServing(ResourceFlowLedger ledger)
        {
            ResidentWaterCycle cycle = Cycle();
            Drink(
                cycle,
                ledger,
                Inventory("cup", ServingMilliliters, NomadResourceIds.Water, ServingMilliliters),
                "drink");
            return cycle;
        }

        private static ResidentWaterCycle DrankAndMetabolizedOneServing(
            ResourceFlowLedger ledger)
        {
            ResidentWaterCycle cycle = DrankOneServing(ledger);
            Assert.AreEqual(
                ServingMilliliters,
                cycle.Advance(4f, ledger).MetabolizedMilliliters);
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
                $"station:{taskId}",
                ServingMilliliters,
                out ResidentWaterActionLease action,
                out _));
            action.Commit();
        }

        private static ResourceInventory Inventory(
            string id,
            int capacityMilliliters,
            ResourceId resource,
            int amountMilliliters)
            => new(
                id,
                ResourceMeasure.Milliliter,
                capacityMilliliters,
                new ResourceQuantity(resource, amountMilliliters));

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
