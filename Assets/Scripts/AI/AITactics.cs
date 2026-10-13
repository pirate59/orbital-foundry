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
            if (!e.active || w.mode != MatchMode.Tactical || e.turn != FactionId.Ai) return null;
            int key = e.round;
            if (key != turnKey) { turnKey = key; triedThisTurn.Clear(); }
            if (e.actionsRemaining <= 0) return End();

            var mine = new List<CraftState>();
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == FactionId.Ai && WorldLookup.IsCraftLive(craft)
                    && !e.actedCraftIds.Contains(id) && !triedThisTurn.Contains(id)) mine.Add(craft);
            }
            if (mine.Count == 0) return MaybeRetreat(w, map) ?? End();

            // 1. Best favourable attack.
            GameCommand bestAttack = null;
            int bestScore = 0;
            foreach (var craft in mine)
            {
                foreach (int cellId in e.battleCellIds)
                {
                    if (!WorldLookup.HasOpposingCraft(w, cellId, FactionId.Ai)) continue;
                    var p = combat.Preview(w, c, map, craft.id, cellId);
                    if (!p.legal || p.attackDamageAfterArmour <= 0) continue;
                    int score = p.attackDamageAfterArmour * 3 - p.counterDamageAfterArmour * 2 + (p.defenderSurvives ? 0 : 20);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAttack = new GameCommand { kind = CommandKind.TacticalAttack, actor = FactionId.Ai, craftId = craft.id, targetCellId = cellId, targetCraftId = p.defenderCraftId };
                    }
                }
            }
            if (bestAttack != null) return bestAttack;

            // 2. Hold a clear control point.
            foreach (var craft in mine)
            {
                var cell = WorldLookup.GetCell(w, craft.cellId);
                if (cell == null || cell.owner == FactionId.Ai || WorldLookup.HasOpposingCraft(w, cell.id, FactionId.Ai)) continue;
                if (w.captureHolds.Exists(h => h.cellId == cell.id && h.faction == FactionId.Ai)) continue;
                return new GameCommand { kind = CommandKind.StartCapture, actor = FactionId.Ai, craftId = craft.id, targetCellId = cell.id };
            }

            // 3. Move toward the objective: the contested cell when attacking, the nearest invader when defending.
            foreach (var craft in mine)
            {
                int objective = Objective(w, map, craft);
                if (objective < 0) continue;
                int current = WorldLookup.CellDistance(map, craft.cellId, objective);
                if (current <= 1 && WorldLookup.HasOpposingCraft(w, objective, FactionId.Ai)) continue; // already in contact
                int bestCell = -1, bestDist = current;
                foreach (int cellId in e.battleCellIds)
                {
                    var path = movement.FindTacticalPath(w, map, c, craft.id, cellId);
                    if (path == null) continue;
                    int d = WorldLookup.CellDistance(map, cellId, objective);
                    if (d >= 0 && d < bestDist) { bestDist = d; bestCell = cellId; }
                }
                if (bestCell >= 0)
                    return new GameCommand { kind = CommandKind.TacticalMove, actor = FactionId.Ai, craftId = craft.id, targetCellId = bestCell };
            }
            return MaybeRetreat(w, map) ?? End();
        }

        int Objective(WorldState w, MapDefinition map, CraftState craft)
        {
            var e = w.encounter;
            if (e.attacker == FactionId.Ai) return e.targetCellId;
            int best = -1, bestDist = int.MaxValue;
            foreach (var other in w.crafts)
            {
                if (other.owner != FactionId.Player || !WorldLookup.IsCraftLive(other) || !e.battleCellIds.Contains(other.cellId)) continue;
                var cell = WorldLookup.GetCell(w, other.cellId);
                if (cell == null || cell.owner != FactionId.Ai) continue; // only chase invaders of AI land
                int d = WorldLookup.CellDistance(map, craft.cellId, other.cellId);
                if (d >= 0 && d < bestDist) { bestDist = d; best = other.cellId; }
            }
            return best;
        }

        GameCommand MaybeRetreat(WorldState w, MapDefinition map)
        {
            var e = w.encounter;
            if (e.attacker != FactionId.Ai || e.round < 6) return null;
            foreach (var craft in w.crafts)
                if (craft.owner == FactionId.Ai && craft.cellId == e.targetCellId && WorldLookup.IsCraftLive(craft)) return null;
            return new GameCommand { kind = CommandKind.Retreat, actor = FactionId.Ai };
        }

        static GameCommand End()
        {
            return new GameCommand { kind = CommandKind.EndTurn, actor = FactionId.Ai };
        }

        /// <summary>Executes one AI action. Returns false when the AI turn is over (or the AI cannot act now).</summary>
        public bool Step(WorldState w, CommandRouter router)
        {
            var e = w.encounter;
            if (!e.active || w.mode != MatchMode.Tactical || e.turn != FactionId.Ai) return false;
            var cmd = ChooseNext(w, s.Catalog, s.Map, s.Combat, s.Movement);
            if (cmd == null) return false;
            var r = router.Execute(w, s.Catalog, s.Map, cmd);
            if (!r.success)
            {
                if (cmd.kind == CommandKind.EndTurn || cmd.kind == CommandKind.Retreat)
                {
                    // Should not happen; force the turn over to avoid an endless AI turn.
                    e.turn = FactionId.Player;
                    e.actionsRemaining = s.Catalog.rules.tacticalActionsPerSide;
                    e.actedCraftIds.Clear();
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
            if (w.encounter.active && w.encounter.turn == FactionId.Ai)
                router.Execute(w, s.Catalog, s.Map, End());
        }
    }
}
