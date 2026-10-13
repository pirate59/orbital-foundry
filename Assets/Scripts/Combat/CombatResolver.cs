using System;

namespace OrbitalFoundry
{
    /// <summary>
    /// One attacker against the target cell's assigned defender, with a survival-gated counterattack.
    /// damage = 0 for a zero stat, otherwise max(1, stat - armour).
    /// </summary>
    public sealed class CombatResolver
    {
        public static int DamageAfterArmour(int stat, int armour)
        {
            if (stat <= 0) return 0;
            return Math.Max(1, stat - armour);
        }

        public CombatPreview Preview(WorldState w, ContentCatalog c, MapDefinition map, int attackerCraftId, int targetCellId)
        {
            var p = new CombatPreview { attackerCraftId = attackerCraftId, defenderCraftId = -1 };
            var attacker = WorldLookup.GetCraft(w, attackerCraftId);
            if (attacker == null || !WorldLookup.IsCraftLive(attacker)) return Illegal(p, "The attacking craft is unavailable.");
            if (attacker.owner == FactionId.Neutral) return Illegal(p, "Neutral craft cannot attack.");
            var cell = WorldLookup.GetCell(w, targetCellId);
            if (cell == null) return Illegal(p, "That cell does not exist.");
            bool assigned;
            var defender = TargetInCell(w, cell, attacker.owner, out assigned);
            if (defender == null) return Illegal(p, "No enemy craft in that cell to attack.");
            p.defenderCraftId = defender.id;

            var aDef = CatalogRepository.GetCraft(c, attacker.typeId);
            var dDef = CatalogRepository.GetCraft(c, defender.typeId);
            if (aDef == null || dDef == null) return Illegal(p, "Unknown craft type.");

            int distance = WorldLookup.CellDistance(map, attacker.cellId, cell.id);
            if (distance < 1) return Illegal(p, "The target must be in another cell.");

            int attackStat;
            if (dDef.airborne)
            {
                if (distance > Math.Max(1, aDef.range)) return Illegal(p, "Target is out of range (" + distance + " cells).");
                attackStat = aDef.airAttackDamage;
                p.attackStatName = "Air Attack";
            }
            else if (distance == 1)
            {
                attackStat = aDef.attackDamage;
                p.attackStatName = "Attack";
            }
            else
            {
                if (distance > aDef.range) return Illegal(p, "Target is out of range (" + distance + " cells, range " + aDef.range + ").");
                attackStat = aDef.rangedAttackDamage;
                p.attackStatName = "Ranged Attack";
            }
            if (attackStat <= 0) return Illegal(p, aDef.displayName + " has no " + p.attackStatName + " against this target.");

            // Defender bonuses apply only to the cell's assigned defender.
            int defenderArmour = dDef.armour + (assigned ? dDef.defenderArmourBonus : 0);
            p.attackBase = attackStat;
            p.attackDamageAfterArmour = DamageAfterArmour(attackStat, defenderArmour);
            p.defenderSurvives = defender.currentHealth - p.attackDamageAfterArmour > 0;

            int counterStat;
            if (aDef.airborne) { counterStat = dDef.airDefenceDamage; p.counterStatName = "Air Defence"; }
            else if (distance > 1) { counterStat = dDef.rangedDefenceDamage; p.counterStatName = "Ranged Defence"; }
            else { counterStat = dDef.defenceDamage; p.counterStatName = "Defence"; }
            if (counterStat > 0 && assigned) counterStat += dDef.defenderCounterBonus;
            p.counterBase = counterStat;
            p.counterDamageAfterArmour = p.defenderSurvives ? DamageAfterArmour(counterStat, aDef.armour) : 0;
            p.legal = true;
            return p;
        }

        /// <summary>
        /// The craft an attack on this cell strikes: the assigned defender when it is an enemy of the attacker;
        /// otherwise (enemy craft standing in a cell they do not own) the lowest-ID enemy craft there, without defender bonuses.
        /// </summary>
        public static CraftState TargetInCell(WorldState w, CellState cell, FactionId attackerOwner, out bool assigned)
        {
            assigned = false;
            var defender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            if (defender != null && WorldLookup.IsCraftLive(defender) && defender.cellId == cell.id && defender.owner != attackerOwner)
            {
                assigned = true;
                return defender;
            }
            CraftState lowest = null;
            foreach (var c in w.crafts)
            {
                if (c.cellId != cell.id || c.owner == attackerOwner || !WorldLookup.IsCraftLive(c)) continue;
                if (lowest == null || c.id < lowest.id) lowest = c;
            }
            return lowest;
        }

        static CombatPreview Illegal(CombatPreview p, string reason)
        {
            p.legal = false;
            p.reason = reason;
            return p;
        }

        /// <summary>
        /// Applies exactly the previewed damage. Fails if the preview is stale (either craft changed HP, cell, or
        /// defender status since). Destroyed craft are removed and their defender slots cleared; buildings are never touched.
        /// </summary>
        public CommandResult Apply(WorldState w, ContentCatalog c, MapDefinition map, CombatPreview preview)
        {
            if (preview == null || !preview.legal) return WorldLookup.Fail("ILLEGAL_ATTACK", preview != null ? preview.reason : "No attack.");
            var attacker = WorldLookup.GetCraft(w, preview.attackerCraftId);
            var defender = WorldLookup.GetCraft(w, preview.defenderCraftId);
            if (attacker == null || defender == null) return WorldLookup.Fail("STALE_PREVIEW", "The combat situation changed.");
            var fresh = Preview(w, c, map, attacker.id, defender.cellId);
            if (!fresh.legal || fresh.defenderCraftId != preview.defenderCraftId
                || fresh.attackDamageAfterArmour != preview.attackDamageAfterArmour
                || fresh.counterDamageAfterArmour != preview.counterDamageAfterArmour)
                return WorldLookup.Fail("STALE_PREVIEW", "The combat situation changed; check the new preview.");

            defender.currentHealth = Math.Max(0, defender.currentHealth - preview.attackDamageAfterArmour);
            string msg = Name(c, attacker) + " hits " + Name(c, defender) + " for " + preview.attackDamageAfterArmour + " (" + preview.attackStatName + ").";
            if (defender.currentHealth <= 0)
            {
                msg += " " + Name(c, defender) + " destroyed.";
                RemoveCraft(w, defender);
            }
            else if (preview.counterDamageAfterArmour > 0)
            {
                attacker.currentHealth = Math.Max(0, attacker.currentHealth - preview.counterDamageAfterArmour);
                msg += " Counter " + preview.counterDamageAfterArmour + " (" + preview.counterStatName + ").";
                if (attacker.currentHealth <= 0)
                {
                    msg += " " + Name(c, attacker) + " destroyed.";
                    RemoveCraft(w, attacker);
                }
            }
            return WorldLookup.Ok(msg);
        }

        static string Name(ContentCatalog c, CraftState craft)
        {
            var def = CatalogRepository.GetCraft(c, craft.typeId);
            string owner = craft.owner == FactionId.Player ? "Your " : "Enemy ";
            return owner + (def != null ? def.displayName : craft.typeId);
        }

        public static void RemoveCraft(WorldState w, CraftState craft)
        {
            craft.currentHealth = 0;
            WorldLookup.ClearDefenderSlot(w, craft.id);
            w.crafts.Remove(craft);
            w.captureHolds.RemoveAll(h => h.craftId == craft.id);
            w.encounter.participantCraftIds.Remove(craft.id);
            w.encounter.actedCraftIds.Remove(craft.id);
            if (w.ai.raidCraftId == craft.id) w.ai.raidCraftId = -1;
        }
    }
}
