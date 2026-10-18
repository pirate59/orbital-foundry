using System.Collections.Generic;
using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// The AI with several enemies and allies (map_d, Four Corners: seat 1 in cell 0, seat 2 in 15, seat 3 in 3, seat 4
    /// in 12), its seeded variety, and the arena's seat matches.
    /// </summary>
    public class AiSeatsTests
    {
        const FactionId S1 = FactionId.Player, S2 = FactionId.Ai, S3 = FactionId.Seat3, S4 = FactionId.Seat4;

        static List<SeatSetup> Seats(int t1, int t2, int t3, int t4)
        {
            return new List<SeatSetup>
            {
                new SeatSetup { seat = S1, team = t1 },
                new SeatSetup { seat = S2, team = t2, ai = true },
                new SeatSetup { seat = S3, team = t3, ai = true },
                new SeatSetup { seat = S4, team = t4, ai = true },
            };
        }

        /// <summary>One think of seat 3's brain, as its controller would run it.</summary>
        static AiBrain ThinkAsSeat3(TestWorld t)
        {
            var brain = new AiBrain();
            using (AiSide.Enter(S3, null, AiProfile.For(Difficulty.Hard), "")) brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory());
            return brain;
        }

        [Test]
        public void ItMeasuresItselfAgainstTheStrongestEnemyStillIn()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0)))
            {
                t.Fortify(13, S4);
                t.Fortify(8, S4);
                using (AiSide.Enter(S3, null, null, ""))
                {
                    Assert.AreEqual(S4, AiSide.RivalOf(t.W), "seat 4 holds the most cells");
                    OwnershipService.Eliminate(t.W, S4);
                    Assert.AreEqual(S1, AiSide.RivalOf(t.W), "once seat 4 is out, the next (the lower seat on a tie)");
                }
            }
        }

        [Test]
        public void EveryEnemysShipsThreatenItButAnAllysDoNot()
        {
            using (var t = new TestWorld("map_d", null, Seats(0, 0, 0, 0)))
            {
                t.AddCraft(S4, "bomber", 2, false); // next to seat 3's headquarters (cell 3)
                Assert.Greater(ThinkAsSeat3(t).Model.ThreatTo[3], 0, "seat 4's bomber, though seat 4 is not its rival");
            }
            using (var t = new TestWorld("map_d", null, Seats(1, 2, 3, 3)))
            {
                t.AddCraft(S4, "bomber", 2, false);
                Assert.AreEqual(0, ThinkAsSeat3(t).Model.ThreatTo[3], "an ally's bomber is no threat");
            }
        }

        [Test]
        public void ItNeverPicksAnAllyToAttack()
        {
            using (var t = new TestWorld("map_d", null, Seats(1, 2, 1, 2)))
            {
                t.Catalog.rules.aiRaids = true;
                t.W.tick = 20000; // well past any grace period
                // Seat 1 (an ally) holds the cells beside seat 3; seat 3 has a fleet that could take any of them.
                t.Fortify(2, S1);
                t.Fortify(7, S1);
                for (int i = 0; i < 6; i++) t.AddCraft(S3, "bomber", 3, false);
                var brain = new AiBrain();
                using (AiSide.Enter(S3, null, AiProfile.For(Difficulty.Hard), ""))
                    for (int i = 0; i < 60; i++) { t.W.tick += 3; brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory()); }
                int target = brain.Decisions.AttackCell;
                if (target >= 0)
                    Assert.IsTrue(WorldLookup.AreEnemies(t.W, S3, WorldLookup.GetCell(t.W, target).owner), "attack cell " + target + " is an enemy's: " + brain.Report());
            }
        }

        [Test]
        public void ItOnlyClaimsCellsItCouldFortify()
        {
            // Two seats on Four Corners: the AI's headquarters is cell 15, whose direct neighbours are 11 and 14.
            using (var t = new TestWorld(MapGenerator.IdForSeats(new[] { true, true, false, false }, 5)))
            {
                t.Catalog.rules.aiExpands = true;
                var brain = new AiBrain();
                using (AiSide.Enter(S2, null, AiProfile.For(Difficulty.Hard), ""))
                    for (int i = 0; i < 30; i++) { t.W.tick += 3; brain.Think(t.W, t.Catalog, t.Map, t.S, new AiMemory()); }
                int target = brain.Decisions.ExpansionCell;
                Assert.IsTrue(target == 11 || target == 14, "a cell next to its headquarters, not a far one: " + target);
                using (AiSide.Enter(S2, null, null, ""))
                {
                    Assert.IsTrue(ExpansionTargetSkill.CanFortify(t.W, t.Catalog, t.Map, 14));
                    Assert.IsFalse(ExpansionTargetSkill.CanFortify(t.W, t.Catalog, t.Map, 10), "diagonal: no Fort zone can face it");
                    Assert.IsFalse(ExpansionTargetSkill.CanFortify(t.W, t.Catalog, t.Map, 5), "far away");
                }
            }
        }

        [Test]
        public void VarietyIsFixedPerMatchAndSeat()
        {
            using (var t = new TestWorld())
            {
                var hard = AiProfile.For(Difficulty.Hard);
                var a = AiProfile.Varied(hard, t.W, S2);
                Assert.AreSame(a, AiProfile.Varied(hard, t.W, S2), "the same match and seat play the same personality");
                Assert.AreEqual(hard.Id, a.Id);
                Assert.LessOrEqual(System.Math.Abs(a.Aggression - hard.Aggression), hard.Variety + 1e-6);
                Assert.LessOrEqual(System.Math.Abs(a.RiskTolerance - hard.RiskTolerance), hard.Variety + 1e-6);
                Assert.AreEqual(0.75f, hard.Aggression, 1e-6, "the preset itself is untouched");

                bool differs = false;
                for (int seed = 1; seed <= 8 && !differs; seed++)
                {
                    t.W.randomSeed = seed;
                    differs = AiProfile.Varied(hard, t.W, S2).Aggression != a.Aggression;
                }
                Assert.IsTrue(differs, "another match plays a different personality");

                var exact = hard.Clone();
                exact.Variety = 0f;
                Assert.AreSame(exact, AiProfile.Varied(exact, t.W, S2), "no variety: exactly to the numbers");
            }
        }

        [Test]
        public void AFourSeatArenaMatchPlaysOut()
        {
            var config = new ArenaConfig { maxMinutes = 8f };
            var seats = new List<ArenaSeat>
            {
                new ArenaSeat { profile = "hard", team = 1 }, new ArenaSeat { profile = "medium", team = 2 },
                new ArenaSeat { profile = "hard", team = 1 }, new ArenaSeat { profile = "medium", team = 2 },
            };
            var r = AiArena.PlaySeatMatch(config, 0, "map_d", 1234, seats, new Dictionary<string, AiProfile>());
            Assert.IsFalse(r.Reason.StartsWith("error"), r.Reason);
            Assert.AreEqual(4, r.Seats.Count);
            foreach (var s in r.Seats)
            {
                Assert.Greater(s.Stats.Sales, 0, s.Seat + " built a factory and sold");
                Assert.Greater(s.Stats.AiMs, 0, s.Seat + " was timed apart");
            }
            Assert.AreEqual(FactionId.Ai, AiSide.Me, "the AI code is back to playing the game's side");
        }
    }
}
