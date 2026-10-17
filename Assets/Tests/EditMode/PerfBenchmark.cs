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

        /// <summary>As on the field, with raids on too, so the AI's strikes and battle skills are timed.</summary>
        [Test] public void GrownFactoryWithRaidsOnAField() { Measure("field-424242", 12000, true); }

        /// <summary>
        /// The battle skills, timed on battles set up directly: an AI fleet attacks a defended player cell, with
        /// spare AI ships nearby to join, and fights each battle out (the player only ends turns). Prints the time
        /// per run of each battle skill, at each difficulty.
        /// </summary>
        [Test]
        public void BattleSkills()
        {
            var sb = new StringBuilder();
            foreach (Difficulty difficulty in new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard })
            {
                AiTiming.Reset();
                int battles = 0, actions = 0;
                for (int round = 0; round < 40; round++)
                {
                    using (var t = new TestWorld())
                    {
                        // Map A: the player holds cell 0; the AI attacks it from cell 1 with spare ships in cell 5.
                        t.W.difficulty = difficulty;
                        t.SetOwner(1, FactionId.Ai);
                        for (int i = 0; i < 3; i++) t.AddCraft(FactionId.Ai, i == 0 ? "fortress" : "bomber", 1, false);
                        t.AddCraft(FactionId.Ai, "bomber", 5, false);
                        t.AddCraft(FactionId.Player, "fortress", 0, false);
                        t.AddCraft(FactionId.Player, "bomber", 4, false);
                        if (!t.S.Encounters.Start(t.W, t.Catalog, t.Map, 0, FactionId.Ai).success) continue;
                        battles++;
                        for (int guard = 0; guard < 60 && t.W.mode == MatchMode.Tactical; guard++)
                        {
                            if (t.W.encounter.turn == FactionId.Ai) { t.S.Ai.PlayTacticalStep(t.W, t.S.Commands); actions++; }
                            else t.Run(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
                        }
                    }
                }
                sb.Append(difficulty).Append(": ").Append(battles).Append(" battles, ").Append(actions).Append(" AI actions\n").Append(AiTiming.Report(System.Math.Max(1, actions)));
            }
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log("BATTLESKILLS\n" + sb);
        }

        static void Measure(string mapId, int growTicks, bool raids = false)
        {
            using (var t = new TestWorld(mapId))
            {
                t.Catalog.rules.aiExpands = true;
                t.Catalog.rules.aiRaids = raids;
                if (raids)
                {
                    // Test setup: a player headquarters that holds out, so battles keep happening rather than the match ending.
                    var hq = t.W.cells.Find(cell => cell.headquartersFor == FactionId.Player);
                    for (int i = 0; i < 3; i++) t.AddCraft(FactionId.Player, "fortress", hq.id, i == 0);
                }
                t.Ai.currency += 5000; // test setup: a bigger factory sooner
                t.Ai.ion += 2000;
                for (int i = 0; i < growTicks; i++) Step(t);

                const int measured = 2000;
                Perf.Frame(10f); // clear the window
                AiTiming.Reset();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < measured; i++) Step(t);
                sw.Stop();
                Perf.Frame(1f);

                var sb = new StringBuilder();
                sb.Append(mapId).Append(": ").Append((sw.Elapsed.TotalMilliseconds / measured).ToString("0.000")).Append(" ms/tick; ")
                  .Append(t.W.buildings.Count).Append(" buildings, ").Append(t.W.belts.Count).Append(" belts, ")
                  .Append(t.W.tokens.Count).Append(" tokens, ").Append(t.W.pullRequests.Count).Append(" requests; AI worst tick ")
                  .Append(Perf.PeakMs(Perf.Stage.Ai).ToString("0.00")).Append(" ms\n");
                sb.Append(" ").Append(t.S.Ai.Brain.Report().Replace("\n", "\n ")).Append('\n');
                sb.Append(" AI: ").Append(t.Ai.currency).Append(" credits, ").Append(t.Ai.cumulativeSales).Append(" sales, ")
                  .Append(t.W.crafts.FindAll(k => k.owner == FactionId.Ai && WorldLookup.IsCraftLive(k)).Count).Append(" ships, ")
                  .Append(t.W.cells.FindAll(cell => cell.owner == FactionId.Ai).Count).Append(" cells; planner: ").Append(t.S.AiPlanner.LastReason).Append('\n');
                {
                    // The AI's solar: arrays, slots, and the goods an array takes from Storage.
                    int arrays = 0, slots = 0, used = 0;
                    var arrayDef = t.Catalog.buildings.Find(b => b.kind == BuildingKind.SolarArray);
                    foreach (var cell in t.W.cells)
                        if (cell.owner == FactionId.Ai) { int u; slots += GridRules.ArraySlots(t.W, t.Catalog, FactionId.Ai, cell.id, out u); used += u; }
                    foreach (var b in t.W.buildings) if (b.owner == FactionId.Ai && b.typeId == arrayDef.id) arrays++;
                    sb.Append(" AI power: ").Append(t.Ai.powerSupply).Append(" MW supply, ").Append(t.Ai.powerDemand).Append(" MW demand; ")
                      .Append(arrays).Append(" arrays, ").Append(used).Append('/').Append(slots).Append(" slots; in Storage:");
                    foreach (var g in arrayDef.goodsCost) sb.Append(' ').Append(g.itemId).Append(' ').Append(EconomyService.StoredCount(t.W, t.Catalog, FactionId.Ai, g.itemId)).Append('/').Append(g.quantity);
                    sb.Append("; storages ").Append(t.W.buildings.FindAll(b => b.owner == FactionId.Ai && b.typeId == "storage").Count).Append('\n');
                    int levels = 0, goodsLevels = 0;
                    foreach (var r in t.Ai.research)
                    {
                        levels += r.level;
                        var node = CatalogRepository.GetResearch(t.Catalog, r.nodeId);
                        for (int i = 0; node != null && i < r.level && i < node.levels.Count; i++) if (node.levels[i].goods.Count > 0) goodsLevels++;
                    }
                    sb.Append(" AI research: ").Append(levels).Append(" levels (").Append(goodsLevels).Append(" paid with goods), ")
                      .Append(t.Ai.researchQueue.Count).Append(" queued; planner: ").Append(t.S.AiPlanner.LastReason).Append('\n');
                }
                sb.Append(" AI skills and steps:\n").Append(AiTiming.Report(measured));
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
