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

        static void RunUntilAiSells(TestWorld t, int minimumSales, int budget = TickBudget)
        {
            int start = t.W.tick;
            while (t.Ai.cumulativeSales < minimumSales)
            {
                if (t.W.mode == MatchMode.Ended) Assert.Fail("The match ended (" + t.W.outcome + ") before the AI sold " + minimumSales + ".");
                t.Loop.TickNormal();
                Assert.GreaterOrEqual(t.Ai.currency, 0, "AI spent below zero");
                // Keep the gate about construction: the player answers any battle turn at once.
                t.AnswerBattles();
                if (t.W.tick - start > budget)
                    Assert.Fail("AI did not sell within " + budget + " ticks. Planner: " + t.S.AiPlanner.LastReason
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
                if (s.fromCellId != s.toCellId)
                {
                    // A drone link: it must fly between two of the AI's pads.
                    var from = WorldLookup.GetBuildingAt(t.W, s.fromCellId, s.fromX, s.fromY);
                    var to = WorldLookup.GetBuildingAt(t.W, s.toCellId, s.toX, s.toY);
                    Assert.IsTrue(DroneLinks.IsPad(t.Catalog, from) && DroneLinks.IsPad(t.Catalog, to) && from.owner == FactionId.Ai && to.owner == FactionId.Ai,
                        "a link between asteroids must join two AI drone pads");
                    continue;
                }
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
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                t.Catalog.rules.aiExpands = t.Catalog.rules.aiRaids = false; // test setup: a defensive AI
                Assert.IsTrue(t.Catalog.rules.aiDefends, "it defends what it holds");
                for (int i = 0; i < 9000; i++) t.Loop.TickNormal();
                Assert.Greater(t.Ai.cumulativeSales, 0, "the AI still runs a factory");
                Assert.AreEqual(3, t.W.cells.FindAll(c => c.owner == FactionId.Ai).Count, "no expansion");
                Assert.IsFalse(t.Fighting, "no raid opened a battle");
                foreach (var craft in t.W.crafts.FindAll(k => k.owner == FactionId.Ai && WorldLookup.IsCraftLive(k)))
                    Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, craft.cellId).owner, "AI craft stay in AI territory");
            }
        }

        [Test]
        public void DefensiveAiGarrisonsEveryCellItHolds()
        {
            using (var t = new TestWorld("map_a"))
            {
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                t.Catalog.rules.aiExpands = t.Catalog.rules.aiRaids = false; // test setup: a defensive AI
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
        public void AiSendsBadlyDamagedCraftHomeToItsShipyardForRepairs()
        {
            using (var t = new TestWorld("map_a"))
            {
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                RunUntilAiSells(t, 1);
                t.Ai.currency += 4000; // test setup
                // A shipyard in the AI headquarters cell (7), wherever there is room for it.
                BuildingState yard = null;
                for (int y = 1; y < 18 && yard == null; y++)
                    for (int x = 1; x < 17 && yard == null; x++)
                        if (t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = "shipyard", fromCellId = 7, fromX = x, fromY = y }).success)
                            yard = WorldLookup.GetBuildingAt(t.W, 7, x, y);
                Assert.IsNotNull(yard, "test setup: room for a shipyard");
                var hurt = t.AddCraft(FactionId.Ai, "guard", 3, false);
                hurt.currentHealth = 3; // test setup: back from a fight with three ships of ten
                bool headedHome = false;
                for (int i = 0; i < 20000 && !headedHome; i++)
                {
                    t.Loop.TickNormal();
                    headedHome = hurt.cellId == 7 || (hurt.worldPathCellIds.Count > 0 && hurt.worldPathCellIds[hurt.worldPathCellIds.Count - 1] == 7);
                }
                Assert.IsTrue(headedHome, "the damaged guard flies to the shipyard. AI: " + t.S.Ai.LastAction);
                Assert.AreNotEqual(hurt.id, WorldLookup.GetCell(t.W, 3).defenderCraftId, "it is not posted as a defender while it heals");
                var fortress = CatalogRepository.GetCraft(t.Catalog, "fortress");
                Assert.IsTrue(fortress.repairParts.Exists(p => p.itemId == "purified_water"));
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
        public void AiFliesOreInFromItsOtherAsteroidsByDrone()
        {
            // Copper only on its fortified asteroids and iron only at home: no single asteroid can make a Signal
            // Relay, so the AI must research Drone Logistics and fly one ore to the other.
            using (var t = new TestWorld("map_a", map =>
            {
                map.cells.Find(c => c.id == 7).nodes.RemoveAll(n => n.itemId == "copper_ore");
                foreach (int id in new[] { 3, 6 }) map.cells.Find(c => c.id == id).nodes.RemoveAll(n => n.itemId == "ore");
            }))
            {
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                t.Ai.currency += 1500; // test setup: research plus a two-asteroid chain before any income
                RunUntilAiSells(t, 1, 30000);
                AssertAiStructuresLegal(t);
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "drone_pad"), "the AI built drone pads");
                Assert.IsTrue(t.W.belts.Exists(s => s.owner == FactionId.Ai && s.fromCellId != s.toCellId && s.linkTicks > 0), "and linked them across");
                Assert.IsTrue(t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == "extractor"
                    && WorldLookup.GetNode(t.W, b.nodeId) != null && WorldLookup.GetNode(t.W, b.nodeId).itemId == "copper_ore"), "copper is mined (away from home)");
            }
        }

        [Test]
        public void AiGathersResearchGoodsInStorageLikeThePlayer()
        {
            using (var t = new TestWorld("map_a"))
            {
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                t.Catalog.rules.aiExpands = t.Catalog.rules.aiRaids = false; // test setup: an AI busy with its economy only
                RunUntilAiSells(t, 1, 12000);
                // Test setup: credits and Ion to spare, so only the goods decide when it can research.
                bool paidGoods = false;
                for (int i = 0; i < 40000 && !paidGoods; i++)
                {
                    if (t.Ai.currency < 3000) t.Ai.currency += 3000;
                    if (t.Ai.ion < 1000) t.Ai.ion += 1000;
                    if (t.W.mode == MatchMode.Ended) Assert.Fail("The match ended (" + t.W.outcome + ").");
                    t.Loop.TickNormal();
                    t.AnswerBattles();
                    if (i % 100 != 0) continue;
                    foreach (var job in t.Ai.researchQueue)
                    {
                        var node = CatalogRepository.GetResearch(t.Catalog, job.nodeId);
                        if (node != null && node.levels[job.level - 1].goods.Count > 0) paidGoods = true;
                    }
                    foreach (var r in t.Ai.research)
                    {
                        var node = CatalogRepository.GetResearch(t.Catalog, r.nodeId);
                        for (int l = 0; node != null && l < r.level; l++) if (node.levels[l].goods.Count > 0) paidGoods = true;
                    }
                }
                var why = new System.Text.StringBuilder();
                foreach (var node in t.Catalog.research)
                {
                    var next = ResearchService.NextPlannedLevel(t.Ai, node);
                    if (next == null) continue;
                    bool open = ResearchService.PlannedLevel(t.Ai, node.id) > 0 || ResearchService.IsOpenPlanned(t.Ai, node);
                    if (!open) continue;
                    var refusal = ResearchService.CheckPurchase(t.W, t.Catalog, t.Ai, node);
                    why.Append(node.id).Append(':').Append(refusal != null ? refusal.code : "ok").Append(next.goods.Count > 0 ? "(goods)" : "").Append(' ');
                }
                Assert.IsTrue(paidGoods, "it researched a level that costs goods. Planner: " + t.S.AiPlanner.LastReason
                    + " | research: " + t.Ai.research.Count + " nodes | queue " + t.Ai.researchQueue.Count + " | target " + t.S.Ai.Brain.Decisions.ResearchReason
                    + " | budget " + t.S.Ai.Brain.Decisions.BudgetReason + " | open: " + why);
                AssertAiStructuresLegal(t);
            }
        }

        [Test]
        public void AiRefinesFlownInOreWhereItIsMined()
        {
            // As above, but a refinery's worth of room on the copper asteroids: copper is refined there and bars fly home.
            using (var t = new TestWorld("map_a", map =>
            {
                map.cells.Find(c => c.id == 7).nodes.RemoveAll(n => n.itemId == "copper_ore");
                foreach (int id in new[] { 3, 6 }) map.cells.Find(c => c.id == id).nodes.RemoveAll(n => n.itemId == "ore");
            }))
            {
                t.GiveAiOutposts(); // test setup: the AI holds three asteroids
                t.Ai.currency += 2500; // test setup: research plus a two-asteroid chain before any income
                RunUntilAiSells(t, 1, 30000);
                AssertAiStructuresLegal(t);
                var copper = t.W.buildings.Find(b => b.owner == FactionId.Ai && b.selectedRecipeId == "refine_copper");
                Assert.IsNotNull(copper, "copper is refined");
                Assert.AreNotEqual(7, copper.cellId, "at the copper, not at home: " + t.S.Ai.Brain.Decisions.SupplyReason);
                Assert.IsTrue(t.Ai.cumulativeSales > 0);
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
