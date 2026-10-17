using NUnit.Framework;
using UnityEngine;

namespace OrbitalFoundry.Tests
{
    // map_a layout (cell IDs):   0 1 2 3
    //                            4 5 6 7
    public class CombatResolverTests
    {
        TestWorld t;
        const FactionId P = FactionId.Player;

        [SetUp]
        public void SetUp()
        {
            t = new TestWorld();
            t.ClearCraft();
        }

        [TearDown]
        public void TearDown() { t.Dispose(); }

        CombatPreview Preview(CraftState attacker, int cell) { return t.S.Combat.Preview(t.W, t.Catalog, t.Map, attacker.id, cell); }

        [Test]
        public void ThreeShipsFightOnAttackReturnFireArmourAndHp()
        {
            // attack, return fire (reactive), armour, HP
            var expected = new System.Collections.Generic.Dictionary<string, int[]>
            {
                { "guard", new[] { 2, 4, 2, 10 } },    // cheap: light attack, light-to-fair return fire
                { "bomber", new[] { 8, 2, 2, 10 } },   // hits hardest, weak return fire, fair armour
                { "fortress", new[] { 5, 7, 4, 14 } }, // shield flagship: fair attack, the strongest return fire and armour
            };
            Assert.AreEqual(4, t.Catalog.crafts.Count, "three core ships and the Rail Lancer");
            foreach (var kv in expected)
            {
                var k = CatalogRepository.GetCraft(t.Catalog, kv.Key);
                Assert.AreEqual(kv.Value[0], k.attackDamage, kv.Key);
                Assert.AreEqual(kv.Value[1], k.defenceDamage, kv.Key);
                Assert.AreEqual(kv.Value[2], k.armour, kv.Key);
                Assert.AreEqual(kv.Value[3], k.maxHealth, kv.Key);
                Assert.AreEqual(k.attackDamage, k.rangedAttackDamage, kv.Key + " ranged attack matches attack");
                Assert.AreEqual(k.attackDamage, k.airAttackDamage, kv.Key + " air attack matches attack");
                Assert.AreEqual(k.defenceDamage, k.rangedDefenceDamage, kv.Key + " ranged reactive matches reactive");
                Assert.AreEqual(k.defenceDamage, k.airDefenceDamage, kv.Key + " air reactive matches reactive");
                Assert.AreEqual(0, k.range, kv.Key + " fights adjacent cells only");
                Assert.IsTrue(k.airborne, kv.Key + " flies");
            }
            var guard = CatalogRepository.GetCraft(t.Catalog, "guard");
            var fortress = CatalogRepository.GetCraft(t.Catalog, "fortress");
            Assert.Less(guard.cost, CatalogRepository.GetCraft(t.Catalog, "bomber").cost);
            Assert.Less(CatalogRepository.GetCraft(t.Catalog, "bomber").cost, fortress.cost);
            Assert.IsTrue(t.Catalog.rules.combatScalesWithHealth);
            Assert.AreEqual(1, fortress.shieldArmour, "the Fortress shields its fleet");
        }

