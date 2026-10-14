using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// The AI construction gate: from starting currency, nodes and craft only, the AI must build a selling factory
    /// through ordinary commands on both maps, and replan after losing a transit cell.
    /// </summary>
    public class AIPlannerTests
    {
        const int TickBudget = 6000;

        static void RunUntilAiSells(TestWorld t, int minimumSales)
        {
            int start = t.W.tick;
            while (t.Ai.cumulativeSales < minimumSales)
            {
                t.Loop.TickNormal();
                Assert.GreaterOrEqual(t.Ai.currency, 0, "AI spent below zero");
                if (t.W.mode == MatchMode.Tactical)
                {
                    // Keep the gate about construction: resolve any battle by player retreat or AI turns.
                    while (t.W.mode == MatchMode.Tactical)
                    {
                        if (t.W.encounter.turn == FactionId.Ai) t.Loop.RunAiTurnImmediately();
                        else if (t.W.encounter.attacker == FactionId.Player) t.Run(new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Player });
                        else t.Run(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
                    }
                }
                if (t.W.tick - start > TickBudget)
                    Assert.Fail("AI did not sell within " + TickBudget + " ticks. Planner: " + t.S.AiPlanner.LastReason
                        + " | Last action: " + t.S.Ai.LastAction + " | Currency: " + t.Ai.currency
                        + " | Buildings: " + Count(t, FactionId.Ai) + " | Belts: " + t.W.belts.Count);
            }
        }

        /// <summary>Production buildings (excludes the pre-placed Headquarters and Forts).</summary>
        static int Count(TestWorld t, FactionId owner)
        {
            int n = 0;
            foreach (var b in t.W.buildings) if (b.owner == owner && b.typeId != "headquarters" && b.typeId != "fort") n++;
            return n;
        }

        static void AssertAiStructuresLegal(TestWorld t)
        {
            foreach (var b in t.W.buildings)
            {
                if (b.owner != FactionId.Ai) continue;
                var cellDef = WorldLookup.GetCellDef(t.Map, b.cellId);
                Assert.IsFalse(GridRules.IsBlocked(cellDef, b.x, b.y), "building on blocked tile");
            }
            foreach (var s in t.W.belts)
            {
                if (s.owner != FactionId.Ai) continue;
                var a = WorldLookup.GetCellDef(t.Map, s.fromCellId);
                var b = WorldLookup.GetCellDef(t.Map, s.toCellId);
                Assert.IsFalse(GridRules.IsBlocked(a, s.fromX, s.fromY) || GridRules.IsBlocked(b, s.toX, s.toY), "belt on blocked tile");
                int d = System.Math.Abs(GridRules.GlobalX(b, s.toX) - GridRules.GlobalX(a, s.fromX)) + System.Math.Abs(GridRules.GlobalY(b, s.toY) - GridRules.GlobalY(a, s.fromY));
                Assert.AreEqual(1, d, "belt must join neighbouring tiles");
            }
            Assert.IsTrue(StateValidator.ValidateWorld(t.W, t.Catalog, t.Map).success, StateValidator.ValidateWorld(t.W, t.Catalog, t.Map).message);
        }

        [TestCase("map_a")]
        [TestCase("map_b")]
        public void AiBuildsASellingFactoryFromStartingResources(string mapId)
        {
            using (var t = new TestWorld(mapId))
            {
                Assert.AreEqual(0, Count(t, FactionId.Ai));
                RunUntilAiSells(t, 1);
                Assert.GreaterOrEqual(Count(t, FactionId.Ai), 5);
                AssertAiStructuresLegal(t);
            }
        }

        [Test]
        public void AiEarnsCraftThroughAShipyard()
        {
            using (var t = new TestWorld("map_a"))
            {
                t.Catalog.rules.aiExpands = true;
                t.Catalog.rules.aiRaids = true;
                int startCraft = t.W.crafts.FindAll(c => c.owner == FactionId.Ai).Count;
                int start = t.W.tick;
                while (t.W.crafts.FindAll(c => c.owner == FactionId.Ai && WorldLookup.IsCraftLive(c)).Count <= startCraft)
                {
                    RunUntilAiSells(t, t.Ai.cumulativeSales + 1);
                    if (t.W.tick - start > 20000)
                        Assert.Fail("AI launched no craft. Planner: " + t.S.AiPlanner.LastReason + " | Last action: " + t.S.Ai.LastAction + " | Currency: " + t.Ai.currency);
                }
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "shipyard"), "craft came from a shipyard");
                Assert.GreaterOrEqual(t.Ai.cumulativeSales, AIFactoryPlanner.ShipyardAfterSales, "military only after earning");
                AssertAiStructuresLegal(t);
            }
        }

        [Test]
        public void DefensiveAiStaysHome()
        {
            using (var t = new TestWorld("map_a"))
            {
                Assert.IsFalse(t.Catalog.rules.aiExpands || t.Catalog.rules.aiRaids, "shipped catalog keeps the AI from expanding or raiding");
                Assert.IsTrue(t.Catalog.rules.aiDefends, "but it defends what it holds");
                for (int i = 0; i < 9000; i++) t.Loop.TickNormal();
                Assert.Greater(t.Ai.cumulativeSales, 0, "the AI still runs a factory");
                Assert.AreEqual(3, t.W.cells.FindAll(c => c.owner == FactionId.Ai).Count, "no expansion");
                Assert.AreEqual(MatchMode.Factory, t.W.mode, "no raid opened a battle");
                foreach (var craft in t.W.crafts.FindAll(k => k.owner == FactionId.Ai && WorldLookup.IsCraftLive(k)))
                    Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, craft.cellId).owner, "AI craft stay in AI territory");
            }
        }

        [Test]
        public void DefensiveAiGarrisonsEveryCellItHolds()
        {
            using (var t = new TestWorld("map_a"))
            {
                // Cell 3 starts without a defender; the AI must build a craft and post it there.
                Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 3).defenderCraftId);
                RunUntilAiSells(t, 1);
                t.Ai.currency += 4000; // test setup: skip the wait for savings
                t.Ai.ion += 2000;
                t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
                bool garrisoned = false;
                for (int i = 0; i < 20000 && !garrisoned; i++)
                {
                    t.Loop.TickNormal();
                    garrisoned = t.W.cells.FindAll(c => c.owner == FactionId.Ai).TrueForAll(c =>
                    {
                        var d = WorldLookup.GetCraft(t.W, c.defenderCraftId);
                        return d != null && WorldLookup.IsCraftLive(d) && d.cellId == c.id;
                    });
                }
                Assert.IsTrue(garrisoned, "every AI cell has a defender. Planner: " + t.S.AiPlanner.LastReason + " | " + t.S.Ai.LastAction);
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "shipyard"), "it built a shipyard to do it");
                AssertAiStructuresLegal(t);
            }
        }

        [Test]
        public void AiAddsSolarWhenItsFactoryOutgrowsItsPower()
        {
            using (var t = new TestWorld("map_a"))
            {
                CatalogRepository.GetBuilding(t.Catalog, "headquarters").powerSupply = 6; // test setup: a cramped grid
                RunUntilAiSells(t, 1);
                t.Ai.currency += 4000; // test setup
                t.Ai.ion += 2000;
                for (int i = 0; i < 15000 && !t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "solar_array"); i++) t.Loop.TickNormal();
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "solar_array"),
                    "the AI built a Solar Array. Planner: " + t.S.AiPlanner.LastReason + " | " + t.S.Ai.LastAction + " | power " + t.Ai.powerDemand + "/" + t.Ai.powerSupply);
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "storage"), "fed from a Storage hub");
                AssertAiStructuresLegal(t);
            }
        }

        [Test]
        public void AiBuildsEachChainOnOneAsteroid()
        {
            // Belts cannot cross between asteroids and the AI does not fly drones: it may use several asteroids, but
            // never lays a belt between them, and it still earns.
            using (var t = new TestWorld("map_a"))
            {
                RunUntilAiSells(t, 1);
                AssertAiStructuresLegal(t);
                Assert.IsFalse(t.W.belts.Exists(s => s.owner == FactionId.Ai && s.fromCellId != s.toCellId), "no AI belt crosses between asteroids");
            }
        }
    }
}
