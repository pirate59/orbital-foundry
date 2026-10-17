using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>The research web: discovery, purchase, gating, bonuses, Photovoltaics and AI research.</summary>
    public class ResearchTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        ResearchNodeDefinition Node(string id) { return CatalogRepository.GetResearch(t.Catalog, id); }
        ResearchVisibility Seen(string id) { return ResearchService.Visibility(t.Catalog, t.Player, Node(id)); }

        void Fund(int credits = 5000, int ion = 5000)
        {
            t.Player.currency += credits;
            t.Player.ion += ion;
        }

        GameCommand Research(string id) { return new GameCommand { kind = CommandKind.Research, actor = P, contentId = id }; }

        /// <summary>Runs research time until the player's queue is empty.</summary>
        void Finish()
        {
            for (int i = 0; i < 200000 && t.Player.researchQueue.Count > 0; i++) ResearchService.Tick(t.W, t.Catalog);
        }

        [Test]
        public void HigherResearchTakesGoodsFromStorage()
        {
            Fund();
            t.Research(P, "water_purification", 1);
            t.Research(P, "copper_rolling", 1);
            var node = Node("coolant_systems");
            Assert.AreEqual("NO_GOODS", ResearchService.CheckPurchase(t.W, t.Catalog, t.Player, node).code, "needs Purified Water put by");
            Assert.AreEqual("NO_GOODS", t.Run(Research("coolant_systems")).code);
            var storage = t.Build(P, "storage", 0, 6, 4);
            WorldLookup.AddItem(storage.inventory, "purified_water", 10); // test setup: water stored
            int credits = t.Player.currency;
            t.MustRun(Research("coolant_systems"));
            Assert.AreEqual(0, WorldLookup.CountItem(storage.inventory, "purified_water"), "the goods were used");
            Assert.AreEqual(credits - node.levels[0].creditCost, t.Player.currency, "and the credits");
            int withGoods = 0, water = 0;
            foreach (var n in t.Catalog.research)
                foreach (var level in n.levels)
                {
                    if (level.goods.Count > 0) withGoods++;
                    if (level.goods.Exists(g => g.itemId == "purified_water")) water++;
                }
            Assert.GreaterOrEqual(withGoods, 10, "the upper research levels all cost goods");
            Assert.GreaterOrEqual(water, 6, "Purified Water has real uses");
        }

        [Test]
        public void TheAiPaysResearchGoodsLikeThePlayer()
        {
            t.Ai.currency += 5000; // test setup
            t.Ai.ion += 5000;
            t.Research(FactionId.Ai, "water_purification", 1);
            t.Research(FactionId.Ai, "copper_rolling", 1);
            var refusal = ResearchService.CheckPurchase(t.W, t.Catalog, t.Ai, Node("coolant_systems"));
            Assert.IsNotNull(refusal, "no Purified Water in its Storage");
            Assert.AreEqual("NO_GOODS", refusal.code);

            var def = CatalogRepository.GetBuilding(t.Catalog, "storage");
            var cellDef = WorldLookup.GetCellDef(t.Map, 7);
            BuildingState storage = null;
            for (int y = 0; y < cellDef.gridHeight && storage == null; y++)
                for (int x = 0; x < cellDef.gridWidth && storage == null; x++)
                    if (GridRules.CanPlaceBuilding(t.W, t.Catalog, t.Map, def, FactionId.Ai, 7, x, y).success) storage = t.Build(FactionId.Ai, "storage", 7, x, y);
            WorldLookup.AddItem(storage.inventory, "purified_water", 10);
            t.MustRun(new GameCommand { kind = CommandKind.Research, actor = FactionId.Ai, contentId = "coolant_systems" });
            Assert.AreEqual(0, WorldLookup.CountItem(storage.inventory, "purified_water"), "the goods were used, as for the player");
        }

        [Test]
        public void LateGoodsSellForWellOverTheirIngredients()
        {
            foreach (var id in new[] { "hull_section", "field_emitter", "precision_circuit", "quantum_core", "coolant_cell" })
            {
                var recipe = t.Catalog.recipes.Find(r => r.outputItemId == id);
                int inputs = 0;
                foreach (var i in recipe.inputs) inputs += CatalogRepository.GetItem(t.Catalog, i.itemId).baseSaleValue * i.quantity;
                Assert.GreaterOrEqual(CatalogRepository.GetItem(t.Catalog, id).baseSaleValue, inputs * 2.5f, id + " pays for climbing the tiers");
            }
        }

        [Test]
        public void TheWebIsDiscoveredOneStepAtATime()
        {
            Assert.AreEqual(ResearchVisibility.Open, Seen("charter"));
            Assert.AreEqual(ResearchVisibility.Hinted, Seen("structural_framing"), "one step out is an undiscovered outline");
            Assert.AreEqual(ResearchVisibility.Hidden, Seen("titanium_forging"), "further out is not drawn");
            Assert.AreEqual("LOCKED", t.Run(Research("structural_framing")).code);

            Fund();
            t.MustRun(Research("charter"));
            Finish();
            Assert.AreEqual(ResearchVisibility.Owned, Seen("charter"));
            Assert.AreEqual(ResearchVisibility.Open, Seen("structural_framing"));
            Assert.AreEqual(ResearchVisibility.Hinted, Seen("titanium_forging"));
            Assert.AreEqual("MAXED", t.Run(Research("charter")).code);
        }

        [Test]
        public void ResearchCostsCreditsAndIon()
        {
            var level = Node("charter").levels[0];
            t.Player.ion = level.ionCost - 1;
            int credits = t.Player.currency;
            Assert.AreEqual("NO_ION", t.Run(Research("charter")).code);
            Assert.AreEqual(credits, t.Player.currency);

            t.Player.ion = level.ionCost;
            t.MustRun(Research("charter"));
            Assert.AreEqual(credits - level.creditCost, t.Player.currency);
            Assert.AreEqual(0, t.Player.ion);
        }

        [Test]
        public void RecipesAndBuildingsWaitForTheirResearch()
        {
            Fund();
            var assembler = t.Build(P, "assembler", 0, 3, 3);
            Assert.AreEqual("LOCKED_RECIPE", t.Run(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = assembler.id, contentId = "build_iron_frame" }).code);
            Assert.AreEqual("LOCKED_BUILDING", t.Run(new GameCommand { kind = CommandKind.PlaceBuilding, actor = P, contentId = "purifier", fromCellId = 0, fromX = 5, fromY = 5 }).code);
            Assert.IsFalse(ResearchService.IsItemUnlocked(t.Catalog, t.Player, "iron_frame"));

            t.MustRun(Research("charter"));
            t.MustRun(Research("structural_framing"));
            t.MustRun(Research("water_purification"));
            Finish();
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = assembler.id, contentId = "build_iron_frame" });
            t.Build(P, "purifier", 0, 5, 5);
            Assert.IsTrue(ResearchService.IsItemUnlocked(t.Catalog, t.Player, "iron_frame"));
        }

        [Test]
        public void BonusesStackAcrossLevels()
        {
            Fund();
            var refine = CatalogRepository.GetRecipe(t.Catalog, "refine_ore");
            var relay = CatalogRepository.GetItem(t.Catalog, "relay");
            t.MustRun(Research("charter"));
            t.MustRun(Research("process_optimisation"));
            t.MustRun(Research("process_optimisation"));
            Finish();
            Assert.AreEqual(17, ResearchService.CraftTicks(t.Catalog, t.Player, "refinery", refine), "+20% speed: 20 ticks become 17");
            var rolling = Node("copper_rolling").levels[0];
            Assert.AreEqual(rolling.ionCost, ResearchService.IonPrice(t.Catalog, t.Player, rolling));
            t.MustRun(Research("ion_efficiency"));
            Finish();
            Assert.AreEqual(rolling.ionCost * 9 / 10, ResearchService.IonPrice(t.Catalog, t.Player, rolling), "research costs 10% less Ion");
            t.MustRun(Research("brokerage_contract"));
            Finish();
            Assert.AreEqual(27, EconomyService.SalePrice(t.Catalog, t.Player, relay), "+10% on 25");
        }

        [Test]
        public void PhotovoltaicsRaisesSolarOutput()
        {
            Fund();
            t.FactoryTick();
            Assert.AreEqual(20, t.Player.powerSupply, "HQ panels");
            t.MustRun(Research("charter"));
            t.MustRun(Research("ion_efficiency"));
            t.MustRun(Research("photovoltaics"));
            Finish();
            t.FactoryTick();
            Assert.AreEqual(25, t.Player.powerSupply, "+25%");
        }

        [Test]
        public void TheAiResearchesOnceItsFactoryRuns()
        {
            for (int i = 0; i < 6000 && t.Ai.cumulativeSales == 0; i++) t.Loop.TickNormal();
            Assert.Greater(t.Ai.cumulativeSales, 0, "the AI factory sells first");
            t.Ai.currency += 3000; // test setup: skip the wait for savings
            t.Ai.ion += 1000;
            for (int i = 0; i < 3000 && t.Ai.research.Count == 0; i++) t.Loop.TickNormal();
            Assert.Greater(t.Ai.research.Count, 0, "the AI bought research. Planner: " + t.S.AiPlanner.LastReason);
            Assert.AreEqual(1, ResearchService.Level(t.Ai, "charter"), "it started at the root like the player");
        }

        [Test]
        public void ResearchTakesTimeByTier()
        {
            Fund();
            int perMinute = 60000 / t.Catalog.rules.tickMilliseconds;
            Assert.AreEqual(0, ResearchService.Tier(t.Catalog, Node("charter")));
            Assert.AreEqual(1, ResearchService.Tier(t.Catalog, Node("structural_framing")));
            t.MustRun(Research("charter"));
            t.MustRun(Research("structural_framing")); // queued behind the prerequisite it needs
            Assert.AreEqual(2, t.Player.researchQueue.Count);
            Assert.AreEqual(2 * perMinute, t.Player.researchQueue[0].ticksTotal, "tier 0: two minutes");
            Assert.AreEqual(3 * perMinute, t.Player.researchQueue[1].ticksTotal, "one step down: three minutes");
            for (int i = 0; i < 2 * perMinute - 1; i++) ResearchService.Tick(t.W, t.Catalog);
            Assert.AreEqual(0, ResearchService.Level(t.Player, "charter"), "not done a tick early");
            ResearchService.Tick(t.W, t.Catalog);
            Assert.AreEqual(1, ResearchService.Level(t.Player, "charter"));
            Assert.AreEqual("structural_framing", ResearchService.Current(t.Player).nodeId, "the next level starts at once");
            Finish();
            Assert.AreEqual(1, ResearchService.Level(t.Player, "structural_framing"));
            Assert.AreEqual(4, ResearchService.ResearchMinutes(t.Catalog, Node("process_optimisation"), 2), "tier 1 is three minutes; its second level adds one");
        }

        [Test]
        public void CancellingRefundsItAndWhatNeededIt()
        {
            Fund();
            int credits = t.Player.currency, ion = t.Player.ion;
            t.MustRun(Research("charter"));
            t.MustRun(Research("structural_framing"));
            t.MustRun(Research("process_optimisation"));
            Assert.Less(t.Player.currency, credits, "paid when queued");
            var r = t.Run(new GameCommand { kind = CommandKind.CancelResearch, actor = P, contentId = "charter" });
            Assert.IsTrue(r.success, r.message);
            Assert.AreEqual(0, t.Player.researchQueue.Count, "everything behind the root needed it: " + r.message);
            Assert.AreEqual(credits, t.Player.currency, "full credit refund");
            Assert.AreEqual(ion, t.Player.ion, "full Ion refund");
            Assert.AreEqual("NOT_QUEUED", t.Run(new GameCommand { kind = CommandKind.CancelResearch, actor = P, contentId = "charter" }).code);
        }

        [Test]
        public void TheQueueHasALimit()
        {
            Fund(100000, 100000);
            t.MustRun(Research("charter"));
            for (int i = 0; i < 2; i++) t.MustRun(Research("process_optimisation"));
            t.MustRun(Research("ion_efficiency"));
            t.MustRun(Research("structural_framing"));
            Assert.AreEqual(ResearchService.MaxQueue, t.Player.researchQueue.Count);
            Assert.AreEqual("QUEUE_FULL", t.Run(Research("brokerage_contract")).code);
        }

        [Test]
        public void ShippedWebIsValid()
        {
            Assert.DoesNotThrow(() => CatalogRepository.ValidateContent(CatalogRepository.LoadContent()));
            var c = CatalogRepository.LoadContent();
            Node("charter"); // indexed
            c.research.Find(n => n.id == "charter").prerequisites.Add(new ResearchPrerequisite { nodeId = "trade_network", minimumLevel = 1 });
            Assert.Throws<CatalogException>(() => CatalogRepository.ValidateContent(c), "prerequisite loops are refused");
        }
    }
}
