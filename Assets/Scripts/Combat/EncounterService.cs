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

        /// <summary>
        /// How far from the target cell a battle reaches: its neighbours. Craft with the range to hit the target from
        /// further out (a Rail Lancer two cells away) join too, with their cell. Speed never widens a battle.
        /// </summary>
        public int BattleRadius()
        {
            return 1;
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
            foreach (var craft in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(craft) || e.battleCellIds.Contains(craft.cellId)) continue;
                var def = CatalogRepository.GetCraft(c, craft.typeId);
                int d = WorldLookup.CellDistance(m, targetCellId, craft.cellId);
                if (def != null && d > radius && d <= def.range) e.battleCellIds.Add(craft.cellId);
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
            e.actionsRemaining = ReadyCount(w, e.turn);
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
            WorldLookup.AddAlert(w, attacker == FactionId.Ai ? AlertKind.Raid : AlertKind.Info, LastEvent, targetCellId);
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

        /// <summary>Every live ship a side has in the battle may act once a turn: move or attack.</summary>
        public static int ReadyCount(WorldState w, FactionId side)
        {
            int n = 0;
            foreach (int id in w.encounter.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == side && WorldLookup.IsCraftLive(craft)) n++;
            }
            return n;
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
            if (r.code == "NOT_PARTICIPANT") return Reinforce(w, cmd, craft);
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

        /// <summary>
        /// Brings one of the side's craft from outside the battle into a battle cell within its movement. Arriving
        /// is its action this turn; it fights from the next.
        /// </summary>
        CommandResult Reinforce(WorldState w, GameCommand cmd, CraftState craft)
        {
            var e = w.encounter;
            if (craft == null) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            if (!e.battleCellIds.Contains(cmd.targetCellId)) return WorldLookup.Fail("OUTSIDE_BATTLE", "Reinforcements must arrive in a cell of this battle.");
            if (CombatResolver.HasTargets(w, catalog, cmd.targetCellId, craft.owner))
                return WorldLookup.Fail("ILLEGAL_MOVE", "Reinforcements cannot arrive in a cell the enemy holds.");
            var def = CatalogRepository.GetCraft(catalog, craft.typeId);
            int distance = WorldLookup.CellDistance(map, craft.cellId, cmd.targetCellId);
            if (def == null || distance < 0 || distance > Math.Max(1, def.movement))
                return WorldLookup.Fail("TOO_FAR", DisplayName(craft) + " is too far away to join this turn (movement " + (def != null ? def.movement : 0) + ").");
            int from = craft.cellId;
            bool wasDefender = WorldLookup.ClearDefenderSlot(w, craft.id) != -1;
            craft.cellId = cmd.targetCellId;
            craft.worldPathCellIds.Clear();
            craft.worldPathIndex = 0;
            craft.worldDestinationCellId = -1;
            w.captureHolds.RemoveAll(h => h.craftId == craft.id);
            if (wasDefender) ReplaceDefender(w, from, craft.owner);
            var dest = WorldLookup.GetCell(w, cmd.targetCellId);
            if (dest.owner == craft.owner && dest.defenderCraftId == -1) dest.defenderCraftId = craft.id;
            e.participantCraftIds.Add(craft.id);
            e.participantCraftIds.Sort();
            if (!e.actedCraftIds.Contains(craft.id)) e.actedCraftIds.Add(craft.id); // arriving is its turn
            LastEvent = DisplayName(craft) + " joins the battle at cell " + cmd.targetCellId + "; it can act next turn.";
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
            // The chosen target: a craft (targetCraftId) or a Fort (buildingId); with neither, the first that may be struck.
            var preview = combat.Preview(w, catalog, map, craft.id, cmd.targetCellId, cmd.targetCraftId, cmd.buildingId);
            if (!preview.legal) return WorldLookup.Fail("ILLEGAL_ATTACK", preview.reason);

            var defender = WorldLookup.GetCraft(w, preview.defenderCraftId);
            var fort = WorldLookup.GetBuilding(w, preview.defenderBuildingId);
            int defenderCell = defender != null ? defender.cellId : fort.cellId;
            FactionId defenderOwner = defender != null ? defender.owner : fort.owner;
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
            if (defender != null && WorldLookup.GetCraft(w, preview.defenderCraftId) == null) ReplaceDefender(w, defenderCell, defenderOwner);
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
            r = capture.StartHold(w, cmd, catalog.rules, catalog);
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
            // The defender has replied: an attacker standing in a target cell with nothing left to fight takes it,
            // no separate hold order needed.
            if (w.mode != MatchMode.Ended && e.active && cmd.actor != e.attacker)
            {
                var target = WorldLookup.GetCell(w, e.targetCellId);
                bool standing = w.crafts.Exists(k => k.owner == e.attacker && k.cellId == e.targetCellId && WorldLookup.IsCraftLive(k));
                if (target != null && target.owner != e.attacker && standing && !CombatResolver.HasTargets(w, catalog, e.targetCellId, e.attacker))
                    ownership.TransferCell(w, catalog, e.targetCellId, e.attacker);
            }
            if (w.mode == MatchMode.Ended || !e.active) { FinishIfResolved(w); return WorldLookup.Ok(LastEvent); }
            if (FinishIfResolved(w)) return WorldLookup.Ok(LastEvent);
            e.turn = WorldLookup.Opponent(cmd.actor);
            if (e.turn == FactionId.Player) e.round++;
            // An attack that has not won within the round limit is spent: the attackers are fatigued and withdraw.
            if (catalog.rules.battleRoundLimit > 0 && e.round > catalog.rules.battleRoundLimit)
            {
                Fatigue(w);
                return WorldLookup.Ok(LastEvent);
            }
            // The side whose turn begins recharges: each cooldown counts down one of its turns.
            foreach (var craft in w.crafts)
                if (craft.owner == e.turn && craft.cooldownTurns > 0) craft.cooldownTurns--;
            e.actedCraftIds.Clear();
            e.actionsRemaining = ReadyCount(w, e.turn);
            LastEvent = (e.turn == FactionId.Player ? "Your turn" : "AI turn") + " (round " + e.round
                + (catalog.rules.battleRoundLimit > 0 ? " of " + catalog.rules.battleRoundLimit : "") + ").";
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult Retreat(WorldState w, GameCommand cmd)
        {
            CraftState none;
            var r = CheckActor(w, cmd, false, out none);
            if (!r.success) return r;
            var e = w.encounter;
            if (cmd.actor != e.attacker) return WorldLookup.Fail("NOT_ATTACKER", "Only the attacking side can retreat.");
            // Ships standing in enemy-held cells fly home with the rest.
            LastEvent = (cmd.actor == FactionId.Player ? "You withdraw" : "The AI withdraws") + " from the battle.";
            SendAttackersHome(w);
            return WorldLookup.Ok(LastEvent);
        }

        /// <summary>
        /// Ends a battle the attacker failed to win in time: its craft are fatigued and fly back to the nearest cell
        /// their side holds, leaving the defender in possession.
        /// </summary>
        void Fatigue(WorldState w)
        {
            var e = w.encounter;
            var attacker = e.attacker;
            LastEvent = (attacker == FactionId.Player ? "Your attack" : "The AI's attack") + " on cell " + e.targetCellId + " ran out of steam after "
                + catalog.rules.battleRoundLimit + " rounds: the attackers are fatigued and withdraw. Production resumes.";
            SendAttackersHome(w);
        }

        /// <summary>Ends the battle with the attacker's craft in cells it does not hold flying to the nearest cell it does.</summary>
        void SendAttackersHome(WorldState w)
        {
            var e = w.encounter;
            var attacker = e.attacker;
            var tired = new List<CraftState>();
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft == null || craft.owner != attacker || !WorldLookup.IsCraftLive(craft)) continue;
                var here = WorldLookup.GetCell(w, craft.cellId);
                if (here == null || here.owner != attacker) tired.Add(craft);
            }
            End(w);
            foreach (var craft in tired)
            {
                List<int> best = null;
                foreach (var cell in w.cells)
                {
                    if (cell.owner != attacker) continue;
                    var path = movement.FindWorldPath(w, map, attacker, craft.cellId, cell.id);
                    if (path != null && (best == null || path.Count < best.Count)) best = path;
                }
                if (best == null) continue;
                WorldLookup.ClearDefenderSlot(w, craft.id);
                craft.worldPathCellIds = best;
                craft.worldPathIndex = 0;
                craft.worldDestinationCellId = -1;
                craft.worldTicksUntilNextCell = catalog.rules.worldMoveTicksPerCell;
                w.captureHolds.RemoveAll(h => h.craftId == craft.id);
            }
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
            foreach (var craft in w.crafts) craft.cooldownTurns = 0; // every weapon is ready for the next battle
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
