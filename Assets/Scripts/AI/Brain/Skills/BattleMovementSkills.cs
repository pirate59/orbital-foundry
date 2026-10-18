using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Formation and Shield Use: in a battle, ships with nothing worth attacking close up on a friendly shield flagship
    /// (a Flying Fortress) so its armour bonus covers them; Rail Lancers keep out of it, they fight from range.
    /// Medium and Hard; Easy moves straight at the objective.
    /// </summary>
    public static class BattleFormation
    {
        public const string Name = "Formation & shield use";

        public static GameCommand Choose(WorldState w, ContentCatalog c, MapDefinition map, MovementRules movement, AiProfile p, List<CraftState> mine)
        {
            if (p.Level < 1) return null;
            var e = AiSide.CurrentBattle(w);
            if (e == null) return null;
            // The shield to gather on: the first AI flagship in the battle.
            CraftState flagship = null;
            foreach (int id in e.participantCraftIds)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k == null || k.owner != AiSide.Me || !WorldLookup.IsCraftLive(k)) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d != null && d.shieldArmour > 0) { flagship = k; break; }
            }
            if (flagship == null) return null;
            foreach (var craft in mine)
            {
                if (craft.id == flagship.id || craft.cellId == flagship.cellId) continue;
                var d = CatalogRepository.GetCraft(c, craft.typeId);
                if (d == null || d.range > 1 || d.shieldArmour > 0) continue; // ranged ships stay back
                if (CombatResolver.HasTargets(w, c, flagship.cellId, AiSide.Me)) continue; // the flagship's cell is contested
                var path = movement.FindTacticalPath(w, map, c, craft.id, flagship.cellId);
                if (path != null && path.Count > 0)
                    return new GameCommand { kind = CommandKind.TacticalMove, actor = AiSide.Me, battleId = AiSide.CurrentBattle(w).id, craftId = craft.id, targetCellId = flagship.cellId };
            }
            return null;
        }
    }

    /// <summary>
    /// Rail Lancer Cooldown Play: a Lancer recharging next to the enemy backs off to a cell out of their reach; a
    /// ready Lancer with no shot moves to two cells from the enemy, where its ranged attack works and nothing answers.
    /// Hard only; Medium and Easy leave Lancers where they are.
    /// </summary>
    public static class LancerPlay
    {
        public const string Name = "Lancer cooldown play";

        public static GameCommand Choose(WorldState w, ContentCatalog c, MapDefinition map, MovementRules movement, AiProfile p, List<CraftState> mine)
        {
            if (p.Level < 2) return null;
            var e = AiSide.CurrentBattle(w);
            if (e == null) return null;
            foreach (var craft in mine)
            {
                var d = CatalogRepository.GetCraft(c, craft.typeId);
                if (d == null || d.range < 2) continue;
                int nearest = NearestEnemy(w, c, map, craft.cellId);
                bool recharging = craft.cooldownTurns > 0;
                // Recharging within reach of the enemy: back off. Ready and not at firing range: step to it.
                int want = recharging ? (nearest <= 1 ? d.range : -1) : (nearest != d.range ? d.range : -1);
                if (want < 0) continue;
                int bestCell = -1, bestScore = int.MaxValue;
                foreach (int cellId in e.battleCellIds)
                {
                    if (cellId == craft.cellId || CombatResolver.HasTargets(w, c, cellId, AiSide.Me)) continue;
                    int there = NearestEnemy(w, c, map, cellId);
                    if (there < 0) continue;
                    int score = System.Math.Abs(there - want) * 10 + (there < 2 ? 50 : 0); // never next to them
                    if (score >= bestScore) continue;
                    var path = movement.FindTacticalPath(w, map, c, craft.id, cellId);
                    if (path == null || path.Count == 0) continue;
                    bestScore = score;
                    bestCell = cellId;
                }
                if (bestCell >= 0)
                    return new GameCommand { kind = CommandKind.TacticalMove, actor = AiSide.Me, battleId = AiSide.CurrentBattle(w).id, craftId = craft.id, targetCellId = bestCell };
            }
            return null;
        }

        /// <summary>Cells from a cell to the nearest battle cell the player holds targets in, or -1.</summary>
        static int NearestEnemy(WorldState w, ContentCatalog c, MapDefinition map, int cellId)
        {
            int best = -1;
            foreach (int other in AiSide.CurrentBattle(w).battleCellIds)
            {
                if (!CombatResolver.HasTargets(w, c, other, AiSide.Me)) continue;
                int d = WorldLookup.CellDistance(map, cellId, other);
                if (d >= 0 && (best < 0 || d < best)) best = d;
            }
            return best;
        }
    }
}
