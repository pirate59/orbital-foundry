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
            Assert.IsNotNull(Find(3, "fort"), "starting AI territory is fortified");
            Assert.IsNotNull(Find(6, "fort"));
            Assert.IsNull(Find(1, "fort"), "neutral cells start empty");
            var hq = Find(0, "headquarters");
            Assert.AreEqual(7, hq.x);
            Assert.AreEqual(7, hq.y);
        }

        [Test]
        public void HeadquartersCannotBeBuiltOrDismantledAndHqCellsNeedNoFort()
        {
            Assert.AreEqual("NOT_BUILDABLE", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "headquarters", fromCellId = 0, fromX = 9, fromY = 9 }).code);
            Assert.AreEqual("NOT_ALLOWED", t.Run(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = Find(0, "headquarters").id }).code);
            Assert.AreEqual("ALREADY_HELD", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "fort", fromCellId = 0, fromX = 9, fromY = 9 }).code);
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

            var extractor = t.Build(P, "extractor", 1, 2, 3);
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 0 });
            for (int i = 0; i < 200 && guard.cellId != 0; i++) t.Loop.TickNormal();
            var cell = WorldLookup.GetCell(t.W, 1);
            Assert.AreNotEqual(-1, cell.unheldSinceTick, "unguarded once the unit leaves");
            Assert.IsTrue(t.W.alerts.Exists(a => a.kind == AlertKind.Territory && a.cellId == 1), "the player is warned");

            Tick(t.Catalog.rules.abandonTicks);
            Assert.AreEqual(FactionId.Neutral, cell.owner, "abandoned after the countdown");
            Assert.AreNotEqual(-1, extractor.inactiveUntilTick, "machines stop but are not destroyed yet");
        }

        [Test]
        public void FortHoldsACellWithoutAUnit()
        {
            CraftState guard;
            ClaimCell1WithGuard(out guard);
            t.Build(P, "fort", 1, 7, 7);
            Assert.AreEqual("ALREADY_HELD", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "fort", fromCellId = 1, fromX = 8, fromY = 8 }).code);
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
            var fort = t.Build(P, "fort", 1, 7, 7);
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
        public void StartingAiTerritoryIsNotAbandoned()
        {
            Tick(t.Catalog.rules.abandonTicks * 2);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 3).owner);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 6).owner);
        }
    }
}
