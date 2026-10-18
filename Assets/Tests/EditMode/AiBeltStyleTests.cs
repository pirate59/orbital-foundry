using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// The AI's belt style: a dedicated route keeps one item to its line and never joins a line carrying another, where
    /// a shared ("sushi") route joins whatever line is shortest. On map_a's cell 0 (ore at 1,1; copper ore at 4,4).
    /// </summary>
    public class AiBeltStyleTests
    {
        const FactionId P = FactionId.Player;
        TestWorld t;
        BuildingState ore, ironWorks;

        [SetUp]
        public void SetUp()
        {
            t = new TestWorld();
            t.Player.currency = 100000;
            // A copper line straight up from the copper ore at (4,4) to a refinery at (4,0): a wall across row 1.
            t.Build(P, "extractor", 0, 4, 4);
            t.Build(P, "refinery", 0, 4, 0, chooseRecipe: false);
            t.Line(P, 0, 4, 4, 0, 4, 3, 0, 4, 2, 0, 4, 1, 0, 4, 0);
            ore = t.Build(P, "extractor", 0, 1, 1);
            ironWorks = t.Build(P, "refinery", 0, 7, 1);
        }

        [TearDown] public void TearDown() { t.Dispose(); }

        static bool TouchesCopperLine(BeltPlanResult plan)
        {
            foreach (var s in plan.steps)
                if ((s.fromX == 4 && s.fromY <= 4) || (s.toX == 4 && s.toY <= 4)) return true;
            return false;
        }

        [Test]
        public void ItKnowsWhatEachBeltCarries()
        {
            var items = AIBeltPlanner.BeltItems(t.W, t.Catalog, P);
            foreach (var b in t.W.belts)
            {
                Assert.IsTrue(items.ContainsKey(b.id), "belt " + b.id + " is fed by the copper extractor");
                CollectionAssert.AreEquivalent(new[] { "copper_ore" }, items[b.id]);
            }
        }

        [Test]
        public void ASharedRouteJoinsTheOtherLineButADedicatedOneGoesRound()
        {
            var shared = t.S.AiBelts.Plan(t.W, t.Catalog, t.Map, ore.id, ironWorks.id);
            Assert.IsTrue(shared.success, shared.reason);
            Assert.IsTrue(TouchesCopperLine(shared), "the shortest way crosses the copper line, joining it");

            var own = t.S.AiBelts.PlanDedicated(t.W, t.Catalog, t.Map, ore.id, ironWorks.id, "ore");
            Assert.IsTrue(own.success, own.reason);
            Assert.IsFalse(TouchesCopperLine(own), "a dedicated line keeps clear of the copper line");
            Assert.Greater(own.steps.Count, shared.steps.Count, "and is longer for it");
        }

        [Test]
        public void ADedicatedRouteSharesBeltsCarryingItsOwnItem()
        {
            var alone = t.S.AiBelts.PlanDedicated(t.W, t.Catalog, t.Map, ore.id, ironWorks.id, "ore");
            // An ore line already leaves the extractor along row 1 and turns down to a second ore refinery at (3,3).
            t.Build(P, "refinery", 0, 3, 3);
            t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1, 0, 3, 2, 0, 3, 3);
            var own = t.S.AiBelts.PlanDedicated(t.W, t.Catalog, t.Map, ore.id, ironWorks.id, "ore");
            Assert.IsTrue(own.success, own.reason);
            Assert.Less(own.steps.Count, alone.steps.Count, "it rides the ore belts already there, laying fewer new ones");
            Assert.IsFalse(TouchesCopperLine(own));
        }
    }
}
