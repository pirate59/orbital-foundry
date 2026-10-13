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
            Assert.Greater(WorldLookup.CountItem(refinery.inventory, "alloy"), 5, "refinery kept producing");
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
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "refinery", 0, 3, 1);
            var yard = t.Build(P, "shipyard", 0, 3, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Line(P, 0, 3, 1, 0, 3, 2, 0, 3, 3);
            int crafts = t.W.crafts.Count;
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            Assert.AreEqual("SHIPYARD_BUSY", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "tank", buildingId = yard.id }).code);

            for (int i = 0; i < 1000 && t.W.crafts.Count == crafts; i++) t.FactoryTick();
            Assert.AreEqual(crafts + 1, t.W.crafts.Count, "guard launched once its alloy arrived");
            var guard = t.W.crafts[t.W.crafts.Count - 1];
            Assert.AreEqual("guard", guard.typeId);
            Assert.AreEqual(0, guard.cellId);
            Assert.AreEqual("", yard.orderedCraftTypeId);
            Assert.AreEqual(0, WorldLookup.CountItem(yard.inventory, "alloy"), "parts were consumed");
        }
    }
}
