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
        /// Four AI seats on Four Corners (map_d), each on its own, with expansion, raids and defence on: what every
        /// machine in a four-player lockstep game simulates. Grows for thirty minutes, then times 2,000 ticks one by one
        /// (the spikes drop frames, not the average) and each seat's AI. Writes Logs/perf_four_seats.txt.
        /// </summary>
        [Test]
        public void FourSeatsOnFourCorners()
        {
            var catalog = CatalogRepository.LoadContent();
            catalog.rules.aiExpands = catalog.rules.aiRaids = catalog.rules.aiDefends = true;
            var map = CatalogRepository.LoadMap("map_d", catalog);
            var seats = GameFactory.DefaultSeats(map);
            foreach (var s in seats) s.ai = true;
            var loop = new GameLoop();
            loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 4321, seats), null);
            var w = loop.World;
            foreach (var f in w.factions) ((AIController)loop.ControllerFor(f.id)).TimingTag = "[" + (int)f.id + "] ";
            for (int i = 0; i < 18000 && w.mode != MatchMode.Ended; i++) StepAll(loop);

            const int measured = 2000;
            var ticks = new double[measured];
            Perf.Frame(10f);
            AiTiming.Reset();
            Perf.TrackAllocations = true;
            Perf.ResetAllocations();
            int gcBefore = System.GC.CollectionCount(0);
            long allocBefore = System.GC.GetAllocatedBytesForCurrentThread();
            var watch = new Stopwatch();
            int n = 0;
            for (; n < measured && w.mode != MatchMode.Ended; n++)
            {
                watch.Restart();
                StepAll(loop);
                ticks[n] = watch.Elapsed.TotalMilliseconds;
            }
            Perf.Frame(1f);
            int collections = System.GC.CollectionCount(0) - gcBefore;
            Perf.TrackAllocations = false;
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            var sorted = new double[n];
            System.Array.Copy(ticks, sorted, n);
            System.Array.Sort(sorted);
            double sum = 0; foreach (var x in sorted) sum += x;
            int over4 = 0, over8 = 0; foreach (var x in sorted) { if (x > 4) over4++; if (x > 8) over8++; }

            var sb = new StringBuilder("Four seats on map_d at tick " + w.tick + " (" + w.buildings.Count + " buildings, " + w.belts.Count + " belts, "
                + w.tokens.Count + " tokens, " + w.crafts.Count + " craft)\n");
            sb.Append(" per tick: mean ").Append((sum / System.Math.Max(1, n)).ToString("0.000")).Append(" ms, median ").Append(sorted[n / 2].ToString("0.000"))
              .Append(", p95 ").Append(sorted[(int)(n * 0.95)].ToString("0.000")).Append(", p99 ").Append(sorted[(int)(n * 0.99)].ToString("0.000"))
              .Append(", worst ").Append(sorted[n - 1].ToString("0.000")).Append(" ms; over 4 ms: ").Append(over4).Append(", over 8 ms: ").Append(over8).Append(" of ").Append(n).Append('\n');
            sb.Append(" garbage: ").Append((allocated / 1024.0 / System.Math.Max(1, n)).ToString("0.0")).Append(" KB allocated a tick, ")
              .Append(collections).Append(" collections in ").Append(n).Append(" ticks\n");
            foreach (var f in w.factions)
            {
                double ms = 0, peak = 0;
                foreach (var line in AiTiming.Lines("[" + (int)f.id + "] ")) { ms += line.TotalMs; if (line.PeakMs > peak) peak = line.PeakMs; }
                sb.Append(" seat ").Append((int)f.id).Append(": AI ").Append((ms / n).ToString("0.000")).Append(" ms/tick (worst step ").Append(peak.ToString("0.00"))
                  .Append(" ms), cells ").Append(w.cells.FindAll(c => c.owner == f.id).Count).Append(", buildings ").Append(w.buildings.FindAll(b => b.owner == f.id).Count)
                  .Append(f.suppressed ? " (out)" : "").Append('\n');
            }
            for (int s = 0; s < (int)Perf.FirstFrameStage; s++)
                sb.Append("  ").Append((Perf.Stage)s).Append(": ").Append((Perf.MsPerSecond((Perf.Stage)s) / n).ToString("0.0000"))
                  .Append(" ms/tick, worst ").Append(Perf.PeakMs((Perf.Stage)s).ToString("0.00"))
                  .Append(", ").Append((Perf.AllocatedBytes((Perf.Stage)s) / 1024.0 / n).ToString("0.0")).Append(" KB/tick allocated\n");
            sb.Append(" AI skills and steps (all seats):\n").Append(AiTiming.Report(n));
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/perf_four_seats.txt", sb.ToString());
            TestContext.WriteLine(sb.ToString());
            UnityEngine.Debug.Log(sb.ToString());
        }

        static void StepAll(GameLoop loop)
        {
            var w = loop.World;
            loop.TickNormal();
            for (int guard = 0; guard < 400 && w.mode == MatchMode.Tactical; guard++)
            {
                int before = w.encounter.round * 8 + (int)w.encounter.turn;
                loop.RunAiTurnImmediately();
                if (w.mode == MatchMode.Tactical && before == w.encounter.round * 8 + (int)w.encounter.turn)
                    loop.Services.Commands.Execute(w, loop.Catalog, loop.Map, new GameCommand { kind = CommandKind.EndTurn, actor = w.encounter.turn });
            }
            if (w.mode == MatchMode.Tactical)
                loop.Services.Commands.Execute(w, loop.Catalog, loop.Map, new GameCommand { kind = CommandKind.Retreat, actor = w.encounter.attacker });
        }

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
