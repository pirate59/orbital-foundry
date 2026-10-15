using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Raw goods ride the belts in stacks (rules.rawStackSize); made goods travel one per belt slot.</summary>
    public class StackedCargoTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [TestCase(5)]
        [TestCase(2)]
        public void OreRidesInStacksAndTenMakeABar(int stack)
        {
            t.Catalog.rules.rawStackSize = stack; // the tunable stack size
            t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 5, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1);
            int biggest = 0;
            for (int i = 0; i < 600 && WorldLookup.CountItem(refinery.inventory, "iron_bar") == 0; i++)
            {
                t.FactoryTick();
                t.AssertBeltCapacity();
                foreach (var k in t.W.tokens) biggest = System.Math.Max(biggest, k.Units);
            }
            Assert.AreEqual(stack, biggest, "ore travels in full stacks");
            Assert.AreEqual(1, WorldLookup.CountItem(refinery.inventory, "iron_bar"), "a bar is made");
            Assert.AreEqual(10, CatalogRepository.GetRecipe(t.Catalog, "refine_ore").inputs[0].quantity, "from ten ore");
        }

        [Test]
        public void MadeGoodsTravelOneAtATime()
        {
            Assert.AreEqual(1, CatalogRepository.StackSize(t.Catalog, "iron_bar"));
            Assert.AreEqual(t.Catalog.rules.rawStackSize, CatalogRepository.StackSize(t.Catalog, "ore"));
            Assert.IsTrue(CatalogRepository.IsRaw(t.Catalog, "ice"));
            Assert.IsFalse(CatalogRepository.IsRaw(t.Catalog, "water"));
        }

        [Test]
        public void IonYieldIsUnchangedByTheBiggerOreCycles()
        {
            Assert.AreEqual(30, t.Catalog.rules.ionPerExtraction, "30 Ion per cycle, as before");
            Assert.AreEqual(30, t.Catalog.rules.nodeOutputPerCycle, "ten times the ore per cycle");
        }
    }
}
