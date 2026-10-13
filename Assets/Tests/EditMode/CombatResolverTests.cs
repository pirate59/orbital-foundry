using NUnit.Framework;

namespace OrbitalFoundry.Tests
{
    // map_a layout (cell IDs):   0 1 2 3
    //                            4 5 6 7
    public class CombatResolverTests
    {
        TestWorld t;

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
        public void CraftFightOnFourStatsWithAirAndRangedNeutral()
        {
            // attack, reactive, armour, HP
            var expected = new System.Collections.Generic.Dictionary<string, int[]>
            {
                { "tank", new[] { 5, 3, 3, 12 } }, { "artillery", new[] { 8, 1, 1, 7 } },
                { "guard", new[] { 2, 5, 3, 11 } }, { "strike", new[] { 6, 2, 1, 8 } },
            };
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
            }
            Assert.IsTrue(t.Catalog.rules.combatScalesWithHealth);
        }

        [Test]
        public void TankVersusTankTradesHitAndScaledCounter()
        {
            var attacker = t.AddCraft(FactionId.Player, "tank", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "tank", 2, true);
            var p = Preview(attacker, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Attack", p.attackStatName);
            Assert.AreEqual(5, p.attackBase);
            Assert.AreEqual(2, p.attackDamageAfterArmour);  // 5 - 3
            Assert.IsTrue(p.defenderSurvives);
            Assert.AreEqual(3, p.counterBase);              // 3 reactive at 10/12 HP rounds up to 3
            Assert.AreEqual(1, p.counterDamageAfterArmour); // 3 - 3, minimum one
            Assert.IsTrue(t.S.Combat.Apply(t.W, t.Catalog, t.Map, p).success);
            Assert.AreEqual(10, defender.currentHealth);
            Assert.AreEqual(11, attacker.currentHealth);
        }

        [Test]
        public void DamagedCraftHitSofter()
        {
            var attacker = t.AddCraft(FactionId.Player, "artillery", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "tank", 2, true);
            attacker.currentHealth = 3;                     // 3/7 of 8 attack rounds up to 4
            var p = Preview(attacker, 2);
            Assert.AreEqual(4, p.attackBase);
            Assert.AreEqual(1, p.attackDamageAfterArmour);  // 4 - 3
            defender.currentHealth = 2;                     // after the hit, 1/12 of 3 reactive rounds up to 1
            p = Preview(attacker, 2);
            Assert.AreEqual(1, p.counterBase);

            t.Catalog.rules.combatScalesWithHealth = false;
            Assert.AreEqual(8, Preview(attacker, 2).attackBase, "the rule can be switched off");
        }

        [Test]
        public void AirborneAndRangedNoLongerChangeTheNumbers()
        {
            var strike = t.AddCraft(FactionId.Player, "strike", 1, false);
            t.AddCraft(FactionId.Ai, "tank", 2, true);
            var p = Preview(strike, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual(3, p.attackDamageAfterArmour);  // 6 - 3
            Assert.AreEqual(2, p.counterDamageAfterArmour); // 3 reactive at 9/12 rounds up to 3, minus 1 armour

            var tank = t.AddCraft(FactionId.Player, "tank", 3, false);
            t.AddCraft(FactionId.Ai, "strike", 7, true);
            var air = Preview(tank, 7);
            Assert.IsTrue(air.legal, air.reason);
            Assert.AreEqual(4, air.attackDamageAfterArmour, "a ground craft hits an airborne one with its normal attack"); // 5 - 1
        }

        [Test]
        public void ALethalHitGetsNoCounter()
        {
            var artillery = t.AddCraft(FactionId.Player, "artillery", 1, false);
            var tank = t.AddCraft(FactionId.Ai, "tank", 2, true);
            tank.currentHealth = 3;
            var p = Preview(artillery, 2);
            Assert.IsFalse(p.defenderSurvives);
            Assert.AreEqual(0, p.counterDamageAfterArmour);
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.IsNull(WorldLookup.GetCraft(t.W, tank.id));
            Assert.AreEqual(7, artillery.currentHealth);
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
            var tank = t.AddCraft(FactionId.Player, "tank", 1, false);
            var guard = t.AddCraft(FactionId.Ai, "guard", 2, true);
            var p = Preview(tank, 2);
            Assert.AreEqual(1, p.attackDamageAfterArmour);  // 5 - (3 + 1)
            Assert.AreEqual(6, p.counterBase);              // (5 + 1) at 10/11 rounds up to 6
            Assert.AreEqual(3, p.counterDamageAfterArmour); // 6 - 3
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.AreEqual(10, guard.currentHealth);
            Assert.AreEqual(9, tank.currentHealth);
        }

        [Test]
        public void RangeIsRejectedBeyondCatalogRange()
        {
            var artillery = t.AddCraft(FactionId.Player, "artillery", 0, false);
            t.AddCraft(FactionId.Ai, "tank", 2, true); // distance 2; every craft now fights adjacent cells only
            var p = Preview(artillery, 2);
            Assert.IsFalse(p.legal);
            StringAssert.Contains("range", p.reason);
        }

        [Test]
        public void AirborneCraftFliesOverDefendedCellButCannotLandThere()
        {
            var strike = t.AddCraft(FactionId.Player, "strike", 0, true);
            t.AddCraft(FactionId.Ai, "tank", 1, true);
            t.AddCraft(FactionId.Ai, "guard", 3, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 3, FactionId.Player).success);

            var path = t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, strike.id, 2);
            Assert.IsNotNull(path, "strike craft should overfly cell 1");
            CollectionAssert.AreEqual(new[] { 1, 2 }, path);
            Assert.IsNull(t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, strike.id, 1), "cannot end on an opposing craft");

