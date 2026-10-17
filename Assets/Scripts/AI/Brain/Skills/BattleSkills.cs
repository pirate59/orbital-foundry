using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Battle Target Selection: how the AI scores one attack in a battle. Easy takes any hit that does damage;
    /// Medium weighs damage dealt against return fire taken, with a bonus for a kill; Hard also values what it hits
    /// (stronger targets are worth more, wounded ones are worth finishing), what a kill opens up (the defender and
    /// Forts shield the rest), and how much its own ship is worth when it takes return fire.
    /// Run by the battle tactics for every attack it considers.
    /// </summary>
    public static class BattleTargeting
    {
        public const string Name = "Battle target selection";

        public static int Score(WorldState w, ContentCatalog c, AiProfile p, CraftState attacker, CombatResolver.Target target, CombatPreview preview)
        {
            int damage = preview.attackDamageAfterArmour, counter = preview.counterDamageAfterArmour;
            bool kill = !preview.defenderSurvives;
            if (p.Level < 1) return damage; // Easy: any hit will do
            int score = damage * 3 - counter * 2 + (kill ? 20 : 0);
            if (p.Level < 2) return score; // Medium
            // Hard: worth of the target and of its own ship.
            int targetStrength = target.IsFort ? AiWorldModel.FortStrength(c, target.Fort) : AiWorldModel.Strength(c, target.Craft);
            int ownStrength = AiWorldModel.Strength(c, attacker);
            int wounded = preview.defenderMaxHealth - preview.defenderHealth; // finish what is already hurt
            bool shield = target.IsFort || (target.Craft != null && WorldLookup.IsDefender(w, target.Craft));
            score += (kill ? targetStrength / 10 : 0) + wounded + (shield ? 8 : 0);
            score -= counter * ownStrength / 200;
            return score;
        }
    }

    /// <summary>
    /// Battle Reinforcement: brings AI ships from outside a battle into it, as the player can, when the battle
    /// matters to it: always on Hard, on Medium when it is defending its own cell. Only spare ships come (never a
    /// cell's defender, nor one waiting for repairs), into a battle cell the player does not hold.
    /// </summary>
    public static class BattleReinforcement
    {
        public const string Name = "Battle reinforcement";

        public static GameCommand Choose(WorldState w, ContentCatalog c, MapDefinition map, AiProfile p, HashSet<int> tried)
        {
            var e = w.encounter;
            if (p.Level < 1) return null; // Easy fights with what it brought
            bool defending = e.attacker == AiSide.Foe;
            if (p.Level < 2 && !defending) return null; // Medium reinforces its own cells
            foreach (var k in w.crafts)
            {
                if (k.owner != AiSide.Me || !WorldLookup.IsCraftLive(k) || e.participantCraftIds.Contains(k.id) || tried.Contains(k.id)) continue;
                if (WorldLookup.IsDefender(w, k) || k.worldPathCellIds.Count > 0 || AIController.WantsRepair(w, c, k)) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                int reach = d != null ? System.Math.Max(1, d.movement) : 1;
                int bestCell = -1, bestDist = int.MaxValue;
                foreach (int cellId in e.battleCellIds)
                {
                    if (CombatResolver.HasTargets(w, c, cellId, AiSide.Me)) continue;
                    int dist = WorldLookup.CellDistance(map, k.cellId, cellId);
                    if (dist < 0 || dist > reach) continue;
                    // Nearest the fighting: the contested cell.
                    int toTarget = WorldLookup.CellDistance(map, cellId, e.targetCellId);
                    if (toTarget >= 0 && toTarget < bestDist) { bestDist = toTarget; bestCell = cellId; }
                }
                if (bestCell >= 0)
                    return new GameCommand { kind = CommandKind.TacticalMove, actor = AiSide.Me, craftId = k.id, targetCellId = bestCell };
            }
            return null;
        }
    }

    /// <summary>
    /// Retreat and Fatigue Judgement: when the AI is attacking, leave a fight it is losing rather than lose the
    /// fleet. It compares its strength in the battle with the player's (ships and Forts there); a cautious profile
    /// stays longer, a sharp one cuts its losses early. It also gives up a fight in which nothing of its is left in
    /// or next to the target late on.
    /// </summary>
    public static class BattleRetreat
    {
        public const string Name = "Retreat & fatigue judgement";

        public static bool ShouldRetreat(WorldState w, ContentCatalog c, MapDefinition map, AiProfile p)
        {
            var e = w.encounter;
            if (e.attacker != AiSide.Me) return false;
            int aiStrength = 0, playerStrength = 0;
            bool atTarget = false;
            foreach (int id in e.participantCraftIds)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k == null || !WorldLookup.IsCraftLive(k)) continue;
                if (k.owner == AiSide.Me)
                {
                    aiStrength += AiWorldModel.Strength(c, k);
                    if (k.cellId == e.targetCellId) atTarget = true;
                }
                else playerStrength += AiWorldModel.Strength(c, k);
            }
            // The same measure the target was picked on (Attack target): the enemy ships in the battle and the target
            // cell's Forts. Forts in the other battle cells are not what it came for, and counting them made a strike
            // planned at many times the odds look lost in round 2.
            foreach (var b in w.buildings)
                if (b.owner == AiSide.Foe && b.cellId == e.targetCellId) playerStrength += AiWorldModel.FortStrength(c, b);
            // Easy fights on until round 8, Medium judges from round 4, Hard from round 2.
            int fromRound = p.Level < 1 ? 8 : p.Level < 2 ? 4 : 2;
            float losingBelow = p.Level < 1 ? 0.5f : p.Level < 2 ? 0.8f : 1f;
            if (e.round >= fromRound && aiStrength < playerStrength * losingBelow) return true;
            return e.round >= 6 && !atTarget && aiStrength == 0;
        }
    }
}
