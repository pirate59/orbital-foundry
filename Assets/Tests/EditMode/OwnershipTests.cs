using System;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    public class OwnershipTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        BuildingState ext, refinery;

        /// <summary>Player factory in cell 1 with an ore token in flight. Returns the capture tick.</summary>
        int CaptureFactoryCellWithTokenInFlight()
        {
            t.Fortify(1, P);
            ext = t.Build(P, "extractor", 1, 2, 3);   // ore node 103
            refinery = t.Build(P, "refinery", 1, 5, 3);
            t.Line(P, 1, 2, 3, 1, 3, 3, 1, 4, 3, 1, 5, 3);
            for (int i = 0; i < 200 && t.W.tokens.Count == 0; i++) t.FactoryTick();
            Assert.Greater(t.W.tokens.Count, 0, "a token should be travelling");
            Assert.IsTrue(t.S.Ownership.TransferCell(t.W, t.Catalog, 1, FactionId.Ai).success);
            return t.W.tick;
        }

        [Test]
        public void CapturedStructuresFreezeForGraceTicksAndRecaptureAt1799Restores()
        {
            int captured = CaptureFactoryCellWithTokenInFlight();
            int grace = t.Catalog.rules.graceTicks;
            Assert.AreEqual(1800, grace);
            Assert.AreEqual(captured + grace, ext.inactiveUntilTick);
            foreach (var s in t.W.belts) Assert.AreEqual(captured + grace, s.inactiveUntilTick);
            var token = t.W.tokens[0];
            Assert.AreEqual(TokenStatus.Frozen, token.status);
            int frozenIndex = token.segmentIndex, frozenTicks = token.ticksUntilNextSegment;

            // A tactical encounter in the middle adds no ticks.
            t.ClearCraft();
            t.AddCraft(P, "fortress", 0, true);
            t.AddCraft(FactionId.Ai, "fortress", 1, false);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, P).success);
            int before = t.W.tick;
            for (int i = 0; i < 50; i++) t.Loop.Update(0.5f);
            Assert.AreEqual(before, t.W.tick);
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = P });
            t.W.crafts.RemoveAll(c => c.owner == FactionId.Ai);

            while (t.W.tick < captured + grace - 1) t.FactoryTick();
            Assert.AreEqual(captured + 1799, t.W.tick);
            Assert.AreEqual(frozenIndex, token.segmentIndex, "frozen token keeps its progress");
            Assert.AreEqual(frozenTicks, token.ticksUntilNextSegment);
            Assert.IsNotNull(WorldLookup.GetBuilding(t.W, ext.id));

            Assert.IsTrue(t.S.Ownership.TransferCell(t.W, t.Catalog, 1, P).success);
            Assert.AreEqual(-1, ext.inactiveUntilTick);
            Assert.AreEqual(-1, refinery.inactiveUntilTick);
            Assert.AreEqual(TokenStatus.Moving, token.status);
            t.FactoryTick();
            Assert.IsNotNull(WorldLookup.GetBuilding(t.W, ext.id), "restored structures do not expire");
        }

        [Test]
        public void StructuresAndStrandedTokensClearExactlyAtGraceExpiry()
        {
            int captured = CaptureFactoryCellWithTokenInFlight();
            while (t.W.tick < captured + 1799) t.FactoryTick();
            Assert.IsNotNull(WorldLookup.GetBuilding(t.W, ext.id));
            Assert.Greater(t.W.tokens.Count, 0);
            t.FactoryTick();
            Assert.AreEqual(captured + 1800, t.W.tick);
            Assert.IsNull(WorldLookup.GetBuilding(t.W, ext.id));
            Assert.IsNull(WorldLookup.GetBuilding(t.W, refinery.id));
            Assert.AreEqual(0, t.W.belts.Count);
            Assert.AreEqual(0, t.W.tokens.Count);
            Assert.AreEqual(-1, WorldLookup.GetNode(t.W, 103).extractorBuildingId);
        }

        [Test]
        public void PlayerHeadquartersCaptureIsDefeat()
        {
            t.ClearCraft();
            t.S.Ownership.TransferCell(t.W, t.Catalog, 0, FactionId.Ai);
            Assert.AreEqual(MatchOutcome.PlayerDefeat, t.W.outcome);
            Assert.AreEqual(MatchMode.Ended, t.W.mode);
            int tick = t.W.tick;
            t.Loop.Update(2f);
            Assert.AreEqual(tick, t.W.tick, "an ended match does not tick");
        }

        [Test]
        public void AiHeadquartersCaptureWithdrawsAiAndStopsItsFactoryButNotThePlayers()
        {
            t.BuildPlayerRelayChain();
            var aiExt = t.Build(FactionId.Ai, "extractor", 7, 1, 1);
            t.S.Ownership.TransferCell(t.W, t.Catalog, 7, P);
            Assert.IsTrue(t.Ai.suppressed);
            foreach (var c in t.W.crafts) if (c.owner == FactionId.Ai) Assert.IsTrue(c.withdrawn);
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 6).defenderCraftId, "AI defender assignments cleared");
            Assert.IsFalse(WorldLookup.IsBuildingActive(t.W, aiExt));
            Assert.AreEqual(MatchMode.Factory, t.W.mode);
            Assert.AreEqual(MatchOutcome.AiConceded, t.W.outcome, "capturing the headquarters wins outright");

            // AI-held cell 6 is now claimable without combat.
            var guard = WorldLookup.CraftsInCell(t.W, 0, P)[0];
            t.MustRun(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = guard.id, targetCellId = 6 });
            for (int i = 0; i < 400 && guard.cellId != 6; i++) t.Loop.TickNormal();
            Assert.AreEqual(6, guard.cellId);
            Assert.IsTrue(t.W.captureHolds.Exists(h => h.craftId == guard.id), "arriving starts the claim automatically");
            for (int i = 0; i < 120; i++) t.Loop.TickNormal();
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 6).owner);

            // Player production continues.
            for (int i = 0; i < 1500 && t.Player.cumulativeSales == 0; i++) t.Loop.TickNormal();
            Assert.Greater(t.Player.cumulativeSales, 0);
        }

        [Test]
        public void CapturingTheAiHeadquartersWinsOnItsOwn()
        {
            t.S.Ownership.TransferCell(t.W, t.Catalog, 7, P);
            Assert.AreEqual(MatchOutcome.AiConceded, t.W.outcome, "no sales needed for a military win");
            Assert.AreEqual(MatchMode.Factory, t.W.mode, "open-ended building continues");
        }

        /// <summary>Runs factory time with the player making and selling relays at a steady rate (credits a minute).</summary>
        void Trade(int ticks, int perMinute, bool made = true)
        {
            // Relays sell for 25: one every (25 * 600 / perMinute) ticks gives the rate.
            int every = Math.Max(1, 25 * 600 / perMinute);
            for (int i = 0; i < ticks; i++)
            {
                t.W.tick++;
                TradeService.Advance(t.W, t.Catalog);
                if (t.W.tick % every == 0)
                {
                    if (made) TradeService.RecordMade(t.W, P, "relay", 1);
                    TradeService.RecordSold(t.W, P, "relay", 1);
                }
                t.S.Ownership.EvaluateOutcome(t.W, t.Catalog.rules);
            }
        }

        [Test]
        public void HoldingTheTradeRateWinsOnItsOwn()
        {
            t.W.difficulty = Difficulty.Easy;
            Assert.AreEqual(6000, TradeService.Target(t.W, t.Catalog));
            Trade(400, 7500);
            Assert.AreEqual(MatchOutcome.None, t.W.outcome, "the rate has to be held for 30 seconds first");
            Assert.GreaterOrEqual(t.W.trade.rate, 6000);
            Trade(400, 7500);
            Assert.AreEqual(MatchOutcome.AiConceded, t.W.outcome, "trade alone wins, without the AI headquarters");
            Assert.AreEqual(MatchMode.Factory, t.W.mode);
        }

        [Test]
        public void ARateThatSlipsStartsTheHoldAgain()
        {
            t.W.difficulty = Difficulty.Easy;
            Trade(400, 7500);
            Assert.Greater(t.W.trade.heldSlices, 0, "at the target, holding");
            Trade(400, 7500, made: false); // the factory stops; only old stock is sold
            Assert.AreEqual(0, t.W.trade.heldSlices, "dropping below the target resets the hold");
            Assert.AreEqual(MatchOutcome.None, t.W.outcome);
        }

        [Test]
        public void SellingAStockpileDoesNotCount()
        {
            t.W.difficulty = Difficulty.Easy;
            Trade(1200, 60000, made: false); // goods saved up earlier, sold fast
            Assert.AreEqual(0, t.W.trade.rate, "only goods made in the window count");
            Assert.AreEqual(MatchOutcome.None, t.W.outcome);
        }

        [Test]
        public void HarderDifficultiesAskForMore()
        {
            t.W.difficulty = Difficulty.Medium;
            int medium = TradeService.Target(t.W, t.Catalog);
            t.W.difficulty = Difficulty.Hard;
            Assert.Greater(TradeService.Target(t.W, t.Catalog), medium);
            Assert.AreEqual(Difficulty.Medium, new WorldState().difficulty, "older saves are Medium");
        }
    }
}