            // A ground craft with the same reach cannot pass the defended cell.
            CatalogRepository.GetCraft(t.Catalog, "tank").movement = 3;
            var tank = t.AddCraft(FactionId.Player, "tank", 0, false);
            t.W.encounter.participantCraftIds.Add(tank.id);
            var groundPath = t.S.Movement.FindTacticalPath(t.W, t.Map, t.Catalog, tank.id, 2);
            Assert.IsTrue(groundPath == null || !groundPath.Contains(1), "ground path must avoid cell 1");
        }

        [Test]
        public void KilledDefenderIsReplacedImmediatelyFromReserves()
        {
            var tank = t.AddCraft(FactionId.Player, "tank", 1, false);
            var weak = t.AddCraft(FactionId.Ai, "tank", 2, true);
            var reserve = t.AddCraft(FactionId.Ai, "guard", 2, false);
            weak.currentHealth = 1;
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            var r = t.Run(new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Player, craftId = tank.id, targetCellId = 2 });
            Assert.IsTrue(r.success, r.message);
            Assert.IsNull(WorldLookup.GetCraft(t.W, weak.id));
            Assert.AreEqual(12, tank.currentHealth, "a destroyed defender does not counter");
            Assert.AreEqual(reserve.id, WorldLookup.GetCell(t.W, 2).defenderCraftId);
            Assert.AreEqual(2, t.W.encounter.actionsRemaining);
        }

        [Test]
        public void PlayerChoosesReplacementWhenSeveralReservesSurvive()
        {
            t.SetOwner(1, FactionId.Player);
            var defender = t.AddCraft(FactionId.Player, "tank", 1, true);
            var r1 = t.AddCraft(FactionId.Player, "guard", 1, false);
            var r2 = t.AddCraft(FactionId.Player, "artillery", 1, false);
            var attacker = t.AddCraft(FactionId.Ai, "artillery", 2, false);
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
            Assert.AreEqual(3, t.W.encounter.actionsRemaining, "answering the replacement prompt is free");
            Assert.AreNotEqual(r1.id, WorldLookup.GetCell(t.W, 1).defenderCraftId);
        }

        [Test]
        public void HoldInterruptedByContestAndRetreatReturnsToFactory()
        {
            var tank = t.AddCraft(FactionId.Player, "tank", 1, false);
            t.AddCraft(FactionId.Ai, "guard", 2, true);
            Assert.IsTrue(t.S.Encounters.Start(t.W, t.Catalog, t.Map, 2, FactionId.Player).success);
            // Claim the neutral cell the tank stands in, then let an enemy contest it.
            t.MustRun(new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Player, craftId = tank.id, targetCellId = 1 });
            Assert.AreEqual(1, t.W.captureHolds.Count);
            var intruder = t.AddCraft(FactionId.Ai, "strike", 1, false);
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
