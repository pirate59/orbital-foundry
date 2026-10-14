using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    public class BeltNetworkTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp() { t = new TestWorld(); }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        [Test]
        public void RelayChainSellsAndLedgerTracksCurrencyAndSalesSeparately()
        {
            t.BuildPlayerRelayChain();
            int afterBuild = t.Player.currency;
            Assert.AreEqual(600 - 450 - 9 * 2, afterBuild);
            Assert.AreEqual(0, t.Player.cumulativeSales);

            bool sharedEdgeCarriedBothItems = false, sawIron = false, sawCopper = false;
            var shared = FindBelt(0, 3, 2, 0, 3, 3);
            for (int i = 0; i < 1500 && t.Player.cumulativeSales == 0; i++)
            {
                t.FactoryTick();
                t.AssertBeltCapacity();
                foreach (var tok in t.W.tokens)
                {
                    if (tok.routeSegmentIds[tok.segmentIndex] != shared.id) continue;
                    if (tok.itemId == "iron_bar") sawIron = true;
                    if (tok.itemId == "copper_bar") sawCopper = true;
                }
            }
            sharedEdgeCarriedBothItems = sawIron && sawCopper;
            Assert.AreEqual(25, t.Player.cumulativeSales, "one relay sold");
            Assert.AreEqual(afterBuild + 25, t.Player.currency);
            Assert.IsTrue(sharedEdgeCarriedBothItems, "mixed items must share the merged edge");

            // Spending does not reduce cumulative sales.
            t.Belt(P, 0, 0, 5, 0, 1, 5);
            Assert.AreEqual(afterBuild + 25 - 2, t.Player.currency);
            Assert.AreEqual(25, t.Player.cumulativeSales);
        }

        BeltSegmentState FindBelt(int c1, int x1, int y1, int c2, int x2, int y2)
        {
            foreach (var s in t.W.belts)
                if (s.fromCellId == c1 && s.fromX == x1 && s.fromY == y1 && s.toCellId == c2 && s.toX == x2 && s.toY == y2) return s;
            Assert.Fail("belt not found");
            return null;
        }

        [Test]
        public void RoutesAreOneWay()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 3, 1);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
            Assert.IsTrue(t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id).success);
            Assert.IsFalse(t.S.Belts.FindShortestRoute(t.W, refinery.id, ext.id).success);
            var dup = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = 2, fromY = 1, toCellId = 0, toX = 1, toY = 1 });
            Assert.IsTrue(dup.success, "drawing the other way reverses the belt");
            Assert.AreEqual("Belt reversed.", dup.message);
            Assert.IsFalse(t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id).success, "the reversed belt no longer carries ore forward");
            var diag = t.Run(new GameCommand { kind = CommandKind.PlaceBelt, actor = P, fromCellId = 0, fromX = 2, fromY = 2, toCellId = 0, toX = 3, toY = 3 });
            Assert.AreEqual("NOT_ADJACENT", diag.code);
        }

        [Test]
        public void EqualLengthRoutesBreakTiesByLowerBeltId()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 2, 2);
            var viaB = t.Line(P, 0, 1, 1, 0, 1, 2, 0, 2, 2); // built first: lower IDs (split at the extractor)
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 2, 2);            // merge at the refinery
            var route = t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id);
            Assert.IsTrue(route.success);
            CollectionAssert.AreEqual(new[] { viaB[0].id, viaB[1].id }, route.segmentIds);
        }

        [Test]
        public void CycleSearchTerminatesWhenTargetIsUnreachable()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 5, 5);
            t.Line(P, 0, 1, 1, 0, 1, 2, 0, 2, 2, 0, 2, 3, 0, 1, 3, 0, 1, 2); // loops back into (1,2)
            var route = t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id);
            Assert.IsFalse(route.success);
        }

        [Test]
        public void TokenWaitsBehindFullDownstreamEdgeWithoutDuplication()
        {
            var ext = t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 0, 4, 1);
            var segs = t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 4, 1);
            var ids = new System.Collections.Generic.List<int> { segs[0].id, segs[1].id, segs[2].id };
            // Blocker sits on the middle edge for a long time; follower arrives at the end of the first edge.
            var blocker = new ItemTokenState { id = WorldLookup.AllocateId(t.W), itemId = "ore", sourceBuildingId = ext.id, targetBuildingId = refinery.id,
                routeSegmentIds = new System.Collections.Generic.List<int>(ids), segmentIndex = 1, ticksUntilNextSegment = 50 };
            var follower = new ItemTokenState { id = WorldLookup.AllocateId(t.W), itemId = "ore", sourceBuildingId = ext.id, targetBuildingId = refinery.id,
                routeSegmentIds = new System.Collections.Generic.List<int>(ids), segmentIndex = 0, ticksUntilNextSegment = 1 };
            t.W.tokens.Add(blocker);
            t.W.tokens.Add(follower);
            // Keep the extractor from dispatching more ore during the check.
            WorldLookup.GetNode(t.W, ext.nodeId).ticksUntilOutput = 1000;
            for (int i = 0; i < 10; i++)
            {
                t.FactoryTick();
                t.AssertBeltCapacity();
            }
            Assert.AreEqual(TokenStatus.Waiting, follower.status);
            Assert.AreEqual(0, follower.segmentIndex);
            Assert.AreEqual(2, t.W.tokens.Count, "no token created or lost");
        }

        [Test]
        public void CapturingTransitCellBreaksAndRecaptureRestoresRoute()
        {
            t.SetOwner(1, P);
            t.Fortify(2, P);
            var ext = t.Build(P, "extractor", 0, 1, 1);
            var refinery = t.Build(P, "refinery", 2, 1, 1);
            // Row y=1 from cell 0 through all of cell 1 into cell 2 (global x 1 -> 31 with 15-tile cells).
            int size = CatalogRepository.CellGridSize;
            t.GlobalRow(P, 1, 1, size * 2 + 1);
            Assert.IsTrue(t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id).success);

            Assert.IsTrue(t.S.Ownership.TransferCell(t.W, t.Catalog, 1, FactionId.Ai).success);
            var broken = t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id);
            Assert.IsFalse(broken.success, "transit capture must break the route");
            Assert.IsTrue(WorldLookup.IsBuildingActive(t.W, refinery), "structures outside the captured cell keep working");

            Assert.IsTrue(t.S.Ownership.TransferCell(t.W, t.Catalog, 1, P).success);
            Assert.IsTrue(t.S.Belts.FindShortestRoute(t.W, ext.id, refinery.id).success, "recapture restores the route");
        }
    }
}
