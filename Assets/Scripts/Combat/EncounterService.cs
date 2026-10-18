using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Turn-based battles on the shared map, fought while the rest of the world plays on. Several can run at once, each
    /// over its own cells. In each, the seats with ships there take turns; every ship acts once a turn. The cell under
    /// attack is under siege: its production stops until the battle ends. A turn runs on a timer: one that runs out is
    /// ended for its seat and counts a strike, and strikes shorten that seat's next turn until it acts in time again.
    /// </summary>
    public sealed class EncounterService
    {
        readonly ContentCatalog catalog;
        readonly MapDefinition map;
        readonly CombatResolver combat;
        readonly MovementRules movement;
        readonly CaptureService capture;
        readonly OwnershipService ownership;

        /// <summary>Message describing the most recent battle event, for the HUD, worded for <see cref="Perspective.Viewer"/>.</summary>
        public string LastEvent = "";

        /// <summary>How a battle can end, for whoever keeps score (the arena).</summary>
        public enum BattleResult { None, Captured, Repelled, Withdrew, Fatigued, SideOut }

        /// <summary>How the most recent battle to end ended.</summary>
        public BattleResult LastResult = BattleResult.None;

        /// <summary>The battle the last successful <see cref="Start"/> began (tests and tools).</summary>
        public EncounterState LastStarted;

        /// <summary>Raised as each battle begins. Observers must only read.</summary>
        public event Action<EncounterState> BattleStarted;

        /// <summary>
        /// Raised as each battle ends in play, with how it ended (not for one cut short because the match was decided or
        /// a seat left it). Observers must only read.
        /// </summary>
        public event Action<EncounterState, BattleResult> BattleEnded;

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
            var target = WorldLookup.GetCell(w, targetCellId);
            if (target == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (attacker == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Neutral cannot attack.");
            if (target.owner == attacker) return WorldLookup.Fail("OWN_CELL", "You cannot attack your own cell.");
            if (WorldLookup.AreAllies(w, attacker, target.owner)) return WorldLookup.Fail("ALLIED_CELL", "That cell belongs to an ally.");
            w.encounters.RemoveAll(x => !x.active);
            if (WorldLookup.BattleAt(w, targetCellId) != null) return WorldLookup.Fail("BATTLE_HERE", "A battle is already being fought there.");

            // The battle's cells: the target and its neighbours, and the cells of ships with the range to strike it;
            // never a cell another battle is already fought over. Its ships: those in its cells not in another battle.
            int radius = BattleRadius();
            var e = new EncounterState { id = WorldLookup.AllocateId(w) };
            foreach (var cell in m.cells)
            {
                int d = WorldLookup.CellDistance(m, targetCellId, cell.id);
                if (d >= 0 && d <= radius && WorldLookup.BattleAt(w, cell.id) == null) e.battleCellIds.Add(cell.id);
            }
            foreach (var craft in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(craft) || e.battleCellIds.Contains(craft.cellId) || WorldLookup.BattleAt(w, craft.cellId) != null) continue;
                var def = CatalogRepository.GetCraft(c, craft.typeId);
                int d = WorldLookup.CellDistance(m, targetCellId, craft.cellId);
                if (def != null && d > radius && d <= def.range) e.battleCellIds.Add(craft.cellId);
            }
            e.battleCellIds.Sort();
            bool attackerPresent = false;
            foreach (var craft in w.crafts)
            {
                if (!WorldLookup.IsCraftLive(craft) || !e.battleCellIds.Contains(craft.cellId) || WorldLookup.InBattle(w, craft.id)) continue;
                e.participantCraftIds.Add(craft.id);
                if (craft.owner == attacker) attackerPresent = true;
            }
            e.participantCraftIds.Sort();
            if (!attackerPresent) return WorldLookup.Fail("NO_ATTACKERS", "No attacking craft are close enough to start a battle.");
            e.active = true;
            e.targetCellId = targetCellId;
            e.attacker = attacker;
            // Every seat with ships in the battle takes turns, in seat order, and so does the target's owner.
            e.sides.Add(attacker);
            if (target.owner != FactionId.Neutral) e.sides.Add(target.owner);
            foreach (int id in e.participantCraftIds)
            {
                var owner = WorldLookup.GetCraft(w, id).owner;
                if (!e.sides.Contains(owner)) e.sides.Add(owner);
            }
            e.sides.Sort();
            foreach (var s in e.sides) e.strikes.Add(0);
            e.turn = e.sides[0];
            e.round = 1;
            e.actionsRemaining = ReadyCount(w, e, e.turn);
            e.pendingDefenderCellId = -1;
            e.pendingDefenderOwner = FactionId.Neutral;
            StartTurnClock(w, e);
            w.encounters.Add(e);
            LastStarted = e;
            if (BattleStarted != null) BattleStarted(e);
            // Participants stop world travel; it resumes as ordinary orders after the battle.
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                craft.worldPathCellIds.Clear();
                craft.worldPathIndex = 0;
                craft.worldDestinationCellId = -1;
                WorldLookup.AutoAssignDefender(w, craft.cellId);
            }
            OwnershipService.RefreshFactoryOnline(w); // the target is under siege
            LastEvent = Perspective.Spectating ? Perspective.SeatName(attacker) + " attacks cell " + targetCellId + ". Its production stops while the battle lasts."
                : Attacks(w, Perspective.Viewer, attacker, targetCellId);
            // Everyone hears of it; the seats under attack loudly.
            foreach (var f in w.factions)
                WorldLookup.AddAlert(w, f.id != attacker && WorldLookup.AreEnemies(w, f.id, attacker) && e.sides.Contains(f.id) ? AlertKind.Raid : AlertKind.Info,
                    Attacks(w, f.id, attacker, targetCellId), targetCellId, f.id);
            return WorldLookup.Ok(LastEvent);
        }

        static string Attacks(WorldState w, FactionId viewer, FactionId attacker, int cellId)
        {
            return WorldLookup.WhoStart(w, viewer, attacker) + (attacker == viewer ? " attack" : " attacks") + " cell " + cellId
                + ". Its production stops while the battle lasts.";
        }

        // ---------------------------------------------------------------- the turn timer

        /// <summary>How many ticks a seat's turn may last: full, or shorter after it ran out of time.</summary>
        public int TurnLimitFor(EncounterState e, FactionId side)
        {
            var r = catalog.rules;
            int i = e.sides.IndexOf(side);
            int strikes = i >= 0 && i < e.strikes.Count ? e.strikes[i] : 0;
            return Math.Max(1, strikes == 0 ? r.battleTurnTicks : strikes == 1 ? r.battleShortTurnTicks : r.battleShortestTurnTicks);
        }

        void StartTurnClock(WorldState w, EncounterState e)
        {
            e.turnStartedTick = w.tick;
            e.turnLimitTicks = TurnLimitFor(e, e.turn);
        }

        /// <summary>Ticks left in the current turn of a battle.</summary>
        public static int TicksLeft(WorldState w, EncounterState e)
        {
            return Math.Max(0, e.turnStartedTick + e.turnLimitTicks - w.tick);
        }

        /// <summary>
        /// Called once a tick: ends every turn whose time has run out, as if its seat had ended it, with a strike against
        /// that seat. A defender choice left waiting goes to the lowest-numbered ship. Battles that have ended are removed.
        /// </summary>
        public void TickBattles(WorldState w)
        {
            for (int i = 0; i < w.encounters.Count; i++)
            {
                var e = w.encounters[i];
                if (!e.active || w.mode == MatchMode.Ended) continue;
                if (w.tick - e.turnStartedTick < e.turnLimitTicks) continue;
                var who = e.turn;
                if (e.pendingDefenderCellId != -1)
                {
                    var cell = WorldLookup.GetCell(w, e.pendingDefenderCellId);
                    var reserves = WorldLookup.CraftsInCell(w, e.pendingDefenderCellId, e.pendingDefenderOwner);
                    if (cell != null && reserves.Count > 0 && cell.defenderCraftId == -1) cell.defenderCraftId = reserves[0].id;
                    e.pendingDefenderCellId = -1;
                    e.pendingDefenderOwner = FactionId.Neutral;
                }
                PassTurn(w, e, who, true);
                if (e.active)
                    LastEvent = (who == Perspective.Viewer && !Perspective.Spectating ? "You ran out of time" : Perspective.WhoStart(w, who) + " ran out of time")
                        + " in the battle for cell " + e.targetCellId + ". " + LastEvent;
            }
            w.encounters.RemoveAll(x => !x.active);
        }

        // ---------------------------------------------------------------- orders

        /// <summary>
        /// The battle an order is for: the one it names, else the one its ship fights in, else where it is the actor's
        /// turn, else any battle the actor is in (oldest first).
        /// </summary>
        public static EncounterState BattleFor(WorldState w, GameCommand cmd)
        {
            if (cmd.battleId >= 0) return WorldLookup.GetBattle(w, cmd.battleId);
            if (cmd.craftId >= 0)
            {
                var mine = WorldLookup.BattleOf(w, cmd.craftId);
                if (mine != null) return mine;
            }
            // A ship joining a battle from outside names a cell of it.
            if (cmd.targetCellId >= 0)
                foreach (var e in w.encounters)
                    if (e.active && e.sides.Contains(cmd.actor) && e.battleCellIds.Contains(cmd.targetCellId)) return e;
            foreach (var e in w.encounters) if (e.active && e.turn == cmd.actor) return e;
            foreach (var e in w.encounters) if (e.active && e.sides.Contains(cmd.actor)) return e;
            return null;
        }

        CommandResult CheckActor(WorldState w, EncounterState e, GameCommand cmd, bool needsCraft, out CraftState craft)
        {
            craft = null;
            if (e == null || !e.active) return WorldLookup.Fail("NO_ENCOUNTER", "There is no battle in progress.");
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

        static CommandResult CheckAction(EncounterState e, CraftState craft)
        {
            if (e.actionsRemaining <= 0) return WorldLookup.Fail("NO_ACTIONS", "No actions remain this turn. End your turn.");
            if (e.actedCraftIds.Contains(craft.id)) return WorldLookup.Fail("ALREADY_ACTED", "That craft has already acted this turn.");
            return WorldLookup.Ok();
        }

        /// <summary>Every live ship a side has in the battle may act once a turn: move or attack.</summary>
        public static int ReadyCount(WorldState w, EncounterState e, FactionId side)
        {
            int n = 0;
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == side && WorldLookup.IsCraftLive(craft)) n++;
            }
            return n;
        }

        static void SpendAction(EncounterState e, CraftState craft)
        {
            e.actionsRemaining--;
            if (!e.actedCraftIds.Contains(craft.id)) e.actedCraftIds.Add(craft.id);
        }

        public CommandResult ApplyMove(WorldState w, GameCommand cmd)
        {
            var e = BattleFor(w, cmd);
            CraftState craft;
            var r = CheckActor(w, e, cmd, true, out craft);
            if (r.code == "NOT_PARTICIPANT") return Reinforce(w, e, cmd, craft);
            if (!r.success) return r;
            r = CheckAction(e, craft);
            if (!r.success) return r;
            var path = movement.FindTacticalPath(w, map, catalog, craft.id, cmd.targetCellId);
            if (path == null || path.Count == 0) return WorldLookup.Fail("ILLEGAL_MOVE", "That cell cannot be reached this action.");

            int from = craft.cellId;
            bool wasDefender = WorldLookup.ClearDefenderSlot(w, craft.id) != -1;
            craft.cellId = cmd.targetCellId;
            if (wasDefender) ReplaceDefender(w, from, craft.owner);
            var dest = WorldLookup.GetCell(w, cmd.targetCellId);
            if (dest.owner == craft.owner && dest.defenderCraftId == -1) dest.defenderCraftId = craft.id;
            SpendAction(e, craft);
            capture.CancelContestedHolds(w);
            LastEvent = DisplayName(craft) + " moves to cell " + cmd.targetCellId + ".";
            FinishIfResolved(w, e);
            return WorldLookup.Ok(LastEvent);
        }

        /// <summary>
        /// Brings one of the side's craft from outside the battle into a battle cell within its movement. Arriving
        /// is its action this turn; it fights from the next.
        /// </summary>
        CommandResult Reinforce(WorldState w, EncounterState e, GameCommand cmd, CraftState craft)
        {
            if (craft == null) return WorldLookup.Fail("CRAFT_UNAVAILABLE", "That craft is unavailable.");
            if (WorldLookup.InBattle(w, craft.id)) return WorldLookup.Fail("IN_BATTLE", DisplayName(craft) + " is fighting another battle.");
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
            var e = BattleFor(w, cmd);
            CraftState craft;
            var r = CheckActor(w, e, cmd, true, out craft);
            if (!r.success) return r;
            r = CheckAction(e, craft);
            if (!r.success) return r;
            if (!e.battleCellIds.Contains(cmd.targetCellId)) return WorldLookup.Fail("OUTSIDE_BATTLE", "That cell is outside this battle.");
            // The chosen target: a craft (targetCraftId) or a Fort (buildingId); with neither, the first that may be struck.
            var preview = combat.Preview(w, catalog, map, craft.id, cmd.targetCellId, cmd.targetCraftId, cmd.buildingId);
            if (!preview.legal) return WorldLookup.Fail("ILLEGAL_ATTACK", preview.reason);

            var defender = WorldLookup.GetCraft(w, preview.defenderCraftId);
            var fort = WorldLookup.GetBuilding(w, preview.defenderBuildingId);
            int defenderCell = defender != null ? defender.cellId : fort.cellId;
            FactionId defenderOwner = defender != null ? defender.owner : fort.owner;
            int attackerCell = craft.cellId;
            FactionId attackerOwner = craft.owner;

            SpendAction(e, craft);
            var result = combat.Apply(w, catalog, map, preview);
            if (!result.success)
            {
                // Stale preview: refund the action since nothing changed.
                e.actionsRemaining++;
                e.actedCraftIds.Remove(craft.id);
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
            FinishIfResolved(w, e);
            return result;
        }

        /// <summary>
        /// Assigns a defender. Free when answering a pending replacement choice, or for a cell no battle is fought over;
        /// otherwise it spends an action in that battle.
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

            var e = WorldLookup.BattleAt(w, cell.id);
            if (e == null)
            {
                cell.defenderCraftId = craft.id;
                return WorldLookup.Ok("Defender assigned.");
            }
            if (e.pendingDefenderCellId == cell.id && e.pendingDefenderOwner == cmd.actor)
            {
                cell.defenderCraftId = craft.id;
                e.pendingDefenderCellId = -1;
                e.pendingDefenderOwner = FactionId.Neutral;
                LastEvent = DisplayName(craft) + " takes over as defender.";
                return WorldLookup.Ok(LastEvent);
            }
            CraftState actor;
            var r = CheckActor(w, e, cmd, true, out actor);
            if (!r.success) return r;
            r = CheckAction(e, craft);
            if (!r.success) return r;
            cell.defenderCraftId = craft.id;
            SpendAction(e, craft);
            LastEvent = DisplayName(craft) + " is now the defender of cell " + cell.id + ".";
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult StartCapture(WorldState w, GameCommand cmd)
        {
            var e = BattleFor(w, cmd);
            CraftState craft;
            var r = CheckActor(w, e, cmd, true, out craft);
            if (!r.success) return r;
            r = CheckAction(e, craft);
            if (!r.success) return r;
            r = capture.StartHold(w, cmd, catalog.rules, catalog);
            if (!r.success) return r;
            SpendAction(e, craft);
            LastEvent = r.message;
            return r;
        }

        public CommandResult EndTurn(WorldState w, GameCommand cmd)
        {
            var e = BattleFor(w, cmd);
            CraftState none;
            var r = CheckActor(w, e, cmd, false, out none);
            if (!r.success) return r;
            return PassTurn(w, e, cmd.actor, false);
        }

        /// <summary>
        /// Ends <paramref name="actor"/>'s turn in a battle and passes it to the next seat. Ending it in time clears the
        /// seat's strikes; running out of time adds one.
        /// </summary>
        CommandResult PassTurn(WorldState w, EncounterState e, FactionId actor, bool timedOut)
        {
            int at = e.sides.IndexOf(actor);
            if (at >= 0 && at < e.strikes.Count) e.strikes[at] = timedOut ? e.strikes[at] + 1 : 0;
            capture.ResolveAfterReply(w, e, actor, ownership, catalog);
            // A defender has replied: an attacker standing in a target cell with nothing left to fight takes it,
            // no separate hold order needed.
            if (w.mode != MatchMode.Ended && e.active && WorldLookup.AreEnemies(w, actor, e.attacker))
            {
                var target = WorldLookup.GetCell(w, e.targetCellId);
                bool standing = w.crafts.Exists(k => k.owner == e.attacker && k.cellId == e.targetCellId && WorldLookup.IsCraftLive(k));
                if (target != null && target.owner != e.attacker && standing && !CombatResolver.HasTargets(w, catalog, e.targetCellId, e.attacker))
                    ownership.TransferCell(w, catalog, e.targetCellId, e.attacker);
            }
            if (w.mode == MatchMode.Ended || !e.active) { FinishIfResolved(w, e); return WorldLookup.Ok(LastEvent); }
            if (FinishIfResolved(w, e)) return WorldLookup.Ok(LastEvent);
            // The next seat in turn order; back at the first, a new round begins.
            int next = (at + 1) % e.sides.Count;
            e.turn = e.sides[next];
            if (next == 0) e.round++;
            // An attack that has not won within the round limit is spent: the attackers are fatigued and withdraw.
            if (catalog.rules.battleRoundLimit > 0 && e.round > catalog.rules.battleRoundLimit)
            {
                Fatigue(w, e);
                return WorldLookup.Ok(LastEvent);
            }
            // The side whose turn begins recharges: each cooldown counts down one of its turns.
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == e.turn && craft.cooldownTurns > 0) craft.cooldownTurns--;
            }
            e.actedCraftIds.Clear();
            e.actionsRemaining = ReadyCount(w, e, e.turn);
            StartTurnClock(w, e);
            LastEvent = (e.turn == Perspective.Viewer && !Perspective.Spectating ? "Your turn" : Perspective.WhoStart(w, e.turn) + "'s turn")
                + " in the battle for cell " + e.targetCellId + " (round " + e.round
                + (catalog.rules.battleRoundLimit > 0 ? " of " + catalog.rules.battleRoundLimit : "") + ").";
            return WorldLookup.Ok(LastEvent);
        }

        public CommandResult Retreat(WorldState w, GameCommand cmd)
        {
            var e = BattleFor(w, cmd);
            CraftState none;
            var r = CheckActor(w, e, cmd, false, out none);
            if (!r.success) return r;
            if (cmd.actor != e.attacker) return WorldLookup.Fail("NOT_ATTACKER", "Only the attacking side can retreat.");
            // Ships standing in enemy-held cells fly home with the rest.
            LastEvent = Perspective.WhoStart(w, cmd.actor) + (cmd.actor == Perspective.Viewer && !Perspective.Spectating ? " withdraw" : " withdraws")
                + " from the battle for cell " + e.targetCellId + ".";
            LastResult = BattleResult.Withdrew;
            SendAttackersHome(w, e);
            return WorldLookup.Ok(LastEvent);
        }

        /// <summary>
        /// Ends a battle the attacker failed to win in time: its craft are fatigued and fly back to the nearest cell
        /// their side holds, leaving the defender in possession.
        /// </summary>
        void Fatigue(WorldState w, EncounterState e)
        {
            var attacker = e.attacker;
            LastEvent = (attacker == Perspective.Viewer && !Perspective.Spectating ? "Your attack" : Perspective.WhoStart(w, attacker) + "'s attack") + " on cell " + e.targetCellId + " ran out of steam after "
                + catalog.rules.battleRoundLimit + " rounds: the attackers are fatigued and withdraw.";
            LastResult = BattleResult.Fatigued;
            SendAttackersHome(w, e);
        }

        /// <summary>Ends the battle with the attacker's craft in cells it does not hold flying to the nearest cell it does.</summary>
        void SendAttackersHome(WorldState w, EncounterState e)
        {
            var attacker = e.attacker;
            var tired = new List<CraftState>();
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft == null || craft.owner != attacker || !WorldLookup.IsCraftLive(craft)) continue;
                var here = WorldLookup.GetCell(w, craft.cellId);
                if (here == null || here.owner != attacker) tired.Add(craft);
            }
            End(w, e);
            foreach (var craft in tired)
            {
                List<int> best = null;
                foreach (var cell in w.cells)
                {
                    if (cell.owner != attacker) continue;
                    var path = movement.FindWorldPath(w, map, attacker, craft.cellId, cell.id, true); // withdrawing: never stranded
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

        /// <summary>Ends a battle when its target changed hands or its attacking force is gone. Returns true if it ended.</summary>
        public bool FinishIfResolved(WorldState w, EncounterState e)
        {
            if (!e.active) return true;
            if (w.mode == MatchMode.Ended) { e.active = false; return true; }
            var target = WorldLookup.GetCell(w, e.targetCellId);
            bool attackerAlive = false;
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == e.attacker && WorldLookup.IsCraftLive(craft)) { attackerAlive = true; break; }
            }
            if (target != null && WorldLookup.AreAllies(w, target.owner, e.attacker))
            {
                LastEvent = Perspective.WhoStart(w, target.owner) + " captured cell " + e.targetCellId + ".";
                LastResult = BattleResult.Captured;
                End(w, e);
                return true;
            }
            if (!attackerAlive)
            {
                LastEvent = Perspective.Spectating ? Perspective.SeatName(e.attacker) + "'s attack on cell " + e.targetCellId + " was repelled."
                    : e.attacker == Perspective.Viewer ? "Your attack on cell " + e.targetCellId + " was repelled."
                    : WorldLookup.AreAllies(w, Perspective.Viewer, e.attacker) ? "Your ally's attack on cell " + e.targetCellId + " was repelled."
                    : "The raid on cell " + e.targetCellId + " was repelled!";
                LastResult = BattleResult.Repelled;
                End(w, e);
                return true;
            }
            if (WorldLookup.IsSuppressed(w, e.attacker) || (target != null && WorldLookup.IsSuppressed(w, target.owner)))
            {
                LastResult = BattleResult.SideOut;
                End(w, e);
                return true;
            }
            return false;
        }

        void End(WorldState w, EncounterState e)
        {
            e.active = false;
            e.pendingDefenderCellId = -1;
            e.pendingDefenderOwner = FactionId.Neutral;
            e.actionsRemaining = 0;
            e.actedCraftIds.Clear();
            w.captureHolds.RemoveAll(h => h.tactical && h.battleId == e.id);
            // Every weapon that fought is ready for the next battle.
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null) craft.cooldownTurns = 0;
            }
            var cells = new List<int>(e.battleCellIds);
            e.participantCraftIds.Clear();
            e.battleCellIds.Clear();
            OwnershipService.RefreshFactoryOnline(w); // the siege is lifted
            foreach (int id in cells) WorldLookup.AutoAssignDefender(w, id);
            Score(w, e, LastResult);
            // Everyone hears how it ended, each seat in its own words.
            foreach (var f in w.factions) WorldLookup.AddAlert(w, AlertKind.Info, Ended(w, f.id, e), e.targetCellId, f.id);
            if (BattleEnded != null) BattleEnded(e, LastResult);
        }

        /// <summary>Counts the battle as won or lost for every seat that fought in it. A side knocked out counts neither.</summary>
        static void Score(WorldState w, EncounterState e, BattleResult result)
        {
            if (result == BattleResult.None || result == BattleResult.SideOut) return;
            bool attackersWon = result == BattleResult.Captured;
            foreach (var side in e.sides)
            {
                var f = WorldLookup.GetFaction(w, side);
                if (f == null) continue;
                if (WorldLookup.AreAllies(w, side, e.attacker) == attackersWon) f.battlesWon++;
                else f.battlesLost++;
            }
        }

        string Ended(WorldState w, FactionId viewer, EncounterState e)
        {
            var target = WorldLookup.GetCell(w, e.targetCellId);
            bool mine = e.attacker == viewer, ours = WorldLookup.AreAllies(w, viewer, e.attacker);
            string attack = mine ? "Your attack" : WorldLookup.WhoStart(w, viewer, e.attacker) + "'s attack";
            switch (LastResult)
            {
                case BattleResult.Captured:
                    return WorldLookup.WhoStart(w, viewer, target != null ? target.owner : e.attacker) + " captured cell " + e.targetCellId + ".";
                case BattleResult.Repelled:
                    return (mine ? "Your attack on cell " + e.targetCellId + " was repelled." : ours ? "Your ally's attack on cell " + e.targetCellId + " was repelled."
                        : "The raid on cell " + e.targetCellId + " was repelled!") + " Its production resumes.";
                case BattleResult.Withdrew:
                    return attack + " on cell " + e.targetCellId + " withdrew. Its production resumes.";
                case BattleResult.Fatigued:
                    return attack + " on cell " + e.targetCellId + " ran out of steam: the attackers are fatigued and withdraw. Its production resumes.";
                default:
                    return "The battle for cell " + e.targetCellId + " is over.";
            }
        }

        /// <summary>
        /// Picks a replacement defender from surviving reserves. In a battle a person chooses when several reserves
        /// remain (the timer gives the lowest ID if they do not); otherwise the lowest craft ID is assigned for free.
        /// </summary>
        public void ReplaceDefender(WorldState w, int cellId, FactionId owner)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null || cell.owner != owner || cell.defenderCraftId != -1) return;
            var reserves = WorldLookup.CraftsInCell(w, cellId, owner);
            if (reserves.Count == 0) return;
            var e = WorldLookup.BattleAt(w, cellId);
            var seat = WorldLookup.GetFaction(w, owner);
            if (e != null && seat != null && !seat.aiControlled && reserves.Count > 1 && e.pendingDefenderCellId == -1)
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
            return Perspective.OwnerWord(craft.owner) + (def != null ? def.displayName : craft.typeId);
        }

        public static List<CraftState> Participants(WorldState w, EncounterState e, FactionId owner)
        {
            var list = new List<CraftState>();
            foreach (int id in e.participantCraftIds)
            {
                var craft = WorldLookup.GetCraft(w, id);
                if (craft != null && craft.owner == owner && WorldLookup.IsCraftLive(craft)) list.Add(craft);
            }
            return list;
        }
    }
}
