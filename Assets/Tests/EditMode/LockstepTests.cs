using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Phase 6: machines in lockstep. Each machine below is a whole game (its own catalog, map, world and loop) joined to
    /// the others in memory. Three seats on a generated map: seat 1 a person, seat 2 a Hard AI (run on every machine),
    /// seat 3 a person. Seat 1 gives the orders a recorded AI gave, through Submit, as a person would.
    /// </summary>
    public class LockstepTests
    {
        static readonly string MapId = MapGenerator.IdForSeats(new[] { true, true, true, false }, 5);
        const FactionId S1 = FactionId.Player, S3 = FactionId.Seat3;
        const int Seed = 1234;

        static MatchRecording recording;

        [OneTimeSetUp]
        public void Record()
        {
            Perspective.Spectating = false;
            // Seat 1's orders as an AI played them on this map (the other seats AIs too): orders a person might give.
            recording = DeterminismHarness.Record(new MatchRecording { mapId = MapId, seed = Seed, ticks = 2400 });
            Assert.Greater(recording.commands.Count, 30, "seat 1 gave orders to replay");
        }

        sealed class Machine
        {
            public GameLoop Loop;
            public LockstepSession Session;
            public ILockstepTransport Transport;
            public int Ran, RanOk;
            public WorldState World { get { return Loop.World; } }
        }

        static Machine Join(LoopbackHub hub, FactionId seat)
        {
            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap(MapId, catalog);
            var seats = new List<SeatSetup>
            {
                new SeatSetup { seat = S1 },
                new SeatSetup { seat = FactionId.Ai, ai = true, aiProfileId = "hard" },
                new SeatSetup { seat = S3 },
            };
            var m = new Machine { Loop = new GameLoop() };
            m.Loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, Seed, seats), null);
            m.Transport = hub.Join();
            m.Session = new LockstepSession(m.Loop, seat, m.Transport);
            m.Loop.UseLockstep(m.Session);
            m.Session.LocalCommandRan += (cmd, r) => { m.Ran++; if (r.success) m.RanOk++; };
            return m;
        }

        /// <summary>Steps every machine until each reaches <paramref name="tick"/> (or stops getting anywhere).</summary>
        static void RunTo(int tick, params Machine[] machines) { RunTo(tick, true, machines); }

        static void RunTo(int tick, bool orders, params Machine[] machines)
        {
            int next = 0;
            var giver = orders ? System.Array.Find(machines, x => x.Session.LocalSeat == S1) : null;
            for (int idle = 0; idle < 50;)
            {
                bool moved = false, all = true;
                foreach (var m in machines)
                {
                    if (m.World.tick >= tick) continue;
                    all = false;
                    if (m == giver)
                        while (next < recording.commands.Count && recording.commands[next].tick <= m.World.tick + 1)
                            m.Loop.Submit(recording.commands[next++].command);
                    moved |= m.Loop.TryTickNormal();
                }
                if (all) return;
                idle = moved ? 0 : idle + 1;
            }
        }

        [SetUp] public void SetUp() { Perspective.Spectating = false; }

        [Test]
        public void TwoMachinesPlayingTheSameMatchStayIdentical()
        {
            var hub = new LoopbackHub { DelayPolls = 2 }; // a little latency
            var a = Join(hub, S1);
            var b = Join(hub, S3);
            int ticks = 2400 + a.Session.InputDelay;
            RunTo(ticks, a, b);
            Assert.AreEqual(ticks, a.World.tick);
            Assert.AreEqual(ticks, b.World.tick);
            Assert.AreEqual(-1, a.Session.DesyncTick, "no drift seen");
            Assert.AreEqual(-1, b.Session.DesyncTick);
            Assert.AreEqual(WorldHash.Of(a.World), WorldHash.Of(b.World), "the same world on both machines");
            Assert.Greater(a.RanOk, 20, "seat 1's orders ran: " + a.RanOk + " of " + a.Ran);
            Assert.Greater(WorldLookup.GetFaction(a.World, FactionId.Ai).cumulativeSales, 0, "the AI seat played on both");
        }

        [Test]
        public void ATickWaitsForEveryPersonsInputs()
        {
            var hub = new LoopbackHub { DelayPolls = 100000 }; // nothing arrives
            var a = Join(hub, S1);
            Join(hub, S3);
            for (int i = 0; i < 50; i++) a.Loop.TryTickNormal();
            Assert.AreEqual(a.Session.InputDelay, a.World.tick, "it plays the ticks nobody could have sent inputs for, then waits");
            Assert.AreEqual(S3, a.Loop.WaitingFor, "for seat 3");
        }

        [Test]
        public void ACommandRunsOnEveryMachineAtTheSameTick()
        {
            var hub = new LoopbackHub();
            var a = Join(hub, S1);
            var b = Join(hub, S3);
            RunTo(10, false, a, b);
            int credits = WorldLookup.GetFaction(b.World, S1).currency;
            // A belt along the edge of seat 1's home asteroid, where no ore lies.
            var r = a.Loop.Submit(new GameCommand { kind = CommandKind.PlaceBelt, actor = S1, fromCellId = 0, fromX = 1, fromY = 0, toCellId = 0, toX = 2, toY = 0 });
            Assert.AreEqual("SENT", r.code, "given, not yet carried out");
            Assert.AreEqual(credits, WorldLookup.GetFaction(a.World, S1).currency, "nothing changes at once");
            RunTo(10 + a.Session.InputDelay + 1, false, a, b);
            Assert.AreEqual(1, a.Ran, "it ran once, here");
            Assert.AreEqual(1, a.RanOk, "and was carried out");
            Assert.AreNotEqual(credits, WorldLookup.GetFaction(b.World, S1).currency, "and on the other machine");
            Assert.AreEqual(WorldHash.Of(a.World), WorldHash.Of(b.World));
        }

        [Test]
        public void DriftIsCaughtAtTheNextFingerprint()
        {
            var hub = new LoopbackHub();
            var a = Join(hub, S1);
            var b = Join(hub, S3);
            RunTo(60, a, b);
            WorldLookup.GetFaction(b.World, S3).currency += 1; // one machine's world parts ways
            RunTo(160, a, b);
            Assert.AreEqual(100, a.Session.DesyncTick, "seen at the first fingerprint after it");
            Assert.AreEqual(S3, a.Session.DesyncWith);
            Assert.AreEqual(100, b.Session.DesyncTick, "by both machines");
        }

        [Test]
        public void ADroppedPersonsSeatIsHandedToAnAiOnEveryMachine()
        {
            var hub = new LoopbackHub { DelayPolls = 1 };
            var a = Join(hub, S1);
            var b = Join(hub, S3);
            var watcher = Join(hub, FactionId.Neutral); // a third machine that only watches
            RunTo(300, a, b, watcher);
            hub.Cut(b.Transport);
            // The others play on until seat 3's last inputs run out, then wait for it.
            RunTo(1000, a, watcher);
            Assert.AreEqual(S3, a.Loop.WaitingFor);
            int last = a.World.tick;
            Assert.Less(last, 1000);
            a.Session.HandOver(S3, last + 1, "medium");
            RunTo(1600, a, watcher);
            Assert.AreEqual(1600, a.World.tick);
            Assert.AreEqual(1600, watcher.World.tick);
            foreach (var m in new[] { a, watcher })
            {
                Assert.IsTrue(WorldLookup.GetFaction(m.World, S3).aiControlled, "an AI plays seat 3");
                Assert.IsInstanceOf<AIController>(m.Loop.ControllerFor(S3));
                Assert.AreEqual(-1, m.Session.DesyncTick);
            }
            Assert.AreEqual(WorldHash.Of(a.World), WorldHash.Of(watcher.World), "and both machines agree");
        }
    }
}
