using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Starting production from the HQ, dismantling, and earning craft through a shipyard.</summary>
    public class ProductionTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void HqExtractorAndDepotEarnMoneyImmediately()
        {
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "sale_depot", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            int start = t.Player.currency;
            for (int i = 0; i < 300; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 15, "raw ore sells from the HQ alone");
            Assert.AreEqual(start + t.Player.cumulativeSales, t.Player.currency);
        }

        [Test]
        public void FactoriesAreServedBeforeDepots()
        {
            // The refinery and the depot share one ore extractor; the refinery must still get every ore it asks for.
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Build(P, "sale_depot", 0, 1, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 1, 2, 0, 1, 3);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "iron_bar"), 5, "refinery kept producing");
            Assert.Greater(t.Player.cumulativeSales, 0, "surplus ore was sold");
        }

        [Test]
        public void DismantlingOwnStructuresRefundsHalfAndClearsDependants()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "refinery", 0, 3, 1);
            var belts = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            for (int i = 0; i < 30 && t.W.tokens.Count == 0; i++) t.FactoryTick();
            Assert.Greater(t.W.tokens.Count, 0);

            int before = t.Player.currency;
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, beltId = belts[0].id });
            Assert.AreEqual(before + 1, t.Player.currency);
            Assert.IsNull(WorldLookup.GetBelt(t.W, belts[0].id));
            Assert.AreEqual(0, t.W.tokens.Count, "items on the removed belt are lost");

            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = ext.id });
            Assert.AreEqual(before + 1 + 35, t.Player.currency);
            Assert.AreEqual(-1, WorldLookup.GetNode(t.W, 101).extractorBuildingId, "node is free again");
            t.Build(P, "extractor", 0, 1, 1);
        }

        [Test]
        public void CannotDismantleEnemyOrOutsideOwnCells()
        {
            var aiExt = t.Build(FactionId.Ai, "extractor", 7, 1, 1);
            Assert.AreEqual("NOT_OWNER", t.Run(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = aiExt.id }).code);
        }

        [Test]
        public void CraftRequireAShipyardAndDeliveredParts()
        {
            var noYard = t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = 999 });
            Assert.IsFalse(noYard.success);

            t.Player.currency += 400; // test setup: fund chain and yard together
            t.Player.ion += 200;
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "refinery", 0, 3, 1);
            var yard = t.Build(P, "shipyard", 0, 3, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 3, 1, 0, 3, 2, 0, 3, 3);
            int crafts = t.W.crafts.Count;
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            Assert.AreEqual("SHIPYARD_BUSY", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "tank", buildingId = yard.id }).code);

            for (int i = 0; i < 1000 && t.W.crafts.Count == crafts; i++) t.FactoryTick();
            Assert.AreEqual(crafts + 1, t.W.crafts.Count, "guard launched once its iron bars arrived");
            var guard = t.W.crafts[t.W.crafts.Count - 1];
            Assert.AreEqual("guard", guard.typeId);
            Assert.AreEqual(0, guard.cellId);
            Assert.AreEqual("", yard.orderedCraftTypeId);
            Assert.AreEqual(0, WorldLookup.CountItem(yard.inventory, "iron_bar"), "parts were consumed");
        }

        [Test]
        public void BeltJoiningMidLineMergesAndBranchingMidLineSplits()
        {
            t.Player.currency += 500; // test setup
            // Merge: copper joins the middle of the ore line, and the depot sells both.
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "extractor", 0, 4, 4);
            t.Build(P, "sale_depot", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 1, 2, 0, 2, 2, 0, 3, 2, 0, 4, 2, 0, 5, 2, 0, 5, 1);
            t.Line(P, 0, 4, 4, 0, 4, 3, 0, 4, 2);
            // Split: a branch off the middle of the same line feeds a refinery.
            var refinery = t.Build(P, "refinery", 0, 2, 4);
            t.Line(P, 0, 2, 2, 0, 2, 3, 0, 2, 4);
            for (int i = 0; i < 600; i++)
            {
                t.FactoryTick();
                t.AssertBeltCapacity();
            }
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "iron_bar"), 3, "the branch fed the refinery");
            // Ore alone (after the refinery's share) cannot reach this total; copper must arrive through the merge too.
            Assert.Greater(t.Player.cumulativeSales, 40, "the depot sold from both merged lines");
        }

        [Test]
        public void DepotSellListFiltersWhatItPulls()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var depot = t.Build(P, "sale_depot", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = depot.id, contentId = "ore" });
            CollectionAssert.Contains(depot.blockedItemIds, "ore");
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.AreEqual(0, t.Player.cumulativeSales, "ore is off the sell list");
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = depot.id, contentId = "ore" });
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0);
            Assert.AreEqual("NOT_DEPOT", t.Run(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = WorldLookup.GetBuildingAt(t.W, 0, 1, 1).id, contentId = "ore" }).code);
        }

        [Test]
        public void DrawingOverABeltTheOtherWayReversesIt()
        {
            var forward = t.Belt(P, 0, 2, 2, 0, 3, 2);
            int before = t.Player.currency;
            var r = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = 3, fromY = 2, toCellId = 0, toX = 2, toY = 2 });
            Assert.IsTrue(r.success, r.message);
            Assert.IsNull(WorldLookup.GetBelt(t.W, forward.id));
            Assert.AreEqual(1, t.W.belts.Count);
            Assert.AreEqual(3, t.W.belts[0].fromX);
            Assert.AreEqual(before, t.Player.currency, "reversing is free");
        }

        [Test]
        public void NeedsFollowTheSelectedRecipeOrOrder()
        {
            var assembler = t.Build(P, "assembler", 0, 3, 3);
            var needs = FactorySimulation.CurrentNeeds(t.W, t.Catalog, assembler);
            Assert.AreEqual(2, needs.Count);
            t.Player.research.Add(new ResearchProgress { nodeId = "quantum_cores", level = 1 });
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = assembler.id, contentId = "build_core" });
            needs = FactorySimulation.CurrentNeeds(t.W, t.Catalog, assembler);
            Assert.AreEqual(2, needs[0].quantity, "quantum core needs 2 iron bars");
        }

        [Test]
        public void MovingAUnitIntoANeutralCellClaimsItAutomatically()
        {
            var guard = WorldLookup.CraftsInCell(t.W, 0, P)[0];
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 1 });
            for (int i = 0; i < 400 && WorldLookup.GetCell(t.W, 1).owner != P; i++) t.Loop.TickNormal();
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 1).owner, "arrival starts the hold, which completes on its own");
            Assert.AreEqual(guard.id, WorldLookup.GetCell(t.W, 1).defenderCraftId);
        }

        [Test]
        public void StorageJoinsSeveralProducersToSeveralMachines()
        {
            // Two ore extractors feed one storage; two refineries draw only from that storage.
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "extractor", 0, 11, 3);
            var storage = t.Build(P, "storage", 0, 6, 1);
            var refineryA = t.Build(P, "refinery", 0, 6, 4);
            var refineryB = t.Build(P, "refinery", 0, 5, 0);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            t.Line(P, 0, 11, 3, 0, 11, 2, 0, 11, 1, 0, 10, 1, 0, 9, 1, 0, 8, 1, 0, 7, 1, 0, 6, 1);
            t.Line(P, 0, 6, 1, 0, 6, 2, 0, 6, 3, 0, 6, 4);
            t.Line(P, 0, 6, 1, 0, 6, 0, 0, 5, 0);
            Assert.IsFalse(t.S.Belts.FindShortestRoute(t.W, WorldLookup.GetBuildingAt(t.W, 0, 1, 1).id, refineryA.id).success,
                "producers reach the refineries only through the storage");
            for (int i = 0; i < 800; i++) { t.FactoryTick(); t.AssertBeltCapacity(); }
            Assert.Greater(WorldLookup.CountItem(refineryA.inventory, "iron_bar"), 2);
            Assert.Greater(WorldLookup.CountItem(refineryB.inventory, "iron_bar"), 2);
            Assert.Greater(WorldLookup.CountItem(storage.inventory, "ore"), 0, "surplus ore is held in storage");
        }

        [Test]
        public void StoragesDoNotFeedEachOther()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var first = t.Build(P, "storage", 0, 3, 1);
            var second = t.Build(P, "storage", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 3, 1, 0, 4, 1, 0, 5, 1);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(first.inventory, "ore"), 0);
            Assert.AreEqual(0, WorldLookup.TotalItems(second.inventory));
        }

        [Test]
        public void StorageAcceptListFiltersWhatItCollects()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = storage.id, contentId = "ore" });
            for (int i = 0; i < 300; i++) t.FactoryTick();
            Assert.AreEqual(0, WorldLookup.CountItem(storage.inventory, "ore"));
        }

        [Test]
        public void StorageReceivingGoodsIsNotReportedIdle()
        {
            // A long lane keeps two ore in flight at once, so no fresh ore request is waiting while it is served.
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 7, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1, 0, 7, 1);
            for (int i = 0; i < 300; i++)
            {
                t.FactoryTick();
                if (i > 150) Assert.IsFalse(t.S.Pull.BlockedReasonByBuilding.ContainsKey(storage.id), "tick " + t.W.tick + ": " + string.Join(";", t.W.pullRequests.ConvertAll(r => r.targetBuildingId + "/" + r.itemId + "@" + r.createdTick)));
            }
        }
    }
}
