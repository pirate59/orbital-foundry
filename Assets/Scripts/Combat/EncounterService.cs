using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Turn-based tactical encounters on the shared map. While active, MatchMode is Tactical and the factory clock is
    /// paused. Each side gets an action pool per turn; each craft acts at most once per turn.
    /// </summary>
    public sealed class EncounterService
    {
        readonly ContentCatalog catalog;
        readonly MapDefinition map;
        readonly CombatResolver combat;
        readonly MovementRules movement;
        readonly CaptureService capture;
        readonly OwnershipService ownership;

        /// <summary>Message describing the most recent tactical event, for the HUD.</summary>
        public string LastEvent = "";

        public EncounterService(ContentCatalog catalog, MapDefinition map, CombatResolver combat, MovementRules movement,
            CaptureService capture, OwnershipService ownership)
        {
            this.catalog = catalog;
            this.map = map;
            this.combat = combat;
            this.movement = movement;
            this.capture = capture;
            this.ownership = ownership;
        }

        public int BattleRadius()
        {
            int r = 1;
            foreach (var k in catalog.crafts) r = Math.Max(r, Math.Max(k.movement, k.range));
            return r;
        }

        public CommandResult Start(WorldState w, ContentCatalog c, MapDefinition m, int targetCellId, FactionId attacker)
        {
            if (w.mode != MatchMode.Factory) return WorldLookup.Fail("WRONG_MODE", "A battle can only start during normal play.");
            if (w.encounter.active) return WorldLookup.Fail("ENCOUNTER_ACTIVE", "A battle is already in progress.");
            var target = WorldLookup.GetCell(w, targetCellId);
            if (target == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (attacker == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Neutral cannot attack.");
            if (target.owner == attacker) return WorldLookup.Fail("OWN_CELL", "You cannot attack your own cell.");

            int radius = BattleRadius();
            var e = w.encounter;
            e.battleCellIds.Clear();
            e.participantCraftIds.Clear();
            e.actedCraftIds.Clear();
            foreach (var cell in m.cells)
            {
                int d = WorldLookup.CellDistance(m, targetCellId, cell.id);
                if (d >= 0 && d <= radius) e.battleCellIds.Add(cell.id);
            }
            e.battleCellIds.Sort();
            bool attackerPresent = false;
            foreach (var craft in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(craft) || !e.battleCellIds.Contains(craft.cellId)) continue;
                e.participantCraftIds.Add(craft.id);
                if (craft.owner == attacker) attackerPresent = true;
            }
            e.participantCraftIds.Sort();
            if (!attackerPresent)
            {
                e.battleCellIds.Clear();
                e.participantCraftIds.Clear();
                return WorldLookup.Fail("NO_ATTACKERS", "No attacking craft are close enough to start a battle.");
            }
            e.active = true;
            e.targetCellId = targetCellId;
            e.attacker = attacker;
            e.turn = FactionId.Player;
            e.round = 1;
            e.actionsRemaining = c.rules.tacticalActionsPerSide;
            e.pendingDefenderCellId = -1;
            e.pendingDefenderOwner = FactionId.Neutral;
            w.mode = MatchMode.Tactical;
            // Participants stop world travel; it resumes as ordinary orders after the battle.
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                craft.worldPathCellIds.Clear();
                craft.worldPathIndex = 0;
                craft.worldDestinationCellId = -1;
                WorldLookup.AutoAssignDefender(w, craft.cellId);
            }
            LastEvent = (attacker == FactionId.Player ? "You attack" : "The AI attacks") + " cell " + targetCellId + ". Production is paused.";
            WorldLookup.AddAlert(w, AlertKind.Raid, LastEvent, targetCellId);
            return WorldLookup.Ok(LastEvent);
        }

        CommandResult CheckActor(WorldState w, GameCommand cmd, bool needsCraft, out CraftState craft)
        {
            craft = null;
            var e = w.encounter;
            if (!e.active || w.mode != MatchMode.Tactical) return WorldLookup.Fail("NO_ENCOUNTER", "There is no battle in progress.");
            if (cmd.actor != e.turn) return WorldLookup.Fail("OUT_OF_TURN", "It is not your turn.");
            if (e.pendingDefenderCellId != -1 && e.pendingDefenderOwner == cmd.actor && cmd.kind != CommandKind.AssignDefender)
                return WorldLookup.Fail("PENDING_DEFENDER", "Choose a replacement defender first.");
            if (!needsCraft) return WorldLookup.Ok();
            craft = WorldLookup.GetCraft(w, cmd.craftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            if (craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            if (!e.participantCraftIds.Contains(craft.id)) return WorldLookup.Fail("NOT_PARTICIPANT", "That craft is not part of this battle.");
            return WorldLookup.Ok();
        }

        CommandResult CheckAction(WorldState w, CraftState craft)
        {
            if (w.encounter.actionsRemaining <= 0) return WorldLookup.Fail("NO_ACTIONS", "No actions remain this turn. End your turn.");
            if (w.encounter.actedCraftIds.Contains(craft.id)) return WorldLookup.Fail("ALREADY_ACTED", "That craft has already acted this turn.");
            return WorldLookup.Ok();
        }

        void SpendAction(WorldState w, CraftState craft)
        {
            w.encounter.actionsRemaining--;
            if (!w.encounter.actedCraftIds.Contains(craft.id)) w.encounter.actedCraftIds.Add(craft.id);
        }

        public CommandResult ApplyMove(WorldState w, GameCommand cmd)
        {
            CraftState craft;
            var r = CheckActor(w, cmd, true, out craft);
            if (!r.success) return r;
            r = CheckAction(w, craft);
            if (!r.success) return r;
            var path = movement.FindTacticalPath(w, map, catalog, craft.id, cmd.targetCellId);
            if (path == null || path.Count == 0) return WorldLookup.Fail("ILLEGAL_MOVE", "That cell cannot be reached this action.");

            int from = craft.cellId;
            bool wasDefender = WorldLookup.ClearDefenderSlot(w, craft.id) != -1;
            craft.cellId = cmd.targetCellId;
            if (wasDefender) ReplaceDefender(w, from, craft.owner);
            var dest = WorldLookup.GetCell(w, cmd.targetCellId);
            if (dest.owner == craft.owner && dest.defenderCraftId == -1) dest.defenderCraftId = craft.id;
            SpendAction(w, craft);
            capture.CancelContestedHolds(w);
            LastEvent = DisplayName(craft) + " moves to cell " + cmd.targetCellId + ".";
            FinishIfResolved(w);
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult ApplyAttack(WorldState w, GameCommand cmd)
        {
            CraftState craft;
            var r = CheckActor(w, cmd, true, out craft);
            if (!r.success) return r;
            r = CheckAction(w, craft);
            if (!r.success) return r;
            if (!w.encounter.battleCellIds.Contains(cmd.targetCellId)) return WorldLookup.Fail("OUTSIDE_BATTLE", "That cell is outside this battle.");
            var preview = combat.Preview(w, catalog, map, craft.id, cmd.targetCellId);
            if (!preview.legal) return WorldLookup.Fail("ILLEGAL_ATTACK", preview.reason);
            if (cmd.targetCraftId != -1 && cmd.targetCraftId != preview.defenderCraftId)
                return WorldLookup.Fail("STALE_TARGET", "The defender in that cell has changed.");

            var defender = WorldLookup.GetCraft(w, preview.defenderCraftId);
            int defenderCell = defender.cellId;
            FactionId defenderOwner = defender.owner;
            int attackerCell = craft.cellId;
            FactionId attackerOwner = craft.owner;

            SpendAction(w, craft);
            var result = combat.Apply(w, catalog, map, preview);
            if (!result.success)
            {
                // Stale preview: refund the action since nothing changed.
                w.encounter.actionsRemaining++;
                w.encounter.actedCraftIds.Remove(craft.id);
                return result;
            }
            if (WorldLookup.GetCraft(w, preview.defenderCraftId) == null) ReplaceDefender(w, defenderCell, defenderOwner);
            if (WorldLookup.GetCraft(w, craft.id) == null)
            {
                var cell = WorldLookup.GetCell(w, attackerCell);
                if (cell != null && cell.defenderCraftId == -1) ReplaceDefender(w, attackerCell, attackerOwner);
            }
            capture.CancelContestedHolds(w);
            LastEvent = result.message;
            FinishIfResolved(w);
            return result;
        }

        /// <summary>
        /// Assigns a defender. Free when answering a pending replacement choice; otherwise spends an action in battle.
        /// Outside battle it is a free manual reassignment.
        /// </summary>
        public CommandResult AssignDefender(WorldState w, GameCommand cmd)
        {
            var craft = WorldLookup.GetCraft(w, cmd.craftId);
            var cell = WorldLookup.GetCell(w, cmd.targetCellId);
            if (craft == null || cell == null) return WorldLookup.Fail("MALFORMED", "Choose a craft and its cell.");
            if (!WorldLookup.IsCraftLive(craft) || craft.owner != cmd.actor) return WorldLookup.Fail("WRONG_OWNER", "That craft is not yours.");
            if (craft.cellId != cell.id) return WorldLookup.Fail("NOT_IN_CELL", "The craft must be in that cell to defend it.");
            if (cell.owner != cmd.actor) return WorldLookup.Fail("NOT_OWNER", "You can only defend cells you own.");
            if (craft.worldPathCellIds.Count > 0) return WorldLookup.Fail("TRAVELLING", "The craft is still travelling.");
            if (cell.defenderCraftId == craft.id) return WorldLookup.Fail("ALREADY_DEFENDER", "That craft is already the defender.");

            if (w.mode != MatchMode.Tactical)
            {
                cell.defenderCraftId = craft.id;
                return WorldLookup.Ok("Defender assigned.");
            }
            var e = w.encounter;
            if (e.pendingDefenderCellId == cell.id && e.pendingDefenderOwner == cmd.actor)
            {
                cell.defenderCraftId = craft.id;
                e.pendingDefenderCellId = -1;
                e.pendingDefenderOwner = FactionId.Neutral;
                LastEvent = DisplayName(craft) + " takes over as defender.";
                return WorldLookup.Ok(LastEvent);
            }
            CraftState actor;
            var r = CheckActor(w, cmd, true, out actor);
            if (!r.success) return r;
            r = CheckAction(w, craft);
            if (!r.success) return r;
            cell.defenderCraftId = craft.id;
            SpendAction(w, craft);
            LastEvent = DisplayName(craft) + " is now the defender of cell " + cell.id + ".";
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult StartCapture(WorldState w, GameCommand cmd)
        {
            CraftState craft;
            var r = CheckActor(w, cmd, true, out craft);
            if (!r.success) return r;
            r = CheckAction(w, craft);
            if (!r.success) return r;
            r = capture.StartHold(w, cmd, catalog.rules);
            if (!r.success) return r;
            SpendAction(w, craft);
            LastEvent = r.message;
            return r;
        }

        public CommandResult EndTurn(WorldState w, GameCommand cmd)
        {
            CraftState none;
            var r = CheckActor(w, cmd, false, out none);
            if (!r.success) return r;
            var e = w.encounter;
            capture.ResolveAfterReply(w, cmd.actor, ownership, catalog);
            if (w.mode == MatchMode.Ended || !e.active) { FinishIfResolved(w); return WorldLookup.Ok(LastEvent); }
            if (FinishIfResolved(w)) return WorldLookup.Ok(LastEvent);
            e.turn = WorldLookup.Opponent(cmd.actor);
            if (e.turn == FactionId.Player) e.round++;
            e.actionsRemaining = catalog.rules.tacticalActionsPerSide;
            e.actedCraftIds.Clear();
            LastEvent = (e.turn == FactionId.Player ? "Your turn" : "AI turn") + " (round " + e.round + ").";
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult Retreat(WorldState w, GameCommand cmd)
        {
            CraftState none;
            var r = CheckActor(w, cmd, false, out none);
            if (!r.success) return r;
            var e = w.encounter;
            if (cmd.actor != e.attacker) return WorldLookup.Fail("NOT_ATTACKER", "Only the attacking side can retreat.");
            foreach (var craft in w.crafts)
                if (craft.owner == e.attacker && craft.cellId == e.targetCellId && WorldLookup.IsCraftLive(craft))
                    return WorldLookup.Fail("IN_TARGET", "You cannot retreat while you hold craft inside the contested cell.");
            LastEvent = (cmd.actor == FactionId.Player ? "You withdraw" : "The AI withdraws") + " from the battle.";
            End(w);
            return WorldLookup.Ok(LastEvent);
        }

        /// <summary>Ends the encounter when the target changed hands or the attacking force is gone. Returns true if it ended.</summary>
        public bool FinishIfResolved(WorldState w)
        {
            var e = w.encounter;
            if (!e.active) return true;
            if (w.mode == MatchMode.Ended) { e.active = false; return true; }
            var target = WorldLookup.GetCell(w, e.targetCellId);
            bool attackerAlive = false;
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == e.attacker && WorldLookup.IsCraftLive(craft)) { attackerAlive = true; break; }
            }
            if (target != null && target.owner == e.attacker)
            {
                LastEvent = (e.attacker == FactionId.Player ? "You captured" : "The AI captured") + " cell " + e.targetCellId + ". Production resumes.";
                End(w);
                return true;
            }
            if (!attackerAlive)
            {
                LastEvent = (e.attacker == FactionId.Player ? "Your attack was repelled." : "The raid was repelled!") + " Production resumes.";
                End(w);
                return true;
            }
            if (WorldLookup.IsSuppressed(w, FactionId.Ai))
            {
                End(w);
                return true;
            }
            return false;
        }

        void End(WorldState w)
        {
            var e = w.encounter;
            e.active = false;
            e.pendingDefenderCellId = -1;
            e.pendingDefenderOwner = FactionId.Neutral;
            e.actionsRemaining = 0;
            e.actedCraftIds.Clear();
            e.participantCraftIds.Clear();
            e.battleCellIds.Clear();
            e.turn = FactionId.Player;
            w.captureHolds.RemoveAll(h => h.tactical);
            if (w.mode == MatchMode.Tactical) w.mode = MatchMode.Factory;
            foreach (var cell in w.cells) WorldLookup.AutoAssignDefender(w, cell.id);
            WorldLookup.AddAlert(w, AlertKind.Info, LastEvent, e.targetCellId);
        }

        /// <summary>
        /// Picks a replacement defender from surviving reserves. In battle, the player chooses when several reserves remain;
        /// otherwise the lowest craft ID is assigned for free.
        /// </summary>
        public void ReplaceDefender(WorldState w, int cellId, FactionId owner)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null || cell.owner != owner || cell.defenderCraftId != -1) return;
            var reserves = WorldLookup.CraftsInCell(w, cellId, owner);
            if (reserves.Count == 0) return;
            var e = w.encounter;
            if (w.mode == MatchMode.Tactical && e.active && owner == FactionId.Player && reserves.Count > 1 && e.pendingDefenderCellId == -1)
            {
                e.pendingDefenderCellId = cellId;
                e.pendingDefenderOwner = owner;
                return;
            }
            cell.defenderCraftId = reserves[0].id;
        }

        string DisplayName(CraftState craft)
        {
            var def = CatalogRepository.GetCraft(catalog, craft.typeId);
            return (craft.owner == FactionId.Player ? "Your " : "Enemy ") + (def != null ? def.displayName : craft.typeId);
        }

        public List<CraftState> Participants(WorldState w, FactionId owner)
        {
            var list = new List<CraftState>();
            foreach (int id in w.encounter.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == owner && WorldLookup.IsCraftLive(craft)) list.Add(craft);
            }
            return list;
        }
    }
}
