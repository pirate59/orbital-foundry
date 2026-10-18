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
            bool defending = AiSide.IsEnemy(w, e.attacker);
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
    /// Retreat and Fatigue Judgement: when the AI is attacking, it judges the exchange, not just its own losses. At the
    /// start of a battle it notes both sides' health (its ships; the defending ships and the target's Forts), and each
    /// turn works out from what each side has lost so far how long the defence would last and how long it would. It
    /// leaves when it would be destroyed first, allowing the profile's RetreatPatience (Easy hangs on, Hard leaves as
    /// soon as the exchange turns against it), or when even a winning exchange cannot finish before the battle's round
    /// limit. A ship down to 60% that has taken 80% off the defence is winning, and stays. It also gives up a fight in
    /// which nothing of its is left in or next to the target late on.
    /// </summary>
    public static class BattleRetreat
    {
        public const string Name = "Retreat & fatigue judgement";

        // Both sides' health when the AI's current attack began (runtime only; a new battle starts a new note).
        static WorldState noted;
        static int notedCell = -1, notedRound = int.MaxValue, notedAttackerHp, notedDefenderHp;
        static FactionId notedAttacker;

        public static bool ShouldRetreat(WorldState w, ContentCatalog c, MapDefinition map, AiProfile p)
        {
            var e = w.encounter;
            if (e.attacker != AiSide.Me) return false;
            int attackerHp, defenderHp;
            bool atTarget;
            Health(w, c, out attackerHp, out defenderHp, out atTarget);
            // A new battle (another world, target or attacker, or the round count went back): note where it started.
            if (noted != w || notedCell != e.targetCellId || notedAttacker != e.attacker || e.round < notedRound)
            {
                noted = w; notedCell = e.targetCellId; notedAttacker = e.attacker; notedRound = e.round;
                notedAttackerHp = attackerHp; notedDefenderHp = defenderHp;
                return false;
            }
            if (e.round >= 6 && !atTarget && attackerHp == 0) return true;
            if (e.round < p.RetreatFromRound || attackerHp == 0 || defenderHp == 0) return false;

            int rounds = System.Math.Max(1, e.round - notedRound);
            float lostA = notedAttackerHp - attackerHp, lostD = notedDefenderHp - defenderHp;
            if (lostA <= 0f) return false;            // untouched: nothing to judge
            if (lostD <= 0f) return true;             // taking hits and doing no damage
            float defenceLasts = defenderHp / (lostD / rounds);   // rounds until the defence falls, at this rate
            float attackLasts = attackerHp / (lostA / rounds);    // rounds until its own ships fall
            if (attackLasts * System.Math.Max(0.1f, p.RetreatPatience) < defenceLasts) return true;
            // Winning, but too slowly to finish before the round limit fatigues it anyway: keep the ships.
            int roundsLeft = c.rules.battleRoundLimit - e.round;
            return p.Level >= 1 && c.rules.battleRoundLimit > 0 && defenceLasts > roundsLeft + 1;
        }

        /// <summary>Health in the battle: its live ships, and the defence (enemy ships there and the target cell's Forts).</summary>
        static void Health(WorldState w, ContentCatalog c, out int attackerHp, out int defenderHp, out bool atTarget)
        {
            var e = w.encounter;
            attackerHp = defenderHp = 0;
            atTarget = false;
            foreach (int id in e.participantCraftIds)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k == null || !WorldLookup.IsCraftLive(k)) continue;
                if (k.owner == AiSide.Me)
                {
                    attackerHp += System.Math.Max(0, k.currentHealth);
                    if (k.cellId == e.targetCellId) atTarget = true;
                }
                else defenderHp += System.Math.Max(0, k.currentHealth);
            }
            foreach (var b in w.buildings)
            {
                if (!AiSide.IsEnemy(w, b.owner) || b.cellId != e.targetCellId) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.combatHealth > 0) defenderHp += System.Math.Max(0, def.combatHealth - b.damage);
            }
        }
    }
}
