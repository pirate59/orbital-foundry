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

        static int Count(TestWorld t, FactionId owner)
        {
            int n = 0;
            foreach (var b in t.W.buildings) if (b.owner == owner) n++;
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
        public void AiReplansAfterLosingATransitCell()
        {
            // Only crystal source is node 104 in cell 2 (granted as if already expanded into), so crystal must transit
            // cell 3 or cell 6 to reach the HQ machines.
            using (var t = new TestWorld("map_a", map =>
            {
                map.cells.Find(c => c.id == 7).nodes.RemoveAll(n => n.id == 110);
                map.cells.Find(c => c.id == 6).nodes.RemoveAll(n => n.id == 108);
                map.cells.Find(c => c.id == 2).startingOwner = FactionId.Ai;
            }))
            {
                RunUntilAiSells(t, 1);
                AssertAiStructuresLegal(t);

                int transit = -1;
                foreach (int candidate in new[] { 3, 6 })
                {
                    bool hasBelt = t.W.belts.Exists(s => s.owner == FactionId.Ai && (s.toCellId == candidate || s.fromCellId == candidate));
                    bool hasBuilding = t.W.buildings.Exists(b => b.owner == FactionId.Ai && b.cellId == candidate);
                    if (hasBelt && !hasBuilding) { transit = candidate; break; }
                }
                Assert.AreNotEqual(-1, transit, "expected the crystal route to cross cell 3 or 6");

                // Clear the cell's AI craft and hand it to the player.
                t.W.crafts.RemoveAll(c => c.owner == FactionId.Ai && c.cellId == transit);
                WorldLookup.GetCell(t.W, transit).defenderCraftId = -1;
                Assert.IsTrue(t.S.Ownership.TransferCell(t.W, t.Catalog, transit, FactionId.Player).success);

                int sales = t.Ai.cumulativeSales;
                int other = transit == 3 ? 6 : 3;
                RunUntilAiSells(t, sales + 25);
                Assert.IsTrue(t.W.belts.Exists(s => s.owner == FactionId.Ai && s.toCellId == other && s.inactiveUntilTick == -1),
                    "the replanned crystal route should run through cell " + other);
                AssertAiStructuresLegal(t);
            }
        }
    }
}
