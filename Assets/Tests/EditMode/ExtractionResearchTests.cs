using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Mining research: more ore per cycle (Foundry Charter, Rapid Extraction), bigger belt loads (Bulk Crates) and
    /// more exit belts per extractor (Multi-Chute Extractors); and speed bonuses are never lost to rounding.
    /// </summary>
    public class ExtractionResearchTests
    {
        const FactionId P = FactionId.Player;

        [Test]
        public void YieldResearchAddsTenOrePerCycle()
        {
            using (var t = new TestWorld())
            {
                Assert.AreEqual(30, ResearchService.ExtractionOutput(t.Catalog, t.Player, "ore"));
                t.Research(P, "charter", 1);
                Assert.AreEqual(40, ResearchService.ExtractionOutput(t.Catalog, t.Player, "ore"), "Foundry Charter: +10");
                t.Research(P, "rapid_extraction", 3);
                Assert.AreEqual(70, ResearchService.ExtractionOutput(t.Catalog, t.Player, "copper_ore"), "and +10 for each Rapid Extraction level");
                Assert.AreEqual(140, ResearchService.ExtractorCapacity(CatalogRepository.GetBuilding(t.Catalog, "extractor"), 70), "the buffer holds two cycles");
            }
        }

        [Test]
        public void AnExtractorMinesItsResearchedYield()
        {
            using (var t = new TestWorld())
            {
                t.Research(P, "charter", 1);
                var extractor = t.Build(P, "extractor", 0, 1, 1);
                for (int i = 0; i < 40 && WorldLookup.TotalItems(extractor.inventory) == 0; i++) t.FactoryTick();
                Assert.AreEqual(40, WorldLookup.TotalItems(extractor.inventory), "one cycle is 40 ore");
            }
        }

        [Test]
        public void BulkCratesGrowRawLoadsTo8Then14Then20()
        {
            using (var t = new TestWorld())
            {
                Assert.AreEqual(5, ResearchService.StackSize(t.Catalog, t.Player, "ore"));
                int[] expected = { 8, 14, 20 };
                for (int level = 1; level <= 3; level++)
                {
                    t.Research(P, "bulk_crates", level);
                    Assert.AreEqual(expected[level - 1], ResearchService.StackSize(t.Catalog, t.Player, "ore"));
                }
                Assert.AreEqual(1, ResearchService.StackSize(t.Catalog, t.Player, "iron_bar"), "made goods still travel one at a time");
            }
        }

        [Test]
        public void BulkCratesCarryBiggerLoadsOnTheBelt()
        {
            using (var t = new TestWorld())
            {
                t.Research(P, "bulk_crates", 1);
                t.Build(P, "extractor", 0, 1, 1);
                t.Build(P, "storage", 0, 5, 1);
                t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1);
                int biggest = 0;
                for (int i = 0; i < 200; i++)
                {
                    t.FactoryTick();
                    foreach (var k in t.W.tokens) biggest = System.Math.Max(biggest, k.Units);
                }
                Assert.AreEqual(8, biggest, "loads of 8 at Bulk Crates level 1");
            }
        }

        [Test]
        public void MultiChuteAddsExitBelts()
        {
            using (var t = new TestWorld())
            {
                Assert.AreEqual(1, ResearchService.Chutes(t.Catalog, t.Player));
                t.Research(P, "multi_chute", 1);
                Assert.AreEqual(2, ResearchService.Chutes(t.Catalog, t.Player));
                t.Research(P, "multi_chute", 2);
                Assert.AreEqual(3, ResearchService.Chutes(t.Catalog, t.Player));
            }
        }

        [TestCase(10, 10, 110)] // +10%: 10 ticks a cycle becomes 9.09 on average
        [TestCase(5, 10, 110)]  // a rich 5-tick deposit gains the full 10% too
        [TestCase(12, 45, 145)]
        public void SpeedBonusesAreNotLostToRounding(int rateTicks, int speed, int divisor)
        {
            using (var t = new TestWorld())
            {
                // test setup: give the Foundry Charter a speed bonus (the shipped tree uses yield instead)
                CatalogRepository.GetResearch(t.Catalog, "charter").levels[0].effects.Add(new ResearchEffect { kind = ResearchService.ExtractionSpeed, amount = speed });
                t.Research(P, "charter", 1);
                int carry = 0, total = 0;
                for (int i = 0; i < 100; i++) total += ResearchService.NextExtractionTicks(t.Catalog, t.Player, "ore", rateTicks, ref carry);
                Assert.AreEqual(rateTicks * 100 * 100 / divisor, total, 1, "cycles average the whole bonus");
                Assert.AreEqual(rateTicks * 100f / divisor * 0.1f, ResearchService.ExtractionSeconds(t.Catalog, t.Player, "ore", rateTicks), 0.001f, "and the panel shows it");
            }
        }
    }
}