        [Test]
        public void RailLancerFiresFromTwoCellsWithNoReturnFire()
        {
            var lancer = t.AddCraft(FactionId.Player, "lancer", 0, false);
            t.AddCraft(FactionId.Ai, "bomber", 2, true); // two cells away (map_a: 0 1 2 3 / 4 5 6 7)
            var p = Preview(lancer, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Ranged attack", p.attackStatName);
            Assert.AreEqual(7, p.attackDamageAfterArmour, "9 against a bomber's 2 armour");
            Assert.AreEqual(0, p.counterDamageAfterArmour, "nothing reaches back two cells");
            t.AddCraft(FactionId.Ai, "guard", 3, true);
            Assert.IsFalse(Preview(lancer, 3).legal, "three cells is out of range");
            Assert.AreEqual(2, CatalogRepository.GetCraft(t.Catalog, "bomber").movement);
        }

        [Test]
        public void RailLancerPointBlankIsWeakAndAnswered()
        {
            var lancer = t.AddCraft(FactionId.Player, "lancer", 1, false);
            t.AddCraft(FactionId.Ai, "bomber", 2, true);
            var p = Preview(lancer, 2);
            Assert.AreEqual("Attack", p.attackStatName);
            Assert.AreEqual(2, p.attackDamageAfterArmour, "point-blank: 4 against 2 armour");
            Assert.AreEqual(2, p.counterDamageAfterArmour, "and the bomber fires back in full (0 armour)");
            var def = CatalogRepository.GetCraft(t.Catalog, "lancer");
            Assert.AreEqual(0, def.armour);
            Assert.AreEqual(0, def.defenceDamage, "no return fire of its own");
            Assert.AreEqual(9, def.maxHealth);
            Assert.AreEqual(3, Fleet.FullFleet(def), "three long ships");
        }

        [Test]
        public void RailLancerRechargesForFourTurnsAndResearchCutsIt()
        {
            var lancer = t.AddCraft(FactionId.Player, "lancer", 0, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            t.AddCraft(FactionId.Ai, "fortress", 2, false); // a reserve, so the cell holds after a kill
            var def = CatalogRepository.GetCraft(t.Catalog, "lancer");
            Assert.AreEqual(4, ResearchService.CooldownTurns(t.Catalog, t.Player, def));
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            Assert.IsTrue(t.W.encounter.participantCraftIds.Contains(lancer.id), "a lancer two cells away joins the battle");
            t.MustRun(new GameCommand { kind = CommandKind.TacticalAttack, actor = P, craftId = lancer.id, targetCellId = 2 });
            Assert.AreEqual(4, lancer.cooldownTurns);
            int fired = 1;
            for (int turn = 2; turn <= 5; turn++)
            {
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P });
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Ai });
                var p = Preview(lancer, 2);
                if (turn < 5) StringAssert.Contains("recharging", p.reason, "turn " + turn);
                else { Assert.IsTrue(p.legal, "ready again on turn 5: " + p.reason); fired++; }
            }
            Assert.AreEqual(2, fired);
            t.Research(P, "capacitor_banks", 1);
            Assert.AreEqual(3, ResearchService.CooldownTurns(t.Catalog, t.Player, def));
            t.Research(P, "capacitor_banks", 2);
            Assert.AreEqual(2, ResearchService.CooldownTurns(t.Catalog, t.Player, def));
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = P });
            Assert.AreEqual(0, lancer.cooldownTurns, "recharged for the next battle");
        }

        [Test]
        public void RailLancerIsALateTierInvestment()
        {
            var lancer = CatalogRepository.GetCraft(t.Catalog, "lancer");
            Assert.Greater(lancer.cost, CatalogRepository.GetCraft(t.Catalog, "bomber").cost);
            Assert.Less(lancer.cost, CatalogRepository.GetCraft(t.Catalog, "fortress").cost);
            Assert.IsTrue(lancer.components.Exists(c => c.itemId == "precision_circuit"), "Precision Circuits finally go into a ship");
            Assert.AreEqual(2, CatalogRepository.GetResearch(t.Catalog, "capacitor_banks").levels.Count);
        }

        BuildingState AiFort(int cellId)
        {
            t.SetOwner(cellId, FactionId.Ai);
            var fort = new BuildingState { id = WorldLookup.AllocateId(t.W), owner = FactionId.Ai, typeId = "fort", cellId = cellId, x = 3, y = 3, anchor = true };
            t.W.buildings.Add(fort); // test setup: a deployed Fort
            return fort;
        }

        [Test]
        public void TheDefenderProtectsTheOtherShipsInItsCell()
        {
            var bomber = t.AddCraft(P, "bomber", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "guard", 2, true);
            var other = t.AddCraft(FactionId.Ai, "lancer", 2, false);
            var blocked = t.S.Combat.Preview(t.W, t.Catalog, t.Map, bomber.id, 2, other.id);
            Assert.IsFalse(blocked.legal);
            StringAssert.Contains("protected", blocked.reason);
            Assert.IsTrue(t.S.Combat.Preview(t.W, t.Catalog, t.Map, bomber.id, 2, defender.id).legal, "the defender can be chosen");
            Assert.AreEqual(defender.id, Preview(bomber, 2).defenderCraftId, "and is the default target");
            t.W.crafts.Remove(defender); // test setup: the defender has fallen
            WorldLookup.GetCell(t.W, 2).defenderCraftId = -1;
            Assert.IsTrue(t.S.Combat.Preview(t.W, t.Catalog, t.Map, bomber.id, 2, other.id).legal, "with no defender any ship can be picked");
        }

        [Test]
        public void AFortGuardsItsCellAndOnlyFiresBack()
        {
            var fort = AiFort(2);
            var hidden = t.AddCraft(FactionId.Ai, "bomber", 2, false);
            var guard = t.AddCraft(P, "guard", 1, false);
            Assert.IsFalse(t.S.Combat.Preview(t.W, t.Catalog, t.Map, guard.id, 2, hidden.id).legal, "the Fort shields the ships in its cell");
            var p = Preview(guard, 2);
            Assert.AreEqual(fort.id, p.defenderBuildingId, "the Fort is the target");
            Assert.AreEqual(1, p.attackDamageAfterArmour, "a guard's 2 against armour 3: one");
            Assert.AreEqual(3, p.counterDamageAfterArmour, "its return fire (5 at 9/10 HP) minus 2 armour");
            var lancer = t.AddCraft(P, "lancer", 0, false); // two cells away
            var shelled = Preview(lancer, 2);
            Assert.AreEqual(6, shelled.attackDamageAfterArmour, "9 against armour 3");
            Assert.AreEqual(0, shelled.counterDamageAfterArmour, "a Fort reaches only its neighbours");
            Assert.IsTrue(t.S.Combat.Apply(t.W, t.Catalog, t.Map, shelled).success);
            Assert.AreEqual(6, fort.damage);
        }

        [Test]
        public void ADestroyedFortMustBeRebuiltWithinTwoMinutes()
        {
            var fort = AiFort(2);
            t.AddCraft(FactionId.Ai, "guard", 2, false); // a unit staying behind does not save the cell
            var lancer = t.AddCraft(P, "lancer", 0, false);
            fort.damage = 8; // test setup: nearly down
            Assert.IsTrue(t.S.Combat.Apply(t.W, t.Catalog, t.Map, Preview(lancer, 2)).success);
            Assert.IsNull(WorldLookup.GetBuilding(t.W, fort.id), "the Fort is destroyed");
            var cell = WorldLookup.GetCell(t.W, 2);
            Assert.AreNotEqual(-1, cell.fortLostTick, "the rebuild clock is running");
            Assert.AreEqual(1200, t.Catalog.rules.fortRebuildTicks, "two minutes");
            for (int i = 0; i < t.Catalog.rules.fortRebuildTicks - 10; i++) t.Loop.TickNormal();
            Assert.AreEqual(FactionId.Ai, cell.owner, "still held while the clock runs");
            for (int i = 0; i < 20; i++) t.Loop.TickNormal();
            Assert.AreEqual(FactionId.Neutral, cell.owner, "not rebuilt in time: neutral");
        }

        [Test]
        public void EveryShipActsEachTurnAndMoreCanJoin()
        {
            t.AddCraft(P, "bomber", 1, false);
            t.AddCraft(P, "guard", 1, false);
            t.AddCraft(P, "guard", 1, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).success);
            Assert.AreEqual(3, t.W.encounter.actionsRemaining, "one action for every ship in the battle");
            var late = t.AddCraft(P, "guard", 0, false); // outside the battle, one cell from its edge
            Assert.IsFalse(t.W.encounter.participantCraftIds.Contains(late.id));
            t.MustRun(new GameCommand { kind = CommandKind.TacticalMove, actor = P, craftId = late.id, targetCellId = 1 });
            Assert.IsTrue(t.W.encounter.participantCraftIds.Contains(late.id), "it joins the battle");
            Assert.IsTrue(t.W.encounter.actedCraftIds.Contains(late.id), "arriving is its turn");
            Assert.AreEqual(3, t.W.encounter.actionsRemaining, "the others still have theirs");
        }

        [Test]
        public void AnAttackThatHasNotWonInTenRoundsIsFatigued()
        {
            var bomber = t.AddCraft(P, "bomber", 1, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).success);
            Assert.AreEqual(10, t.Catalog.rules.battleRoundLimit);
            for (int round = 1; round <= 10; round++)
            {
                Assert.IsTrue(t.W.encounter.active, "still fighting in round " + round);
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P });
                t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Ai });
            }
            Assert.IsFalse(t.W.encounter.active, "fatigued after round 10");
            Assert.AreEqual(MatchMode.Factory, t.W.mode);
            Assert.IsTrue(bomber.worldPathCellIds.Count > 0 && bomber.worldPathCellIds[bomber.worldPathCellIds.Count - 1] == 0,
                "the attackers fly home to a cell they hold");
            Assert.AreEqual(FactionId.Ai, WorldLookup.GetCell(t.W, 2).owner, "the defender keeps its cell");
        }

        void EndRound()
        {
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = P });
            if (t.W.encounter.active && t.W.encounter.turn == FactionId.Ai) t.S.AiTactics.PlayTurn(t.W, t.S.Commands);
        }

        [Test]
        public void StandingInAClearedTargetCapturesItAfterTheEnemyReplies()
        {
            t.SetOwner(2, FactionId.Ai);
            var bomber = t.AddCraft(P, "bomber", 1, false);
            var guard = t.AddCraft(P, "guard", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "guard", 2, true);
            defender.currentHealth = 1; // test setup: one hit from falling
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).success);
            t.MustRun(new GameCommand { kind = CommandKind.TacticalAttack, actor = P, craftId = bomber.id, targetCellId = 2 });
            t.MustRun(new GameCommand { kind = CommandKind.TacticalMove, actor = P, craftId = guard.id, targetCellId = 2 });
            Assert.IsTrue(t.W.encounter.active, "not yet: the enemy gets its reply");
            EndRound();
            Assert.AreEqual(P, WorldLookup.GetCell(t.W, 2).owner, "no hold order needed");
            Assert.IsFalse(t.W.encounter.active, "the battle is won and over");
        }

        [Test]
        public void RetreatWorksWithShipsInsideTheContestedCell()
        {
            t.SetOwner(2, FactionId.Ai);
            var guard = t.AddCraft(P, "guard", 2, false); // already inside the target cell
            t.AddCraft(P, "bomber", 1, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, P).success);
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = P });
            Assert.IsFalse(t.W.encounter.active);
            Assert.IsTrue(guard.worldPathCellIds.Count > 0, "the ship in the enemy cell flies home");
        }

        [Test]
        public void FleetsShowOneShipPerShareOfHp()
        {
            var guard = CatalogRepository.GetCraft(t.Catalog, "guard");
            var bomber = CatalogRepository.GetCraft(t.Catalog, "bomber");
            var fortress = CatalogRepository.GetCraft(t.Catalog, "fortress");
            Assert.AreEqual(10, Fleet.Ships(guard, 10), "ten Mobile Guards, one per HP");
            Assert.AreEqual(3, Fleet.Ships(guard, 3));
            Assert.AreEqual(4, Fleet.Ships(bomber, 10), "a Heavy Bomber ship per 3 HP: 10 HP is four");
            Assert.AreEqual(3, Fleet.Ships(bomber, 7));
            Assert.AreEqual(1, Fleet.Ships(bomber, 1));
            Assert.AreEqual(1, Fleet.Ships(fortress, 14), "the Flying Fortress flies alone");
            Assert.AreEqual(1, Fleet.Ships(fortress, 1));
            Assert.AreEqual(0, Fleet.Ships(guard, 0), "a destroyed craft shows no ships");
            Assert.AreEqual(1f, Fleet.Scale(guard));
            Assert.AreEqual(2f, Fleet.Scale(bomber), "bombers are twice a guard's size");
            Assert.AreEqual(5f, Fleet.Scale(fortress), "the fortress longer again");
            Assert.AreEqual(1.8f, Fleet.Girth(fortress), 0.001f, "and fattened, so the slim zeppelin reads as heavy");
            Assert.AreEqual(1f, Fleet.Girth(bomber));
            var wedge = Fleet.Wedge(10);
            Assert.AreEqual(Vector2.zero, wedge[0], "the leader flies at the point");
            Assert.AreEqual(3f, wedge[9].y, "ten ships make four rows");
        }

        [Test]
        public void BomberHitsAFortressHardAndTakesItsHeavyReturnFire()
        {
            var attacker = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "fortress", 2, true);
            var p = Preview(attacker, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Attack", p.attackStatName);
            Assert.AreEqual(8, p.attackBase);
            Assert.AreEqual(3, p.attackDamageAfterArmour);  // 8 - (4 + 1 defender armour)
            Assert.IsTrue(p.defenderSurvives);
            Assert.AreEqual(7, p.counterBase);              // (7 + 1) at 11/14 HP rounds up to 7
            Assert.AreEqual(5, p.counterDamageAfterArmour); // 7 - 2
            Assert.IsTrue(t.S.Combat.Apply(t.W, t.Catalog, t.Map, p).success);
            Assert.AreEqual(11, defender.currentHealth);
            Assert.AreEqual(5, attacker.currentHealth);
        }

        [Test]
        public void DamagedCraftHitSofter()
        {
            var attacker = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "fortress", 2, true);
            attacker.currentHealth = 3;                     // 3/10 of 8 attack rounds up to 3
            var p = Preview(attacker, 2);
            Assert.AreEqual(3, p.attackBase);
            Assert.AreEqual(1, p.attackDamageAfterArmour);  // 3 - 5, minimum one
            defender.currentHealth = 2;                     // after the hit, 1/14 of 8 return fire rounds up to 1
            p = Preview(attacker, 2);
            Assert.AreEqual(1, p.counterBase);

            t.Catalog.rules.combatScalesWithHealth = false;
            Assert.AreEqual(8, Preview(attacker, 2).attackBase, "the rule can be switched off");
        }

        [Test]
        public void AGuardBarelyScratchesABomber()
        {
            var guard = t.AddCraft(FactionId.Player, "guard", 1, false);
            t.AddCraft(FactionId.Ai, "bomber", 2, true);
            var p = Preview(guard, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual(1, p.attackDamageAfterArmour);  // 2 - 2, minimum one
            Assert.AreEqual(1, p.counterDamageAfterArmour); // 2 return fire at 9/10 rounds up to 2, minus 2 armour: one
        }

        [Test]
        public void ALethalHitGetsNoCounter()
        {
            var bomber = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var fortress = t.AddCraft(FactionId.Ai, "fortress", 2, true);
            fortress.currentHealth = 3;
            var p = Preview(bomber, 2);
            Assert.IsFalse(p.defenderSurvives);
            Assert.AreEqual(0, p.counterDamageAfterArmour);
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.IsNull(WorldLookup.GetCraft(t.W, fortress.id));
            Assert.AreEqual(10, bomber.currentHealth);
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 2).defenderCraftId);
            var e = t.S.Combat.Events[t.S.Combat.Events.Count - 1];
            Assert.AreEqual(3, e.defenderHealthBefore);
            Assert.AreEqual(0, e.defenderHealthAfter);
            Assert.AreEqual(0, e.counter);
            Assert.IsTrue(e.defenderAssigned);
        }

        [Test]
        public void GuardDefenderBonusAppliesToArmourAndCounter()
        {
            var bomber = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var guard = t.AddCraft(FactionId.Ai, "guard", 2, true);
            var p = Preview(bomber, 2);
            Assert.AreEqual(5, p.attackDamageAfterArmour);  // 8 - (2 + 1)
            Assert.AreEqual(3, p.counterBase);              // (4 + 1) at 5/10 rounds up to 3
            Assert.AreEqual(1, p.counterDamageAfterArmour); // 3 - 2
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.AreEqual(5, guard.currentHealth);
            Assert.AreEqual(9, bomber.currentHealth);
        }

        [Test]
        public void RangeIsRejectedBeyondCatalogRange()
        {
            var artillery = t.AddCraft(FactionId.Player, "bomber", 0, false);
            t.AddCraft(FactionId.Ai, "fortress", 2, true); // distance 2; every craft now fights adjacent cells only
            var p = Preview(artillery, 2);
            Assert.IsFalse(p.legal);
            StringAssert.Contains("range", p.reason);
        }

        [Test]
        public void AirborneCraftFliesOverDefendedCellButCannotLandThere()
        {
            // A battle over cell 1 (map_a: 0 1 2 3 / 4 5 6 7) takes in its neighbours 0, 2 and 5.
            var strike = t.AddCraft(FactionId.Player, "bomber", 0, true);
            t.AddCraft(FactionId.Ai, "fortress", 1, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, FactionId.Player).success);

            var path = t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, strike.id, 2);
            Assert.IsNotNull(path, "a flying craft overflies cell 1");
            CollectionAssert.AreEqual(new[] { 1, 2 }, path);
            Assert.IsNull(t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, strike.id, 1), "cannot end on an opposing craft");

            // A ground craft with the same reach cannot pass the defended cell (test setup: every ship flies now).
            var grounded = CatalogRepository.GetCraft(t.Catalog, "guard");
            grounded.airborne = false;
            grounded.movement = 3;
            var walker = t.AddCraft(FactionId.Player, "guard", 0, false);
            t.W.encounter.participantCraftIds.Add(walker.id);
            var groundPath = t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, walker.id, 2);
            Assert.IsTrue(groundPath == null || !groundPath.Contains(1), "ground path must avoid cell 1");
        }

        [Test]
        public void BattlesStayLocalHoweverFastTheShipsAre()
        {
            Assert.AreEqual(2, CatalogRepository.GetCraft(t.Catalog, "bomber").movement);
            Assert.AreEqual(1, t.S.Encounters.BattleRadius(), "reach comes from attack range, not movement");
            t.AddCraft(FactionId.Player, "bomber", 1, false);
            var far = t.AddCraft(FactionId.Player, "guard", 0, false); // two cells from the target
            t.AddCraft(FactionId.Ai, "fortress", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            Assert.IsFalse(t.W.encounter.participantCraftIds.Contains(far.id), "a craft two cells away stays out of it");
        }

        [Test]
        public void KilledDefenderIsReplacedImmediatelyFromReserves()
        {
            var bomber = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var weak = t.AddCraft(FactionId.Ai, "fortress", 2, true);
            var reserve = t.AddCraft(FactionId.Ai, "guard", 2, false);
            weak.currentHealth = 1;
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            var r = t.Run(new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Player, craftId = bomber.id, targetCellId = 2 });
            Assert.IsTrue(r.success, r.message);
            Assert.IsNull(WorldLookup.GetCraft(t.W, weak.id));
            Assert.AreEqual(10, bomber.currentHealth, "a destroyed defender does not counter");
            Assert.AreEqual(reserve.id, WorldLookup.GetCell(t.W, 2).defenderCraftId);
            Assert.AreEqual(0, t.W.encounter.actionsRemaining, "its only ship has acted");
        }

        [Test]
        public void PlayerChoosesReplacementWhenSeveralReservesSurvive()
        {
            t.SetOwner(1, FactionId.Player);
            var defender = t.AddCraft(FactionId.Player, "fortress", 1, true);
            var r1 = t.AddCraft(FactionId.Player, "guard", 1, false);
            var r2 = t.AddCraft(FactionId.Player, "bomber", 1, false);
            var attacker = t.AddCraft(FactionId.Ai, "bomber", 2, false);
            defender.currentHealth = 1;
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 1, FactionId.Ai).success);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
            t.MustRun(new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Ai, craftId = attacker.id, targetCellId = 1 });
            Assert.AreEqual(1, t.W.encounter.pendingDefenderCellId);
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 1).defenderCraftId);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Ai });
            var blocked = t.Run(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
            Assert.AreEqual("PENDING_DEFENDER", blocked.code);
            t.MustRun(new GameCommand { kind = CommandKind.AssignDefender, actor = FactionId.Player, craftId = r2.id, targetCellId = 1 });
            Assert.AreEqual(r2.id, WorldLookup.GetCell(t.W, 1).defenderCraftId);
            Assert.AreEqual(2, t.W.encounter.actionsRemaining, "answering the replacement prompt is free: both surviving reserves can still act");
            Assert.AreNotEqual(r1.id, WorldLookup.GetCell(t.W, 1).defenderCraftId);
        }

        [Test]
        public void HoldInterruptedByContestAndRetreatReturnsToFactory()
        {
            var bomber = t.AddCraft(FactionId.Player, "bomber", 1, false);
            t.AddCraft(FactionId.Ai, "guard", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            // Claim the neutral cell the bomber stands in, then let an enemy contest it.
            t.MustRun(new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Player, craftId = bomber.id, targetCellId = 1 });
            Assert.AreEqual(1, t.W.captureHolds.Count);
            var intruder = t.AddCraft(FactionId.Ai, "guard", 1, false);
            t.W.encounter.participantCraftIds.Add(intruder.id);
            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Player });
            Assert.AreEqual(0, t.W.captureHolds.Count, "contested hold must be cancelled");
            Assert.AreEqual(FactionId.Neutral, WorldLookup.GetCell(t.W, 1).owner);

            t.MustRun(new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Ai });
            int tickBefore = t.W.tick;
            t.Loop.Update(5f);
            Assert.AreEqual(tickBefore, t.W.tick, "the factory clock is frozen in tactical mode");
            t.MustRun(new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Player });
            Assert.AreEqual(MatchMode.Factory, t.W.mode);
            Assert.IsFalse(t.W.encounter.active);
        }
    }
}
