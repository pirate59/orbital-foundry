using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Online game time against real time: machines driven frame by frame, as the game drives them, one of them
    /// throttled the way a browser throttles a tab in the background (a frame a second).
    /// </summary>
    public class LockstepPacingTests
    {
        static readonly string MapId = MapGenerator.IdForSeats(new[] { true, true, true, false }, 5);

        static GameLoop Machine(LoopbackHub hub, FactionId seat)
        {
            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap(MapId, catalog);
            var seats = new List<SeatSetup>
            {
                new SeatSetup { seat = FactionId.Player },
                new SeatSetup { seat = FactionId.Ai, ai = true, aiProfileId = "hard" },
                new SeatSetup { seat = FactionId.Seat3 },
            };
            var loop = new GameLoop();
            loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 1234, seats), null);
            loop.UseLockstep(new LockstepSession(loop, seat, hub.Join()));
            return loop;
        }

        /// <summary>Thirty seconds of real time: <paramref name="a"/> at 60 frames a second, <paramref name="b"/> every <paramref name="bEvery"/> seconds (0: never).</summary>
        static void Run(GameLoop a, GameLoop b, float bEvery, bool bHidden)
        {
            b.PageHidden = bHidden;
            float sinceB = 0f;
            for (int frame = 0; frame < 30 * 60; frame++)
            {
                a.Update(1f / 60f);
                sinceB += 1f / 60f;
                if (bEvery > 0f && sinceB >= bEvery) { b.Update(sinceB); sinceB = 0f; }
            }
        }

        [Test]
        public void TwoMachinesInViewRunAtFullSpeed()
        {
            var hub = new LoopbackHub();
            var a = Machine(hub, FactionId.Player);
            var b = Machine(hub, FactionId.Seat3);
            Run(a, b, 1f / 60f, false);
            Assert.Greater(a.World.tick, 280, "about ten ticks a second for thirty seconds: " + a.World.tick);
        }

        [Test]
        public void AMachineInABackgroundTabIsAwayAndDoesNotSlowTheOthers()
        {
            var hub = new LoopbackHub();
            hub.RelayFor.AddRange(new[] { FactionId.Player, FactionId.Seat3 }); // the relay fills an away seat in
            var a = Machine(hub, FactionId.Player);
            var b = Machine(hub, FactionId.Seat3);
            int aiAtStart = a.World.buildings.FindAll(x => x.owner == FactionId.Ai).Count;
            Run(a, b, 1f, true); // a hidden tab: a frame a second
            Assert.Greater(a.World.tick, 270, "the machine in view plays on at full speed: " + a.World.tick);
            Assert.Greater(b.World.tick, 250, "and the hidden one keeps up: " + b.World.tick);
            Assert.AreEqual(-1, a.Lockstep.DesyncTick);
            Assert.IsTrue(b.Lockstep.Away, "its player is away");
            int aiNow = a.World.buildings.FindAll(x => x.owner == FactionId.Ai).Count;
            Assert.GreaterOrEqual(aiNow - aiAtStart, 4, "and the AI seat builds its factory (" + aiAtStart + " buildings at the start, " + aiNow + " now)");
            Assert.AreEqual(WorldHash.Of(a.World) == WorldHash.Of(b.World), a.World.tick == b.World.tick, "the same world whenever they reach the same tick");
        }
    }
}
