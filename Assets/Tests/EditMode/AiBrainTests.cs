using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// The AI's skills: situation assessment, stance, budget and targets, and how difficulty changes them.
    /// Map A: the player holds cell 0; the AI holds 3, 6 and 7 (its headquarters), with a Fortress in 6 (the old
    /// three-cell start, given by TestWorld.GiveAiOutposts; games now start each side on one cell).
    /// </summary>
    public class AiBrainTests
    {
        TestWorld t;
        AiBrain brain;
        const FactionId P = FactionId.Player, A = FactionId.Ai;

        [SetUp]
        public void SetUp() { t = new TestWorld(); t.GiveAiOutposts(); brain = new AiBrain(); profile = null; incomeFlowing = false; }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        // A profile to think with instead of the difficulty's preset (null: the preset).
        AiProfile profile;

        /// <summary>When Medium's grace period ends in this match: the preset's, shifted by the match's variety.</summary>
        int MediumGraceEnds()
        {
            return AiProfile.Varied(AiProfile.For(Difficulty.Medium), t.W, FactionId.Ai).EarliestAttackTick;
        }
        bool incomeFlowing;

        void Think()
        {
            if (profile == null) { brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory()); return; }
            if (incomeFlowing) t.S.Factory.Stats.AddCredits(A, 2000); // test setup: steady income
            using (AiSide.Enter(A, null, profile, "")) brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory());
        }

        /// <summary>A context for calling a skill's helper directly, as the brain last thought.</summary>
        AiContext BrainContext()
        {
            Think();
            var f = typeof(AiBrain).GetField("ctx", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (AiContext)f.GetValue(brain);
        }

        /// <summary>Test setup: the difficulty's preset with the industry gate open (its factory counts as built up).</summary>
        void IndustryBuiltUp(Difficulty d)
        {
            profile = AiProfile.For(d).Clone();
            profile.RecoupMinutes = 1e6f; // test setup: any fleet counts as one it could recover from
            if (!t.W.buildings.Exists(b => b.owner == A && b.typeId == "shipyard"))
            {
                var yard = CatalogRepository.GetBuilding(t.Catalog, "shipyard");
                var cellDef = WorldLookup.GetCellDef(t.Map, 7);
                for (int y = 0; y < cellDef.gridHeight; y++)
                    for (int x = 0; x < cellDef.gridWidth; x++)
                        if (!t.W.buildings.Exists(b => b.owner == A && b.typeId == "shipyard") && GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, yard, A, 7, x, y).success)
                        { t.Ai.currency += yard.cost; t.Build(A, "shipyard", 7, x, y); }
            }
            t.FactoryTick(); // starts the sales record
            incomeFlowing = true; // and income to pay for it
        }

        /// <summary>Thinks repeatedly while the clock runs on, long enough for the profile's reaction time.</summary>
        void ThinkFor(int ticks)
        {
            for (int i = 0; i < ticks; i += 10) { t.W.tick += 10; Think(); }
        }

        [Test]
        public void EachDifficultyHasItsOwnProfile()
        {
            var easy = AiProfile.For(Difficulty.Easy);
            var medium = AiProfile.For(Difficulty.Medium);
            var hard = AiProfile.For(Difficulty.Hard);
            Assert.Less(easy.Aggression, medium.Aggression);
            Assert.Less(medium.Aggression, hard.Aggression);
            Assert.Greater(easy.NeededEase, hard.NeededEase, "a cautious AI wants better odds");
            Assert.Greater(easy.ReactionTicks, hard.ReactionTicks, "and is slower to react");
            Assert.AreEqual(int.MaxValue, easy.EarliestAttackTick, "Easy never attacks");
            Assert.Less(hard.EarliestAttackTick, medium.EarliestAttackTick);
        }

        [Test]
        public void AThreatenedCellIsDefendedAtOnce()
        {
            for (int i = 0; i < 3; i++) t.AddCraft(P, "bomber", 2, false); // next to AI cells 3 and 6
            Think();
            Assert.IsTrue(brain.Model.MostThreatenedCell == 3 || brain.Model.MostThreatenedCell == 6, "threat on " + brain.Model.MostThreatenedCell);
            Assert.AreEqual(AiStance.Defend, brain.Decisions.Stance, "defending needs no reaction time");
            Assert.IsFalse(brain.Decisions.ResearchAllowed, "ships before research");
        }

        [Test]
        public void AnUnseenFleetStillCounts()
        {
            t.W.difficulty = Difficulty.Easy; // sees one cell beyond its own
            t.AddCraft(P, "fortress", 0, false); // three cells from the nearest AI cell
            Think();
            Assert.Greater(brain.Model.PlayerStrength, 0, "the player's fleet is known even where the AI cannot see it");
            Assert.AreEqual(-1, brain.Model.MostThreatenedCell, "but it is no threat it can see");
            Assert.AreEqual(AiStance.Build, brain.Decisions.Stance);
        }

        [Test]
        public void AnAttackTargetNeedsTheOdds()
        {
            t.Catalog.rules.aiRaids = true;
            Think();
            Assert.AreEqual(0, brain.Decisions.AttackCell, "its Fortress can take the player's lightly held headquarters: " + brain.Decisions.AttackReason);
            t.AddCraft(P, "fortress", 0, false);
            ThinkFor(100); // targets are chosen every 2.5 s
            Assert.AreEqual(-1, brain.Decisions.AttackCell, "with a Fortress there too, the odds are not good enough");
        }

        [Test]
        public void TheGracePeriodHoldsBackAttacks()
        {
            IndustryBuiltUp(Difficulty.Medium);
            t.Catalog.rules.aiRaids = true;
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            for (int i = 0; i < 4; i++) t.AddCraft(A, "bomber", 7, false);
            ThinkFor(1000);
            Assert.AreNotEqual(AiStance.Attack, brain.Decisions.Stance, "too early: " + brain.Report());
            t.W.tick = MediumGraceEnds();
            ThinkFor(400);
            Assert.AreEqual(AiStance.Attack, brain.Decisions.Stance, brain.Report());

            IndustryBuiltUp(Difficulty.Easy);
            ThinkFor(400);
            Assert.AreNotEqual(AiStance.Attack, brain.Decisions.Stance, "Easy never attacks");
        }

        [Test]
        public void ItCountersAPlayerCloseToATradeWin()
        {
            t.Catalog.rules.aiRaids = true;
            WorldLookup.GetFaction(t.W, FactionId.Player).trade.rate = TradeService.Target(t.W, t.Catalog) * 95 / 100;
            ThinkFor(400);
            Assert.AreEqual(AiStance.Counter, brain.Decisions.Stance, brain.Report());
            Assert.IsTrue(brain.Decisions.RaidNow, "it raids without waiting for the raid timer");

            t.W.difficulty = Difficulty.Easy;
            ThinkFor(400);
            Assert.AreNotEqual(AiStance.Counter, brain.Decisions.Stance, "Easy does not watch the player's economy");
        }

        [Test]
        public void ItExpandsTowardsNewOres()
        {
            t.Catalog.rules.aiExpands = true;
            Think();
            int cell = brain.Decisions.ExpansionCell;
            Assert.GreaterOrEqual(cell, 0, "a neutral cell to claim");
            Assert.AreEqual(FactionId.Neutral, WorldLookup.GetCell(t.W, cell).owner);
            StringAssert.Contains("new ores", brain.Decisions.ExpansionReason);
            StringAssert.Contains("AI Medium", brain.Report());
        }

        /// <summary>Test setup: an AI with an earning economy and three Bombers in cell 3, past its grace period.</summary>
        void ReadyToStrike()
        {
            t.Catalog.rules.aiRaids = true;
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            for (int i = 0; i < 3; i++) t.AddCraft(A, "bomber", 3, false);
            t.W.tick = MediumGraceEnds();
            t.W.ai.nextRaidTick = 0;
            ThinkFor(400); // past the reaction time into Attack
        }

        [Test]
        public void AStrikeGathersNextToItsTargetThenLaunches()
        {
            IndustryBuiltUp(Difficulty.Medium);
            ReadyToStrike();
            var d = brain.Decisions;
            Assert.AreEqual(AiStance.Attack, d.Stance, brain.Report());
            Assert.AreEqual(0, d.AttackCell);
            Assert.Greater(d.StrikeGroup.Count, 0, "ships picked for the strike");
            Assert.AreEqual(1, WorldLookup.CellDistance(t.Map, d.RallyCell, 0), "it gathers next to the target");
            Assert.IsFalse(d.LaunchNow, "not until the ships are there");
            StringAssert.Contains("gathering", d.StrikeReason);

            foreach (int id in d.StrikeGroup) WorldLookup.GetCraft(t.W, id).cellId = d.RallyCell; // test setup: they arrived
            ThinkFor(10);
            Assert.IsTrue(d.StrikeAssembled, d.StrikeReason);
            Assert.IsTrue(d.LaunchNow, d.StrikeReason);
        }

        [Test]
        public void FortsAtTheTargetCallForBombers()
        {
            ReadyToStrike();
            t.Fortify(0, P); // a Fort in the player's headquarters cell
            ThinkFor(30);
            Assert.AreEqual(0, brain.Decisions.AttackCell, brain.Report());
            Assert.AreEqual("bomber", brain.Decisions.BuildOrder[0], brain.Decisions.BuildReason);
        }

        [Test]
        public void ExpansionWaitsWhileDefendingAndHardClaimsTwoAtOnce()
        {
            t.Catalog.rules.aiExpands = true;
            t.Ai.currency += 5000;
            Think();
            Assert.IsTrue(brain.Decisions.ExpandNow, brain.Decisions.ExpandReason);
            Assert.AreEqual(1, brain.Decisions.MaxExpansions);
            for (int i = 0; i < 3; i++) t.AddCraft(P, "bomber", 2, false);
            ThinkFor(30); // the situation is reassessed every few thinks
            Assert.AreEqual(AiStance.Defend, brain.Decisions.Stance);
            Assert.IsFalse(brain.Decisions.ExpandNow, "defending first");

            t.W.difficulty = Difficulty.Hard;
            t.ClearCraft();
            ThinkFor(100);
            Assert.AreEqual(2, brain.Decisions.MaxExpansions, "a bold AI claims two cells at once");
        }

        /// <summary>Credits the AI keeps from selling ten relays through a depot, at a difficulty.</summary>
        int AiEarns(Difficulty difficulty)
        {
            using (var w = new TestWorld())
            {
                w.W.difficulty = difficulty;
                var def = CatalogRepository.GetBuilding(w.Catalog, "sale_depot");
                var cellDef = WorldLookup.GetCellDef(w.Map, 7);
                BuildingState depot = null;
                for (int y = 0; y < cellDef.gridHeight && depot == null; y++)
                    for (int x = 0; x < cellDef.gridWidth && depot == null; x++)
                        if (GridRules.CanPlaceBuilding(w.W, w.Catalog, w.Map, def, A, 7, x, y).success) depot = w.Build(A, "sale_depot", 7, x, y);
                WorldLookup.AddItem(depot.inventory, "relay", 10);
                int before = w.Ai.currency;
                w.FactoryTick();
                return w.Ai.currency - before;
            }
        }

        [Test]
        public void EveryDifficultyEarnsThePriceLikeThePlayer()
        {
            foreach (var difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                Assert.AreEqual(250, AiEarns(difficulty), difficulty + ": the same prices as the player");
        }

        [Test]
        public void BattleTargetScoringGrowsSharperWithDifficulty()
        {
            var bomber = t.AddCraft(A, "bomber", 3, false);
            var guard = t.AddCraft(P, "guard", 2, false);
            var fortress = t.AddCraft(P, "fortress", 2, false);
            var hit = new CombatPreview { attackDamageAfterArmour = 6, counterDamageAfterArmour = 2, defenderSurvives = false, defenderHealth = 6, defenderMaxHealth = 10 };
            var guardTarget = new CombatResolver.Target { Craft = guard };
            var fortressTarget = new CombatResolver.Target { Craft = fortress };
            Assert.AreEqual(6, BattleTargeting.Score(t.W, t.Catalog, AiProfile.For(Difficulty.Easy), bomber, guardTarget, hit), "Easy: any hit, by its damage");
            Assert.AreEqual(6 * 3 - 2 * 2 + 20, BattleTargeting.Score(t.W, t.Catalog, AiProfile.For(Difficulty.Medium), bomber, guardTarget, hit));
            var hard = AiProfile.For(Difficulty.Hard);
            Assert.Greater(BattleTargeting.Score(t.W, t.Catalog, hard, bomber, fortressTarget, hit), BattleTargeting.Score(t.W, t.Catalog, hard, bomber, guardTarget, hit),
                "Hard values killing the stronger ship");
        }

        [Test]
        public void ALosingAttackRetreatsUnlessEasy()
        {
            var attacker = t.AddCraft(A, "guard", 2, false);  // an attacker next to the player's cell 1
            var defenders = new System.Collections.Generic.List<CraftState>();
            for (int i = 0; i < 2; i++) defenders.Add(t.AddCraft(P, "fortress", 1, false));
            t.SetOwner(1, P);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, A).success);
            Assert.IsFalse(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Medium)), "the start of the battle is noted");
            // Test setup: three rounds on, it has lost 60% of its health and taken only 10% off the defence.
            t.W.encounter.round += 3;
            attacker.currentHealth = attacker.currentHealth * 4 / 10;
            foreach (var d in defenders) d.currentHealth = d.currentHealth * 9 / 10;
            Assert.IsTrue(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Medium)), "Medium cuts its losses");
            Assert.IsTrue(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Hard)), "and so does Hard");
            Assert.IsFalse(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Easy)), "Easy fights on until round 8");
        }

        [Test]
        public void AnAttackThatIsWinningTheExchangeFightsOn()
        {
            var attacker = t.AddCraft(A, "fortress", 2, false); // one ship against the player's guarded cell 1
            var defender = t.AddCraft(P, "fortress", 1, false);
            t.SetOwner(1, P);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, A).success);
            Assert.IsFalse(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Hard)));
            // Test setup: two rounds on, it is down 40% but has taken 80% off the defence: deep in front.
            t.W.encounter.round += 2;
            attacker.currentHealth = attacker.currentHealth * 6 / 10;
            defender.currentHealth = System.Math.Max(1, defender.currentHealth * 2 / 10);
            foreach (Difficulty d in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
                Assert.IsFalse(BattleRetreat.ShouldRetreat(t.W, t.Catalog, t.Map, AiProfile.For(d)), d + " presses on with a winning exchange");
        }

        [Test]
        public void SpareShipsReinforceADefenceUnlessEasy()
        {
            t.AddCraft(P, "bomber", 5, false);                // the player attacks AI cell 6 from cell 5
            var spare = t.AddCraft(A, "bomber", 3, false);    // outside the battle, within reach of it
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 6, P).success);
            Assert.IsFalse(t.W.encounter.participantCraftIds.Contains(spare.id));
            var move = BattleReinforcement.Choose(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Medium), new System.Collections.Generic.HashSet<int>());
            Assert.IsNotNull(move, "Medium brings it in to defend");
            Assert.AreEqual(spare.id, move.craftId);
            Assert.IsTrue(t.W.encounter.battleCellIds.Contains(move.targetCellId));
            Assert.IsNull(BattleReinforcement.Choose(t.W, t.Catalog, t.Map, AiProfile.For(Difficulty.Easy), new System.Collections.Generic.HashSet<int>()), "Easy fights with what is there");
        }

        [Test]
        public void TheHeadquartersIsGuardedFirst()
        {
            Think();
            Assert.AreEqual(7, brain.Decisions.GuardOrder[0], "the AI headquarters is worth most");
            Assert.AreEqual(3, brain.Decisions.GuardOrder.Count, "every AI cell is ranked");
        }

        [Test]
        public void TopTierGoodsEarnMorePerMegawatt()
        {
            var f = t.Ai;
            Assert.Greater(AiEconomics.CreditsPerMwMinute(t.Catalog, f, "quantum_core"), AiEconomics.CreditsPerMwMinute(t.Catalog, f, "relay"));
            Assert.Greater(AiEconomics.CreditsPerMwMinute(t.Catalog, f, "relay"), AiEconomics.CreditsPerMwMinute(t.Catalog, f, "iron_bar"));
        }

        [Test]
        public void OnceEarningItBuildsTheBestValueChainsFirst()
        {
            Think();
            StringAssert.Contains("simplest first", brain.Decisions.ProductReason);
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            ThinkFor(60);
            var order = brain.Decisions.ProductOrder;
            Assert.Greater(order.Count, 0);
            for (int i = 1; i < order.Count; i++)
                Assert.GreaterOrEqual(AiEconomics.CreditsPerMwMinute(t.Catalog, t.Ai, order[i - 1]), AiEconomics.CreditsPerMwMinute(t.Catalog, t.Ai, order[i]), "by value per MW");
        }

        [Test]
        public void ResearchHasAGoalUnlessEasy()
        {
            ThinkFor(60);
            Assert.IsNotEmpty(brain.Decisions.ResearchTarget, brain.Decisions.ResearchReason);
            StringAssert.Contains("unlocks", brain.Decisions.ResearchReason);
            t.W.difficulty = Difficulty.Easy;
            ThinkFor(600); // research goals are reviewed every 15 s
            Assert.IsEmpty(brain.Decisions.ResearchTarget, "Easy buys the cheapest level");
        }

        [Test]
        public void NeedsAnalysisSpotsAnIonShortfall()
        {
            t.Ai.ion = 0;
            Think();
            StringAssert.Contains("Ion", brain.Decisions.NeedsReason);
            Assert.AreEqual(0, brain.Decisions.IonWanted, "starting out, credits go to income before more Ion");
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            ThinkFor(120); // needs are reviewed every 3 s
            Assert.Greater(brain.Decisions.IonWanted, 0, "once earning, the shortfall sends it after more Ion");
        }

        [Test]
        public void PowerGoesFirstWhenHeadroomIsThinUnlessEasy()
        {
            t.Ai.powerSupply = 20; t.Ai.powerDemand = 19; // test setup: 1 MW spare
            Think();
            Assert.IsTrue(brain.Decisions.PowerFirst, brain.Decisions.PowerReason);
            t.W.difficulty = Difficulty.Easy;
            ThinkFor(60);
            Assert.IsFalse(brain.Decisions.PowerFirst, "Easy waits until it is nearly at its limit");
        }

        [Test]
        public void AStarvedMachineLeadsToAReplan()
        {
            // An AI refinery set to a recipe but fed nothing.
            var def = CatalogRepository.GetBuilding(t.Catalog, "refinery");
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            BuildingState refinery = null;
            for (int y = 0; y < cellDef.gridHeight && refinery == null; y++)
                for (int x = 0; x < cellDef.gridWidth && refinery == null; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, A, 7, x, y).success) refinery = t.Build(A, "refinery", 7, x, y);
            Assert.IsNotEmpty(refinery.selectedRecipeId);
            bool replanned = false;
            for (int i = 0; i < 1500 && !replanned; i++)
            {
                t.FactoryTick();
                if (i % 3 == 0) { Think(); replanned |= brain.Decisions.ReplanNow; }
            }
            Assert.Greater(brain.Decisions.StarvedMachines, 0, brain.Decisions.BottleneckReason);
            Assert.IsTrue(replanned, "a minute stuck asks the planner to replan");
        }

        /// <summary>Test setup: an AI refinery in its headquarters cell, set to refine ore.</summary>
        BuildingState AiRefinery()
        {
            var def = CatalogRepository.GetBuilding(t.Catalog, "refinery");
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            for (int y = 0; y < cellDef.gridHeight; y++)
                for (int x = 0; x < cellDef.gridWidth; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, A, 7, x, y).success) return t.Build(A, "refinery", 7, x, y);
            return null;
        }

        [Test]
        public void AMachineWorkingFlatOutIsNotLoadedFurther()
        {
            var refinery = AiRefinery();
            // Keep it fed so it never rests: an endless supply of its ingredient.
            var recipe = CatalogRepository.GetRecipe(t.Catalog, refinery.selectedRecipeId);
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            for (int i = 0; i < 400; i++)
            {
                foreach (var input in recipe.inputs)
                    if (WorldLookup.CountItem(refinery.inventory, input.itemId) < input.quantity * 2) WorldLookup.AddItem(refinery.inventory, input.itemId, input.quantity);
                WorldLookup.RemoveItem(refinery.inventory, recipe.outputItemId, WorldLookup.CountItem(refinery.inventory, recipe.outputItemId));
                t.FactoryTick();
                if (i % 3 == 0) Think();
            }
            Assert.IsTrue(brain.Decisions.Saturated.Contains(refinery.id), brain.Decisions.ThroughputReason);
            t.W.difficulty = Difficulty.Easy;
            ThinkFor(500); // throughput is reviewed every 12 s
            Assert.AreEqual(0, brain.Decisions.Saturated.Count, "Easy reuses machines as they are");
        }

        [Test]
        public void NewChainsStartAtHomeUntilEarningThenWhereTheOresAre()
        {
            ThinkFor(60);
            Assert.AreEqual(-1, brain.Decisions.NewChainCell, "starting out: the planner's own choice, home");
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            ThinkFor(700); // where chains start is reviewed every 20 s
            Assert.GreaterOrEqual(brain.Decisions.NewChainCell, 0, brain.Decisions.GrowthReason);
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, brain.Decisions.NewChainCell).owner);
            StringAssert.Contains("free ores", brain.Decisions.GrowthReason);
        }

        /// <summary>Test setup: the player attacks AI cell 6 from cell 5; returns the AI's ships in the battle that have not acted.</summary>
        System.Collections.Generic.List<CraftState> DefendCellSix(params (string type, int cell)[] aiShips)
        {
            t.ClearCraft(); // only the ships placed here (map A starts with an AI Fortress in cell 6)
            t.AddCraft(P, "bomber", 5, false);
            var mine = new System.Collections.Generic.List<CraftState>();
            foreach (var ship in aiShips) mine.Add(t.AddCraft(A, ship.type, ship.cell, false));
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 6, P).success);
            mine.RemoveAll(k => !t.W.encounter.participantCraftIds.Contains(k.id));
            return mine;
        }

        [Test]
        public void ShipsCloseUpOnTheShieldUnlessEasy()
        {
            var mine = DefendCellSix(("fortress", 7), ("bomber", 6));
            var move = BattleFormation.Choose(t.W, t.Catalog, t.Map, t.S.Movement, AiProfile.For(Difficulty.Medium), mine);
            Assert.IsNotNull(move, "the Bomber joins the Fortress");
            Assert.AreEqual(7, move.targetCellId);
            Assert.IsNull(BattleFormation.Choose(t.W, t.Catalog, t.Map, t.S.Movement, AiProfile.For(Difficulty.Easy), mine));
        }

        [Test]
        public void ARechargingLancerBacksOffOnHard()
        {
            var mine = DefendCellSix(("lancer", 6));
            mine[0].cooldownTurns = 2; // test setup: just fired
            var move = LancerPlay.Choose(t.W, t.Catalog, t.Map, t.S.Movement, AiProfile.For(Difficulty.Hard), mine);
            Assert.IsNotNull(move, "it backs off out of reach");
            Assert.GreaterOrEqual(WorldLookup.CellDistance(t.Map, move.targetCellId, 5), 2, "away from the Bomber in cell 5");
            Assert.IsNull(LancerPlay.Choose(t.W, t.Catalog, t.Map, t.S.Movement, AiProfile.For(Difficulty.Medium), mine), "Medium leaves it be");
        }

        [Test]
        public void SupplyLinesCountAsRaidTargets()
        {
            t.Catalog.rules.aiRaids = true;
            t.W.difficulty = Difficulty.Hard;
            var def = CatalogRepository.GetBuilding(t.Catalog, "storage");
            var cellDef = WorldLookup.GetCellDef(t.Map, 0);
            bool built = false;
            t.Player.currency += 1000;
            for (int y = 0; y < cellDef.gridHeight && !built; y++)
                for (int x = 0; x < cellDef.gridWidth && !built; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, P, 0, x, y).success) { t.Build(P, "storage", 0, x, y); built = true; }
            Think();
            StringAssert.Contains("1 supply links", brain.Decisions.AttackReason);
        }

        [Test]
        public void AQueuingBeltLineIsUpgradedUnlessEasy()
        {
            t.Ai.currency += 5000;
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            BeltSegmentState belt = null, next = null;
            for (int y = 0; y < cellDef.gridHeight && next == null; y++)
                for (int x = 0; x + 2 < cellDef.gridWidth && next == null; x++)
                {
                    var r1 = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = A, fromCellId = 7, fromX = x, fromY = y, toCellId = 7, toX = x + 1, toY = y });
                    if (!r1.success) continue;
                    belt = t.W.belts[t.W.belts.Count - 1];
                    var r2 = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = A, fromCellId = 7, fromX = x + 1, fromY = y, toCellId = 7, toX = x + 2, toY = y });
                    if (r2.success) next = t.W.belts[t.W.belts.Count - 1];
                }
            // Test setup: an item stuck partway, on the first belt waiting for the next, think after think.
            t.W.tokens.Add(new ItemTokenState { id = WorldLookup.AllocateId(t.W), itemId = "iron_bar", status = TokenStatus.Waiting, routeSegmentIds = new System.Collections.Generic.List<int> { belt.id, next.id }, segmentIndex = 0, targetBuildingId = -1, sourceBuildingId = -1, quantity = 1 });
            bool upgrade = false;
            for (int i = 0; i < 150 && !upgrade; i++) { t.W.tick += 10; Think(); upgrade = brain.Decisions.UpgradeBeltId == belt.id; }
            Assert.IsTrue(upgrade, brain.Decisions.BeltReason);

            t.W.difficulty = Difficulty.Easy;
            ThinkFor(1500);
            Assert.AreEqual(-1, brain.Decisions.UpgradeBeltId, "Easy never upgrades belts");
        }

        /// <summary>
        /// Test setup: an AI ore extractor on node (1,1) of its headquarters cell 7, belted to a refinery a few tiles
        /// away. Returns the extractor and refinery.
        /// </summary>
        (BuildingState extractor, BuildingState refinery) AiOreLine()
        {
            var extractor = t.Build(A, "extractor", 7, 1, 1);
            var refinery = t.Build(A, "refinery", 7, 5, 1);
            if (refinery.selectedRecipeId != "refine_ore")
                t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = A, buildingId = refinery.id, contentId = "refine_ore" });
            var plan = t.S.AiBelts.Plan(t.W, t.Catalog, t.Map, extractor.id, refinery.id);
            foreach (var st in plan.steps)
                t.MustRun(new GameCommand { kind = CommandKind.PlaceBelt, actor = A, fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY });
            return (extractor, refinery);
        }

        [Test]
        public void ASeparateLaneSharesNoBeltOrTile()
        {
            t.Ai.currency += 5000;
            var line = AiOreLine();
            var tiles = new System.Collections.Generic.HashSet<int>();
            foreach (var b in t.W.belts) { tiles.Add(BeltNetwork.VertexKey(b.fromCellId, b.fromX, b.fromY)); tiles.Add(BeltNetwork.VertexKey(b.toCellId, b.toX, b.toY)); }
            var lane = t.S.AiBelts.PlanBetween(t.W, t.Catalog, t.Map, A, 7, 1, 1, 7, 5, 1, true);
            Assert.IsTrue(lane.success, lane.reason);
            Assert.Greater(lane.steps.Count, 0, "a new lane, not the existing one");
            for (int i = 0; i < lane.steps.Count - 1; i++)
                Assert.IsFalse(tiles.Contains(BeltNetwork.VertexKey(lane.steps[i].toCellId, lane.steps[i].toX, lane.steps[i].toY)), "crosses no tile another belt touches");
        }

        [Test]
        public void AHeldBackExtractorGetsASecondLane()
        {
            t.Ai.currency += 5000;
            t.Research(A, "multi_chute", 1); // two chutes
            var line = AiOreLine();
            bool laned = false;
            for (int i = 0; i < 2000 && !laned; i++)
            {
                t.FactoryTick();
                if (i % 3 == 0) { Think(); laned = brain.Decisions.RefactorCommands.Count > 0; }
            }
            Assert.IsTrue(laned, brain.Decisions.RefactorReason);
            StringAssert.Contains("second lane", brain.Decisions.RefactorReason);
        }

        [Test]
        public void ABackedUpMachineWithNowhereToSellGetsAnOutlet()
        {
            t.Ai.currency += 5000;
            var refinery = AiRefinery();
            var recipe = CatalogRepository.GetRecipe(t.Catalog, refinery.selectedRecipeId);
            bool outlet = false;
            for (int i = 0; i < 1500 && !outlet; i++)
            {
                // Test setup: ingredients to hand but its store full of product it cannot send anywhere.
                foreach (var input in recipe.inputs)
                    if (WorldLookup.CountItem(refinery.inventory, input.itemId) < input.quantity) WorldLookup.AddItem(refinery.inventory, input.itemId, input.quantity);
                int room = CatalogRepository.GetBuilding(t.Catalog, "refinery").inventoryCapacity - WorldLookup.TotalItems(refinery.inventory);
                if (room > 0) WorldLookup.AddItem(refinery.inventory, recipe.outputItemId, room);
                t.FactoryTick();
                if (i % 3 == 0) { Think(); outlet = brain.Decisions.OutletCommands.Exists(k => k.kind == CommandKind.PlaceBuilding && k.contentId == "sale_depot"); }
            }
            Assert.IsTrue(outlet, brain.Decisions.OutletReason);
        }

        [Test]
        public void ALostCellIsRememberedAndWorthRetaking()
        {
            t.W.buildings.Add(new BuildingState { id = WorldLookup.AllocateId(t.W), owner = A, typeId = "refinery", cellId = 3, x = 5, y = 5 }); // test setup
            Think();
            t.SetOwner(3, P);
            ThinkFor(200); // losses are reviewed every 5 s
            Assert.IsTrue(brain.Decisions.LostCells.ContainsKey(3), brain.Decisions.LostReason);
            t.SetOwner(3, A);
            ThinkFor(200);
            Assert.IsFalse(brain.Decisions.LostCells.ContainsKey(3), "held again");
        }

        [Test]
        public void ShortOfPowerItRushesArrayGoodsToStorageThenPutsItBack()
        {
            t.Ai.currency += 5000;
            var def = CatalogRepository.GetBuilding(t.Catalog, "storage");
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            BuildingState storage = null;
            for (int y = 0; y < cellDef.gridHeight && storage == null; y++)
                for (int x = 0; x < cellDef.gridWidth && storage == null; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, A, 7, x, y).success) storage = t.Build(A, "storage", 7, x, y);
            t.Ai.powerSupply = 20; t.Ai.powerDemand = 30; // test setup: short of power, HQ slots free, no array goods
            Think();
            var raise = brain.Decisions.PriorityCommands.Find(k => k.buildingId == storage.id);
            Assert.IsNotNull(raise, brain.Decisions.SolarReason);
            Assert.AreEqual("very_high", raise.contentId);
            t.MustRun(raise);

            t.Ai.powerSupply = 40; t.Ai.powerDemand = 10; // power has caught up
            Assert.IsTrue(ThinkUntil(200, () => brain.Decisions.PriorityCommands.Exists(k => k.buildingId == storage.id && k.contentId == "normal")),
                "it puts back the priority it raised: " + brain.Decisions.SolarReason);
        }

        /// <summary>Thinks every 10 ticks until the condition holds, or the time runs out.</summary>
        bool ThinkUntil(int ticks, System.Func<bool> done)
        {
            for (int i = 0; i < ticks; i += 10) { t.W.tick += 10; Think(); if (done()) return true; }
            return false;
        }

        /// <summary>The first free spot for a building of this type in the AI's headquarters cell 7.</summary>
        BuildingState AiBuild(string type)
        {
            var def = CatalogRepository.GetBuilding(t.Catalog, type);
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            for (int y = 0; y < cellDef.gridHeight; y++)
                for (int x = 0; x < cellDef.gridWidth; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, A, 7, x, y).success) return t.Build(A, type, 7, x, y);
            return null;
        }

        [Test]
        public void TheResearchQueueGrowsWithDifficulty()
        {
            t.Ai.currency += 100000; t.Ai.ion += 100000;
            // The rule is the player's (five queued); how many the AI keeps queued is its choice by difficulty.
            int queued = 0;
            for (int tries = 0; tries < 10; tries++)
            {
                ResearchNodeDefinition open = null;
                foreach (var node in t.Catalog.research)
                    if (ResearchService.CheckPurchase(t.W, t.Catalog, t.Ai, node) == null) { open = node; break; }
                if (open == null) break;
                t.MustRun(new GameCommand { kind = CommandKind.Research, actor = A, contentId = open.id });
                queued++;
            }
            Assert.AreEqual(ResearchService.MaxQueue, queued, "the AI may queue as many as the player");
            Assert.AreEqual(1, AiProfile.For(Difficulty.Easy).ResearchQueue);
            Assert.AreEqual(2, AiProfile.For(Difficulty.Medium).ResearchQueue);
            Assert.AreEqual(3, AiProfile.For(Difficulty.Hard).ResearchQueue);
        }

        [Test]
        public void AShipyardBuildingAShipGetsHighPriorityUntilItLaunches()
        {
            t.Ai.currency += 5000;
            var yard = AiBuild("shipyard");
            Assert.IsNotNull(yard, "test setup: room for a shipyard");
            yard.orderedCraftTypeId = "guard"; // test setup: a ship on order
            Think();
            var raise = brain.Decisions.PriorityCommands.Find(k => k.buildingId == yard.id);
            Assert.IsNotNull(raise, brain.Decisions.PriorityReason);
            Assert.AreEqual("high", raise.contentId);
            t.MustRun(raise);

            yard.orderedCraftTypeId = null; // launched
            Assert.IsTrue(ThinkUntil(200, () => brain.Decisions.PriorityCommands.Exists(k => k.buildingId == yard.id && k.contentId == "normal")),
                brain.Decisions.PriorityReason);

            t.W.difficulty = Difficulty.Easy;
            yard.orderedCraftTypeId = "guard";
            Assert.IsFalse(ThinkUntil(200, () => brain.Decisions.PriorityCommands.Exists(k => k.buildingId == yard.id)), "Easy leaves priorities alone");
        }

        [Test]
        public void ABusyAsteroidWithoutStorageGetsAHub()
        {
            t.Ai.currency += 20000;
            Assert.IsNotNull(AiBuild("shipyard"), "test setup: hubs come after the first shipyard");
            for (int i = 0; i < StorageHubSkill.MachinesForHub; i++) Assert.IsNotNull(AiBuild("refinery"), "test setup: room for machines");
            foreach (var b in t.W.buildings.ToArray())
                if (b.owner == A && b.cellId == 7 && CatalogRepository.GetBuilding(t.Catalog, b.typeId).kind == BuildingKind.Storage) t.W.buildings.Remove(b);
            Think();
            Assert.IsTrue(brain.Decisions.HubCommands.Exists(k => k.kind == CommandKind.PlaceBuilding && k.contentId == "storage" && k.fromCellId == 7),
                brain.Decisions.HubReason);
            foreach (var k in brain.Decisions.HubCommands) t.MustRun(k);

            brain.Decisions.HubCommands.Clear();
            ThinkFor(400);
            Assert.AreEqual(0, brain.Decisions.HubCommands.Count, "one hub is enough: " + brain.Decisions.HubReason);
        }

        [Test]
        public void ADeadEndBeltAndAnOrphanMachineAreClearedAway()
        {
            t.Ai.currency += 5000;
            var orphan = AiBuild("refinery");
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            BeltSegmentState stub = null;
            for (int y = cellDef.gridHeight - 1; y >= 0 && stub == null; y--)
                for (int x = cellDef.gridWidth - 2; x >= 0 && stub == null; x--)
                    if (WorldLookup.GetBuildingAt(t.W, 7, x + 1, y) == null && GridRules.FootprintAt(t.W, t.Catalog, 7, x + 1, y) == null
                        && t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = A, fromCellId = 7, fromX = x, fromY = y, toCellId = 7, toX = x + 1, toY = y }).success)
                        stub = t.W.belts[t.W.belts.Count - 1];
            Assert.IsNotNull(stub, "test setup: a belt to nowhere");
            bool belt = false, machine = false;
            for (int i = 0; i < 3000 && !(belt && machine); i++)
            {
                t.FactoryTick();
                if (i % 3 != 0) continue;
                Think();
                foreach (var k in brain.Decisions.RelayoutCommands)
                {
                    if (k.beltId == stub.id) belt = true;
                    if (k.buildingId == orphan.id) machine = true;
                }
            }
            Assert.IsTrue(belt, "the dead-end belt goes: " + brain.Decisions.RelayoutReason);
            Assert.IsTrue(machine, "the orphan machine goes: " + brain.Decisions.RelayoutReason);
        }

        [Test]
        public void WithDronesItRefinesAtTheOreUnlessEasy()
        {
            Think();
            Assert.AreEqual(0, brain.Decisions.SmeltCells.Count, "no drones yet: " + brain.Decisions.SupplyReason);
            UnlockDrones();
            Think(); // unlocking drones runs it at once
            Assert.Greater(brain.Decisions.SmeltCells.Count, 0, brain.Decisions.SupplyReason);

            t.W.difficulty = Difficulty.Easy;
            ThinkFor(2100);
            Assert.AreEqual(0, brain.Decisions.SmeltCells.Count, "Easy flies raw ore");
        }
    
        /// <summary>Test setup: the AI researches Drone Pads and their prerequisites.</summary>
        void UnlockDrones()
        {
            var pad = CatalogRepository.GetBuilding(t.Catalog, "drone_pad");
            var gate = ResearchService.BuildingGate(t.Catalog, pad.id);
            Assert.IsNotNull(gate, "test setup: research unlocks drone pads");
            var stack = new System.Collections.Generic.List<ResearchNodeDefinition> { gate };
            for (int i = 0; i < stack.Count; i++)
                foreach (var p in stack[i].prerequisites) stack.Add(CatalogRepository.GetResearch(t.Catalog, p.nodeId));
            for (int i = stack.Count - 1; i >= 0; i--) t.Research(A, stack[i].id, 1);
            Assert.IsTrue(ResearchService.IsBuildingUnlocked(t.Catalog, t.Ai, pad.id), "test setup: drones researched");
        }

        [Test]
        public void OresOnDifferentAsteroidsMakeDronesTheResearchGoal()
        {
            t.Dispose();
            // Copper only on its fortified asteroids, iron only at home: nothing needing both can be made on one.
            t = new TestWorld("map_a", map =>
            {
                map.cells.Find(c => c.id == 7).nodes.RemoveAll(n => n.itemId == "copper_ore");
                foreach (int id in new[] { 3, 6 }) map.cells.Find(c => c.id == id).nodes.RemoveAll(n => n.itemId == "ore");
            });
            t.GiveAiOutposts();
            brain = new AiBrain();
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales; // earning, so it has products in view
            var gate = ResearchService.BuildingGate(t.Catalog, "drone_pad");
            Assert.IsTrue(ThinkUntil(1000, () => brain.Decisions.ResearchTarget == gate.id), brain.Decisions.ResearchReason);
            StringAssert.Contains("different asteroids", brain.Decisions.ResearchReason);
        }

        [Test]
        public void ARefineryFedFlownInOreMovesOutToTheOre()
        {
            t.Ai.currency += 5000;
            UnlockDrones();
            var refinery = AiBuild("refinery");
            refinery.selectedRecipeId = "refine_copper"; // test setup
            var extractor = new BuildingState { id = WorldLookup.AllocateId(t.W), owner = A, typeId = "extractor", cellId = 6, x = 0, y = 0 };
            t.W.buildings.Add(extractor);
            // Test setup: a drone flight from asteroid 6 to 7 carrying its copper ore.
            var flight = new BeltSegmentState { id = WorldLookup.AllocateId(t.W), owner = A, fromCellId = 6, fromX = 0, fromY = 1, toCellId = 7, toX = 0, toY = 0 };
            t.W.belts.Add(flight);
            t.W.tokens.Add(new ItemTokenState { id = WorldLookup.AllocateId(t.W), itemId = "copper_ore", status = TokenStatus.Moving,
                routeSegmentIds = new System.Collections.Generic.List<int> { flight.id }, segmentIndex = 0,
                targetBuildingId = refinery.id, sourceBuildingId = extractor.id, quantity = 1 });
            Think(); // drones just unlocked: outposts picked
            Assert.IsTrue(brain.Decisions.SmeltCells.Contains(6), "test setup: asteroid 6 refines for others: " + brain.Decisions.SupplyReason);
            Assert.IsTrue(ThinkUntil(1500, () => brain.Decisions.SupplyCommands.Exists(k => k.kind == CommandKind.Dismantle && k.buildingId == refinery.id)),
                brain.Decisions.SupplyReason);
        }
    
        [Test]
        public void ATargetWithNowhereBesideItToGatherIsPassedOver()
        {
            // Test setup: the player holds cell 0 and every cell next to it, so a strike has nowhere to gather beside 0.
            foreach (var cell in t.W.cells)
                if (WorldLookup.CellDistance(t.Map, 0, cell.id) == 1) t.SetOwner(cell.id, P);
            t.Catalog.rules.aiRaids = true;
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            for (int i = 0; i < 6; i++) t.AddCraft(A, "bomber", 7, false);
            t.W.tick = MediumGraceEnds();
            ThinkFor(400);
            Assert.AreNotEqual(0, brain.Decisions.AttackCell, "cell 0 cannot be struck yet: " + brain.Decisions.AttackReason);
            if (brain.Decisions.AttackCell >= 0)
                foreach (var cell in t.W.cells)
                    if (WorldLookup.CellDistance(t.Map, brain.Decisions.AttackCell, cell.id) == 1 && cell.owner != P) return; // its target has a gathering cell
            if (brain.Decisions.AttackCell >= 0) Assert.Fail("the target it chose has nowhere beside it to gather: " + brain.Decisions.AttackReason);
        }
    
        /// <summary>Test setup: the player's cell 1, held by a Fort and a defending Guard; and an AI fleet of the given ships.</summary>
        System.Collections.Generic.List<CraftState> FortifiedTargetAndFleet(params string[] fleet)
        {
            t.Fortify(1, P);
            t.AddCraft(P, "guard", 1, true);
            var list = new System.Collections.Generic.List<CraftState>();
            foreach (var type in fleet) list.Add(t.AddCraft(A, type, 2, false));
            return list;
        }

        [Test]
        public void ALoneDefenceShipIsNotCombatReadyAgainstAFort()
        {
            var fleet = FortifiedTargetAndFleet("guard");
            var sim = AiEngagement.Simulate(t.W, t.Catalog, fleet, 1);
            Assert.IsFalse(AiEngagement.Ready(t.Catalog, fleet, sim, AiProfile.For(Difficulty.Hard), false), "one Guard is no strike");
        }

        [Test]
        public void AttackShipsWinTheWholeBattleForAFort()
        {
            var fleet = FortifiedTargetAndFleet("bomber", "bomber", "bomber");
            var sim = AiEngagement.Simulate(t.W, t.Catalog, fleet, 1);
            Assert.IsTrue(sim.Won, "three Bombers break the Fort and its Guard within the round limit (" + sim.Rounds + " rounds)");
            Assert.LessOrEqual(sim.Rounds, t.Catalog.rules.battleRoundLimit);
            Assert.IsTrue(AiEngagement.Ready(t.Catalog, fleet, sim, AiProfile.For(Difficulty.Hard), false), "kept " + sim.Kept);
        }

        [Test]
        public void DefenceShipsAloneGoOnlyInTheNumbersTheirTradingValueNeeds()
        {
            var hard = AiProfile.For(Difficulty.Hard);
            var fleet = FortifiedTargetAndFleet("guard");
            // Add Guards until the whole battle, played out, is won: the count comes from what each Guard trades
            // against a Fort and its defender (1 HP a hit on the Fort, return fire back), not a fixed number.
            AiEngagement.Result sim = AiEngagement.Simulate(t.W, t.Catalog, fleet, 1);
            while (!AiEngagement.Ready(t.Catalog, fleet, sim, hard, false) && fleet.Count < 30)
            {
                fleet.Add(t.AddCraft(A, "guard", 2, false));
                sim = AiEngagement.Simulate(t.W, t.Catalog, fleet, 1);
            }
            Assert.IsTrue(sim.Won, "enough Guards win in the end");
            Assert.Greater(fleet.Count, 2, "but it takes many of them against a Fort (" + fleet.Count + ")");
            var bombers = FortifiedTargetAndFleet();
            for (int i = 0; i < 3; i++) bombers.Add(t.AddCraft(A, "bomber", 2, false));
            Assert.IsTrue(AiEngagement.Ready(t.Catalog, bombers, AiEngagement.Simulate(t.W, t.Catalog, bombers, 1), hard, false),
                "three Bombers carry more trading value than " + fleet.Count + " Guards");
        }
    
        [Test]
        public void AFewAttackShipsAreABetterBuyThanACrowdOfDefenceShips()
        {
            var hard = AiProfile.For(Difficulty.Hard);
            var none = FortifiedTargetAndFleet();
            var guardsOnly = AiEngagement.Cheapest(t.W, t.Catalog, none, 1, hard, false, new[] { "guard" }, 30);
            var either = AiEngagement.Cheapest(t.W, t.Catalog, none, 1, hard, false, new[] { "guard", "bomber" }, 30);
            Assert.IsTrue(guardsOnly.Found, "Guards alone can do it, in numbers (" + guardsOnly.Ships + ")");
            Assert.AreEqual("bomber", either.TypeId, "but Bombers win the battle for less");
            Assert.Less(either.Ships, guardsOnly.Ships, "and fewer of them");
            Assert.Less(either.Price, guardsOnly.Price, either.Ships + " Bombers cost " + either.Price + " with losses; " + guardsOnly.Ships + " Guards " + guardsOnly.Price);
        }
    
        [Test]
        public void NoAttackUntilItsIndustryIsBuiltUp()
        {
            t.Catalog.rules.aiRaids = true;
            t.Ai.cumulativeSales = AIFactoryPlanner.ShipyardAfterSales;
            for (int i = 0; i < 4; i++) t.AddCraft(A, "bomber", 3, false);
            t.W.tick = MediumGraceEnds();
            ThinkFor(400);
            Assert.AreNotEqual(AiStance.Attack, brain.Decisions.Stance, "a strong fleet, but a small factory: it builds first (" + brain.Decisions.StanceReason + ")");
            Assert.IsFalse(GrandStrategySkill.CanRecoup(BrainContext()), "no shipyard and no income: it could not recover the fleet: " + brain.Decisions.IndustryReason);
        }
    }
}
