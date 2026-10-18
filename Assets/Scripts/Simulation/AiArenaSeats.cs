using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OrbitalFoundry
{
    /// <summary>
    /// The arena's seat matches: up to four AI seats, in teams or each on its own, on a map with a headquarters per seat.
    /// Every seat is an AI, so a match runs as fast as the simulation allows, like the two-sided matches.
    /// </summary>
    public static partial class AiArena
    {
        /// <summary>How one seat match ended, and each seat's part in it.</summary>
        public sealed class SeatMatchResult
        {
            public int Index, Seed, Ticks, Battles;
            public string Map = "", Reason = "";
            public float Minutes;
            public double WallSeconds;
            public readonly List<SeatLine> Seats = new List<SeatLine>();
        }

        public sealed class SeatLine
        {
            public FactionId Seat;
            public string Profile = "";
            public int Team;
            public bool Won, Out;
            public SideStats Stats;
        }

        static void RunSeatMatches(ArenaConfig config, Dictionary<string, AiProfile> known, string outDir, Action<string> log, ref int index)
        {
            var csv = new StringBuilder("match,map,seed,minutes,battles,reason,seat,profile,team,won,out,cells,sales,ships,machines,research_levels,ai_ms_per_tick\n");
            var results = new List<SeatMatchResult>();
            foreach (var match in config.seatMatches)
            {
                int n = match.seats.Count;
                for (int repeat = 0; repeat < Math.Max(1, config.repeats); repeat++)
                    for (int turn = 0; turn < (config.rotateSeats ? n : 1); turn++)
                    {
                        // Rotation: seat i plays the entry (i + turn) of the list.
                        var seats = new List<ArenaSeat>();
                        for (int i = 0; i < n; i++) seats.Add(match.seats[(i + turn) % n]);
                        var r = PlaySeatMatch(config, index, match.map, config.worldSeed + repeat, seats, known);
                        results.Add(r);
                        foreach (var line in r.Seats)
                        {
                            var st = line.Stats;
                            csv.Append(r.Index).Append(',').Append(r.Map).Append(',').Append(r.Seed).Append(',')
                               .Append(r.Minutes.ToString("0.0", CultureInfo.InvariantCulture)).Append(',').Append(r.Battles).Append(',')
                               .Append(r.Reason.Replace(',', ';')).Append(',').Append((int)line.Seat).Append(',').Append(line.Profile).Append(',')
                               .Append(line.Team).Append(',').Append(line.Won ? 1 : 0).Append(',').Append(line.Out ? 1 : 0).Append(',')
                               .Append(st.Cells).Append(',').Append(st.Sales).Append(',').Append(st.Ships).Append(',').Append(st.Machines).Append(',')
                               .Append(st.ResearchLevels).Append(',').Append(st.AiMs.ToString("0.000", CultureInfo.InvariantCulture)).Append('\n');
                        }
                        log(string.Format(CultureInfo.InvariantCulture, "seat match {0} on {1} seed {2}: {3} after {4:0.0} min; {5:0.0}s wall",
                            r.Index, r.Map, r.Seed, r.Reason, r.Minutes, r.WallSeconds));
                        index++;
                        File.WriteAllText(Path.Combine(outDir, "seats.csv"), csv.ToString());
                    }
            }
            string summary = SeatSummary(config, results);
            File.WriteAllText(Path.Combine(outDir, "seats_summary.txt"), summary);
            log(summary);
        }

        /// <summary>Plays one seat match to a decision or the time limit.</summary>
        public static SeatMatchResult PlaySeatMatch(ArenaConfig config, int index, string mapId, int seed, List<ArenaSeat> seats, Dictionary<string, AiProfile> known)
        {
            var catalog = CatalogRepository.LoadContent();
            catalog.rules.aiExpands = config.rules.aiExpands;
            catalog.rules.aiRaids = config.rules.aiRaids;
            catalog.rules.aiDefends = config.rules.aiDefends;
            if (!config.tradeWin) catalog.rules.tradeWinPerMinute.Clear();
            var map = CatalogRepository.LoadMap(mapId, catalog);
            var offered = GameFactory.DefaultSeats(map);
            if (offered.Count != seats.Count)
                throw new ArgumentException("Map " + mapId + " has " + offered.Count + " seats; the match lists " + seats.Count + ".");
            var setup = new List<SeatSetup>();
            for (int i = 0; i < seats.Count; i++) setup.Add(new SeatSetup { seat = offered[i].seat, ai = true, team = seats[i].team });
            var world = GameFactory.CreateNew(catalog, map, seed, setup);
            world.difficulty = ParseDifficulty(config.difficulty);
            var loop = new GameLoop();
            loop.Initialize(catalog, map, world, null);
            for (int i = 0; i < seats.Count; i++)
            {
                var ai = (AIController)loop.ControllerFor(offered[i].seat);
                ai.Profile = Resolve(seats[i].profile, known);
                ai.TimingTag = "[" + (int)offered[i].seat + "] ";
            }

            AiTiming.Reset();
            var r = new SeatMatchResult { Index = index, Map = mapId, Seed = seed };
            int tickMs = Math.Max(1, catalog.rules.tickMilliseconds);
            int maxTicks = (int)(config.maxMinutes * 60000f / tickMs);
            var wall = Stopwatch.StartNew();
            loop.Services.Encounters.BattleStarted += battle => r.Battles++;
            try
            {
                while (world.outcome == MatchOutcome.None && world.mode != MatchMode.Ended && world.tick < maxTicks) loop.TickNormal();
                r.Reason = world.outcome == MatchOutcome.Decided
                    ? "team " + WorldLookup.TeamOf(world, world.winners[0]) + " won (" + string.Join("+", world.winners.ConvertAll(f => ((int)f).ToString())) + ")"
                      + (world.factions.Exists(f => world.winners.Contains(f.id) && f.trade.won) ? " by trade" : "")
                    : "time limit";
            }
            catch (Exception e)
            {
                r.Reason = "error: " + e.GetType().Name + ": " + e.Message.Replace('\n', ' ');
                UnityEngine.Debug.LogException(e);
            }
            r.WallSeconds = wall.Elapsed.TotalSeconds;
            r.Ticks = world.tick;
            r.Minutes = world.tick * tickMs / 60000f;
            for (int i = 0; i < seats.Count; i++)
            {
                var seat = offered[i].seat;
                var st = SideStats.Of(world, catalog, seat);
                double ms = 0;
                foreach (var line in AiTiming.Lines("[" + (int)seat + "] ")) ms += line.TotalMs;
                st.AiMs = ms / Math.Max(1, world.tick);
                r.Seats.Add(new SeatLine
                {
                    Seat = seat, Profile = seats[i].profile, Team = seats[i].team, Stats = st,
                    Won = WorldLookup.HasWon(world, seat), Out = WorldLookup.IsSuppressed(world, seat),
                });
            }
            return r;
        }

        /// <summary>Per profile: seats played, wins (a seat on the winning team), how often it was knocked out, and averages.</summary>
        static string SeatSummary(ArenaConfig config, List<SeatMatchResult> results)
        {
            var order = new List<string>();
            var played = new Dictionary<string, int>();
            var won = new Dictionary<string, int>();
            var outs = new Dictionary<string, int>();
            var cells = new Dictionary<string, float>();
            var sales = new Dictionary<string, float>();
            var ms = new Dictionary<string, double>();
            int decided = 0, errors = 0;
            foreach (var r in results)
            {
                if (r.Reason.StartsWith("team")) decided++;
                if (r.Reason.StartsWith("error")) errors++;
                foreach (var s in r.Seats)
                {
                    if (!played.ContainsKey(s.Profile)) { order.Add(s.Profile); played[s.Profile] = 0; won[s.Profile] = 0; outs[s.Profile] = 0; cells[s.Profile] = 0; sales[s.Profile] = 0; ms[s.Profile] = 0; }
                    played[s.Profile]++;
                    if (s.Won) won[s.Profile]++;
                    if (s.Out) outs[s.Profile]++;
                    cells[s.Profile] += s.Stats.Cells;
                    sales[s.Profile] += s.Stats.Sales;
                    ms[s.Profile] += s.Stats.AiMs;
                }
            }
            var sb = new StringBuilder();
            sb.Append("Arena '").Append(config.name).Append("' seat matches: ").Append(results.Count).Append(" played, ")
              .Append(decided).Append(" decided, ").Append(results.Count - decided - errors).Append(" at the time limit, ").Append(errors).Append(" errors\n");
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,-18} {1,6} {2,5} {3,6} {4,5} {5,10} {6,10} {7,11}\n", "profile", "seats", "wins", "win%", "outs", "avg cells", "avg sales", "AI ms/tick"));
            foreach (var p in order)
            {
                int n = Math.Max(1, played[p]);
                sb.Append(string.Format(CultureInfo.InvariantCulture, "{0,-18} {1,6} {2,5} {3,6:0} {4,5} {5,10:0.0} {6,10:0} {7,11:0.000}\n",
                    p, played[p], won[p], won[p] * 100f / n, outs[p], cells[p] / n, sales[p] / n, ms[p] / n));
            }
            return sb.ToString();
        }
    }
}
