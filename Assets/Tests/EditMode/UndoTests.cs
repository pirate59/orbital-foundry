using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Undo: a fresh placement comes back for everything it cost, only for a short while.</summary>
    public class UndoTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void AFreshBuildingIsUndoneForAFullRefund()
        {
            t.Player.currency += 5000;
            int before = t.Player.currency;
            var b = t.Build(P, "refinery", 0, 3, 3, false);
            Assert.Less(t.Player.currency, before);
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = b.id, contentId = "undo" });
            Assert.IsNull(WorldLookup.GetBuilding(t.W, b.id));
            Assert.AreEqual(before, t.Player.currency, "every credit back");
        }

        [Test]
        public void UndoRunsOutAfterAMinute()
        {
            t.Player.currency += 5000;
            var b = t.Build(P, "refinery", 0, 3, 3, false);
            t.W.tick += CommandRouter.UndoTicks + 1;
            Assert.AreEqual("TOO_LATE", t.Run(new GameCommand { kind = CommandKind.Dismantle, actor = P, buildingId = b.id, contentId = "undo" }).code);
            Assert.IsNotNull(WorldLookup.GetBuilding(t.W, b.id));
        }

        [Test]
        public void ABeltIsUndoneWithItsUpgrades()
        {
            t.Player.currency += 5000;
            int before = t.Player.currency;
            t.MustRun(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = 2, fromY = 6, toCellId = 0, toX = 3, toY = 6 });
            var belt = t.W.belts[t.W.belts.Count - 1];
            t.MustRun(new GameCommand { kind = CommandKind.UpgradeBelt, actor = P, beltId = belt.id });
            t.MustRun(new GameCommand { kind = CommandKind.Dismantle, actor = P, beltId = belt.id, contentId = "undo" });
            Assert.AreEqual(before, t.Player.currency);
        }

        [Test]
        public void AStarvedMachineIsMarkedAsABottleneck()
        {
            t.Player.currency += 5000;
            var b = t.Build(P, "refinery", 0, 3, 3);
            Assert.IsFalse(string.IsNullOrEmpty(b.selectedRecipeId));
            Assert.AreEqual(FactorySimulation.Stall.None, t.S.Factory.StallOf(t.W, t.Catalog, b), "no history yet");
            for (int i = 0; i < FactorySimulation.UptimeTicks; i++) t.FactoryTick();
            Assert.AreEqual(FactorySimulation.Stall.Starved, t.S.Factory.StallOf(t.W, t.Catalog, b), "nothing feeds it");
            var idle = t.Build(P, "assembler", 0, 5, 5, false);
            Assert.AreEqual(FactorySimulation.Stall.NoRecipe, t.S.Factory.StallOf(t.W, t.Catalog, idle));
        }

        [Test]
        public void ProductionStatsGiveRatesOverTheLastMinute()
        {
            var stats = new ProductionStats();
            stats.Advance(t.W, t.Catalog);
            stats.Add(P, ProductionStats.Kind.Made, "iron_plate", 10);
            int halfMinute = 30000 / t.Catalog.rules.tickMilliseconds;
            for (int i = 0; i < halfMinute; i++) { t.W.tick++; stats.Advance(t.W, t.Catalog); }
            Assert.AreEqual(20f, stats.PerMinute(t.Catalog, P, ProductionStats.Kind.Made, "iron_plate"), 0.5f, "10 in half a minute is 20 a minute");
            for (int i = 0; i < 2 * halfMinute; i++) { t.W.tick++; stats.Advance(t.W, t.Catalog); }
            Assert.AreEqual(0f, stats.PerMinute(t.Catalog, P, ProductionStats.Kind.Made, "iron_plate"), "older than a minute drops out");
        }

        [Test]
        public void ADamagedCraftFindsItsShipyard()
        {
            t.Player.currency += 5000;
            var craft = t.AddCraft(P, "guard", 0, false);
            Assert.AreEqual(-1, FactorySimulation.NearestRepairYard(t.W, t.Catalog, t.Map, t.S.Movement, craft), "no shipyard yet");
            t.Build(P, "shipyard", 0, 6, 6);
            Assert.AreEqual(0, FactorySimulation.NearestRepairYard(t.W, t.Catalog, t.Map, t.S.Movement, craft), "docked in the yard's cell");
        }

        [Test]
        public void AnInstantAiTurnIsOverInOneUpdate()
        {
            t.AddCraft(P, "bomber", 1, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).success);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P });
            Assert.AreEqual(FactionId.Ai, t.W.encounter.turn);
            t.Loop.AiTurnInstant = true;
            t.Loop.Update(0.01f);
            Assert.IsTrue(!t.W.encounter.active || t.W.encounter.turn == FactionId.Player, "the AI's whole turn played at once");
        }

        [Test]
        public void AFortressShieldsTheOtherShipsInItsCell()
        {
            var bomber = t.AddCraft(P, "bomber", 1, false);
            var guard = t.AddCraft(FactionId.Ai, "guard", 2, true);
            int unshielded = t.S.Combat.Preview(t.W, t.Catalog, t.Map, bomber.id, 2, guard.id).attackDamageAfterArmour;
            var fortress = t.AddCraft(FactionId.Ai, "fortress", 2, false);
            Assert.AreEqual(1, CombatResolver.ShieldBonus(t.W, t.Catalog, guard));
            Assert.AreEqual(0, CombatResolver.ShieldBonus(t.W, t.Catalog, fortress), "it does not shield itself");
            Assert.AreEqual(unshielded - 1, t.S.Combat.Preview(t.W, t.Catalog, t.Map, bomber.id, 2, guard.id).attackDamageAfterArmour);
        }

        [Test]
        public void OrderingAShipCostsIon()
        {
            t.Player.currency += 5000;
            var yard = t.Build(P, "shipyard", 0, 6, 6);
            t.Player.ion = 10;
            Assert.AreEqual("NO_ION", t.Run(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id }).code);
            int credits = t.Player.currency;
            t.Player.ion = 25;
            t.MustRun(new GameCommand { kind = CommandKind.BuyCraft, actor = P, contentId = "guard", buildingId = yard.id });
            Assert.AreEqual(5, t.Player.ion);
            Assert.AreEqual(credits - 100, t.Player.currency);
        }
    }
}
