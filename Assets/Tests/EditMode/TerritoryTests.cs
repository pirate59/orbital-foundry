using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>HQ buildings, Forts, and keeping or losing claimed cells.</summary>
    public class TerritoryTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        BuildingState Find(int cellId, string type)
        {
            return t.W.buildings.Find(b => b.cellId == cellId && b.typeId == type);
        }

        void Tick(int n) { for (int i = 0; i < n; i++) t.Loop.TickNormal(); }

        [Test]
        public void HeadquartersAndStartingFortsArePlaced()
        {
            Assert.AreEqual(P, Find(0, "headquarters").owner);
            Assert.AreEqual(FactionId.Ai, Find(7, "headquarters").owner);
            foreach (var cell in t.W.cells)
                if (cell.headquartersFor == FactionId.Neutral)
                {
                    Assert.AreEqual(FactionId.Neutral, cell.owner, "each side starts on its headquarters cell alone");
                    Assert.IsNull(Find(cell.id, "fort"), "neutral cells start empty");
                }
            // The headquarters sits as near the asteroid's centre as its whole 3x3 allows (clear of the ice at 11,11).
            var hq = Find(0, "headquarters");
            Assert.LessOrEqual(System.Math.Abs(hq.x - 10) + System.Math.Abs(hq.y - 10), 1);
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    Assert.IsNull(WorldLookup.GetNodeAt(t.W, 0, hq.x + dx, hq.y + dy), "its footprint is clear of deposits");
        }

        [Test]
        public void HeadquartersCannotBeBuiltOrDismantledAndHqCellsNeedNoFort()
        {
            Assert.AreEqual("NOT_BUILDABLE", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "headquarters", fromCellId = 0, fromX = 9, fromY = 9 }).code);
            Assert.AreEqual("NOT_ALLOWED", t.Run(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = Find(0, "headquarters").id }).code);
            Assert.AreEqual("NOT_BUILDABLE", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "fort", fromCellId = 0, fromX = 9, fromY = 9 }).code);
            Assert.IsTrue(WorldLookup.GetCell(t.W, 0).factoryOnline, "the HQ switches its own cell's factory on");
        }

        void ClaimCell1WithGuard(out CraftState guard)
        {
            guard = WorldLookup.CraftsInCell(t.W, 0, P)[0];
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 1 });
            for (int i = 0; i < 400 && WorldLookup.GetCell(t.W, 1).owner != P; i++) t.Loop.TickNormal();
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 1).owner);
        }

        [Test]
        public void StationedUnitHoldsACellAndLeavingStartsTheCountdown()
        {
            CraftState guard;
            ClaimCell1WithGuard(out guard);
            Tick(t.Catalog.rules.abandonTicks + 50);
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 1).owner, "a stationed unit keeps the cell");
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 1).unheldSinceTick);

            Assert.AreEqual("NEEDS_FORT", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "extractor", fromCellId = 1, fromX = 2, fromY = 3 }).code,
                "a unit holds the cell but only a Fort switches its factory on");
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 0 });
            for (int i = 0; i < 200 && guard.cellId != 0; i++) t.Loop.TickNormal();
            var cell = WorldLookup.GetCell(t.W, 1);
            Assert.AreNotEqual(-1, cell.unheldSinceTick, "unguarded once the unit leaves");
            Assert.IsTrue(t.W.alerts.Exists(a => a.kind == AlertKind.Territory && a.cellId == 1), "the player is warned");

            Tick(t.Catalog.rules.abandonTicks);
            Assert.AreEqual(FactionId.Neutral, cell.owner, "abandoned after the countdown");
        }

        /// <summary>A construction zone on cell 0's east edge, faces cell 1; filled by hand, it deploys a Fort there.</summary>
        BuildingState DeployFortIntoCell1()
        {
            var zone = t.Build(P, "fort_site", 0, 19, 5);
            Assert.AreEqual(1, zone.targetCellId);
            Assert.IsTrue(zone.underConstruction);
            foreach (var m in CatalogRepository.GetBuilding(t.Catalog, "fort_site").materials)
                WorldLookup.AddItem(zone.inventory, m.itemId, m.quantity); // test setup: delivered
            t.FactoryTick();
            Assert.IsNull(WorldLookup.GetBuilding(t.W, zone.id), "the zone is used up");
            var fort = t.W.buildings.Find(b => b.cellId == 1 && b.typeId == "fort");
            Assert.IsNotNull(fort, "a Fort was deployed into cell 1");
            Assert.IsTrue(WorldLookup.GetCell(t.W, 1).factoryOnline);
            return fort;
        }

        [Test]
        public void FortHoldsACellWithoutAUnit()
        {
            CraftState guard;
            ClaimCell1WithGuard(out guard);
            Assert.AreEqual("NOT_BUILDABLE", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "fort", fromCellId = 1, fromX = 8, fromY = 8 }).code,
                "Forts are deployed, never placed");
            DeployFortIntoCell1();
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 0 });
            Tick(t.Catalog.rules.abandonTicks + 300);
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 1).owner);
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 1).unheldSinceTick);
        }

        [Test]
        public void DismantlingTheFortStartsTheCountdown()
        {
            CraftState guard;
            ClaimCell1WithGuard(out guard);
            var fort = DeployFortIntoCell1();
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 0 });
            for (int i = 0; i < 200 && guard.cellId != 0; i++) t.Loop.TickNormal();
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = fort.id });
            Tick(2);
            Assert.AreNotEqual(-1, WorldLookup.GetCell(t.W, 1).unheldSinceTick);
        }

        [Test]
        public void CapturedHeadquartersChangesHandsAndHoldsTheCell()
        {
            t.ClearCraft();
            t.S.Ownership.TransferCell(t.W, t.Catalog, 7, P);
            Assert.AreEqual(P, Find(7, "headquarters").owner);
            Assert.AreEqual(-1, Find(7, "headquarters").inactiveUntilTick);
            Tick(t.Catalog.rules.abandonTicks + 10);
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 7).owner, "the captured HQ building keeps the cell");
        }

        [Test]
        public void FortifiedAiTerritoryIsNotAbandoned()
        {
            t.GiveAiOutposts(); // test setup: two fortified AI asteroids beside its headquarters
            Tick(t.Catalog.rules.abandonTicks * 2);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 3).owner);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 6).owner);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 7).owner, "and its headquarters cell");
        }
    
        [Test]
        public void ShipsCannotFlyThroughEnemyTerritoryOnlyIntoItsEdge()
        {
            // Map A: the player holds cell 0. Test setup: the AI holds the cells around it (1, 4 and 5), with no ships.
            foreach (int id in new[] { 1, 4, 5 }) t.SetOwner(id, FactionId.Ai);
            var m = t.S.Movement;
            Assert.IsNull(m.FindWorldPath(t.W, t.Map, P, 0, 2), "no way through enemy-held cells to the cell behind them");
            Assert.IsNotNull(m.FindWorldPath(t.W, t.Map, P, 0, 1), "but an enemy cell on the edge can be entered");
            Assert.IsNotNull(m.FindWorldPath(t.W, t.Map, P, 0, 2, true), "and ships withdrawing after a battle are never stranded");
            t.SetOwner(1, FactionId.Neutral);
            Assert.IsNotNull(m.FindWorldPath(t.W, t.Map, P, 0, 2), "a cell taken back opens the way");
        }
    }
}
