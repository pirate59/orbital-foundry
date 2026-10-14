using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Buying belt upgrades per belt or per line, and machines that fill up with a first-batch priority.</summary>
    public class BeltUpgradeAndFillTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        GameCommand Upgrade(BeltSegmentState s, bool line)
        {
            return new GameCommand { kind = CommandKind.UpgradeBelt, actor = P, beltId = s.id, contentId = line ? "line" : "" };
        }

        [Test]
        public void ALineRunsBetweenBuildingsAndJunctions()
        {
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "sale_depot", 0, 6, 1);
            var main = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            Assert.AreEqual(5, t.S.Belts.LineOf(t.W, main[2].id).Count, "extractor to depot is one line");
            t.Line(P, 0, 3, 1, 0, 3, 2, 0, 3, 3);   // a branch splits the line at (3,1)
            Assert.AreEqual(2, t.S.Belts.LineOf(t.W, main[0].id).Count, "the split ends the first line");
            Assert.AreEqual(3, t.S.Belts.LineOf(t.W, main[4].id).Count);
        }

        [Test]
        public void UpgradesCostPerBeltAndGoOneTierAtATime()
        {
            t.Build(P, "extractor", 0, 1, 1);
            t.Build(P, "sale_depot", 0, 6, 1);
            var main = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            var tiers = t.Catalog.rules.beltTiers;
            int credits = t.Player.currency;
            t.MustRun(Upgrade(main[2], false));
            Assert.AreEqual(1, main[2].tier);
            Assert.AreEqual(0, main[1].tier, "only that belt");
            Assert.AreEqual(credits - tiers[1].upgradeCostPerSegment, t.Player.currency);

            credits = t.Player.currency;
            t.MustRun(Upgrade(main[0], true));
            foreach (var s in main) Assert.AreEqual(1, s.tier, "the whole line is Fast now");
            Assert.AreEqual(credits - 4 * tiers[1].upgradeCostPerSegment, t.Player.currency, "the already-upgraded belt is free");

            t.MustRun(Upgrade(main[0], true));
            Assert.AreEqual("MAX_TIER", t.Run(Upgrade(main[0], true)).code);
        }

        [Test]
        public void MachinesFillUpButEveryMachineGetsItsFirstBatchFirst()
        {
            // One extractor feeds two refineries down a split line.
            t.Build(P, "extractor", 0, 1, 1);
            var a = t.Build(P, "refinery", 0, 6, 1);
            var b = t.Build(P, "refinery", 0, 3, 4);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1, 0, 5, 1, 0, 6, 1);
            t.Line(P, 0, 3, 1, 0, 3, 2, 0, 3, 3, 0, 3, 4);
            t.FactoryTick();
            Assert.AreEqual(8, t.W.pullRequests.FindAll(r => r.targetBuildingId == a.id).Count, "a refinery asks to fill all 8 slots");
            Assert.AreEqual(1, t.W.pullRequests.FindAll(r => r.targetBuildingId == a.id && !r.fill).Count, "one of them is its first batch");

            bool bothFed = false;
            for (int i = 0; i < 200 && !bothFed; i++)
            {
                t.FactoryTick();
                int toA = Delivered(a), toB = Delivered(b);
                if (toA + toB >= 2) { bothFed = toA >= 1 && toB >= 1; break; }
            }
            Assert.IsTrue(bothFed, "the first two items went one to each refinery, not both to one");
        }

        int Delivered(BuildingState b)
        {
            int n = WorldLookup.TotalItems(b.inventory);
            foreach (var k in t.W.tokens) if (k.targetBuildingId == b.id) n++;
            return n;
        }
    }
}
