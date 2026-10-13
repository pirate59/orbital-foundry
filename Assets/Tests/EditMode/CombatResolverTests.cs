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
        public void TankCatalogValuesMatchUserSuppliedProfile()
        {
            var tank = CatalogRepository.GetCraft(t.Catalog, "tank");
            Assert.AreEqual(4, tank.attackDamage);
            Assert.AreEqual(0, tank.rangedAttackDamage);
            Assert.AreEqual(0, tank.airAttackDamage);
            Assert.AreEqual(3, tank.defenceDamage);
            Assert.AreEqual(1, tank.rangedDefenceDamage);
            Assert.AreEqual(0, tank.airDefenceDamage);
            Assert.AreEqual(10, tank.maxHealth);
            Assert.AreEqual(3, tank.armour);
        }

        [Test]
        public void TankVersusTankAdjacentDealsOneAndCountersOne()
        {
            var attacker = t.AddCraft(FactionId.Player, "tank", 1, false);
            var defender = t.AddCraft(FactionId.Ai, "tank", 2, true);
            var p = Preview(attacker, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Attack", p.attackStatName);
            Assert.AreEqual(4, p.attackBase);
            Assert.AreEqual(1, p.attackDamageAfterArmour);
            Assert.IsTrue(p.defenderSurvives);
            Assert.AreEqual("Defence", p.counterStatName);
            Assert.AreEqual(1, p.counterDamageAfterArmour); // 3 - 3 armour, minimum one
            Assert.IsTrue(t.S.Combat.Apply(t.W, t.Catalog, t.Map, p).success);
            Assert.AreEqual(9, defender.currentHealth);
            Assert.AreEqual(9, attacker.currentHealth);
        }

        [Test]
        public void TankCannotInitiateRangedOrAirAttacks()
        {
            var tank = t.AddCraft(FactionId.Player, "tank", 0, false);
            t.AddCraft(FactionId.Ai, "tank", 2, true);
            var ranged = Preview(tank, 2);
            Assert.IsFalse(ranged.legal, "distance 2 with Range 0 must be illegal");

            t.AddCraft(FactionId.Ai, "strike", 1, true);
            var air = Preview(tank, 1);
            Assert.IsFalse(air.legal);
            Assert.AreEqual("Air Attack", air.attackStatName);
        }

        [Test]
        public void ArtilleryRangedHitTriggersTankRangedDefenceOnlyIfItSurvives()
        {
            var artillery = t.AddCraft(FactionId.Player, "artillery", 0, false);
            var tank = t.AddCraft(FactionId.Ai, "tank", 2, true);
            var p = Preview(artillery, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Ranged Attack", p.attackStatName);
            Assert.AreEqual(4, p.attackDamageAfterArmour); // 7 - 3
            Assert.AreEqual("Ranged Defence", p.counterStatName);
            Assert.AreEqual(1, p.counterDamageAfterArmour); // max(1, 1 - 1)
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.AreEqual(6, tank.currentHealth);
            Assert.AreEqual(5, artillery.currentHealth);

            // A lethal hit gets no counter.
            tank.currentHealth = 3;
            var lethal = Preview(artillery, 2);
            Assert.IsFalse(lethal.defenderSurvives);
            Assert.AreEqual(0, lethal.counterDamageAfterArmour);
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, lethal);
            Assert.IsNull(WorldLookup.GetCraft(t.W, tank.id));
            Assert.AreEqual(5, artillery.currentHealth);
            Assert.AreEqual(-1, WorldLookup.GetCell(t.W, 2).defenderCraftId);
        }

        [Test]
        public void AirborneAttackerFacesAirDefenceZeroFromTank()
        {
            var strike = t.AddCraft(FactionId.Player, "strike", 1, false);
            var tank = t.AddCraft(FactionId.Ai, "tank", 2, true);
            var p = Preview(strike, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Attack", p.attackStatName);
            Assert.AreEqual(2, p.attackDamageAfterArmour); // 5 - 3
            Assert.AreEqual("Air Defence", p.counterStatName);
            Assert.AreEqual(0, p.counterDamageAfterArmour);
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.AreEqual(8, tank.currentHealth);
            Assert.AreEqual(6, strike.currentHealth);
        }

        [Test]
        public void AirTargetsUseAirAttackAndCounterFollowsAttackerTypeAndDistance()
        {
            var strikeAttacker = t.AddCraft(FactionId.Player, "strike", 0, false);
            var airTarget = t.AddCraft(FactionId.Ai, "strike", 2, true);
            // Airborne attacker at range: Air Attack in, Air Defence back.
            var p = Preview(strikeAttacker, 2);
            Assert.IsTrue(p.legal, p.reason);
            Assert.AreEqual("Air Attack", p.attackStatName);
            Assert.AreEqual(2, p.attackDamageAfterArmour); // 3 - 1
            Assert.AreEqual("Air Defence", p.counterStatName);
            Assert.AreEqual(airTarget.id, p.defenderCraftId);

            // Ground ranged attacker on an airborne target: Air Attack in, Ranged Defence back.
            CatalogRepository.GetCraft(t.Catalog, "artillery").airAttackDamage = 4;
            var artillery = t.AddCraft(FactionId.Player, "artillery", 0, false);
            var a = Preview(artillery, 2);
            Assert.IsTrue(a.legal, a.reason);
            Assert.AreEqual("Air Attack", a.attackStatName);
            Assert.AreEqual(3, a.attackDamageAfterArmour); // 4 - 1
            Assert.AreEqual("Ranged Defence", a.counterStatName);
            Assert.AreEqual(1, a.counterDamageAfterArmour);

            // Ground adjacent attacker on an airborne target: Air Attack in, Defence back.
            var guard = t.AddCraft(FactionId.Player, "guard", 1, false);
            var g = Preview(guard, 2);
            Assert.IsTrue(g.legal, g.reason);
            Assert.AreEqual("Air Attack", g.attackStatName);
            Assert.AreEqual("Defence", g.counterStatName);
        }

        [Test]
        public void GuardDefenderBonusAppliesToArmourAndCounter()
        {
            var tank = t.AddCraft(FactionId.Player, "tank", 1, false);
            var guard = t.AddCraft(FactionId.Ai, "guard", 2, true);
            var p = Preview(tank, 2);
            Assert.AreEqual(1, p.attackDamageAfterArmour); // 4 - (2 + 1)
            Assert.AreEqual(5, p.counterBase);             // 4 + 1
            Assert.AreEqual(2, p.counterDamageAfterArmour); // 5 - 3
            t.S.Combat.Apply(t.W, t.Catalog, t.Map, p);
            Assert.AreEqual(8, guard.currentHealth);
            Assert.AreEqual(8, tank.currentHealth);
        }

        [Test]
        public void RangeIsRejectedBeyondCatalogRange()
        {
            var artillery = t.AddCraft(FactionId.Player, "artillery", 0, false);
            t.AddCraft(FactionId.Ai, "tank", 3, true); // distance 3, range 2
            var p = Preview(artillery, 3);
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
            Assert.AreEqual(10, tank.currentHealth, "a destroyed defender does not counter");
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
            var attacker = t.AddCraft(FactionId.Ai, "artillery", 3, false);
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
