using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Turns the relay's start message into a match: the same world on every machine (the seed and the seats make the
    /// map and the factions), and this machine's lockstep session through the relay, with the match so far preloaded
    /// for a returning player.
    /// </summary>
    public static class OnlineMatch
    {
        /// <summary>The seat this machine plays (Neutral: it watches).</summary>
        public static FactionId LocalSeat(RelayMessage start)
        {
            return start.you >= 1 && start.you <= 4 ? (FactionId)start.you : FactionId.Neutral;
        }

        /// <summary>The match's map and starting world. Throws CatalogException if the seats make no game.</summary>
        public static WorldState Build(ContentCatalog catalog, RelayMessage start, out MapDefinition map)
        {
            var on = new bool[4];
            var seats = new List<SeatSetup>();
            foreach (var s in start.seats)
            {
                if (s.mode == "off" || s.seat < 1 || s.seat > 4) continue;
                on[s.seat - 1] = true;
                bool ai = s.mode == "ai";
                seats.Add(new SeatSetup { seat = (FactionId)s.seat, ai = ai, aiProfileId = ai ? s.profile : "", team = s.team });
            }
            map = CatalogRepository.LoadMap(MapGenerator.IdForSeats(on, start.seed), catalog);
            var world = GameFactory.CreateNew(catalog, map, start.seed, seats);
            world.difficulty = (Difficulty)UnityEngine.Mathf.Clamp(start.difficulty, 0, 2);
            return world;
        }

        /// <summary>Joins <paramref name="loop"/> (already holding the built world) to the match through the relay.</summary>
        public static LockstepSession Join(GameLoop loop, RelayClient client, RelayMessage start)
        {
            var session = new LockstepSession(loop, LocalSeat(start), client.Transport());
            var through = new List<(FactionId, int)>();
            foreach (var x in start.inputsThrough) through.Add(((FactionId)x.seat, x.tick));
            session.Preload(start.log, through);
            // A returning player's seat has been away; it claims it back once the game is running.
            if (start.awaySeats.Contains(start.you)) session.StartAway();
            loop.UseLockstep(session);
            loop.SharedSpeed = start.speed;
            // Every machine plays the same rules: the AIs' battle pace is the standard one online.
            loop.Catalog.rules.aiBattleStepTicks = 6;
            return session;
        }
    }
}
