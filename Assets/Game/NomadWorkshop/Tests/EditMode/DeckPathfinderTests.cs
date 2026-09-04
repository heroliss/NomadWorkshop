using NUnit.Framework;

namespace Game.NomadWorkshop.Simulation.Tests
{
    public sealed class DeckPathfinderTests
    {
        [Test]
        public void TryFindPath_StraightRouteIncludesStartAndGoal()
        {
            var grid = new FacilityPlacementGrid(0, 0, 5, 3);

            bool found = DeckPathfinder.TryFindPath(
                grid,
                new DeckCell(0, 1),
                new DeckCell(3, 1),
                out DeckPath path);

            Assert.That(found, Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    new DeckCell(0, 1),
                    new DeckCell(1, 1),
                    new DeckCell(2, 1),
                    new DeckCell(3, 1),
                },
                path.Cells);
        }

        [Test]
        public void TryFindPath_RoutesAroundFacilityOccupiedCell()
        {
            var grid = new FacilityPlacementGrid(0, 0, 4, 3);
            Assert.That(
                grid.TryPlace(
                    SingleCellObstacle("obstacle", new DeckCell(1, 0)),
                    out _,
                    out _),
                Is.True);

            bool found = DeckPathfinder.TryFindPath(
                grid,
                new DeckCell(0, 0),
                new DeckCell(2, 0),
                out DeckPath path);

            Assert.That(found, Is.True);
            CollectionAssert.AreEqual(
                new[]
                {
                    new DeckCell(0, 0),
                    new DeckCell(0, 1),
                    new DeckCell(1, 1),
                    new DeckCell(2, 1),
                    new DeckCell(2, 0),
                },
                path.Cells);
        }

        [Test]
        public void TryFindPath_PreviewWallCanDisconnectWithoutMutatingGrid()
        {
            var grid = new FacilityPlacementGrid(0, 0, 3, 3);
            var previewWall = new[]
            {
                new DeckCell(1, 0),
                new DeckCell(1, 1),
                new DeckCell(1, 2),
            };

            bool foundBeforePreview = DeckPathfinder.TryFindPath(
                grid,
                new DeckCell(0, 1),
                new DeckCell(2, 1),
                out _);
            bool foundWithPreview = DeckPathfinder.TryFindPath(
                grid,
                new DeckCell(0, 1),
                new DeckCell(2, 1),
                previewWall,
                out DeckPath blockedPath);

            Assert.That(foundBeforePreview, Is.True);
            Assert.That(foundWithPreview, Is.False);
            Assert.That(blockedPath, Is.Null);
            Assert.That(grid.Count, Is.Zero, "预览查询不得把临时障碍提交到摆放真值。 ");
        }

        [Test]
        public void TryFindPath_OccupiedGoalIsNotReachable()
        {
            var grid = new FacilityPlacementGrid(0, 0, 3, 3);
            Assert.That(
                grid.TryPlace(
                    SingleCellObstacle("goal", new DeckCell(2, 1)),
                    out _,
                    out _),
                Is.True);

            Assert.That(
                DeckPathfinder.TryFindPath(
                    grid,
                    new DeckCell(0, 1),
                    new DeckCell(2, 1),
                    out DeckPath path),
                Is.False);
            Assert.That(path, Is.Null);
        }

        [Test]
        public void TryFindPath_EqualInputsProduceSameTieBreakRoute()
        {
            var grid = new FacilityPlacementGrid(-2, -2, 5, 5);
            Assert.That(
                grid.TryPlace(
                    SingleCellObstacle("center", new DeckCell(0, 0)),
                    out _,
                    out _),
                Is.True);

            Assert.That(
                DeckPathfinder.TryFindPath(
                    grid,
                    new DeckCell(-1, 0),
                    new DeckCell(1, 0),
                    out DeckPath first),
                Is.True);
            Assert.That(
                DeckPathfinder.TryFindPath(
                    grid,
                    new DeckCell(-1, 0),
                    new DeckCell(1, 0),
                    out DeckPath second),
                Is.True);

            CollectionAssert.AreEqual(first.Cells, second.Cells);
        }

        private static FacilityPlacementRequest SingleCellObstacle(
            string instanceId,
            DeckCell pivot) =>
            new(
                instanceId,
                "obstacle",
                pivot,
                FacilityQuarterTurn.North,
                new FacilityFootprint(
                    new[] { new DeckCell(0, 0) },
                    new[] { new DeckCell(0, 1) }));
    }
}
