using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Loads whose route is cut: they find a new way to their building, or any other that can use them; failing
    /// that they are stranded (flagged, no longer counted by any building) and lost after a while unless reconnected.
    /// </summary>
    public class StrandedLoadTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        GameCommand DismantleBelt(BeltSegmentState s) { return new GameCommand { kind = CommandKind.Dismantle, actor = P, beltId = s.id }; }

        void RunUntilLoadsHeadFor(BuildingState b)
        {
            for (int i = 0; i < 200 && !t.W.tokens.Exists(k => k.targetBuildingId == b.id && k.segmentIndex >= 1); i++) t.FactoryTick();
            Assert.IsTrue(t.W.tokens.Exists(k => k.targetBuildingId == b.id), "test setup: ore is on its way");
        }

        [Test]
        public void ACutRouteSendsLoadsToAnotherBuildingThatCanUseThem()
        {
            // Ore runs east to a Storage; a branch off the line leads to a Sale Depot.
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 6, 1);
            t.Build(P, "sale_depot", 0, 4, 3);
            var main = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            t.Line(P, 0, 4, 1, 0, 4, 2, 0, 4, 3);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = WorldLookup.GetBuildingAt(t.W, 0, 4, 3).id, contentId = "ore" }); // test setup: the shop sells nothing yet
            RunUntilLoadsHeadFor(storage);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = WorldLookup.GetBuildingAt(t.W, 0, 4, 3).id, contentId = "ore" });
            t.MustRun(DismantleBelt(main[4])); // cut the line just before the Storage
            for (int i = 0; i < 120; i++) t.FactoryTick();
            Assert.Greater(t.Player.cumulativeSales, 0, "ore cut off from the Storage was sold at the depot instead");
            // Only loads already past the junction (belts run one way) have nowhere to go.
            Assert.IsTrue(t.W.tokens.FindAll(k => k.strandedSinceTick >= 0).TrueForAll(k => k.routeSegmentIds[k.segmentIndex] == main[3].id),
                "only loads beyond the junction were stranded");
        }

        [Test]
        public void LoadsForARemovedBuildingFindAnotherInsteadOfVanishing()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 6, 1);
            var second = t.Build(P, "storage", 0, 4, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            t.Line(P, 0, 4, 1, 0, 4, 2, 0, 4, 3);
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = second.id, contentId = "ore" }); // test setup
            RunUntilLoadsHeadFor(storage);
            int onTheWay = 0;
            foreach (var k in t.W.tokens) if (k.targetBuildingId == storage.id) onTheWay += k.Units;
            t.MustRun(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = P, buildingId = second.id, contentId = "ore" });
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = storage.id });
            Assert.AreEqual(onTheWay, SumUnits(), "the loads are still on the belt");
            for (int i = 0; i < 80; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(second.inventory, "ore"), 0, "they went to the other Storage");
        }

        int SumUnits()
        {
            int n = 0;
            foreach (var k in t.W.tokens) n += k.Units;
            return n;
        }

        [Test]
        public void WithNowhereToGoLoadsAreStrandedThenLostUnlessReconnected()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var storage = t.Build(P, "storage", 0, 6, 1);
            var main = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            RunUntilLoadsHeadFor(storage);
            t.MustRun(DismantleBelt(main[4]));
            for (int i = 0; i < 20; i++) t.FactoryTick();
            var stranded = t.W.tokens.FindAll(k => k.strandedSinceTick >= 0);
            Assert.Greater(stranded.Count, 0, "loads cut off from everything are stranded");
            Assert.IsTrue(stranded.TrueForAll(k => k.targetBuildingId == -1), "and no longer counted as on their way to the Storage");
            Assert.IsTrue(t.W.alerts.Exists(a => a.kind == AlertKind.BlockedRoute && a.message.Contains("stranded")), "the player is told");
            Assert.IsTrue(StateValidator.ValidateWorld(t.W, t.Catalog, t.Map).success, "the world with stranded loads is still valid");

            // Reconnected in time, they carry on to the Storage.
            t.Line(P, 0, 5, 1, 0, 6, 1);
            for (int i = 0; i < 60; i++) t.FactoryTick();
            Assert.IsFalse(t.W.tokens.Exists(k => k.strandedSinceTick >= 0), "reconnected: none stranded");
            Assert.Greater(WorldLookup.CountItem(storage.inventory, "ore"), 0, "they reached the Storage");

            // Cut again and left alone, they are lost after the grace period.
            t.MustRun(DismantleBelt(WorldLookup.GetBelt(t.W, t.W.belts.Find(b => b.fromX == 5 && b.toX == 6).id)));
            for (int i = 0; i < 20; i++) t.FactoryTick();
            Assert.Greater(t.W.tokens.FindAll(k => k.strandedSinceTick >= 0).Count, 0);
            for (int i = 0; i < FactorySimulation.StrandedTicks + 20; i++) t.FactoryTick();
            Assert.IsFalse(t.W.tokens.Exists(k => k.strandedSinceTick >= 0 && t.W.tick - k.strandedSinceTick > FactorySimulation.StrandedTicks), "lost once the grace period ran out");
        }
    }
}
