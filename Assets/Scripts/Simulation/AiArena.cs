using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The headless AI arena: AI v AI matches with no visuals, for volume-testing AI profiles and their weights.
    /// Side A plays the player's faction and side B the game's AI faction; each gets a profile (a preset, or one from
    /// the config with values changed). Both play through the same commands and rules as a person. Matches run as
    /// fast as the simulation allows, and the results go to CSV and JSON files. Run it with tools/ai-arena.sh, or
    /// from code with <see cref="Run(ArenaConfig, List{AiProfile}, string, Action{string})"/>.
    /// </summary>
    public static partial class AiArena
    {
        public const string SideA = "[A] ", SideB = "[B] ";

        /// <summary>
        /// Optional: called at every sample with the match's loop (both AIs: loop.Services.Ai is side B, loop.PlayerSide
        /// side A, an <see cref="AIController"/>). For custom measurements or tracing while stress-testing; it must only read.
        /// </summary>
        public static Action<GameLoop> Probe;

        /// <summary>Reads a config file (see tools/arena/example.json) and runs every match in it.</summary>
        public static ArenaSummary RunFile(string configPath, string outDir, Action<string> log)
        {
            string json = File.ReadAllText(configPath);
            var config = JsonUtility.FromJson<ArenaConfig>(json);
            var profiles = AiProfiles.Parse(json, null);
            if (string.IsNullOrEmpty(config.name)) config.name = Path.GetFileNameWithoutExtension(configPath);
            return Run(config, profiles, outDir, log);
        }

        /// <summary>Runs every pairing, map and repeat in the config; writes matches.csv, skills.csv, samples.csv and summary.json.</summary>
        public static ArenaSummary Run(ArenaConfig config, List<AiProfile> profiles, string outDir, Action<string> log)
        {
            log = log ?? (s => { });
            Directory.CreateDirectory(outDir);
            var known = new Dictionary<string, AiProfile>();
            foreach (var p in profiles) known[p.Id] = p;

            var maps = new List<string>(config.maps);
            for (int i = 0; i < config.randomFields; i++) maps.Add("field-" + (config.fieldSeedStart + i));
            if (maps.Count == 0) maps.Add("field-424242");
            var pairings = new List<ArenaPairing>(config.pairings);
            if (pairings.Count == 0 && config.seatMatches.Count == 0) pairings.Add(new ArenaPairing { a = "medium", b = "medium" });

            var matches = new List<MatchResult>();
            var matchCsv = new StringBuilder(MatchResult.CsvHeader + "\n");
            var skillCsv = new StringBuilder("match,side,profile,skill,ms_per_tick,ms_per_run,runs,worst_ms\n");
            var sampleCsv = new StringBuilder("match,minute,side,profile,credits,sales,sales_per_min,cells,ships,machines,research_levels,power_supply,power_demand\n");
            var reports = new StringBuilder();
            int index = 0;
            var wall = Stopwatch.StartNew();
            foreach (var pairing in pairings)
                foreach (var mapId in maps)
                    for (int repeat = 0; repeat < Math.Max(1, config.repeats); repeat++)
                        for (int swap = 0; swap < (config.swapSides ? 2 : 1); swap++)
                        {
                            string a = swap == 0 ? pairing.a : pairing.b, b = swap == 0 ? pairing.b : pairing.a;
                            var pa = Resolve(a, known);
                            var pb = Resolve(b, known);
                            int seed = config.worldSeed + repeat;
                            var result = PlayMatch(config, index, mapId, seed, pa, pb, skillCsv, sampleCsv);
                            matches.Add(result);
                            matchCsv.Append(result.Csv()).Append('\n');
                            reports.Append("== match ").Append(index).Append(": ").Append(pa.Id).Append(" (A) v ").Append(pb.Id).Append(" (B) on ").Append(mapId)
                                   .Append(": ").Append(result.Winner).Append(", ").Append(result.Reason)
                                   .Append("\n   attacks by A: ").Append(result.A.AttackText()).Append("; by B: ").Append(result.B.AttackText()).Append("\n-- A (player side)\n").Append(result.ReportA)
                                   .Append("\n-- B (AI side)\n").Append(result.ReportB).Append("\n\n");
                            log(string.Format(CultureInfo.InvariantCulture, "match {0}: {1} (A) v {2} (B) on {3} seed {4}: {5} after {6:0.0} min ({7}); {8:0.0}s wall",
                                index, pa.Id, pb.Id, mapId, seed, result.Winner, result.Minutes, result.Reason, result.WallSeconds));
                            index++;
                            // Written as it goes, so a long run can be read before it finishes.
                            File.WriteAllText(Path.Combine(outDir, "matches.csv"), matchCsv.ToString());
                            File.WriteAllText(Path.Combine(outDir, "skills.csv"), skillCsv.ToString());
                            File.WriteAllText(Path.Combine(outDir, "samples.csv"), sampleCsv.ToString());
                            File.WriteAllText(Path.Combine(outDir, "reports.txt"), reports.ToString());
                        }
            if (config.seatMatches.Count > 0) RunSeatMatches(config, known, outDir, log, ref index);
            var summary = ArenaSummary.From(config, matches, wall.Elapsed.TotalSeconds);
            if (matches.Count > 0)
            {
                File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonUtility.ToJson(summary, true));
                File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.Text());
                log(summary.Text());
            }
            return summary;
        }

        static AiProfile Resolve(string id, Dictionary<string, AiProfile> known)
        {
            AiProfile p;
            if (known.TryGetValue(id, out p)) return p;
            p = AiProfiles.Get(id);
            if (p == null) throw new ArgumentException("Unknown profile '" + id + "' (presets: " + string.Join(", ", AiProfiles.PresetIds()) + ").");
            return p;
        }

        /// <summary>Plays one AI v AI match to a result or the time limit.</summary>
        public static MatchResult PlayMatch(ArenaConfig config, int index, string mapId, int seed, AiProfile a, AiProfile b,
            StringBuilder skillCsv, StringBuilder sampleCsv)
        {
            var catalog = CatalogRepository.LoadContent();
            catalog.rules.aiExpands = config.rules.aiExpands;
            catalog.rules.aiRaids = config.rules.aiRaids;
            catalog.rules.aiDefends = config.rules.aiDefends;
            if (!config.tradeWin) catalog.rules.tradeWinPerMinute.Clear(); // conquest only: the trade win is the player side's alone
            var map = CatalogRepository.LoadMap(mapId, catalog);
            var world = GameFactory.CreateNew(catalog, map, seed);
            world.difficulty = ParseDifficulty(config.difficulty);
            string saveDir = Path.Combine(Path.GetTempPath(), "OrbitalFoundryArena", Guid.NewGuid().ToString("N"));
            var loop = new GameLoop();
            loop.Initialize(catalog, map, world, new SaveService(saveDir));
            var s = loop.Services;
            s.Ai.Profile = b;
            s.Ai.TimingTag = SideB;
            var sideA = new AIController(s, FactionId.Player, new AIFactoryPlanner(), new AITactics(s)) { Profile = a, TimingTag = SideA };
            loop.PlayerSide = sideA;

            AiTiming.Reset();
            var result = new MatchResult { Index = index, Map = mapId, Seed = seed, ProfileA = a.Id, ProfileB = b.Id };
            int tickMs = Math.Max(1, catalog.rules.tickMilliseconds);
            int maxTicks = (int)(config.maxMinutes * 60000f / tickMs);
            int sampleTicks = Math.Max(1, (int)(config.sampleEverySeconds * 1000f / tickMs));
            var wall = Stopwatch.StartNew();
            var lastSales = new Dictionary<FactionId, int> { { FactionId.Player, 0 }, { FactionId.Ai, 0 } };
            bool wasTactical = false;
            int tacticalSteps = 0;
            FactionId attacker = FactionId.Neutral;
            try
            {
                while (world.outcome == MatchOutcome.None && world.mode != MatchMode.Ended && world.tick < maxTicks)
                {
                    if (world.mode == MatchMode.Tactical)
                    {
                        if (!wasTactical) { result.Battles++; tacticalSteps = 0; attacker = world.encounter.attacker; }
                        wasTactical = true;
                        int before = world.encounter.round * 4 + (int)world.encounter.turn;
                        loop.RunAiTurnImmediately();
                        int after = world.encounter.round * 4 + (int)world.encounter.turn;
                        // A turn that changed nothing (an AI with no move) is ended for it, so a battle cannot stall.
                        if (world.mode == MatchMode.Tactical && before == after)
                            s.Commands.Execute(world, catalog, map, new GameCommand { kind = CommandKind.EndTurn, actor = world.encounter.turn });
                        if (++tacticalSteps > config.maxBattleTurns && world.mode == MatchMode.Tactical)
                        {
                            s.Commands.Execute(world, catalog, map, new GameCommand { kind = CommandKind.Retreat, actor = world.encounter.attacker });
                            result.StalledBattles++;
                        }
                        continue;
                    }
                    if (wasTactical) BattleEnded(result, attacker, s.Encounters.LastResult);
                    wasTactical = false;
                    loop.TickNormal();
                    if (world.tick % sampleTicks == 0)
                    {
                        Sample(sampleCsv, result, world, catalog, lastSales, sampleTicks, tickMs);
                        if (Probe != null) Probe(loop);
                    }
                }
            }
            catch (Exception e)
            {
                result.Reason = "error: " + e.GetType().Name + ": " + e.Message.Replace(',', ';').Replace('\n', ' ');
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                try { if (Directory.Exists(saveDir)) Directory.Delete(saveDir, true); } catch (IOException) { }
            }
            result.WallSeconds = wall.Elapsed.TotalSeconds;
            result.Ticks = world.tick;
            result.Minutes = world.tick * tickMs / 60000f;
            var battlesA = result.A; var battlesB = result.B;
            result.A = SideStats.Of(world, catalog, FactionId.Player);
            result.B = SideStats.Of(world, catalog, FactionId.Ai);
            result.A.CopyAttacks(battlesA);
            result.B.CopyAttacks(battlesB);
            // What each side was thinking at the end: stance, budget, targets and the reasons behind them.
            result.ReportA = sideA.Brain.Report() + "\n  planner: " + sideA.Planner.LastReason + "  ·  last action: " + sideA.LastAction;
            result.ReportB = s.Ai.Brain.Report() + "\n  planner: " + s.AiPlanner.LastReason + "  ·  last action: " + s.Ai.LastAction;
            result.A.AiMs = Timing(skillCsv, result, SideA, a.Id, "A");
            result.B.AiMs = Timing(skillCsv, result, SideB, b.Id, "B");
            if (string.IsNullOrEmpty(result.Reason))
            {
                if (WorldLookup.HasWon(world, FactionId.Ai)) { result.Winner = "B"; result.Reason = WorldLookup.GetFaction(world, FactionId.Ai).trade.won ? "B's trade win" : "B took A's headquarters"; }
                else if (WorldLookup.HasWon(world, FactionId.Player)) { result.Winner = "A"; result.Reason = WorldLookup.GetFaction(world, FactionId.Player).trade.won ? "A's trade win" : "A took B's headquarters"; }
                else { result.Winner = "draw"; result.Reason = "time limit"; }
            }
            else result.Winner = "error";
            // At the time limit, who was ahead: more cells, then more sales.
            result.Leader = result.A.Cells != result.B.Cells ? (result.A.Cells > result.B.Cells ? "A" : "B")
                : result.A.Sales != result.B.Sales ? (result.A.Sales > result.B.Sales ? "A" : "B") : "even";
            return result;
        }

        /// <summary>Counts how a battle ended against the side that started it (from the battle's closing event).</summary>
        static void BattleEnded(MatchResult r, FactionId attacker, EncounterService.BattleResult how)
        {
            var st = attacker == FactionId.Player ? r.A : r.B;
            st.Attacks++;
            switch (how)
            {
                case EncounterService.BattleResult.Captured: st.AttacksWon++; break;
                case EncounterService.BattleResult.Fatigued: st.AttacksFatigued++; break;
                case EncounterService.BattleResult.Withdrew: st.AttacksWithdrawn++; break;
                case EncounterService.BattleResult.Repelled: st.AttacksRepelled++; break;
            }
        }

        static Difficulty ParseDifficulty(string text)
        {
            switch ((text ?? "").ToLowerInvariant())
            {
                case "easy": return Difficulty.Easy;
                case "hard": return Difficulty.Hard;
                default: return Difficulty.Medium;
            }
        }

        static void Sample(StringBuilder csv, MatchResult r, WorldState w, ContentCatalog c, Dictionary<FactionId, int> lastSales, int sampleTicks, int tickMs)
        {
            float minute = w.tick * tickMs / 60000f;
            foreach (var side in new[] { FactionId.Player, FactionId.Ai })
            {
                var st = SideStats.Of(w, c, side);
                float perMinute = (st.Sales - lastSales[side]) * 60000f / (sampleTicks * tickMs);
                lastSales[side] = st.Sales;
                csv.Append(r.Index).Append(',').Append(minute.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                   .Append(side == FactionId.Player ? "A" : "B").Append(',').Append(side == FactionId.Player ? r.ProfileA : r.ProfileB).Append(',')
                   .Append(st.Credits).Append(',').Append(st.Sales).Append(',').Append(perMinute.ToString("0", CultureInfo.InvariantCulture)).Append(',')
                   .Append(st.Cells).Append(',').Append(st.Ships).Append(',').Append(st.Machines).Append(',').Append(st.ResearchLevels).Append(',')
                   .Append(st.PowerSupply).Append(',').Append(st.PowerDemand).Append('\n');
            }
        }

        /// <summary>Writes the side's skill timings and returns its AI milliseconds per game tick.</summary>
        static double Timing(StringBuilder csv, MatchResult r, string tag, string profile, string side)
        {
            double total = 0;
            int ticks = Math.Max(1, r.Ticks);
            foreach (var line in AiTiming.Lines(tag))
            {
                total += line.TotalMs;
                csv.Append(r.Index).Append(',').Append(side).Append(',').Append(profile).Append(',').Append(line.Name.Replace(',', ';')).Append(',')
                   .Append((line.TotalMs / ticks).ToString("0.00000", CultureInfo.InvariantCulture)).Append(',')
                   .Append((line.TotalMs / Math.Max(1, line.Runs)).ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                   .Append(line.Runs).Append(',').Append(line.PeakMs.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
            }
            return total / ticks;
        }
    }

    /// <summary>An arena run: what to play, where, how often and for how long.</summary>
    [Serializable]
    public sealed class ArenaConfig
    {
        public string name = "";
        /// <summary>Map ids ("map_a", "field-424242", ...).</summary>
        public List<string> maps = new List<string>();
        /// <summary>Also play this many generated fields, field-(fieldSeedStart) onwards.</summary>
        public int randomFields;
        public int fieldSeedStart = 1000;
        /// <summary>Profile pairs: a plays the player's side, b the AI's.</summary>
        public List<ArenaPairing> pairings = new List<ArenaPairing>();
        /// <summary>Each pairing on each map this many times (world seed worldSeed, worldSeed + 1, ...).</summary>
        public int repeats = 1;
        public int worldSeed = 1234;
        /// <summary>Also play each match with the sides swapped, so neither profile keeps a map's better start.</summary>
        public bool swapSides = true;
        /// <summary>Game minutes before a match is called a draw.</summary>
        public float maxMinutes = 60f;
        /// <summary>Battle turns before an attack is called off (a stalled battle).</summary>
        public int maxBattleTurns = 400;
        /// <summary>The trade win only exists for the player's side, so it is off unless asked for.</summary>
        public bool tradeWin;
        /// <summary>The match difficulty (the trade target, and the preset for any side without a profile).</summary>
        public string difficulty = "medium";
        public float sampleEverySeconds = 60f;
        public ArenaRules rules = new ArenaRules();
        /// <summary>
        /// Matches of up to four seats on maps with that many headquarters (map_d), each seat an AI profile with an
        /// optional team. Each is played on each repeat; with <see cref="rotateSeats"/>, once per rotation of who sits
        /// where. Results go to seats.csv and seats_summary.txt.
        /// </summary>
        public List<ArenaSeatMatch> seatMatches = new List<ArenaSeatMatch>();
        /// <summary>Also play each seat match with the seats rotated, so no profile keeps a corner.</summary>
        public bool rotateSeats = true;
    }

    /// <summary>One seat match: the map, and per seat in seat order, the profile and team (0: on its own).</summary>
    [Serializable]
    public sealed class ArenaSeatMatch
    {
        public string map = "map_d";
        public List<ArenaSeat> seats = new List<ArenaSeat>();
    }

    [Serializable]
    public sealed class ArenaSeat { public string profile = "medium"; public int team; }

    [Serializable]
    public sealed class ArenaPairing { public string a = "", b = ""; }

    [Serializable]
    public sealed class ArenaRules { public bool aiExpands = true, aiRaids = true, aiDefends = true; }

    /// <summary>A side's state at the end of a match (or a sample).</summary>
    [Serializable]
    public sealed class SideStats
    {
        public int Credits, Sales, Cells, Ships, Machines, ResearchLevels, PowerSupply, PowerDemand;
        /// <summary>Battles this side started, and how they ended: cell taken, withdrew (retreat), ran out of rounds, wiped out.</summary>
        public int Attacks, AttacksWon, AttacksWithdrawn, AttacksFatigued, AttacksRepelled;
        public double AiMs;

        public void CopyAttacks(SideStats from)
        {
            Attacks = from.Attacks; AttacksWon = from.AttacksWon; AttacksWithdrawn = from.AttacksWithdrawn;
            AttacksFatigued = from.AttacksFatigued; AttacksRepelled = from.AttacksRepelled;
        }

        public string AttackText()
        {
            return Attacks + " (" + AttacksWon + " won/" + AttacksWithdrawn + " withdrew/" + AttacksFatigued + " fatigued/" + AttacksRepelled + " repelled)";
        }

        public static SideStats Of(WorldState w, ContentCatalog c, FactionId side)
        {
            var f = WorldLookup.GetFaction(w, side);
            var st = new SideStats();
            if (f == null) return st;
            st.Credits = f.currency;
            st.Sales = f.cumulativeSales;
            st.PowerSupply = f.powerSupply;
            st.PowerDemand = f.powerDemand;
            foreach (var r in f.research) st.ResearchLevels += r.level;
            foreach (var cell in w.cells) if (cell.owner == side) st.Cells++;
            foreach (var k in w.crafts) if (k.owner == side && WorldLookup.IsCraftLive(k)) st.Ships++;
            foreach (var b in w.buildings) if (b.owner == side) st.Machines++;
            return st;
        }
    }

    [Serializable]
    public sealed class MatchResult
    {
        public int Index, Seed, Ticks, Battles, StalledBattles;
        public string Map = "", ProfileA = "", ProfileB = "", Winner = "", Reason = "", Leader = "";
        [NonSerialized] public string ReportA = "", ReportB = "";
        public float Minutes;
        public double WallSeconds;
        public SideStats A = new SideStats(), B = new SideStats();

        public const string CsvHeader = "match,map,seed,profile_a,profile_b,winner,reason,leader,minutes,battles,stalled_battles,wall_s,"
            + "a_sales,a_credits,a_cells,a_ships,a_machines,a_research,a_ai_ms_per_tick,a_attacks,a_attacks_won,a_withdrew,a_fatigued,a_repelled,"
            + "b_sales,b_credits,b_cells,b_ships,b_machines,b_research,b_ai_ms_per_tick,b_attacks,b_attacks_won,b_withdrew,b_fatigued,b_repelled";

        public string Csv()
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(",", new[]
            {
                Index.ToString(inv), Map, Seed.ToString(inv), ProfileA, ProfileB, Winner, Reason.Replace(',', ';'), Leader,
                Minutes.ToString("0.0", inv), Battles.ToString(inv), StalledBattles.ToString(inv), WallSeconds.ToString("0.0", inv),
                A.Sales.ToString(inv), A.Credits.ToString(inv), A.Cells.ToString(inv), A.Ships.ToString(inv), A.Machines.ToString(inv), A.ResearchLevels.ToString(inv), A.AiMs.ToString("0.0000", inv),
                A.Attacks.ToString(inv), A.AttacksWon.ToString(inv), A.AttacksWithdrawn.ToString(inv), A.AttacksFatigued.ToString(inv), A.AttacksRepelled.ToString(inv),
                B.Sales.ToString(inv), B.Credits.ToString(inv), B.Cells.ToString(inv), B.Ships.ToString(inv), B.Machines.ToString(inv), B.ResearchLevels.ToString(inv), B.AiMs.ToString("0.0000", inv),
                B.Attacks.ToString(inv), B.AttacksWon.ToString(inv), B.AttacksWithdrawn.ToString(inv), B.AttacksFatigued.ToString(inv), B.AttacksRepelled.ToString(inv)
            });
        }
    }

    /// <summary>Per profile: matches, wins, losses, draws, and averages, over a run.</summary>
    [Serializable]
    public sealed class ArenaSummary
    {
        [Serializable]
        public sealed class ProfileLine
        {
            public string Profile = "";
            public int Matches, Wins, Losses, Draws, Errors, LedAtLimit, Attacks, AttacksWon;
            public float WinRate, AvgSales, AvgCells, AvgMinutesToWin;
            public double AvgAiMsPerTick;
        }

        public string Name = "";
        public int Matches;
        public double WallSeconds;
        public List<ProfileLine> Profiles = new List<ProfileLine>();

        public static ArenaSummary From(ArenaConfig config, List<MatchResult> matches, double wallSeconds)
        {
            var summary = new ArenaSummary { Name = config.name, Matches = matches.Count, WallSeconds = wallSeconds };
            var lines = new Dictionary<string, ProfileLine>();
            var winMinutes = new Dictionary<string, float>();
            foreach (var m in matches)
                for (int side = 0; side < 2; side++)
                {
                    string id = side == 0 ? m.ProfileA : m.ProfileB, me = side == 0 ? "A" : "B";
                    var st = side == 0 ? m.A : m.B;
                    ProfileLine l;
                    if (!lines.TryGetValue(id, out l)) { lines[id] = l = new ProfileLine { Profile = id }; summary.Profiles.Add(l); winMinutes[id] = 0f; }
                    l.Matches++;
                    if (m.Winner == me) { l.Wins++; winMinutes[id] += m.Minutes; }
                    else if (m.Winner == "draw") { l.Draws++; if (m.Leader == me) l.LedAtLimit++; }
                    else if (m.Winner == "error") l.Errors++;
                    else l.Losses++;
                    l.AvgSales += st.Sales;
                    l.Attacks += st.Attacks;
                    l.AttacksWon += st.AttacksWon;
                    l.AvgCells += st.Cells;
                    l.AvgAiMsPerTick += st.AiMs;
                }
            foreach (var l in summary.Profiles)
            {
                l.WinRate = l.Matches > 0 ? (float)l.Wins / l.Matches : 0f;
                l.AvgSales /= Math.Max(1, l.Matches);
                l.AvgCells /= Math.Max(1, l.Matches);
                l.AvgAiMsPerTick /= Math.Max(1, l.Matches);
                l.AvgMinutesToWin = l.Wins > 0 ? winMinutes[l.Profile] / l.Wins : 0f;
            }
            return summary;
        }

        public string Text()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("Arena '").Append(Name).Append("': ").Append(Matches).Append(" matches in ").Append(WallSeconds.ToString("0", inv)).Append(" s\n");
            sb.Append("profile            matches  wins losses draws(led) errors  win%   avg sales  avg cells  min to win  attacks(won)  AI ms/tick\n");
            foreach (var l in Profiles)
                sb.Append(l.Profile.PadRight(18)).Append(l.Matches.ToString(inv).PadLeft(8)).Append(l.Wins.ToString(inv).PadLeft(6))
                  .Append(l.Losses.ToString(inv).PadLeft(7)).Append((l.Draws + "(" + l.LedAtLimit + ")").PadLeft(11)).Append(l.Errors.ToString(inv).PadLeft(7))
                  .Append((l.WinRate * 100f).ToString("0", inv).PadLeft(6)).Append(l.AvgSales.ToString("0", inv).PadLeft(12))
                  .Append(l.AvgCells.ToString("0.0", inv).PadLeft(11)).Append(l.AvgMinutesToWin.ToString("0.0", inv).PadLeft(12))
                  .Append((l.Attacks + "(" + l.AttacksWon + ")").PadLeft(14))
                  .Append(l.AvgAiMsPerTick.ToString("0.000", inv).PadLeft(12)).Append('\n');
            return sb.ToString();
        }
    }
}
