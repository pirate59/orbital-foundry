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

        [Test]
        public void TheWebIsDiscoveredOneStepAtATime()
        {
            Assert.AreEqual(ResearchVisibility.Open, Seen("charter"));
            Assert.AreEqual(ResearchVisibility.Hinted, Seen("structural_framing"), "one step out is an undiscovered outline");
            Assert.AreEqual(ResearchVisibility.Hidden, Seen("titanium_forging"), "further out is not drawn");
            Assert.AreEqual("LOCKED", t.Run(Research("structural_framing")).code);

            Fund();
            t.MustRun(Research("charter"));
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
            Assert.AreEqual(17, ResearchService.CraftTicks(t.Catalog, t.Player, "refinery", refine), "+20% speed: 20 ticks become 17");
            var rolling = Node("copper_rolling").levels[0];
            Assert.AreEqual(rolling.ionCost, ResearchService.IonPrice(t.Catalog, t.Player, rolling));
            t.MustRun(Research("ion_efficiency"));
            Assert.AreEqual(rolling.ionCost * 9 / 10, ResearchService.IonPrice(t.Catalog, t.Player, rolling), "research costs 10% less Ion");
            t.MustRun(Research("brokerage_contract"));
            Assert.AreEqual(31, EconomyService.SalePrice(t.Catalog, t.Player, relay), "+25% on 25");
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
