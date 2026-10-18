using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    /// <summary>
    /// Battles fought while the world runs: several at once, the besieged cell alone stops producing, ships in a battle
    /// take no map orders, and turns run on a timer whose strikes shorten a slow seat's next turn. On a two-seat Four
    /// Corners field (seat 1 in cell 0, the AI in cell 15), where cells 1 and 14 are far enough apart for two battles.
    /// </summary>
    public class LiveBattleTests
    {
        const FactionId P = FactionId.Player, A = FactionId.Ai;
        static readonly string Field = MapGenerator.IdForSeats(new[] { true, true, false, false }, 5);
        TestWorld t;

        [SetUp] public void SetUp() { t = new TestWorld(Field); t.ClearCraft(); }
        [TearDown] public void TearDown() { t.Dispose(); }

        /// <summary>The AI holds <paramref name="target"/> with a guard; the player attacks it with a bomber from <paramref name="from"/>.</summary>
        EncounterState Attack(int target, int from)
        {
            t.AddCraft(A, "guard", target, true);
            t.AddCraft(P, "bomber", from, false);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, target, P).success);
            return t.S.Encounters.LastStarted;
        }

        [Test]
        public void TheWorldRunsOnAndOnlyTheBesiegedCellStops()
        {
            t.Fortify(1, A);
            t.Fortify(2, A);
            var e = Attack(1, 0);
            Assert.AreEqual(MatchMode.Factory, t.W.mode, "no frozen battle mode");
            Assert.IsFalse(WorldLookup.GetCell(t.W, 1).factoryOnline, "the target is under siege");
            Assert.IsTrue(WorldLookup.GetCell(t.W, 2).factoryOnline, "its neighbour, also in the battle, still produces");
            int before = t.W.tick;
            for (int i = 0; i < 20; i++) t.Loop.TickNormal();
            Assert.AreEqual(before + 20, t.W.tick, "the clock keeps going");
            Assert.IsTrue(e.active);
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = P, battleId = e.id });
            Assert.IsTrue(WorldLookup.GetCell(t.W, 1).factoryOnline, "the siege is lifted");
        }

        [Test]
        public void EachSeatCountsItsBattlesWonAndLost()
        {
            var e = Attack(1, 0);
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = P, battleId = e.id });
            Assert.IsFalse(e.active);
            Assert.AreEqual(0, t.Player.battlesWon);
            Assert.AreEqual(1, t.Player.battlesLost, "the attacker who retreated lost");
            Assert.AreEqual(1, t.Ai.battlesWon, "the defender held");
            Assert.AreEqual(0, t.Ai.battlesLost);
        }

        [Test]
        public void TwoBattlesRunAtOnceEachWithItsOwnTurns()
        {
            var north = Attack(1, 0);
            t.SetOwner(13, P);
            var south = Attack(14, 13);
            Assert.AreEqual(2, t.W.encounters.FindAll(x => x.active).Count);
            Assert.AreNotEqual(north.id, south.id);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P, battleId = north.id });
            Assert.AreEqual(A, north.turn, "the north battle moved on");
            Assert.AreEqual(P, south.turn, "the south one did not");
            Assert.AreEqual("BATTLE_HERE", t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).code, "no second battle over the same cells");
        }

        [Test]
        public void AShipInABattleTakesNoMapOrders()
        {
            var e = Attack(1, 0);
            var bomber = t.W.crafts.Find(k => k.owner == P);
            var r = t.Run(new GameCommand { kind = CommandKind.MoveWorld, actor = P, craftId = bomber.id, targetCellId = 4 });
            Assert.AreEqual("IN_BATTLE", r.code);
            Assert.IsTrue(e.participantCraftIds.Contains(bomber.id));
        }

        [Test]
        public void ARunningOutTurnPassesOnAndShortensTheNext()
        {
            var r = t.Catalog.rules;
            var e = Attack(1, 0);
            Assert.AreEqual(P, e.turn);
            Assert.AreEqual(r.battleTurnTicks, e.turnLimitTicks, "a full first turn");

            Run(r.battleTurnTicks);
            Assert.AreEqual(A, e.turn, "the player's turn ran out");
            Assert.AreEqual(1, e.strikes[e.sides.IndexOf(P)], "one strike");
            Run(r.battleTurnTicks); // the AI's turn: it ends it itself, or it runs out
            Assert.AreEqual(P, e.turn);
            Assert.AreEqual(r.battleShortTurnTicks, e.turnLimitTicks, "a shorter turn after running out");

            Run(r.battleShortTurnTicks);
            Run(r.battleTurnTicks);
            Assert.AreEqual(P, e.turn);
            Assert.AreEqual(r.battleShortestTurnTicks, e.turnLimitTicks, "shorter still after running out twice");

            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P, battleId = e.id });
            Assert.AreEqual(0, e.strikes[e.sides.IndexOf(P)], "acting in time clears the strikes");
            Run(r.battleTurnTicks);
            Assert.AreEqual(r.battleTurnTicks, e.turnLimitTicks, "and the full turn comes back");
        }

        /// <summary>Ticks until the turn changes hands (or <paramref name="limit"/> ticks have passed).</summary>
        void Run(int limit)
        {
            var e = t.Battle;
            var turn = e.turn;
            for (int i = 0; i <= limit + 1 && e.active && e.turn == turn; i++) t.Loop.TickNormal();
        }

        [Test]
        public void TheAiPlaysItsTurnAtAWatchablePace()
        {
            var e = Attack(1, 0);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P, battleId = e.id });
            Assert.AreEqual(A, e.turn);
            int step = t.Catalog.rules.aiBattleStepTicks;
            for (int i = 0; i < step - 1; i++) t.Loop.TickNormal();
            Assert.AreEqual(A, e.turn, "not before its first step");
            for (int i = 0; i < step * 4 && e.active && e.turn == A; i++) t.Loop.TickNormal();
            Assert.IsTrue(!e.active || e.turn == P, "it played its turn well within the timer");
        }

        [Test]
        public void OthersBattlesAreWatchedOnlyByChoiceAndYourTurnComesFirst()
        {
            // Three seats: seat 1 (the player, cell 0), seat 2 (cell 15) and seat 3 (cell 3).
            using (var three = new TestWorld(MapGenerator.IdForSeats(new[] { true, true, true, false }, 5)))
            {
                three.ClearCraft();
                three.AddCraft(A, "guard", 7, true);                    // seat 2 holds cell 7
                three.AddCraft(FactionId.Seat3, "bomber", 3, false);    // seat 3 attacks it from its corner
                Assert.IsTrue(three.S.Encounters.Start(three.W, three.Catalog, three.Map, 7, FactionId.Seat3).success);
                var theirs = three.S.Encounters.LastStarted;
                Assert.IsNull(Perspective.FocusBattle(three.W, -1, -1), "seat 1 is not pulled into a battle it is not in");
                Assert.IsFalse(Perspective.InBattle(theirs));
                Assert.AreSame(theirs, Perspective.FocusBattle(three.W, -1, theirs.id), "watching it shows it");

                three.AddCraft(FactionId.Seat3, "guard", 4, true);      // seat 3 holds cell 4
                three.AddCraft(P, "bomber", 0, false);
                Assert.IsTrue(three.S.Encounters.Start(three.W, three.Catalog, three.Map, 4, P).success);
                var mine = three.S.Encounters.LastStarted;
                Assert.AreEqual(P, mine.turn);
                Assert.AreSame(mine, Perspective.FocusBattle(three.W, -1, theirs.id), "its own turn comes before watching");
            }
        }

        [Test]
        public void AnOldSaveWithAFrozenBattleUpgrades()
        {
            var e = Attack(1, 0);
            // Lay it out as schema 4 kept it: the one battle on the world, which was frozen in Tactical mode.
            t.W.encounters.Clear();
            t.W.encounter = e;
            t.W.mode = MatchMode.Tactical;
            var envelope = new SaveEnvelope { schemaVersion = 4, catalogVersion = t.Catalog.version, world = t.W };
            Directory.CreateDirectory(t.SaveDir);
            File.WriteAllText(t.Loop.Saves.SlotPath, JsonUtility.ToJson(envelope));
            WorldState loaded;
            var r = t.Loop.Saves.TryLoad(t.Catalog, t.Map, out loaded);
            Assert.IsTrue(r.success, r.code + " " + r.message);
            Assert.AreEqual(MatchMode.Factory, loaded.mode, "the world runs again");
            Assert.AreEqual(1, loaded.encounters.Count);
            Assert.IsTrue(loaded.encounters[0].active);
            Assert.AreEqual(loaded.encounters[0].sides.Count, loaded.encounters[0].strikes.Count);
            Assert.IsFalse(loaded.encounter.active, "the old slot is emptied");
        }
    }
}
