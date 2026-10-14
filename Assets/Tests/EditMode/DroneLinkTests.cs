using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Belts between asteroids are drone links: slow by distance, quicker by tier, one item per round trip.</summary>
    public class DroneLinkTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        /// <summary>A row near the top of cells 0 and 1 with no nodes, blocked tiles or buildings across the border.</summary>
        int ClearRow()
        {
            int size = CatalogRepository.CellGridSize;
            for (int y = 1; y < size - 1; y++)
            {
                bool clear = true;
                for (int gx = size - 3; gx <= size + 3 && clear; gx++)
                {
                    int cell, x, ly;
                    GridRules.TryGlobalToLocal(t.Map, gx, y, out cell, out x, out ly);
                    if (t.W.nodes.Exists(n => n.cellId == cell && n.x == x && n.y == ly)) clear = false;
                    if (t.W.buildings.Exists(b => b.cellId == cell && b.x == x && b.y == ly)) clear = false;
                    if (WorldLookup.GetCellDef(t.Map, cell).blockedTiles.Exists(b => b.x == x && b.y == ly)) clear = false;
                }
                if (clear) return y;
            }
            Assert.Fail("no clear row across the border");
            return -1;
        }

        /// <summary>A row of belts from cell 0 across the border into cell 1; returns the link across the gap.</summary>
        BeltSegmentState CrossBorder()
        {
            t.Fortify(1, P);
            int size = CatalogRepository.CellGridSize;
            t.GlobalRow(P, ClearRow(), size - 2, size + 2);
            return t.W.belts.Find(b => b.fromCellId != b.toCellId);
        }

        [Test]
        public void LinksBetweenAsteroidsFlyTheirDistance()
        {
            var link = CrossBorder();
            Assert.IsNotNull(link);
            float distance = MapLayout.Distance(t.Map, link.fromCellId, link.fromX, link.fromY, link.toCellId, link.toX, link.toY);
            Assert.Greater(distance, MapLayout.Gap * 0.8f, "the asteroids are far apart");
            Assert.AreEqual((int)System.Math.Ceiling(distance * t.Catalog.rules.droneTicksPerUnit), link.linkTicks);
            Assert.AreEqual(link.linkTicks, BeltNetwork.TravelTicks(t.Catalog, link), "base tier flies at the base drone speed");

            var inside = t.W.belts.Find(b => b.fromCellId == b.toCellId);
            Assert.AreEqual(0, inside.linkTicks, "belts within an asteroid are ordinary conveyors");
            Assert.AreEqual(t.Catalog.rules.beltTiers[0].ticksPerSegment, BeltNetwork.TravelTicks(t.Catalog, inside));
        }

        [Test]
        public void UpgradingALinkSpeedsUpItsDrone()
        {
            var link = CrossBorder();
            int baseTicks = BeltNetwork.TravelTicks(t.Catalog, link);
            t.Player.currency = 1000;
            t.MustRun(new GameCommand { kind = CommandKind.UpgradeBelt, actor = P, beltId = link.id });
            t.MustRun(new GameCommand { kind = CommandKind.UpgradeBelt, actor = P, beltId = link.id });
            var tiers = t.Catalog.rules.beltTiers;
            int expressTicks = BeltNetwork.TravelTicks(t.Catalog, link);
            Assert.AreEqual((int)System.Math.Ceiling(baseTicks * (double)tiers[2].ticksPerSegment / tiers[0].ticksPerSegment), expressTicks);
            Assert.Less(expressTicks, baseTicks);
        }

        [Test]
        public void ALinkTakesNoItemUntilItsDroneIsHome()
        {
            var link = CrossBorder();
            Assert.IsTrue(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id));
            BeltNetwork.DroneDelivered(t.W, t.Catalog, link);
            Assert.IsFalse(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "the drone is flying home");
            t.W.tick += BeltNetwork.TravelTicks(t.Catalog, link) - 1;
            Assert.IsFalse(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id));
            t.W.tick += 1;
            Assert.IsTrue(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "back on its pad");
        }

        [Test]
        public void ItemsCrossOneDroneTripAtATime()
        {
            // A stocked storage at the edge of cell 0 supplies a sale depot just across the gap in cell 1.
            int size = CatalogRepository.CellGridSize;
            t.Fortify(1, P);
            int row = ClearRow();
            var store = t.Build(P, "storage", 0, size - 2, row);
            WorldLookup.AddItem(store.inventory, "iron_bar", 30);
            t.Build(P, "sale_depot", 1, 2, row);
            t.GlobalRow(P, row, size - 2, size + 2);
            var link = t.W.belts.Find(b => b.fromCellId != b.toCellId);
            int roundTrip = BeltNetwork.TravelTicks(t.Catalog, link) * 2;
            int ticks = roundTrip * 4;
            int before = WorldLookup.CountItem(store.inventory, "iron_bar");
            for (int i = 0; i < ticks; i++) t.FactoryTick();
            int shipped = before - WorldLookup.CountItem(store.inventory, "iron_bar");
            Assert.Greater(shipped, 0, "goods do cross");
            Assert.LessOrEqual(shipped, ticks / roundTrip + 2, "no more than one item per drone round trip (plus what is in flight)");
        }
    }
}
