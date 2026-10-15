using System.Diagnostics;
using System.Text;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Not a gate: run on demand (-testFilter PerfBenchmark) to see what a tick costs once the AI has grown a factory.
    /// Prints milliseconds per tick for each simulation stage.
    /// </summary>
    [Explicit, Category("Perf")]
    public class PerfBenchmark
    {
        [Test] public void GrownFactoryOnMapA() { Measure("map_a", 12000); }

        [Test] public void GrownFactoryOnAField() { Measure("field-424242", 12000); }

        static void Measure(string mapId, int growTicks)
        {
            using (var t = new TestWorld(mapId))
            {
                t.Catalog.rules.aiExpands = true;
                t.Ai.currency += 5000; // test setup: a bigger factory sooner
                t.Ai.ion += 2000;
                for (int i = 0; i < growTicks; i++) Step(t);

                const int measured = 2000;
                Perf.Frame(10f); // clear the window
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < measured; i++) Step(t);
                sw.Stop();
                Perf.Frame(1f);

                var sb = new StringBuilder();
                sb.Append(mapId).Append(": ").Append((sw.Elapsed.TotalMilliseconds / measured).ToString("0.000")).Append(" ms/tick; ")
                  .Append(t.W.buildings.Count).Append(" buildings, ").Append(t.W.belts.Count).Append(" belts, ")
                  .Append(t.W.tokens.Count).Append(" tokens, ").Append(t.W.pullRequests.Count).Append(" requests\n");
                for (int s = 0; s < (int)Perf.FirstFrameStage; s++)
                    sb.Append("  ").Append((Perf.Stage)s).Append(": ").Append((Perf.MsPerSecond((Perf.Stage)s) / measured).ToString("0.0000")).Append(" ms/tick\n");
                TestContext.WriteLine(sb.ToString());
                UnityEngine.Debug.Log(sb.ToString());
            }
        }

        static void Step(TestWorld t)
        {
            t.Loop.TickNormal();
            while (t.W.mode == MatchMode.Tactical)
            {
                if (t.W.encounter.turn == FactionId.Ai) t.Loop.RunAiTurnImmediately();
                else if (t.W.encounter.attacker == FactionId.Player) t.Run(new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Player });
                else t.Run(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
            }
        }
    }
}
