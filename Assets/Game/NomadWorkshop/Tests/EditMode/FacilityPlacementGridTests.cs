using System;
using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class FacilityPlacementGridTests
    {
        private static readonly FacilityFootprint TwoByOneWithFrontAccess = new(
            new[]
            {
                new DeckCell(0, 0),
                new DeckCell(1, 0),
            },
            new[]
            {
                new DeckCell(0, -1),
                new DeckCell(1, -1),
            });

        [Test]
        public void Rotate_EastMatchesUnityClockwiseQuarterTurn()
        {
            Assert.That(
                FacilityPlacementGrid.Rotate(new DeckCell(2, 1), FacilityQuarterTurn.East),
                Is.EqualTo(new DeckCell(1, -2)));
        }

        [Test]
        public void TryPlace_RotatesFootprintAndInteractionCellsAroundPivot()
        {
            var grid = new FacilityPlacementGrid(-4, -4, 9, 9);

            bool placed = grid.TryPlace(
                Request("kitchen", new DeckCell(1, 2), FacilityQuarterTurn.East),
                out PlacedFacility facility,
                out FacilityPlacementFailure failure);

            Assert.That(placed, Is.True);
            Assert.That(failure, Is.EqualTo(FacilityPlacementFailure.None));
            Assert.That(facility.OccupiedCells, Is.EquivalentTo(new[]
            {
                new DeckCell(1, 2),
                new DeckCell(1, 1),
            }));
            Assert.That(facility.InteractionCells, Is.EquivalentTo(new[]
            {
                new DeckCell(0, 2),
                new DeckCell(0, 1),
            }));
        }

        [Test]
        public void TryPlace_OutOfBoundsDoesNotPartiallyOccupyGrid()
        {
            var grid = new FacilityPlacementGrid(0, 0, 2, 2);

            bool placed = grid.TryPlace(
                Request("edge", new DeckCell(1, 1)),
                out PlacedFacility facility,
                out FacilityPlacementFailure failure);

            Assert.That(placed, Is.False);
            Assert.That(facility, Is.Null);
            Assert.That(failure, Is.EqualTo(FacilityPlacementFailure.FootprintOutOfBounds));
            Assert.That(grid.Count, Is.Zero);
            Assert.That(grid.IsOccupied(new DeckCell(1, 1)), Is.False);
        }

        [Test]
        public void TryPlace_RejectsOverlapAndDuplicateStableIdentity()
        {
            var grid = new FacilityPlacementGrid(-4, -4, 9, 9);
            Assert.That(grid.TryPlace(Request("first", new DeckCell(0, 0)), out _, out _), Is.True);

            Assert.That(
                grid.TryPlace(Request("second", new DeckCell(1, 0)), out _, out var overlapFailure),
                Is.False);
            Assert.That(overlapFailure, Is.EqualTo(FacilityPlacementFailure.FootprintOccupied));

            Assert.That(
                grid.TryPlace(Request("first", new DeckCell(3, 3)), out _, out var identityFailure),
                Is.False);
            Assert.That(identityFailure, Is.EqualTo(FacilityPlacementFailure.DuplicateInstanceId));
        }

        [Test]
        public void TryPlace_ProtectsExistingInteractionCellsButAllowsSharedAisle()
        {
            var grid = new FacilityPlacementGrid(-5, -5, 11, 11);
            Assert.That(grid.TryPlace(Request("first", new DeckCell(0, 0)), out _, out _), Is.True);

            var singleCell = new FacilityFootprint(
                new[] { new DeckCell(0, 0) },
                new[] { new DeckCell(0, -1) });
            var blocking = new FacilityPlacementRequest(
                "blocking",
                "crate",
                new DeckCell(0, -1),
                FacilityQuarterTurn.North,
                singleCell);
            Assert.That(grid.TryPlace(blocking, out _, out var blockFailure), Is.False);
            Assert.That(blockFailure, Is.EqualTo(FacilityPlacementFailure.BlocksExistingInteraction));

            var sharedAisle = new FacilityPlacementRequest(
                "shared",
                "opposite-counter",
                new DeckCell(0, -2),
                FacilityQuarterTurn.North,
                new FacilityFootprint(
                    new[] { new DeckCell(0, 0) },
                    new[] { new DeckCell(0, 1) }));
            Assert.That(grid.TryPlace(sharedAisle, out _, out var sharedFailure), Is.True);
            Assert.That(sharedFailure, Is.EqualTo(FacilityPlacementFailure.None));
            Assert.That(grid.IsInteractionProtected(new DeckCell(0, -1)), Is.True);
        }

        [Test]
        public void Remove_ReleasesFootprintAndItsInteractionProtection()
        {
            var grid = new FacilityPlacementGrid(-4, -4, 9, 9);
            Assert.That(grid.TryPlace(Request("kitchen", new DeckCell(0, 0)), out _, out _), Is.True);

            Assert.That(grid.Remove("kitchen"), Is.True);
            Assert.That(grid.Count, Is.Zero);
            Assert.That(grid.IsOccupied(new DeckCell(0, 0)), Is.False);
            Assert.That(grid.IsInteractionProtected(new DeckCell(0, -1)), Is.False);
            Assert.That(grid.Remove("kitchen"), Is.False);
        }

        [Test]
        public void FacilityFootprint_RejectsDuplicateOrOverlappingDefinitionCells()
        {
            Assert.Throws<ArgumentException>(() => new FacilityFootprint(
                new[] { new DeckCell(0, 0), new DeckCell(0, 0) },
                new[] { new DeckCell(0, -1) }));
            Assert.Throws<ArgumentException>(() => new FacilityFootprint(
                new[] { new DeckCell(0, 0) },
                new[] { new DeckCell(0, 0) }));
        }

        private static FacilityPlacementRequest Request(
            string instanceId,
            DeckCell pivot,
            FacilityQuarterTurn rotation = FacilityQuarterTurn.North) =>
            new(instanceId, "field-kitchen", pivot, rotation, TwoByOneWithFrontAccess);
    }
}
