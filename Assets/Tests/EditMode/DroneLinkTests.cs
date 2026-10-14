using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Drone Pads link asteroids: researched, limited by range, slow by distance, quicker with tiers and engines, one
    /// trip at a time with more per trip from capacity research; plain belts stop at an asteroid's edge.
    /// </summary>
    public class DroneLinkTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;
        int size;

        [SetUp]
        public void SetUp()
        {
            t = new TestWorld();
            size = CatalogRepository.CellGridSize;
            t.Fortify(1, P);
            t.Research(P, "drone_logistics", 1);
            t.Player.currency = 5000;
        }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        /// <summary>A row with no nodes, blocked tiles or buildings near the border between cells 0 and 1.</summary>
        int ClearRow()
        {
            for (int y = 1; y < size - 1; y++)
            {
                bool clear = true;
                for (int gx = size - 4; gx <= size + 4 && clear; gx++)
                {
                    int cell, x, ly;
                    GridRules.TryGlobalToLocal(t.Map, gx, y, out cell, out x, out ly);
                    if (t.W.nodes.Exists(n => n.cellId == cell && n.x == x && n.y == ly)) clear = false;
                    if (t.W.buildings.Exists(b => b.cellId == cell && b.x == x && b.y == ly)) clear = false;
                    if (WorldLookup.GetCellDef(t.Map, cell).blockedTiles.Exists(b => b.x == x && b.y == ly)) clear = false;
                }
                if (clear) return y;
            }
            Assert.Fail("no clear row near the border");
            return -1;
        }

        CommandResult LinkCmd(BuildingState pad, int cell, int x, int y)
        {
            return t.Run(new GameCommand { kind = CommandKind.LinkDronePad, actor = P, buildingId = pad.id, toCellId = cell, toX = x, toY = y });
        }

        /// <summary>A pad on cell 0's east edge linked to a new pad on cell 1's west edge, on a clear row.</summary>
        BeltSegmentState Linked(out BuildingState from, out BuildingState to, out int row)
        {
            t.Catalog.rules.droneBaseRange = 1000;
            row = ClearRow();
            from = t.Build(P, "drone_pad", 0, size - 1, row);
            var r = LinkCmd(from, 1, 0, row);
            Assert.IsTrue(r.success, r.code + " " + r.message);
            to = WorldLookup.GetBuildingAt(t.W, 1, 0, row);
            Assert.IsNotNull(to, "a receiving pad is placed at the far end");
            return DroneLinks.Outgoing(t.W, from);
        }

        [Test]
        public void PadsNeedDroneLogisticsResearch()
        {
            t.Player.research.Clear();
            var r = t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "drone_pad", fromCellId = 0, fromX = size - 1, fromY = ClearRow() });
            Assert.AreEqual("LOCKED_BUILDING", r.code);
        }

        [Test]
        public void PlainBeltsStopAtTheAsteroidsEdge()
        {
            int row = ClearRow();
            var r = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = size - 1, fromY = row, toCellId = 1, toX = 0, toY = row });
            Assert.AreEqual("USE_DRONE_PAD", r.code);
        }

        [Test]
        public void LinksFlyTheirDistance()
        {
            BuildingState from, to;
            int row;
            var link = Linked(out from, out to, out row);
            float distance = MapLayout.Distance(t.Map, 0, size - 1, row, 1, 0, row);
            Assert.AreEqual((int)System.Math.Ceiling(distance * t.Catalog.rules.droneTicksPerUnit), link.linkTicks);
            Assert.AreEqual(link.linkTicks, BeltNetwork.TravelTicks(t.W, t.Catalog, link), "base tier flies at the base drone speed");
        }

        [Test]
        public void RangeLimitsLinksAndResearchExtendsIt()
        {
            int row = ClearRow();
            var pad = t.Build(P, "drone_pad", 0, size - 1, row);
            float distance = DroneLinks.FlatDistance(t.Map, pad, 1, 0, row);
            t.Catalog.rules.droneBaseRange = (int)distance - 1;
            Assert.AreEqual("OUT_OF_RANGE", LinkCmd(pad, 1, 0, row).code);
            t.Research(P, "drone_logistics", 2);   // +10 range
            var r = LinkCmd(pad, 1, 0, row);
            Assert.IsTrue(r.success, "range research brings it in reach: " + r.message);
        }

        [Test]
        public void LinksMustReachAnotherOwnedAsteroid()
        {
            t.Catalog.rules.droneBaseRange = 1000;
            int row = ClearRow();
            var pad = t.Build(P, "drone_pad", 0, size - 1, row);
            Assert.AreEqual("SAME_ASTEROID", LinkCmd(pad, 0, size - 3, row).code);
            Assert.AreEqual("NOT_OWNER", LinkCmd(pad, 2, 0, row).code, "cell 2 is not held");
        }

        [Test]
        public void EnginesAndTiersSpeedDronesUp()
        {
            BuildingState from, to;
            int row;
            var link = Linked(out from, out to, out row);
            int baseTicks = BeltNetwork.TravelTicks(t.W, t.Catalog, link);
            t.Research(P, "drone_engines", 1);   // +25%
            Assert.AreEqual((int)System.Math.Ceiling(link.linkTicks * 100.0 / 125), BeltNetwork.TravelTicks(t.W, t.Catalog, link));
            t.MustRun(new GameCommand { kind = CommandKind.UpgradeBelt, actor = P, beltId = link.id });
            Assert.Less(BeltNetwork.TravelTicks(t.W, t.Catalog, link), baseTicks);
        }

        ItemTokenState TokenOn(BeltSegmentState link)
        {
            var token = new ItemTokenState { id = WorldLookup.AllocateId(t.W), itemId = "iron_bar", routeSegmentIds = new List<int> { link.id }, segmentIndex = 0 };
            token.ticksUntilNextSegment = t.S.Belts.Board(t.W, t.Catalog, link);
            t.W.tokens.Add(token);
            return token;
        }

        [Test]
        public void ADroneCarriesOneTripAtATimeAndFliesHome()
        {
            BuildingState from, to;
            int row;
            var link = Linked(out from, out to, out row);
            var token = TokenOn(link);
            Assert.IsFalse(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "one item per trip without Cargo Drones");
            Assert.AreEqual(BeltNetwork.TravelTicks(t.W, t.Catalog, link), token.ticksUntilNextSegment, "no loading wait for a single item");
            t.W.tokens.Remove(token);
            BeltNetwork.DroneDelivered(t.W, t.Catalog, link);
            Assert.IsFalse(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "the drone is flying home");
            t.W.tick += BeltNetwork.TravelTicks(t.W, t.Catalog, link);
            Assert.IsTrue(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "back on its pad");
        }

        [Test]
        public void CargoDronesCarryMorePerTrip()
        {
            BuildingState from, to;
            int row;
            var link = Linked(out from, out to, out row);
            t.Research(P, "cargo_drones", 1);   // 2 per trip
            var first = TokenOn(link);
            Assert.IsTrue(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "a second item can board while the drone loads");
            var second = TokenOn(link);
            Assert.AreEqual(first.ticksUntilNextSegment, second.ticksUntilNextSegment, "they land together");
            Assert.IsFalse(t.S.Belts.HasCapacity(t.W, t.Catalog, link.id), "full");
            BeltNetwork.DroneDelivered(t.W, t.Catalog, link);
            Assert.IsFalse(BeltNetwork.DroneAway(t.W, link), "the drone waits for the whole load to be dropped off");
            BeltNetwork.DroneDelivered(t.W, t.Catalog, link);
            Assert.IsTrue(BeltNetwork.DroneAway(t.W, link), "then flies home");
        }

        [Test]
        public void GoodsCrossThroughPadsOneTripAtATime()
        {
            BuildingState from, to;
            int row;
            var link = Linked(out from, out to, out row);
            var store = t.Build(P, "storage", 0, size - 4, row);
            WorldLookup.AddItem(store.inventory, "iron_bar", 30);
            t.Build(P, "sale_depot", 1, 3, row);
            t.Line(P, 0, size - 4, row, 0, size - 3, row, 0, size - 2, row, 0, size - 1, row);
            t.Line(P, 1, 0, row, 1, 1, row, 1, 2, row, 1, 3, row);
            int roundTrip = BeltNetwork.TravelTicks(t.W, t.Catalog, link) * 2;
            int ticks = roundTrip * 4;
            var boarded = new HashSet<int>();
            for (int i = 0; i < ticks; i++)
            {
                t.FactoryTick();
                foreach (var token in t.W.tokens)
                    if (token.segmentIndex >= 0 && token.segmentIndex < token.routeSegmentIds.Count && token.routeSegmentIds[token.segmentIndex] == link.id)
                        boarded.Add(token.id);
            }
            Assert.Greater(boarded.Count, 1, "goods pass through the pads and across, trip after trip");
            Assert.LessOrEqual(boarded.Count, ticks / roundTrip + 1, "no more than one item per round trip");
        }

        [Test]
        public void RemovingAPadRemovesItsLinkAndUnlinkWorks()
        {
            BuildingState from, to;
            int row;
            Linked(out from, out to, out row);
            t.MustRun(new GameCommand { kind = CommandKind.LinkDronePad, actor = P, buildingId = from.id, contentId = "unlink" });
            Assert.IsNull(DroneLinks.Outgoing(t.W, from));
            Assert.IsTrue(LinkCmd(from, 1, 0, row).success, "relinks to the existing pad");
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = to.id });
            Assert.IsNull(DroneLinks.Outgoing(t.W, from), "dismantling the far pad cuts the link");
        }
    }
}
