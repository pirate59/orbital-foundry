using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Lockstep multiplayer sends only commands and trusts every machine to compute the same world from them. These
    /// matches check that: AI v AI on map_a, Hard against Hard with expansion, raids and defence on, so factories,
    /// research, fleets and battles all come into play.
    /// </summary>
    public class DeterminismTests
    {
        static MatchRecording Setup(int ticks)
        {
            return new MatchRecording { mapId = "map_a", seed = 1234, profileA = "hard", profileB = "hard", ticks = ticks };
        }

        [Test]
        public void TheSameMatchPlayedTwiceComesOutTheSame()
        {
            var first = DeterminismHarness.Record(Setup(6000));
            var second = DeterminismHarness.Record(Setup(6000));
            Assert.AreEqual(-1, DeterminismHarness.FirstDifference(first.checkpoints, second.checkpoints), "first tick the two runs differ");
            Assert.AreEqual(first.commands.Count, second.commands.Count);
        }

        [TestCase("map_a", "hard", "hard", 12000)]
        [TestCase("field-424242", "medium", "hard", 12000)]
        [TestCase("map_d", "hard", "hard", 9000)] // four seats: seat 1 replayed, three AIs live
        public void ASideReplayedFromItsCommandsAloneReproducesTheMatch(string mapId, string profileA, string profileB, int ticks)
        {
            var rec = DeterminismHarness.Record(new MatchRecording { mapId = mapId, seed = 1234, profileA = profileA, profileB = profileB, ticks = ticks });
            int misplaced;
            var replay = DeterminismHarness.Replay(rec, out misplaced);
            Debug.Log("Replay check " + mapId + ": " + rec.commands.Count + " side A commands, " + rec.battles + " battles, ended at tick " + rec.checkpoints[rec.checkpoints.Count - 1].tick);
            Assert.Greater(rec.commands.Count, 50, "side A played enough to be a real test");
            Assert.AreEqual(-1, misplaced, "first replayed slot that fell on a different tick");
            Assert.AreEqual(-1, DeterminismHarness.FirstDifference(rec.checkpoints, replay), "first tick the replay differs");
        }

        [Test]
        public void ADroppedCommandIsCaught()
        {
            var rec = DeterminismHarness.Record(Setup(6000));
            // Lose one of side A's successful orders partway through, as a peer that missed a message would.
            int drop = rec.commands.FindIndex(e => e.tick > 1000 && e.command.kind == CommandKind.PlaceBuilding);
            Assert.GreaterOrEqual(drop, 0, "side A placed a building after tick 1000");
            int droppedAt = rec.commands[drop].tick;
            rec.commands.RemoveAt(drop);
            int misplaced;
            var replay = DeterminismHarness.Replay(rec, out misplaced);
            int at = DeterminismHarness.FirstDifference(rec.checkpoints, replay);
            Assert.AreNotEqual(-1, at, "the replay noticed the missing command");
            Assert.GreaterOrEqual(at, droppedAt, "and not before it was missed");
            Assert.LessOrEqual(at, droppedAt + rec.checkpointEvery, "within one checkpoint");
        }

        [Test]
        public void TwoMachinesLoadingTheSameSaveStayInStep()
        {
            var setup = Setup(6000);
            var loop = DeterminismHarness.NewLoop(setup, null);
            loop.PlayerSide = DeterminismHarness.NewAi(loop, FactionId.Player, setup.profileA);
            DeterminismHarness.Run(loop, setup, 3000, new List<WorldCheckpoint>());
            string dir = Path.Combine(Path.GetTempPath(), "OrbitalFoundryDeterminism", System.Guid.NewGuid().ToString("N"));
            try
            {
                var saves = new SaveService(dir);
                Assert.IsTrue(saves.Save(loop.World, loop.Catalog).success);
                var hashes = new List<List<WorldCheckpoint>>();
                for (int machine = 0; machine < 2; machine++)
                {
                    WorldState loaded;
                    Assert.IsTrue(saves.TryLoad(loop.Catalog, loop.Map, out loaded).success);
                    var copy = DeterminismHarness.NewLoop(setup, loaded);
                    copy.PlayerSide = DeterminismHarness.NewAi(copy, FactionId.Player, setup.profileA);
                    var into = new List<WorldCheckpoint>();
                    DeterminismHarness.Run(copy, setup, setup.ticks, into);
                    hashes.Add(into);
                }
                Assert.AreEqual(-1, DeterminismHarness.FirstDifference(hashes[0], hashes[1]), "first tick the two loaded copies differ");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        /// <summary>
        /// Not a pass/fail check: how soon a reloaded copy parts from the game that carried on. The AIs' working memory
        /// is not saved, so a reload thinks afresh; this measures how much that matters. Writes Logs/determinism_reload.txt.
        /// </summary>
        [Test, Explicit]
        public void ProbeReloadAgainstCarryingOn()
        {
            var setup = Setup(9000);
            var loop = DeterminismHarness.NewLoop(setup, null);
            loop.PlayerSide = DeterminismHarness.NewAi(loop, FactionId.Player, setup.profileA);
            DeterminismHarness.Run(loop, setup, 3000, new List<WorldCheckpoint>());
            var loaded = JsonUtility.FromJson<WorldState>(JsonUtility.ToJson(loop.World));
            var copy = DeterminismHarness.NewLoop(setup, loaded);
            copy.PlayerSide = DeterminismHarness.NewAi(copy, FactionId.Player, setup.profileA);
            var carried = new List<WorldCheckpoint>();
            var reloaded = new List<WorldCheckpoint>();
            DeterminismHarness.Run(loop, setup, setup.ticks, carried);
            DeterminismHarness.Run(copy, setup, setup.ticks, reloaded);
            int at = DeterminismHarness.FirstDifference(carried, reloaded);
            string line = at < 0 ? "A reload at tick 3000 played on identically to tick " + setup.ticks + "."
                : "A reload at tick 3000 parted from the carried-on game by tick " + at + " (" + (at - 3000) + " ticks later).";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/determinism_reload.txt", line + "\n");
            Debug.Log(line);
        }
    }
}
