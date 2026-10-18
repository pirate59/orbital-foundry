using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Chooses AI tactical actions under exactly the player's rules: favourable attacks first, then control-point holds,
    /// then moves toward the objective; ends the turn (or retreats) when nothing useful remains.
    /// </summary>
    public sealed class AITactics
    {
        readonly GameServices s;
        readonly HashSet<int> triedThisTurn = new HashSet<int>();
        int turnKey = -1;

        public AITactics(GameServices services)
        {
            s = services;
        }

        public GameCommand ChooseNext(WorldState w, ContentCatalog c, MapDefinition map, CombatResolver combat, MovementRules movement)
        {
            var e = w.encounter;
            if (!e.active || w.mode != MatchMode.Tactical || e.turn != AiSide.Me) return null;
            int key = e.round;
            if (key != turnKey) { turnKey = key; triedThisTurn.Clear(); }
            if (e.actionsRemaining <= 0) return End();
            var profile = AiProfile.Current(w);

            var mine = new List<CraftState>();
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == AiSide.Me && WorldLookup.IsCraftLive(craft)
                    && !e.actedCraftIds.Contains(id) && !triedThisTurn.Contains(id)) mine.Add(craft);
            }
            // Losing an attack: leave before the fleet is lost (Retreat & fatigue judgement).
            long timer = AiTiming.Begin();
            bool retreat = BattleRetreat.ShouldRetreat(w, c, map, profile);
            AiTiming.End(BattleRetreat.Name, timer);
            if (retreat) return new GameCommand { kind = CommandKind.Retreat, actor = AiSide.Me };
            if (mine.Count == 0)
            {
                timer = AiTiming.Begin();
                var join = BattleReinforcement.Choose(w, c, map, profile, triedThisTurn);
                AiTiming.End(BattleReinforcement.Name, timer);
                return join ?? End();
            }

            // 1. Best favourable attack, over every target it may choose in each cell.
            GameCommand bestAttack = null;
            int bestScore = 0;
            timer = AiTiming.Begin();
            foreach (var craft in mine)
            {
                foreach (int cellId in e.battleCellIds)
                {
                    bool guarded;
                    foreach (var target in CombatResolver.Targets(w, c, cellId, AiSide.Me, out guarded))
                    {
                        int craftTarget = target.IsFort ? -1 : target.Craft.id, fortTarget = target.IsFort ? target.Fort.id : -1;
                        var p = combat.Preview(w, c, map, craft.id, cellId, craftTarget, fortTarget);
                        if (!p.legal || p.attackDamageAfterArmour <= 0) continue;
                        int score = BattleTargeting.Score(w, c, profile, craft, target, p);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestAttack = new GameCommand { kind = CommandKind.TacticalAttack, actor = AiSide.Me, craftId = craft.id, targetCellId = cellId,
                                targetCraftId = craftTarget, buildingId = fortTarget };
                        }
                    }
                }
            }
            AiTiming.End(BattleTargeting.Name, timer); // the previews and scores of every attack considered
            if (bestAttack != null) return bestAttack;

            // 2. Hold a clear control point.
            foreach (var craft in mine)
            {
                var cell = WorldLookup.GetCell(w, craft.cellId);
                if (cell == null || cell.owner == AiSide.Me || CombatResolver.HasTargets(w, c, cell.id, AiSide.Me)) continue;
                if (w.captureHolds.Exists(h => h.cellId == cell.id && h.faction == AiSide.Me)) continue;
                return new GameCommand { kind = CommandKind.StartCapture, actor = AiSide.Me, craftId = craft.id, targetCellId = cell.id };
            }

            // 3. Bring spare ships from outside the battle (Battle reinforcement).
            timer = AiTiming.Begin();
            var reinforce = BattleReinforcement.Choose(w, c, map, profile, triedThisTurn);
            AiTiming.End(BattleReinforcement.Name, timer);
            if (reinforce != null) return reinforce;

            // 4. Lancers keep to firing range and back off while recharging (Lancer cooldown play).
            timer = AiTiming.Begin();
            var lancerMove = LancerPlay.Choose(w, c, map, movement, profile, mine);
            AiTiming.End(LancerPlay.Name, timer);
            if (lancerMove != null) return lancerMove;

            // 5. Close up on the shield flagship (Formation & shield use).
            timer = AiTiming.Begin();
            var formation = BattleFormation.Choose(w, c, map, movement, profile, mine);
            AiTiming.End(BattleFormation.Name, timer);
            if (formation != null) return formation;

            // 6. Move toward the objective: the contested cell when attacking, the nearest invader when defending.
            foreach (var craft in mine)
            {
                int objective = Objective(w, map, craft);
                if (objective < 0) continue;
                int current = WorldLookup.CellDistance(map, craft.cellId, objective);
                if (current <= 1 && CombatResolver.HasTargets(w, c, objective, AiSide.Me)) continue; // already in contact
                int bestCell = -1, bestDist = current;
                foreach (int cellId in e.battleCellIds)
                {
                    var path = movement.FindTacticalPath(w, map, c, craft.id, cellId);
                    if (path == null) continue;
                    int d = WorldLookup.CellDistance(map, cellId, objective);
                    if (d >= 0 && d < bestDist) { bestDist = d; bestCell = cellId; }
                }
                if (bestCell >= 0)
                    return new GameCommand { kind = CommandKind.TacticalMove, actor = AiSide.Me, craftId = craft.id, targetCellId = bestCell };
            }
            return End();
        }

        int Objective(WorldState w, MapDefinition map, CraftState craft)
        {
            var e = w.encounter;
            if (e.attacker == AiSide.Me) return e.targetCellId;
            int best = -1, bestDist = int.MaxValue;
            foreach (var other in w.crafts)
            {
                if (!AiSide.IsEnemy(w, other.owner) || !WorldLookup.IsCraftLive(other) || !e.battleCellIds.Contains(other.cellId)) continue;
                var cell = WorldLookup.GetCell(w, other.cellId);
                if (cell == null || !WorldLookup.AreAllies(w, cell.owner, AiSide.Me)) continue; // only chase invaders of its side's land
                int d = WorldLookup.CellDistance(map, craft.cellId, other.cellId);
                if (d >= 0 && d < bestDist) { bestDist = d; best = other.cellId; }
            }
            return best;
        }

        static GameCommand End()
        {
            return new GameCommand { kind = CommandKind.EndTurn, actor = AiSide.Me };
        }

        /// <summary>Executes one AI action. Returns false when the AI turn is over (or the AI cannot act now).</summary>
        public bool Step(WorldState w, CommandRouter router)
        {
            var e = w.encounter;
            if (!e.active || w.mode != MatchMode.Tactical || e.turn != AiSide.Me) return false;
            var cmd = ChooseNext(w, s.Catalog, s.Map, s.Combat, s.Movement);
            if (cmd == null) return false;
            var r = router.Execute(w, s.Catalog, s.Map, cmd);
            if (!r.success)
            {
                if (cmd.kind == CommandKind.EndTurn || cmd.kind == CommandKind.Retreat)
                {
                    // Should not happen; force the turn on to the next seat to avoid an endless AI turn.
                    int at = e.sides.IndexOf(AiSide.Me), next = e.sides.Count > 0 ? (at + 1) % e.sides.Count : 0;
                    if (e.sides.Count > 0) { e.turn = e.sides[next]; if (next == 0) e.round++; }
                    e.actedCraftIds.Clear();
                    e.actionsRemaining = EncounterService.ReadyCount(w, e.turn);
                    return false;
                }
                triedThisTurn.Add(cmd.craftId);
                return true;
            }
            return cmd.kind != CommandKind.EndTurn && cmd.kind != CommandKind.Retreat;
        }

        /// <summary>Plays the whole AI turn immediately (used by tests and headless runs).</summary>
        public void PlayTurn(WorldState w, CommandRouter router)
        {
            for (int guard = 0; guard < 32; guard++)
                if (!Step(w, router)) return;
            if (w.encounter.active && w.encounter.turn == AiSide.Me)
                router.Execute(w, s.Catalog, s.Map, End());
        }
    }
}
