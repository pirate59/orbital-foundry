using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Refineries and assemblers keep a bucket per item, like Satisfactory's machine slots: each ingredient and the
    /// product have their own room, so a full bucket of one never stops another arriving and belts do not jam.
    /// </summary>
    public class MachineBucketTests
    {
        const FactionId P = FactionId.Player;
        TestWorld t;

        [SetUp] public void SetUp() { t = new TestWorld(); }
        [TearDown] public void TearDown() { t.Dispose(); }

        /// <summary>An assembler on <paramref name="recipeId"/>, researched if it needs to be.</summary>
        BuildingState Assembler(string recipeId, int x)
        {
            var gate = ResearchService.RecipeGate(t.Catalog, recipeId);
            if (gate != null) t.Research(P, gate.id, 1);
            var b = t.Build(P, "assembler", 0, x, 1, chooseRecipe: false);
            t.MustRun(new GameCommand { kind = CommandKind.SelectRecipe, actor = P, buildingId = b.id, contentId = recipeId });
            Assert.IsTrue(ResearchService.IsRecipeUnlocked(t.Catalog, t.Player, CatalogRepository.GetRecipe(t.Catalog, recipeId)));
            return b;
        }

        [Test]
        public void AFullIngredientBucketLeavesRoomForTheOther()
        {
            var b = Assembler("build_electronics", 3); // 1 silicon wafer + 2 copper wire
            int bucket = CatalogRepository.GetBuilding(t.Catalog, "assembler").inventoryCapacity;
            WorldLookup.AddItem(b.inventory, "copper_wire", bucket);
            Assert.IsFalse(PullRouter.CanTake(t.W, t.Catalog, b, "copper_wire", 1), "the wire bucket is full");
            Assert.IsTrue(PullRouter.CanTake(t.W, t.Catalog, b, "silicon_wafer", 1), "the wafer has its own bucket");

            WorldLookup.AddItem(b.inventory, "electronics", bucket);
            Assert.IsTrue(FactorySimulation.IsStorageFull(t.W, t.Catalog, b), "the product's bucket is full: it backs up");
            Assert.IsTrue(PullRouter.CanTake(t.W, t.Catalog, b, "silicon_wafer", 1), "but finished goods never take an ingredient's room");
            Assert.IsTrue(StateValidator.ValidateWorld(t.W, t.Catalog, t.Map).success, "each bucket within its own room is valid");
        }

        [Test]
        public void ACycleWaitsForRoomInItsProductsBucketOnly()
        {
            var b = Assembler("draw_copper_wire", 3); // 1 copper bar -> 2 wire
            int bucket = CatalogRepository.GetBuilding(t.Catalog, "assembler").inventoryCapacity;
            WorldLookup.AddItem(b.inventory, "copper_bar", bucket);
            WorldLookup.AddItem(b.inventory, "copper_wire", bucket - 2);
            for (int i = 0; i < 400; i++) t.FactoryTick();
            Assert.AreEqual(bucket, WorldLookup.CountItem(b.inventory, "copper_wire"), "it fills the wire bucket and stops");
            Assert.AreEqual(bucket - 1, WorldLookup.CountItem(b.inventory, "copper_bar"), "having used just one bar");
        }

        [Test]
        public void AMachineDoesNotHandOutItsOwnIngredients()
        {
            var drawer = Assembler("draw_copper_wire", 3);
            var builder = Assembler("build_electronics", 5);
            Assert.IsTrue(PullRouter.Offers(t.W, t.Catalog, drawer, "copper_wire"), "wire is what the drawer makes");
            Assert.IsFalse(PullRouter.Offers(t.W, t.Catalog, builder, "copper_wire"), "wire is what the builder uses");
        }
    }
}
