using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// One attacker against the target cell's assigned defender, with a survival-gated counterattack.
    /// damage = 0 for a zero stat, otherwise max(1, stat - armour). With rules.combatScalesWithHealth, the attacker's
    /// stat scales with its current HP and the counter with the defender's HP left after the hit (rounded up).
    /// </summary>
    public sealed class CombatResolver
    {
        /// <summary>Attacks resolved since the presentation last drained this list, oldest first.</summary>
        public readonly List<CombatEvent> Events = new List<CombatEvent>();

        public static int DamageAfterArmour(int stat, int armour)
        {
            if (stat <= 0) return 0;
            return Math.Max(1, stat - armour);
        }

        /// <summary>
        /// Armour a craft gains from a shield flagship of its side in the same cell (the strongest one; a flagship
        /// does not shield itself).
        /// </summary>
        public static int ShieldBonus(WorldState w, ContentCatalog c, CraftState craft)
        {
            int best = 0;
            foreach (var k in w.crafts)
            {
                if (k.id == craft.id || k.owner != craft.owner || k.cellId != craft.cellId || !WorldLookup.IsCraftLive(k)) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d != null && d.shieldArmour > best) best = d.shieldArmour;
            }
            return best;
        }

        /// <summary>A stat scaled to remaining HP, rounded up so any surviving craft keeps some bite.</summary>
        public static int ScaleByHealth(ContentCatalog c, int stat, int health, int maxHealth)
        {
            if (!c.rules.combatScalesWithHealth || stat <= 0 || maxHealth <= 0) return stat;
            return (int)Math.Ceiling(stat * (double)Math.Max(0, health) / maxHealth);
        }

        /// <summary>A battle target: an enemy craft, or an enemy structure that can be attacked (a Fort).</summary>
        public struct Target
        {
            public CraftState Craft;
            public BuildingState Fort;
            public bool IsFort { get { return Fort != null; } }
        }

        /// <summary>
        /// What an attacker may strike in a cell. The cell's Fort and its assigned defender guard the rest: while
        /// either stands only they can be chosen (<paramref name="guarded"/> is true); once they are gone any enemy
        /// craft there can be. Fort first, then the defender, then the others by ID.
        /// </summary>
        public static List<Target> Targets(WorldState w, ContentCatalog c, int cellId, FactionId attacker, out bool guarded)
        {
            var list = new List<Target>();
            var cell = WorldLookup.GetCell(w, cellId);
            guarded = false;
            if (cell == null) return list;
            foreach (var b in w.buildings)
                if (b.cellId == cellId && WorldLookup.AreEnemies(w, attacker, b.owner) && IsAttackable(w, c, b)) list.Add(new Target { Fort = b });
            var defender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            if (defender != null && WorldLookup.IsCraftLive(defender) && defender.cellId == cellId && WorldLookup.AreEnemies(w, attacker, defender.owner))
                list.Add(new Target { Craft = defender });
            if (list.Count > 0) { guarded = true; return list; }
            var others = new List<CraftState>();
            foreach (var k in w.crafts)
                if (k.cellId == cellId && WorldLookup.AreEnemies(w, attacker, k.owner) && WorldLookup.IsCraftLive(k)) others.Add(k);
            others.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var k in others) list.Add(new Target { Craft = k });
            return list;
        }

        /// <summary>
        /// Whether a structure can be attacked in battle: a working Fort with HP left. A switched-off one (inactive, or
        /// its faction has conceded) defends nothing.
        /// </summary>
        public static bool IsAttackable(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = c != null && b != null ? CatalogRepository.GetBuilding(c, b.typeId) : null;
            return def != null && def.combatHealth > 0 && !b.underConstruction && b.inactiveUntilTick == -1 && b.damage < def.combatHealth
                && !WorldLookup.IsSuppressed(w, b.owner);
        }

        /// <summary>Whether a cell holds anything an attacker could strike: an enemy craft or an enemy Fort.</summary>
        public static bool HasTargets(WorldState w, ContentCatalog c, int cellId, FactionId attacker)
        {
            bool guarded;
            return Targets(w, c, cellId, attacker, out guarded).Count > 0;
        }

        /// <summary>
        /// Previews an attack on a cell. <paramref name="targetCraftId"/> or <paramref name="targetBuildingId"/> picks
        /// the target; with neither, the first that may be struck (the Fort, then the defender, then the lowest ID).
        /// </summary>
        public CombatPreview Preview(WorldState w, ContentCatalog c, MapDefinition map, int attackerCraftId, int targetCellId,
            int targetCraftId = -1, int targetBuildingId = -1)
        {
            var p = new CombatPreview { attackerCraftId = attackerCraftId, defenderCraftId = -1 };
            var attacker = WorldLookup.GetCraft(w, attackerCraftId);
            if (attacker == null || !WorldLookup.IsCraftLive(attacker)) return Illegal(p, "The attacking craft is unavailable.");
            if (attacker.owner == FactionId.Neutral) return Illegal(p, "Neutral craft cannot attack.");
            var cell = WorldLookup.GetCell(w, targetCellId);
            if (cell == null) return Illegal(p, "That cell does not exist.");
            bool guarded;
            var targets = Targets(w, c, cell.id, attacker.owner, out guarded);
            if (targets.Count == 0) return Illegal(p, "Nothing in that cell to attack.");
            var target = targets[0];
            if (targetCraftId >= 0 || targetBuildingId >= 0)
            {
                int found = targets.FindIndex(t => targetBuildingId >= 0 ? t.IsFort && t.Fort.id == targetBuildingId : !t.IsFort && t.Craft.id == targetCraftId);
                if (found < 0)
                    return Illegal(p, guarded ? "That ship is protected: attack the cell's Fort or its defender first." : "That target is not in the cell.");
                target = targets[found];
            }

            var aDef = CatalogRepository.GetCraft(c, attacker.typeId);
            if (aDef == null) return Illegal(p, "Unknown craft type.");
            // The target's side of the exchange: a craft's own stats, or a Fort's armour and return fire.
            int armour, returnFire, reach, health, maxHealth;
            bool assigned = false;
            if (target.IsFort)
            {
                var fDef = CatalogRepository.GetBuilding(c, target.Fort.typeId);
                p.defenderBuildingId = target.Fort.id;
                armour = fDef.combatArmour;
                returnFire = fDef.combatReturnFire;
                reach = Math.Max(1, fDef.combatReach);
                maxHealth = fDef.combatHealth;
                health = maxHealth - target.Fort.damage;
            }
            else
            {
                var dDef = CatalogRepository.GetCraft(c, target.Craft.typeId);
                if (dDef == null) return Illegal(p, "Unknown craft type.");
                p.defenderCraftId = target.Craft.id;
                assigned = cell.defenderCraftId == target.Craft.id;
                // Defender bonuses apply only to the cell's assigned defender.
                armour = dDef.armour + (assigned ? dDef.defenderArmourBonus : 0) + ShieldBonus(w, c, target.Craft);
                returnFire = dDef.defenceDamage > 0 && assigned ? dDef.defenceDamage + dDef.defenderCounterBonus : dDef.defenceDamage;
                reach = Math.Max(1, dDef.range);
                maxHealth = dDef.maxHealth;
                health = target.Craft.currentHealth;
            }
            p.defenderHealth = health;
            p.defenderMaxHealth = maxHealth;

            int distance = WorldLookup.CellDistance(map, attacker.cellId, cell.id);
            if (distance < 1) return Illegal(p, "The target must be in another cell.");

            // A craft reaches neighbouring cells, or further with range. Up close it uses its attack, from further
            // out its ranged attack (a Rail Lancer's point-blank shot is the weaker of the two).
            int attackReach = Math.Max(1, aDef.range);
            if (distance > attackReach) return Illegal(p, "Target is out of range (" + distance + " cells, range " + attackReach + ").");
            if (attacker.cooldownTurns > 0)
                return Illegal(p, aDef.displayName + " is recharging (" + attacker.cooldownTurns + (attacker.cooldownTurns == 1 ? " turn)." : " turns)."));
            int attackStat = distance > 1 ? aDef.rangedAttackDamage : aDef.attackDamage;
            p.attackStatName = distance > 1 ? "Ranged attack" : "Attack";
            if (attackStat <= 0) return Illegal(p, aDef.displayName + " has no " + p.attackStatName + " against this target.");
            attackStat = ScaleByHealth(c, attackStat, attacker.currentHealth, aDef.maxHealth);

            p.attackBase = attackStat;
            p.attackDamageAfterArmour = DamageAfterArmour(attackStat, armour);
            p.defenderSurvives = health - p.attackDamageAfterArmour > 0;

            // A target fires back only at an attacker within its own reach: nothing answers a Rail Lancer from two cells.
            int counterStat = distance > reach ? 0 : returnFire;
            p.counterStatName = "Return fire";
            counterStat = ScaleByHealth(c, counterStat, health - p.attackDamageAfterArmour, maxHealth);
            p.counterBase = counterStat;
            p.counterDamageAfterArmour = p.defenderSurvives ? DamageAfterArmour(counterStat, aDef.armour + ShieldBonus(w, c, attacker)) : 0;
            p.legal = true;
            return p;
        }

        static CombatPreview Illegal(CombatPreview p, string reason)
        {
            p.legal = false;
            p.reason = reason;
            return p;
        }

        /// <summary>
        /// Applies exactly the previewed damage. Fails if the preview is stale (either side changed HP, cell, or
        /// defender status since). Destroyed craft are removed and their defender slots cleared; a destroyed Fort is
        /// removed, and unless the attacker stands in the cell its owner must rebuild it before the cell turns neutral.
        /// </summary>
        public CommandResult Apply(WorldState w, ContentCatalog c, MapDefinition map, CombatPreview preview)
        {
            if (preview == null || !preview.legal) return WorldLookup.Fail("ILLEGAL_ATTACK", preview != null ? preview.reason : "No attack.");
            var attacker = WorldLookup.GetCraft(w, preview.attackerCraftId);
            var defender = WorldLookup.GetCraft(w, preview.defenderCraftId);
            var fort = preview.defenderBuildingId >= 0 ? WorldLookup.GetBuilding(w, preview.defenderBuildingId) : null;
            if (attacker == null || (defender == null && fort == null)) return WorldLookup.Fail("STALE_PREVIEW", "The combat situation changed.");
            int targetCell = fort != null ? fort.cellId : defender.cellId;
            var fresh = Preview(w, c, map, attacker.id, targetCell, preview.defenderCraftId, preview.defenderBuildingId);
            if (!fresh.legal || fresh.defenderCraftId != preview.defenderCraftId || fresh.defenderBuildingId != preview.defenderBuildingId
                || fresh.attackDamageAfterArmour != preview.attackDamageAfterArmour
                || fresh.counterDamageAfterArmour != preview.counterDamageAfterArmour)
                return WorldLookup.Fail("STALE_PREVIEW", "The combat situation changed; check the new preview.");

            var aDef = CatalogRepository.GetCraft(c, attacker.typeId);
            var cell = WorldLookup.GetCell(w, targetCell);
            var record = new CombatEvent
            {
                attackerType = attacker.typeId, defenderType = fort != null ? fort.typeId : defender.typeId,
                attackerOwner = attacker.owner, defenderOwner = fort != null ? fort.owner : defender.owner,
                attackerMaxHealth = aDef != null ? aDef.maxHealth : attacker.currentHealth,
                defenderMaxHealth = preview.defenderMaxHealth,
                attackerHealthBefore = attacker.currentHealth, defenderHealthBefore = preview.defenderHealth,
                defenderAssigned = fort != null || (cell != null && cell.defenderCraftId == defender.id),
                damage = preview.attackDamageAfterArmour,
            };

            string targetName = fort != null ? (Perspective.Spectating ? Perspective.SeatName(fort.owner) + "'s Fort" : fort.owner == Perspective.Viewer ? "your Fort" : "the enemy Fort") : Name(c, defender);
            int healthAfter = Math.Max(0, preview.defenderHealth - preview.attackDamageAfterArmour);
            if (fort != null) fort.damage += preview.attackDamageAfterArmour;
            else defender.currentHealth = healthAfter;
            string msg = Name(c, attacker) + " hits " + targetName + " for " + preview.attackDamageAfterArmour + " (" + preview.attackStatName + ").";
            if (healthAfter <= 0)
            {
                msg += " " + Capitalised(targetName) + " destroyed.";
                if (fort != null) DestroyFort(w, c, fort, attacker.owner);
                else RemoveCraft(w, defender);
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
            // A craft with a cooldown must recharge before its next attack.
            if (attacker.currentHealth > 0) attacker.cooldownTurns = ResearchService.CooldownTurns(c, WorldLookup.GetFaction(w, attacker.owner), aDef);
            record.defenderHealthAfter = healthAfter;
            record.attackerHealthAfter = Math.Max(0, attacker.currentHealth);
            record.counter = record.attackerHealthBefore - record.attackerHealthAfter;
            Events.Add(record);
            if (Events.Count > 32) Events.RemoveAt(0); // nothing is watching (tests, headless): keep it bounded
            return WorldLookup.Ok(msg);
        }

        static string Capitalised(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        /// <summary>
        /// Removes a destroyed Fort. With the attacker not standing in the cell, the owner keeps it for now but must
        /// deploy a new Fort within rules.fortRebuildTicks (see OwnershipService.TickUpkeep).
        /// </summary>
        static void DestroyFort(WorldState w, ContentCatalog c, BuildingState fort, FactionId attacker)
        {
            var cell = WorldLookup.GetCell(w, fort.cellId);
            OwnershipService.RemoveStructures(w, new HashSet<int> { fort.id }, new HashSet<int>());
            OwnershipService.RefreshFactoryOnline(w);
            if (cell == null) return;
            bool occupied = w.crafts.Exists(k => k.cellId == cell.id && k.owner == attacker && WorldLookup.IsCraftLive(k));
            if (occupied || cell.owner == attacker || cell.owner == FactionId.Neutral) return;
            cell.fortLostTick = w.tick;
            WorldLookup.AddAlert(w, AlertKind.Territory, "Your Fort in cell " + cell.id + " was destroyed. Deploy a new Fort there within "
                + (c.rules.fortRebuildTicks * c.rules.tickMilliseconds / 1000) + "s or the cell turns neutral.", cell.id, cell.owner);
        }

        static string Name(ContentCatalog c, CraftState craft)
        {
            var def = CatalogRepository.GetCraft(c, craft.typeId);
            string owner = Perspective.OwnerWord(craft.owner);
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
            var raider = WorldLookup.AiStateOf(w, craft.owner);
            if (raider != null && raider.raidCraftId == craft.id) raider.raidCraftId = -1;
        }
    }
}
