using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Two players through a real relay (relay/worker.js under `npx wrangler dev`): the lobby, a match played in lockstep
    /// over WebSockets, and a player who drops and comes back, rebuilding the match from the relay's log. Skipped unless
    /// ORBITAL_FOUNDRY_RELAY names the relay (for example ws://127.0.0.1:8790).
    /// </summary>
    public class RelayIntegrationTests
    {
        const int Seed = 5;

        sealed class Machine
        {
            public GameLoop Loop = new GameLoop();
            public LockstepSession Session;
        }

        static string Relay()
        {
            var relay = Environment.GetEnvironmentVariable("ORBITAL_FOUNDRY_RELAY");
            if (string.IsNullOrEmpty(relay)) Assert.Ignore("Set ORBITAL_FOUNDRY_RELAY to a running relay to run this.");
            return relay;
        }

        static void Pump(Func<bool> done, params RelayClient[] clients)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!done())
            {
                if (DateTime.UtcNow > until) Assert.Fail("Timed out waiting on the relay.");
                foreach (var c in clients) c.Poll(0f);
                Thread.Sleep(5);
            }
        }

        static Machine Build(RelayClient client, RelayMessage start)
        {
            var catalog = CatalogRepository.LoadContent();
            MapDefinition map;
            var world = OnlineMatch.Build(catalog, start, out map);
            var m = new Machine();
            m.Loop.Initialize(catalog, map, world, null);
            m.Session = OnlineMatch.Join(m.Loop, client, start);
            return m;
        }

        /// <summary>Plays both machines to <paramref name="tick"/>, the host giving recorded orders as it goes.</summary>
        static void Play(int tick, Machine a, RelayClient ca, Machine b, RelayClient cb, List<JournalEntry> orders, ref int next)
        {
            var until = DateTime.UtcNow.AddSeconds(60);
            while (a.Loop.World.tick < tick || b.Loop.World.tick < tick)
            {
                if (DateTime.UtcNow > until) Assert.Fail("Stalled at ticks " + a.Loop.World.tick + " / " + b.Loop.World.tick);
                ca.Poll(0f);
                cb.Poll(0f);
                while (next < orders.Count && orders[next].tick <= a.Loop.World.tick + 1) a.Loop.Submit(orders[next++].command);
                bool moved = false;
                for (int i = 0; i < 50 && a.Loop.World.tick < tick; i++) moved |= a.Loop.TryTickNormal();
                for (int i = 0; i < 500 && b.Loop.World.tick < tick; i++) moved |= b.Loop.TryTickNormal();
                if (!moved) Thread.Sleep(2);
            }
        }

        [Test]
        public void TwoPlayersThroughTheRelayStayInStepAndOneComesBack()
        {
            string relay = Relay();
            Perspective.Spectating = false;
            string code = "T" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant().Replace('0', 'Z').Replace('1', 'Y');
            string guestId = "guest-" + Guid.NewGuid().ToString("N");

            RelayMessage hostLobby = null, guestLobby = null, hostStart = null, guestStart = null;
            var host = new RelayClient(relay, code, "host-" + Guid.NewGuid().ToString("N"), "Ann");
            host.Lobby += m => hostLobby = m;
            host.Started += m => hostStart = m;
            Pump(() => hostLobby != null, host);
            Assert.IsTrue(hostLobby.host);
            host.Configure(new List<RelaySeat>
            {
                new RelaySeat { seat = 1, mode = "person" }, new RelaySeat { seat = 2, mode = "person" },
                new RelaySeat { seat = 3, mode = "ai", profile = "hard" }, new RelaySeat { seat = 4, mode = "off" },
            }, 1);
            Pump(() => hostLobby.seats[1].mode == "person", host);

            var guest = new RelayClient(relay, code, guestId, "Bo");
            guest.Lobby += m => guestLobby = m;
            guest.Started += m => guestStart = m;
            Pump(() => guestLobby != null && guestLobby.you == 2, host, guest);
            Pump(() => hostLobby.notReady == "", host, guest);
            host.StartMatch(Seed);
            Pump(() => hostStart != null && guestStart != null, host, guest);
            Assert.AreEqual(1, hostStart.you);
            Assert.AreEqual(2, guestStart.you);

            // Seat 1's orders: what an AI gave on this map (the other seats AIs too).
            var orders = DeterminismHarness.Record(new MatchRecording { mapId = MapGenerator.IdForSeats(new[] { true, true, true, false }, Seed), seed = Seed, ticks = 900 }).commands;
            int next = 0;
            var a = Build(host, hostStart);
            var b = Build(guest, guestStart);
            Play(600, a, host, b, guest, orders, ref next);
            Assert.AreEqual(WorldHash.Of(a.Loop.World), WorldHash.Of(b.Loop.World), "the same world on both machines");

            // The guest drops and comes back: it rebuilds the match from the relay's log, catches up, and plays on.
            guest.Close();
            Thread.Sleep(200);
            RelayMessage again = null;
            var back = new RelayClient(relay, code, guestId, "Bo");
            back.Started += m => again = m;
            Pump(() => again != null, host, back);
            Assert.AreEqual(2, again.you, "still seat 2");
            Assert.Greater(again.log.Count, 0, "the orders so far, to replay");
            var b2 = Build(back, again);
            Play(900, a, host, b2, back, orders, ref next);
            Assert.AreEqual(-1, a.Session.DesyncTick);
            Assert.AreEqual(-1, b2.Session.DesyncTick);
            Assert.AreEqual(WorldHash.Of(a.Loop.World), WorldHash.Of(b2.Loop.World), "the returning machine caught up exactly");
            host.Close();
            back.Close();
        }
    }
}
