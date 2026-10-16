using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Per-belt timers: parallel lanes share a busy node's output, and faster belt tiers carry more.</summary>
    public class BeltThroughputTests
    {
        const FactionId P = FactionId.Player;

        /// <summary>Iron node 101 at (1,1) into a Sale Depot at (6,1); returns ore sold after the run.</summary>
        static int Delivered(System.Action<TestWorld> lay, int ticks = 600)
        {
            using (var t = new TestWorld())
            {
                t.Build(P, "extractor", 0, 1, 1);
                t.Build(P, "sale_depot", 0, 6, 1);
                lay(t);
                for (int i = 0; i < ticks; i++) { t.FactoryTick(); t.AssertBeltCapacity(); }
                return t.Player.cumulativeSales; // Iron Ore sells for 1
            }
        }

        static void OneLane(TestWorld t) { t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1); }

        [Test]
        public void TwoLanesOutOfANodeCarryMoreThanOne()
        {
            int single = Delivered(OneLane);
            System.Action<TestWorld> twoLanes = t =>
            {
                OneLane(t);
                // A second lane leaves the extractor the other way and rejoins at the depot.
                t.Line(P, 0, 1, 1, 0, 1, 2, 0, 2, 2, 0, 3, 2, 0, 4, 2, 0, 5, 2, 0, 6, 2, 0, 6, 1);
            };
            // One chute: the extractor feeds only its first exit belt, so a second lane adds nothing.
            Assert.AreEqual(single, Delivered(twoLanes), "without Multi-Chute the second exit is not fed");
            int split = Delivered(t => { t.Research(P, "multi_chute", 1); twoLanes(t); });
            Assert.Greater(single, 0);
            Assert.Greater(split, single * 13 / 10, "the second lane carried items in parallel (" + single + " vs " + split + ")");
        }

        [Test]
        public void FasterBeltTiersCarryMore()
        {
            int slow = Delivered(OneLane);
            int fast = Delivered(t =>
            {
                OneLane(t);
                foreach (var s in t.W.belts) s.tier = 2; // test setup: an upgraded line
            });
            Assert.Greater(fast, slow * 13 / 10, "express belts move items faster (" + slow + " vs " + fast + ")");
        }

        [Test]
        public void EveryBeltStartsAtTheFirstTier()
        {
            var belt = new TestWorld();
            try
            {
                var s = belt.Belt(P, 0, 2, 2, 0, 3, 2);
                Assert.AreEqual(0, s.tier);
                Assert.AreEqual(belt.Catalog.rules.beltTiers[0].ticksPerSegment, BeltNetwork.TravelTicks(belt.W, belt.Catalog, s));
            }
            finally { belt.Dispose(); }
        }
    }
}
