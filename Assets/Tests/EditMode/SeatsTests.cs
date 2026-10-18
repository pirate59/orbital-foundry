using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Up to four seats, any of them played by a person or an AI, optionally in teams. Most of these use map_d, Four
    /// Corners (4×4, a headquarters in every corner): seat 1 at (0,0) in cell 0, seat 2 at (3,3) in cell 15, seat 3 at
    /// (3,0) in cell 3 and seat 4 at (0,3) in cell 12.
    /// </summary>
    public class SeatsTests
    {
        const FactionId S1 = FactionId.Player, S2 = FactionId.Ai, S3 = FactionId.Seat3, S4 = FactionId.Seat4;

        /// <summary>A four-seat setup: each seat's team (0: on its own) and whether an AI plays it.</summary>
        static List<SeatSetup> Seats(int t1, int t2, int t3, int t4, bool ai1 = false, bool ai2 = false, bool ai3 = false, bool ai4 = false)
        {
            return new List<SeatSetup>
            {
                new SeatSetup { seat = S1, team = t1, ai = ai1 },
                new SeatSetup { seat = S2, team = t2, ai = ai2 },
                new SeatSetup { seat = S3, team = t3, ai = ai3 },
                new SeatSetup { seat = S4, team = t4, ai = ai4 },
            };
        }

        [Test]
        public void AFourCornerMapOffersFourSeats()
        {
            using (var t = new TestWorld("map_d", null))
            {
                CollectionAssert.AreEqual(new[] { S1, S2, S3, S4 }, t.W.factions.ConvertAll(f => f.id));
                Assert.IsFalse(t.Player.aiControlled, "seat 1 is the person by default");
                Assert.IsTrue(t.W.factions.TrueForAll(f => f.id == S1 || f.aiControlled), "every other seat an AI");
                Assert.IsTrue(t.W.factions.TrueForAll(f => f.team == 0), "and nobody in a team");
                Assert.IsTrue(StateValidator.ValidateWorld(t.W, t.Catalog, t.Map).success);
                Assert.IsTrue(t.Loop.ControllerFor(S3) is AIController, "AI seats get their AI");
                Assert.IsNull(t.Loop.ControllerFor(S1), "a person's seat gets none");
            }
        }

        [Test]
        public void TheSetupMustFillEverySeat()
        {
            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap("map_d", catalog);
            var three = Seats(0, 0, 0, 0);
            three.RemoveAt(3);
            Assert.Throws<CatalogException>(() => GameFactory.CreateNew(catalog, map, 1, three));
        }

        [Test]
        public void AlliesNeitherFightNorClaimEachOther()
        {
            using (var t = new TestWorld("map_d", null, Seats(1, 2, 1, 2)))
            {
                var scout = t.AddCraft(S3, "guard", 1, false); // next to seat 1's headquarters
                var attack = t.Run(new GameCommand { kind = CommandKind.StartAttack, actor = S3, craftId = scout.id, targetCellId = 0 });
                Assert.AreEqual("ALLIED_CELL", attack.code);
                Assert.IsFalse(WorldLookup.HasOpposingCraft(t.W, 0, S3), "an ally's guard does not block");
                Assert.IsTrue(WorldLookup.HasOpposingCraft(t.W, 0, S2), "but it blocks an enemy");
                bool guarded;
                Assert.IsEmpty(CombatResolver.Targets(t.W, t.Catalog, 0, S3, out guarded), "nothing of an ally's to shoot");
                Assert.IsNotEmpty(CombatResolver.Targets(t.W, t.Catalog, 0, S2, out guarded));

                scout.cellId = 0;
                var claim = t.Run(new GameCommand { kind = CommandKind.StartCapture, actor = S3, craftId = scout.id, targetCellId = 0 });
                Assert.AreEqual("ALLIED_CELL", claim.code);
            }
        }

        [Test]
        public void AFallenHeadquartersTakesOneSeatOutAndPlayGoesOn()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0)))
            {
                t.S.Ownership.TransferCell(t.W, t.Catalog, 3, S1);
                var three = WorldLookup.GetFaction(t.W, S3);
                Assert.IsTrue(three.suppressed, "seat 3 is out");
                Assert.IsTrue(t.W.crafts.FindAll(k => k.owner == S3).TrueForAll(k => k.withdrawn), "its craft withdraw");
                Assert.AreEqual(MatchOutcome.None, t.W.outcome, "three seats are still in");
                Assert.AreEqual(MatchMode.Factory, t.W.mode);
                Assert.AreEqual("SUPPRESSED", t.Run(new GameCommand { kind = CommandKind.EndTurn, actor = S3 }).code, "it can give no orders");
                Assert.IsTrue(t.W.alerts.Exists(a => a.to == S3 && a.kind == AlertKind.Defeat), "seat 3 hears it is out");
                Assert.IsTrue(t.W.alerts.Exists(a => a.to == S1 && a.kind == AlertKind.Victory), "seat 1 hears of its capture");
            }
        }

        [Test]
        public void TheLastTeamStandingWinsWithItsFallenTeammates()
        {
            using (var t = new TestWorld("map_d", null, Seats(1, 1, 2, 2)))
            {
                t.S.Ownership.TransferCell(t.W, t.Catalog, 15, S3); // seat 2 falls first
                Assert.AreEqual(MatchOutcome.None, t.W.outcome);
                t.S.Ownership.TransferCell(t.W, t.Catalog, 3, S1);
                Assert.AreEqual(MatchOutcome.None, t.W.outcome, "seat 4 still stands for team 2");
                t.S.Ownership.TransferCell(t.W, t.Catalog, 12, S1);
                Assert.AreEqual(MatchOutcome.Decided, t.W.outcome);
                CollectionAssert.AreEqual(new[] { S1, S2 }, t.W.winners, "team 1 wins, seat 2 with it");
                Assert.AreEqual(MatchMode.Factory, t.W.mode, "a person won, so building goes on");
            }
        }

        [Test]
        public void WhenNoPersonWinsTheMatchEnds()
        {
            using (var t = new TestWorld("map_d", null, Seats(1, 2, 1, 2, ai2: true, ai3: true, ai4: true)))
            {
                t.S.Ownership.TransferCell(t.W, t.Catalog, 0, S2);
                t.S.Ownership.TransferCell(t.W, t.Catalog, 3, S4);
                CollectionAssert.AreEqual(new[] { S2, S4 }, t.W.winners);
                Assert.AreEqual(MatchMode.Ended, t.W.mode);
            }
        }

        [Test]
        public void AnySeatCanWinByTradeForItsTeam()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 3, 3, ai3: true)))
            {
                WorldLookup.GetFaction(t.W, S3).trade.won = true;
                t.S.Ownership.EvaluateOutcome(t.W, t.Catalog.rules);
                CollectionAssert.AreEqual(new[] { S3, S4 }, t.W.winners);
                Assert.IsTrue(t.Player.suppressed && t.Ai.suppressed, "the rest formally concede");
                Assert.AreEqual(MatchMode.Factory, t.W.mode, "seat 4, a person, keeps building");
            }
        }

        [Test]
        public void AlertsGoToTheirSeatAndNoneToAnAi()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0, ai2: true)))
            {
                t.SetOwner(1, S3); // no Fort and no ship: unguarded
                t.SetOwner(2, S2);
                t.S.Ownership.TickUpkeep(t.W, t.Catalog);
                var unguarded = t.W.alerts.FindAll(a => a.message.Contains("unguarded"));
                Assert.AreEqual(1, unguarded.Count, unguarded.Count + " alerts");
                Assert.AreEqual(S3, unguarded[0].to, "seat 3's own warning; the AI seat gets none");
            }
        }

        [Test]
        public void BattleTurnsGoRoundEverySeatInIt()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0)))
            {
                t.ClearCraft();
                t.AddCraft(S3, "guard", 1, true);  // seat 3 holds cell 1
                t.AddCraft(S1, "bomber", 0, false); // seat 1 attacks it from its headquarters
                t.AddCraft(S4, "guard", 5, false);  // seat 4 has a ship in reach
                Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, S1).success);
                var e = t.W.encounter;
                CollectionAssert.AreEqual(new[] { S1, S3, S4 }, e.sides);
                Assert.AreEqual(S1, e.turn);
                Assert.AreEqual(1, e.round);
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = S1 });
                Assert.AreEqual(S3, e.turn);
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = S3 });
                Assert.AreEqual(S4, e.turn);
                Assert.AreEqual(1, e.round);
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = S4 });
                Assert.AreEqual(S1, e.turn, "back to the first seat");
                Assert.AreEqual(2, e.round, "a new round");
            }
        }

        [Test]
        public void FourAiSeatsPlayWithoutErrors()
        {
            // Catches anything in the rules still sized for two sides (it once was: per-faction power and stats arrays).
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0, ai1: true, ai2: true, ai3: true, ai4: true)))
            {
                for (int i = 0; i < 1500 && t.W.mode == MatchMode.Factory; i++) t.Loop.TickNormal();
                Assert.AreEqual(1500, t.W.tick);
                foreach (var f in t.W.factions)
                    Assert.Greater(t.W.buildings.FindAll(b => b.owner == f.id).Count, 1, f.id + " built something");
            }
        }

        /// <summary>
        /// Not pass/fail on the AI's play: four AIs, three of whom still think in terms of one opponent until the AI learns
        /// about several (phase 2b), play ten minutes without the rules throwing. Writes Logs/four_ai_smoke.txt.
        /// </summary>
        [Test, Explicit]
        public void ProbeFourAiSeats()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0, ai1: true, ai2: true, ai3: true, ai4: true)))
            {
                string error = "";
                try { for (int i = 0; i < 6000 && t.W.mode == MatchMode.Factory; i++) t.Loop.TickNormal(); }
                catch (System.Exception e) { error = e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
                var sb = new System.Text.StringBuilder("Four AI seats to tick " + t.W.tick + (error == "" ? ", no errors" : ", ERROR " + error) + "\n");
                foreach (var f in t.W.factions)
                    sb.Append(f.id).Append(": cells ").Append(t.W.cells.FindAll(c => c.owner == f.id).Count)
                      .Append(", buildings ").Append(t.W.buildings.FindAll(b => b.owner == f.id).Count)
                      .Append(", sales ").Append(f.cumulativeSales).Append('\n');
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/four_ai_smoke.txt", sb.ToString());
                Debug.Log(sb.ToString());
            }
        }

        [Test]
        public void TheBattleLogSpeaksToWhoeverIsWatching()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0, ai2: true)))
            {
                t.ClearCraft();
                t.AddCraft(S1, "guard", 1, true);
                t.AddCraft(S3, "bomber", 2, false);
                try
                {
                    Perspective.Viewer = S3; // this machine plays seat 3
                    Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, S3).success);
                    StringAssert.StartsWith("You attack cell 1", t.S.Encounters.LastEvent);
                    Assert.IsTrue(t.W.alerts.Exists(a => a.to == S1 && a.message.StartsWith("The enemy attacks cell 1")), "seat 1 reads it its own way");
                    Assert.IsFalse(t.W.alerts.Exists(a => a.to == S2), "and the AI seat gets nothing");
                }
                finally { Perspective.Viewer = FactionId.Player; }
            }
        }

        [Test]
        public void ASeatSetupMapHasOnlyTheChosenSeats()
        {
            var catalog = CatalogRepository.LoadContent();
            string id = MapGenerator.IdForSeats(new[] { true, false, true, false }, 77);
            Assert.AreEqual("seats-1010-77", id);
            var map = CatalogRepository.LoadMap(id, catalog);
            CollectionAssert.AreEqual(new[] { S1, S3 }, GameFactory.DefaultSeats(map).ConvertAll(x => x.seat));
            Assert.AreEqual(FactionId.Neutral, map.cells.Find(c => c.id == 15).headquartersFor, "seat 2's corner is an ordinary cell");
            Assert.IsFalse(map.startingCrafts.Exists(k => k.faction == S2 || k.faction == S4));
            Assert.AreEqual(id, CatalogRepository.LoadMap(id, catalog).id, "rebuilt the same from its ID");
        }

        [Test]
        public void AnExhibitionWithoutSeatOneSavesAndLoads()
        {
            var seats = new List<SeatSetup>
            {
                new SeatSetup { seat = S2, ai = true, aiProfileId = "hard_bold" },
                new SeatSetup { seat = S3, ai = true, aiProfileId = "easy_cautious" },
                new SeatSetup { seat = S4, ai = true },
            };
            string id = MapGenerator.IdForSeats(new[] { false, true, true, true }, 5);
            using (var t = new TestWorld(id, null, seats))
            {
                Assert.AreEqual(3, t.W.factions.Count);
                for (int i = 0; i < 300; i++) t.Loop.TickNormal();
                string before = JsonUtility.ToJson(t.W);
                Assert.IsTrue(t.Loop.Saves.Save(t.W, t.Catalog).success);
                WorldState loaded;
                var r = t.Loop.Saves.TryLoad(t.Catalog, CatalogRepository.LoadMap(MapGenerator.TemplateMapId, t.Catalog), out loaded);
                Assert.IsTrue(r.success, r.code + " " + r.message);
                Assert.AreEqual(id, t.Loop.Saves.LoadedMap.id, "the save rebuilds its own map");
                Assert.AreEqual(before, JsonUtility.ToJson(loaded));
            }
        }

        [Test]
        public void TheCautiousAndBoldPresetsExist()
        {
            foreach (var level in new[] { "easy", "medium", "hard" })
            {
                var normal = AiProfiles.Get(level);
                Assert.Less(AiProfiles.Get(level + "_cautious").Aggression, normal.Aggression, level);
                Assert.Greater(AiProfiles.Get(level + "_bold").Aggression, normal.Aggression, level);
            }
        }

        [Test]
        public void AFourSeatGameSavesAndLoads()
        {
            using (var t = new TestWorld("map_d", null, Seats(1, 1, 2, 2, ai2: true, ai4: true)))
            {
                WorldLookup.GetFaction(t.W, S4).aiProfileId = "hard";
                string before = JsonUtility.ToJson(t.W);
                Assert.IsTrue(t.Loop.Saves.Save(t.W, t.Catalog).success);
                WorldState loaded;
                var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
                Assert.IsTrue(r.success, r.code + " " + r.message);
                Assert.AreEqual(before, JsonUtility.ToJson(loaded));
            }
        }

        [Test]
        public void AnOldTwoSidedSaveUpgradesToSeats()
        {
            using (var t = new TestWorld())
            {
                // Lay the world out as schema 3 kept it: the AI's state, its profile and the player's trade on the world.
                t.W.ai = t.Ai.ai;
                t.W.ai.queuedCommands.Add(new GameCommand { kind = CommandKind.Research, actor = FactionId.Ai, contentId = "charter" });
                t.Ai.ai = new AiState();
                t.Ai.aiControlled = false;
                t.W.aiProfileId = "hard";
                t.W.trade = t.Player.trade;
                t.W.trade.rate = 1234;
                t.Player.trade = new TradeLedger();
                t.W.outcome = MatchOutcome.AiConceded;
                WorldLookup.AddAlert(t.W, AlertKind.Info, "An old alert.", -1, FactionId.Neutral);
                var envelope = new SaveEnvelope { schemaVersion = 3, catalogVersion = t.Catalog.version, world = t.W };
                Directory.CreateDirectory(t.SaveDir);
                File.WriteAllText(t.Loop.Saves.SlotPath, JsonUtility.ToJson(envelope));

                WorldState loaded;
                var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
                Assert.IsTrue(r.success, r.code + " " + r.message);
                var ai = WorldLookup.GetFaction(loaded, FactionId.Ai);
                Assert.IsTrue(ai.aiControlled);
                Assert.AreEqual(1, ai.ai.queuedCommands.Count, "the AI's queue moved to its seat");
                Assert.AreEqual("hard", ai.aiProfileId);
                Assert.AreEqual(1234, WorldLookup.GetFaction(loaded, FactionId.Player).trade.rate, "the trade ledger moved to the player");
                Assert.AreEqual(MatchOutcome.Decided, loaded.outcome);
                CollectionAssert.AreEqual(new[] { FactionId.Player }, loaded.winners);
                Assert.IsTrue(loaded.alerts.TrueForAll(a => a.to == FactionId.Player), "the old alerts were all the player's");
            }
        }
    }
}
