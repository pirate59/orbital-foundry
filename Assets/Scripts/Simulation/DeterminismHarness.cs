using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// A match to record or replay, and what came of it: the setup (map, seed, the two sides' AI profiles, the rules),
    /// side A's command journal, and world hashes at regular ticks. Side A stands in for a person: in a replay it is
    /// played from its journal alone while side B's AI runs live, exactly as a lockstep peer would see it.
    /// </summary>
    [Serializable]
    public sealed class MatchRecording
    {
        public string mapId = "map_a";
        public int seed = 1234;
        public string profileA = "hard";
        public string profileB = "hard";
        public bool aiExpands = true, aiRaids = true, aiDefends = true;
        /// <summary>Game ticks to play (the match may end sooner).</summary>
        public int ticks = 6000;
        public int checkpointEvery = 100;
        /// <summary>Battle turns before a battle is called off, so a stalemate cannot hang the run.</summary>
        public int maxBattleSteps = 400;

        public List<JournalEntry> commands = new List<JournalEntry>();
        public List<WorldCheckpoint> checkpoints = new List<WorldCheckpoint>();
        public int battles;

        /// <summary>The same setup with nothing recorded yet.</summary>
        public MatchRecording Setup()
        {
            return new MatchRecording
            {
                mapId = mapId, seed = seed, profileA = profileA, profileB = profileB,
                aiExpands = aiExpands, aiRaids = aiRaids, aiDefends = aiDefends,
                ticks = ticks, checkpointEvery = checkpointEvery, maxBattleSteps = maxBattleSteps,
            };
        }
    }

    /// <summary>
    /// Checks that play is deterministic, the property lockstep multiplayer rests on: the same match played twice comes
    /// out the same, and a side replayed from its commands alone reproduces the original match, hash for hash.
    /// Headless, like the arena: AI v AI with no visuals.
    /// </summary>
    public static class DeterminismHarness
    {
        /// <summary>Plays the match with both AIs, recording side A's commands and the world hashes.</summary>
        public static MatchRecording Record(MatchRecording setup)
        {
            var rec = setup.Setup();
            var loop = NewLoop(rec, null);
            var side = new RecordingSide(NewAi(loop, FactionId.Player, rec.profileA));
            loop.PlayerSide = side;
            rec.battles = Run(loop, rec, rec.ticks, rec.checkpoints);
            rec.commands = side.Entries;
            return rec;
        }

        /// <summary>
        /// Plays the recording again with side A driven by its journal and side B's AI live. Returns the world hashes;
        /// <paramref name="firstMisplacedSlot"/> is the first slot that fell on a different tick, or -1.
        /// </summary>
        public static List<WorldCheckpoint> Replay(MatchRecording rec, out int firstMisplacedSlot)
        {
            var loop = NewLoop(rec, null);
            var side = new ReplaySide(rec.commands, loop.Catalog, loop.Map);
            loop.PlayerSide = side;
            var hashes = new List<WorldCheckpoint>();
            Run(loop, rec, rec.ticks, hashes);
            firstMisplacedSlot = side.FirstMisplacedSlot;
            return hashes;
        }

        /// <summary>
        /// A loop for the match: the setup's rules, side B's AI with its profile, and a new world (or
        /// <paramref name="world"/>, a loaded one). Side A is left for the caller to attach.
        /// </summary>
        public static GameLoop NewLoop(MatchRecording setup, WorldState world)
        {
            var catalog = CatalogRepository.LoadContent();
            catalog.rules.aiExpands = setup.aiExpands;
            catalog.rules.aiRaids = setup.aiRaids;
            catalog.rules.aiDefends = setup.aiDefends;
            catalog.rules.tradeWinPerMinute.Clear(); // conquest only, as in the arena: the trade win is side A's alone
            var map = CatalogRepository.LoadMap(setup.mapId, catalog);
            var loop = new GameLoop();
            loop.Initialize(catalog, map, world ?? GameFactory.CreateNew(catalog, map, setup.seed), null);
            loop.Services.Ai.Profile = Profile(setup.profileB);
            return loop;
        }

        /// <summary>An AI for <paramref name="side"/> with its own planner and tactics, as the arena's side A.</summary>
        public static AIController NewAi(GameLoop loop, FactionId side, string profileId)
        {
            var s = loop.Services;
            return new AIController(s, side, new AIFactoryPlanner(), new AITactics(s)) { Profile = Profile(profileId) };
        }

        static AiProfile Profile(string id)
        {
            var p = AiProfiles.Get(id);
            if (p == null) throw new ArgumentException("Unknown AI profile '" + id + "'.");
            return p;
        }

        /// <summary>
        /// Plays to <paramref name="untilTick"/> or the match's end, adding a world hash every checkpoint interval and at
        /// the end (leaving out side A's AI state, as side A stands in for a person). Battles are played turn by turn; a side with no move has its turn ended for it, and an overlong
        /// battle is called off, as in the arena. Returns how many battles were fought.
        /// </summary>
        public static int Run(GameLoop loop, MatchRecording setup, int untilTick, List<WorldCheckpoint> into)
        {
            var w = loop.World;
            var router = loop.Services.Commands;
            int battles = 0, steps = 0;
            bool wasTactical = false;
            while (w.outcome == MatchOutcome.None && w.mode != MatchMode.Ended && w.tick < untilTick)
            {
                if (w.mode == MatchMode.Tactical)
                {
                    if (!wasTactical) { battles++; steps = 0; }
                    wasTactical = true;
                    int before = w.encounter.round * 4 + (int)w.encounter.turn;
                    loop.RunAiTurnImmediately();
                    int after = w.encounter.round * 4 + (int)w.encounter.turn;
                    if (w.mode == MatchMode.Tactical && before == after)
                        router.Execute(w, loop.Catalog, loop.Map, new GameCommand { kind = CommandKind.EndTurn, actor = w.encounter.turn });
                    if (++steps > setup.maxBattleSteps && w.mode == MatchMode.Tactical)
                        router.Execute(w, loop.Catalog, loop.Map, new GameCommand { kind = CommandKind.Retreat, actor = w.encounter.attacker });
                    continue;
                }
                wasTactical = false;
                loop.TickNormal();
                if (w.tick % setup.checkpointEvery == 0) into.Add(new WorldCheckpoint { tick = w.tick, hash = WorldHash.Of(w, FactionId.Player) });
            }
            into.Add(new WorldCheckpoint { tick = w.tick, hash = WorldHash.Of(w, FactionId.Player) });
            return battles;
        }

        /// <summary>The tick of the first checkpoint where two runs differ, or -1 when they agree throughout.</summary>
        public static int FirstDifference(List<WorldCheckpoint> a, List<WorldCheckpoint> b)
        {
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++)
                if (a[i].tick != b[i].tick || a[i].hash != b[i].hash) return Math.Min(a[i].tick, b[i].tick);
            if (a.Count != b.Count) return n > 0 ? a[n - 1].tick : 0;
            return -1;
        }
    }
}
