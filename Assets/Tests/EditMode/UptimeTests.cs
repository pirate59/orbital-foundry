using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>Uptime: the share of recent time a machine spent working rather than paused for input, room or power.</summary>
    public class UptimeTests
    {
        const FactionId P = FactionId.Player;

        [Test]
        public void AnExtractorWithNowhereToSendOreFallsIdle()
        {
            using (var t = new TestWorld())
            {
                var extractor = t.Build(P, "extractor", 0, 1, 1);
                for (int i = 0; i < 20; i++) t.FactoryTick();
                Assert.Greater(t.S.Factory.Uptime(extractor.id), 0.9f, "mining while its store has room");
                for (int i = 0; i < 2 * FactorySimulation.UptimeTicks; i++) t.FactoryTick();
                Assert.Less(t.S.Factory.Uptime(extractor.id), 0.2f, "its store filled and it stopped");
            }
        }

        [Test]
        public void AFedRefineryStaysBusyAndAStarvedOneDoesNot()
        {
            using (var t = new TestWorld())
            {
                t.Build(P, "extractor", 0, 1, 1);
                var fed = t.Build(P, "refinery", 0, 3, 1);
                var starved = t.Build(P, "refinery", 0, 6, 4);
                t.Line(P, 0, 1, 1, 0, 2, 1, 0, 3, 1);
                for (int i = 0; i < 2 * FactorySimulation.UptimeTicks; i++) t.FactoryTick();
                Assert.Greater(t.S.Factory.Uptime(fed.id), 0.6f, "fed refinery works most of the time");
                Assert.Less(t.S.Factory.Uptime(starved.id), 0.05f, "a refinery with no ore never works");
                Assert.AreEqual(-1f, t.S.Factory.Uptime(-5), "no history for a building that is not there");
            }
        }
    }
}
