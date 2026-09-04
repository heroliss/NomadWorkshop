using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    /// <summary>锁定真实库存、携带中所有权、容量背压与加工原子性的 Foundation 契约。</summary>
    public sealed class ResourceFlowLedgerTests
    {
        private const ulong Ada = 0xADA01UL;

        [Test]
        public void Haul_ReservesWholeContract_ThenMovesResourceThroughCarrier()
        {
            ResourceInventory source = Inventory("pantry", 4, NomadResourceIds.FoodIngredient, 2);
            var carrier = new ResourceInventory("ada-hands", 1);
            var destination = new ResourceInventory("kitchen-input", 2);
            var ledger = new ResourceFlowLedger();
            HaulTaskRequest request = Haul(
                "haul-food-01", source, carrier, destination, NomadResourceIds.FoodIngredient,
                "厨房缺少一份食材");

            Assert.IsTrue(ledger.TryReserveHaul(request, out HaulTaskLease lease, out ResourceFlowBlocker blocker));
            Assert.IsFalse(blocker.IsBlocked);
            Assert.AreEqual(1, ledger.GetAvailableAmount(source, NomadResourceIds.FoodIngredient));
            Assert.AreEqual(0, ledger.GetAvailableCapacity(carrier));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(destination));
            Assert.AreEqual(2, source.GetAmount(NomadResourceIds.FoodIngredient), "预留本身不能瞬移资源。 ");

            lease.PickUp();

            Assert.AreEqual(HaulTaskState.Carrying, lease.State);
            Assert.AreEqual(1, source.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, carrier.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(0, destination.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(2, Total(NomadResourceIds.FoodIngredient, source, carrier, destination));

            lease.Deliver();

            Assert.AreEqual(HaulTaskState.Delivered, lease.State);
            Assert.AreEqual(0, carrier.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, destination.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(2, Total(NomadResourceIds.FoodIngredient, source, carrier, destination));
        }

        [Test]
        public void Haul_DestinationFull_FailsWithoutConsumingSourceOrCarrierCapacity()
        {
            ResourceInventory source = Inventory("tank", 4, NomadResourceIds.Water, 2);
            var carrier = new ResourceInventory("ada-hands", 1);
            ResourceInventory destination = Inventory("kitchen-water", 1, NomadResourceIds.Water, 1);
            var ledger = new ResourceFlowLedger();

            bool reserved = ledger.TryReserveHaul(
                Haul("haul-water", source, carrier, destination, NomadResourceIds.Water, "补充厨房用水"),
                out HaulTaskLease lease,
                out ResourceFlowBlocker blocker);

            Assert.IsFalse(reserved);
            Assert.IsNull(lease);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, blocker.Reason);
            Assert.AreEqual("kitchen-water", blocker.InventoryId);
            Assert.AreEqual(2, ledger.GetAvailableAmount(source, NomadResourceIds.Water));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(carrier));
        }

        [Test]
        public void CompetingHauls_CannotOverbookSourceOrSharedDestinationCapacity()
        {
            ResourceInventory source = Inventory("tank", 8, NomadResourceIds.Water, 3);
            var firstCarrier = new ResourceInventory("ada-hands", 2);
            var secondCarrier = new ResourceInventory("bo-hands", 2);
            var destination = new ResourceInventory("kitchen-water", 3);
            var ledger = new ResourceFlowLedger();

            Assert.IsTrue(ledger.TryReserveHaul(
                Haul("first", source, firstCarrier, destination, NomadResourceIds.Water, "补水", Ada, 2),
                out HaulTaskLease first,
                out _));
            Assert.IsFalse(ledger.TryReserveHaul(
                Haul("overbook-source", source, secondCarrier, destination, NomadResourceIds.Water, "补水", 2),
                out _,
                out ResourceFlowBlocker sourceBlocker));
            Assert.AreEqual(ResourceFlowBlockReason.SourceInsufficient, sourceBlocker.Reason);

            ResourceInventory otherSource = Inventory("rain-tank", 8, NomadResourceIds.Water, 3);
            Assert.IsFalse(ledger.TryReserveHaul(
                Haul("overbook-destination", otherSource, secondCarrier, destination,
                    NomadResourceIds.Water, "补水", 2),
                out _,
                out ResourceFlowBlocker capacityBlocker));
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, capacityBlocker.Reason);
            first.Dispose();
        }

        [Test]
        public void CancelBeforePickup_ReleasesEverythingWithoutMutation()
        {
            ResourceInventory source = Inventory("pantry", 2, NomadResourceIds.FoodIngredient, 1);
            var carrier = new ResourceInventory("ada-hands", 1);
            var destination = new ResourceInventory("kitchen-input", 1);
            var ledger = new ResourceFlowLedger();
            Assert.IsTrue(ledger.TryReserveHaul(
                Haul("cancel", source, carrier, destination, NomadResourceIds.FoodIngredient, "补充食材"),
                out HaulTaskLease lease,
                out _));

            lease.Dispose();
            lease.Dispose();

            Assert.AreEqual(HaulTaskState.Cancelled, lease.State);
            Assert.IsFalse(lease.CargoRequiresRecovery);
            Assert.AreEqual(1, source.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, ledger.GetAvailableAmount(source, NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(carrier));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(destination));
        }

        [Test]
        public void CancelAfterPickup_LeavesRealCargoInCarrierForRecovery()
        {
            ResourceInventory source = Inventory("tank", 2, NomadResourceIds.Water, 1);
            var carrier = new ResourceInventory("ada-hands", 1);
            var destination = new ResourceInventory("kitchen-water", 1);
            var ledger = new ResourceFlowLedger();
            Assert.IsTrue(ledger.TryReserveHaul(
                Haul("interrupted", source, carrier, destination, NomadResourceIds.Water, "补充厨房用水"),
                out HaulTaskLease lease,
                out _));
            lease.PickUp();

            lease.Dispose();

            Assert.IsTrue(lease.CargoRequiresRecovery);
            Assert.AreEqual(1, carrier.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, source.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(1, ledger.GetAvailableAmount(carrier, NomadResourceIds.Water));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(destination));
        }

        [Test]
        public void InteractionConflict_FailsWithoutLeakingNumericReservations()
        {
            var interactions = new ReservationLedger();
            Assert.IsTrue(interactions.TryAcquire(99, new[] { "station:kitchen-input" }, out ReservationLease occupied));
            ResourceInventory source = Inventory("pantry", 2, NomadResourceIds.FoodIngredient, 1);
            var carrier = new ResourceInventory("ada-hands", 1);
            var destination = new ResourceInventory("kitchen-input", 1);
            var ledger = new ResourceFlowLedger(interactions);

            Assert.IsFalse(ledger.TryReserveHaul(
                Haul("blocked", source, carrier, destination, NomadResourceIds.FoodIngredient, "补充食材"),
                out _,
                out ResourceFlowBlocker blocker));

            Assert.AreEqual(ResourceFlowBlockReason.InteractionUnavailable, blocker.Reason);
            Assert.AreEqual(1, ledger.GetAvailableAmount(source, NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(carrier));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(destination));
            occupied.Dispose();
        }

        [Test]
        public void DuplicateStableTaskId_CannotBeClaimedTwiceEvenWithDifferentEndpoints()
        {
            ResourceInventory firstSource = Inventory("first-source", 2, NomadResourceIds.Water, 1);
            ResourceInventory secondSource = Inventory("second-source", 2, NomadResourceIds.Water, 1);
            var firstCarrier = new ResourceInventory("ada-hands", 1);
            var secondCarrier = new ResourceInventory("bo-hands", 1);
            var firstDestination = new ResourceInventory("first-destination", 1);
            var secondDestination = new ResourceInventory("second-destination", 1);
            var ledger = new ResourceFlowLedger();

            Assert.IsTrue(ledger.TryReserveHaul(
                Haul("same-stable-task", firstSource, firstCarrier, firstDestination,
                    NomadResourceIds.Water, "第一位居民领取", Ada),
                out HaulTaskLease first,
                out _));
            Assert.IsFalse(ledger.TryReserveHaul(
                Haul("same-stable-task", secondSource, secondCarrier, secondDestination,
                    NomadResourceIds.Water, "第二位居民重复领取", 0xB001UL),
                out _,
                out ResourceFlowBlocker blocker));

            Assert.AreEqual(ResourceFlowBlockReason.TaskAlreadyReserved, blocker.Reason);
            Assert.AreEqual(1, ledger.GetAvailableAmount(secondSource, NomadResourceIds.Water));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(secondCarrier));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(secondDestination));
            first.Dispose();
        }

        [Test]
        public void Process_ReservesThenAtomicallyConsumesAndProducesExplicitOutputs()
        {
            ResourceInventory foodInput = Inventory("kitchen-food", 2, NomadResourceIds.FoodIngredient, 1);
            ResourceInventory waterInput = Inventory("kitchen-water", 2, NomadResourceIds.Water, 1);
            var meals = new ResourceInventory("meal-shelf", 2);
            var waste = new ResourceInventory("waste-tank", 2);
            var ledger = new ResourceFlowLedger();
            ProcessTaskRequest request = MealProcess(foodInput, waterInput, meals, waste);

            Assert.IsTrue(ledger.TryReserveProcess(request, out ProcessTaskLease lease, out ResourceFlowBlocker blocker));
            Assert.IsFalse(blocker.IsBlocked);
            Assert.AreEqual(1, foodInput.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, waterInput.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, ledger.GetAvailableAmount(foodInput, NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(meals));

            lease.Commit();

            Assert.IsTrue(lease.IsCommitted);
            Assert.AreEqual(0, foodInput.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(0, waterInput.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(1, meals.GetAmount(NomadResourceIds.PreparedMeal));
            Assert.AreEqual(1, waste.GetAmount(NomadResourceIds.WasteWater));
        }

        [Test]
        public void Process_FullWasteTank_BlocksWholeRecipeWithoutConsumingInputs()
        {
            ResourceInventory foodInput = Inventory("kitchen-food", 2, NomadResourceIds.FoodIngredient, 1);
            ResourceInventory waterInput = Inventory("kitchen-water", 2, NomadResourceIds.Water, 1);
            var meals = new ResourceInventory("meal-shelf", 2);
            ResourceInventory waste = Inventory("waste-tank", 1, NomadResourceIds.WasteWater, 1);
            var ledger = new ResourceFlowLedger();

            Assert.IsFalse(ledger.TryReserveProcess(
                MealProcess(foodInput, waterInput, meals, waste),
                out ProcessTaskLease lease,
                out ResourceFlowBlocker blocker));

            Assert.IsNull(lease);
            Assert.AreEqual(ResourceFlowBlockReason.DestinationFull, blocker.Reason);
            Assert.AreEqual("waste-tank", blocker.InventoryId);
            Assert.AreEqual(1, foodInput.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, waterInput.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(0, meals.GetAmount(NomadResourceIds.PreparedMeal));
        }

        [Test]
        public void Process_CanReuseCapacityFreedByInputsInSameInventory()
        {
            var kitchen = new ResourceInventory(
                "kitchen",
                2,
                new ResourceQuantity(NomadResourceIds.FoodIngredient, 1),
                new ResourceQuantity(NomadResourceIds.Water, 1));
            var ledger = new ResourceFlowLedger();
            var request = new ProcessTaskRequest(
                "cook-in-place",
                Ada,
                "制作一份餐食并留下清洗污水",
                new[]
                {
                    new InventoryResourceQuantity(kitchen, NomadResourceIds.FoodIngredient, 1),
                    new InventoryResourceQuantity(kitchen, NomadResourceIds.Water, 1),
                },
                new[]
                {
                    new InventoryResourceQuantity(kitchen, NomadResourceIds.PreparedMeal, 1),
                    new InventoryResourceQuantity(kitchen, NomadResourceIds.WasteWater, 1),
                },
                new[] { "station:kitchen-work" });

            Assert.IsTrue(ledger.TryReserveProcess(request, out ProcessTaskLease lease, out _));
            lease.Commit();

            Assert.AreEqual(2, kitchen.TotalAmount);
            Assert.AreEqual(1, kitchen.GetAmount(NomadResourceIds.PreparedMeal));
            Assert.AreEqual(1, kitchen.GetAmount(NomadResourceIds.WasteWater));
        }

        [Test]
        public void ProcessCancellation_ReleasesInputsOutputsAndInteractionWithoutMutation()
        {
            ResourceInventory foodInput = Inventory("kitchen-food", 2, NomadResourceIds.FoodIngredient, 1);
            ResourceInventory waterInput = Inventory("kitchen-water", 2, NomadResourceIds.Water, 1);
            var meals = new ResourceInventory("meal-shelf", 1);
            var waste = new ResourceInventory("waste-tank", 1);
            var interactions = new ReservationLedger();
            var ledger = new ResourceFlowLedger(interactions);
            ProcessTaskRequest request = MealProcess(foodInput, waterInput, meals, waste);
            Assert.IsTrue(ledger.TryReserveProcess(request, out ProcessTaskLease lease, out _));

            lease.Dispose();
            lease.Dispose();

            Assert.IsFalse(lease.IsCommitted);
            Assert.AreEqual(1, foodInput.GetAmount(NomadResourceIds.FoodIngredient));
            Assert.AreEqual(1, waterInput.GetAmount(NomadResourceIds.Water));
            Assert.AreEqual(1, ledger.GetAvailableCapacity(meals));
            Assert.IsTrue(interactions.TryAcquire(Ada, new[] { "station:kitchen-work" }, out ReservationLease next));
            next.Dispose();
        }

        [Test]
        public void InvalidTaskContract_IsRejectedBeforeEnteringLedger()
        {
            var source = new ResourceInventory("source", 1);
            var carrier = new ResourceInventory("carrier", 1);

            Assert.Throws<ArgumentException>(() => new HaulTaskRequest(
                "invalid",
                Ada,
                source,
                carrier,
                source,
                NomadResourceIds.Water,
                1,
                "不能把目的地隐去",
                new[] { "station:any" }));
            Assert.Throws<ArgumentException>(() => new HaulTaskRequest(
                "invalid",
                Ada,
                source,
                carrier,
                new ResourceInventory("destination", 1),
                NomadResourceIds.Water,
                1,
                string.Empty,
                new[] { "station:any" }));
        }

        private static ResourceInventory Inventory(
            string id,
            int capacity,
            ResourceId resource,
            int amount)
            => new(id, capacity, new ResourceQuantity(resource, amount));

        private static HaulTaskRequest Haul(
            string id,
            ResourceInventory source,
            ResourceInventory carrier,
            ResourceInventory destination,
            ResourceId resource,
            string reason,
            int amount = 1)
            => Haul(id, source, carrier, destination, resource, reason, Ada, amount);

        private static HaulTaskRequest Haul(
            string id,
            ResourceInventory source,
            ResourceInventory carrier,
            ResourceInventory destination,
            ResourceId resource,
            string reason,
            ulong owner,
            int amount = 1)
            => new(
                id,
                owner,
                source,
                carrier,
                destination,
                resource,
                amount,
                reason,
                new[] { $"station:{source.Id}", $"station:{destination.Id}" });

        private static ProcessTaskRequest MealProcess(
            ResourceInventory foodInput,
            ResourceInventory waterInput,
            ResourceInventory meals,
            ResourceInventory waste)
            => new(
                "prepare-meal-01",
                Ada,
                "制作一份餐食并留下清洗污水",
                new[]
                {
                    new InventoryResourceQuantity(foodInput, NomadResourceIds.FoodIngredient, 1),
                    new InventoryResourceQuantity(waterInput, NomadResourceIds.Water, 1),
                },
                new[]
                {
                    new InventoryResourceQuantity(meals, NomadResourceIds.PreparedMeal, 1),
                    new InventoryResourceQuantity(waste, NomadResourceIds.WasteWater, 1),
                },
                new[] { "station:kitchen-work" });

        private static int Total(ResourceId resource, params ResourceInventory[] inventories)
        {
            int total = 0;
            for (int i = 0; i < inventories.Length; i++)
                total += inventories[i].GetAmount(resource);
            return total;
        }
    }
}
