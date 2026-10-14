using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>The Ion power stockpile and the tiered resource tree.</summary>
    public class IonAndResourceTreeTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void IonNodeFillsTheStockpileAndNeverTheBelts()
        {
            var extractor = t.Build(P, "extractor", 0, 7, 11);  // Ion node 125
            int start = t.Player.ion;
            for (int i = 0; i < 150; i++) t.FactoryTick();
            Assert.AreEqual(start + 10 * t.Catalog.rules.ionPerExtraction, t.Player.ion, "one cycle every 15 ticks, ten Ion each");
            Assert.AreEqual(0, WorldLookup.TotalItems(extractor.inventory), "Ion never enters the extractor");
            Assert.AreEqual(0, t.W.tokens.Count);
        }

        [Test]
        public void BuildingsCostCreditsNotIon()
        {
            int ion = t.Player.ion, credits = t.Player.currency;
            t.Build(P, "refinery", 0, 3, 1);
            Assert.AreEqual(ion, t.Player.ion, "Ion is for making parts, not construction");
            Assert.AreEqual(credits - CatalogRepository.GetBuilding(t.Catalog, "refinery").cost, t.Player.currency);
        }

        [Test]
        public void RecipeHoldsUntilTheStockpileCoversItsIon()
        {
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            t.Player.ion = 0;
            for (int i = 0; i < 200; i++) t.FactoryTick();
            Assert.AreEqual(0, WorldLookup.CountItem(refinery.inventory, "iron_bar"), "no Ion, no bars");
            Assert.AreEqual(CatalogRepository.GetRecipe(t.Catalog, "refine_ore").craftTicks, refinery.recipeProgressTicks, "the finished cycle is held");

            t.Player.ion = 2;
            for (int i = 0; i < 60; i++) t.FactoryTick();
            Assert.AreEqual(1, WorldLookup.CountItem(refinery.inventory, "iron_bar"), "2 Ion pays for exactly one cycle");
            Assert.AreEqual(0, t.Player.ion);
        }

        [Test]
        public void PurifierTurnsMeltedIceIntoPurifiedWater()
        {
            t.Player.currency += 500; // test setup
            t.Player.research.Add(new ResearchProgress { nodeId = "water_purification", level = 1 });
            t.Build(P, "extractor", 0, 11, 11);                 // ice node 126
            var melter = t.Build(P, "refinery", 0, 11, 9);
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = melter.id, contentId = "melt_ice" });
            var purifier = t.Build(P, "purifier", 0, 11, 7);
            Assert.AreEqual("purify_water", purifier.selectedRecipeId, "a single-recipe machine selects it on placement");
            t.Line(P, 0, 11, 11, 0, 11, 10, 0, 11, 9);
            t.Line(P, 0, 11, 9, 0, 11, 8, 0, 11, 7);
            for (int i = 0; i < 600; i++) t.FactoryTick();
            Assert.Greater(WorldLookup.CountItem(purifier.inventory, "purified_water"), 0);
        }

        [Test]
        public void CatalogRejectsIonAsABeltedIngredient()
        {
            var c = CatalogRepository.LoadContent();
            CatalogRepository.GetRecipe(c, "build_relay").inputs[1].itemId = c.rules.ionItemId;
            Assert.Throws<CatalogException>(() => CatalogRepository.ValidateContent(c));
        }
    }
}
