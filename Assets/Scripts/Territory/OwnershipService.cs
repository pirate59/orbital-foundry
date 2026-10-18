using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>Cell transfer, grace-period expiry of captured structures, and match outcome.</summary>
    public sealed class OwnershipService
    {
        public CommandResult TransferCell(WorldState w, ContentCatalog c, int cellId, FactionId newOwner)
        {
            if (newOwner == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_CLAIM", "Cells cannot be captured for neutral.");
            return ChangeOwner(w, c, cellId, newOwner);
        }

        CommandResult ChangeOwner(WorldState w, ContentCatalog c, int cellId, FactionId newOwner)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (cell.owner == newOwner) return WorldLookup.Fail("ALREADY_OWNED", "That cell already belongs to this faction.");

            FactionId oldOwner = cell.owner;
            cell.owner = newOwner;
            cell.lastOwnerChangeTick = w.tick;
            cell.unheldSinceTick = -1;

            // The Headquarters building is a landmark: it changes hands with its cell and is never cleared.
            foreach (var b in w.buildings)
            {
                if (b.cellId != cellId) continue;
                var bdef = CatalogRepository.GetBuilding(c, b.typeId);
                if (bdef != null && bdef.kind == BuildingKind.Headquarters && newOwner != FactionId.Neutral)
                {
                    b.owner = newOwner;
                    b.inactiveUntilTick = -1;
                }
            }

            // Former owner's structures stop for the grace period.
            if (oldOwner != FactionId.Neutral)
            {
                int until = w.tick + c.rules.graceTicks;
                foreach (var b in w.buildings)
                    if (b.cellId == cellId && b.owner == oldOwner && b.inactiveUntilTick == -1) b.inactiveUntilTick = until;
                foreach (var s in w.belts)
                    if (s.toCellId == cellId && s.owner == oldOwner && s.inactiveUntilTick == -1) s.inactiveUntilTick = until;
            }
            // Recapture before expiry restores the new owner's own structures.
            foreach (var b in w.buildings)
                if (b.cellId == cellId && b.owner == newOwner) b.inactiveUntilTick = -1;
            foreach (var s in w.belts)
                if (s.toCellId == cellId && s.owner == newOwner) s.inactiveUntilTick = -1;

            RefreshTokenFreeze(w);

            // Defender slot follows the new owner.
            var oldDefender = WorldLookup.GetCraft(w, cell.defenderCraftId);
            if (oldDefender == null || oldDefender.owner != newOwner) cell.defenderCraftId = -1;
            WorldLookup.AutoAssignDefender(w, cellId);

            // Any remaining holds on this cell are resolved by the transfer.
            w.captureHolds.RemoveAll(h => h.cellId == cellId);

            // A seat whose headquarters falls is out of the match.
            var fallen = cell.headquartersFor;
            if (fallen != FactionId.Neutral && newOwner != fallen && !WorldLookup.IsSuppressed(w, fallen))
            {
                Eliminate(w, fallen);
                EvaluateOutcome(w, c.rules);
                // The match goes on: tell everyone (a decided match sends its own result alerts).
                if (w.outcome != MatchOutcome.Decided)
                    foreach (var f in w.factions)
                        WorldLookup.AddAlert(w, f.id == fallen ? AlertKind.Defeat : WorldLookup.AreAllies(w, f.id, fallen) ? AlertKind.Raid : AlertKind.Victory,
                            f.id == fallen ? "Your headquarters has fallen. You are out of the match."
                                : "Headquarters captured! " + WorldLookup.WhoStart(w, f.id, fallen) + " " + (WorldLookup.Who(w, f.id, fallen) == "you" ? "are" : "is")
                                  + " out of the match: its craft withdraw and its factories stop.", cellId, f.id);
                return WorldLookup.Ok("Headquarters captured.");
            }
            EvaluateOutcome(w, c.rules);
            return WorldLookup.Ok();
        }

        /// <summary>
        /// Takes a seat out of the match: its craft withdraw, its claims and deliveries are dropped, its AI stops, and
        /// a battle it is part of ends. Its structures stay, switched off, for whoever holds their cells to clear.
        /// </summary>
        public static void Eliminate(WorldState w, FactionId seat)
        {
            var f = WorldLookup.GetFaction(w, seat);
            if (f == null) return;
            f.suppressed = true;
            foreach (var craft in w.crafts)
            {
                if (craft.owner != seat) continue;
                craft.withdrawn = true;
                craft.worldPathCellIds.Clear();
                craft.worldDestinationCellId = -1;
                WorldLookup.ClearDefenderSlot(w, craft.id);
            }
            w.captureHolds.RemoveAll(h => h.faction == seat);
            w.pullRequests.RemoveAll(r =>
            {
                var b = WorldLookup.GetBuilding(w, r.targetBuildingId);
                return b == null || b.owner == seat;
            });
            f.ai.queuedCommands.Clear();
            f.ai.goal = AiGoal.None;
            f.ai.raidCraftId = -1;
            f.ai.raidTargetCellId = -1;
            f.ai.raidWarningShown = false;
            // Its battles are over.
            foreach (var e in w.encounters)
                if (e.active && (e.attacker == seat || e.sides.Contains(seat))) StopBattle(w, e);
        }

        /// <summary>Ends a battle on the spot, with no result (a seat left the match, or the match is decided).</summary>
        static void StopBattle(WorldState w, EncounterState e)
        {
            e.active = false;
            e.pendingDefenderCellId = -1;
            e.participantCraftIds.Clear();
            e.battleCellIds.Clear();
            w.captureHolds.RemoveAll(h => h.tactical && h.battleId == e.id);
            RefreshFactoryOnline(w);
        }

        /// <summary>
        /// Abandons an owned cell that has had no Fort, Headquarters or stationed unit for <c>rules.abandonTicks</c>.
        /// It becomes neutral; the former owner's structures there stop and start the normal grace timer.
        /// </summary>
        public CommandResult AbandonCell(WorldState w, ContentCatalog c, int cellId)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null || cell.owner == FactionId.Neutral) return WorldLookup.Fail("BAD_CELL", "Only owned cells can be abandoned.");
            if (cell.headquartersFor != FactionId.Neutral) return WorldLookup.Fail("NOT_ALLOWED", "Headquarters cells are never abandoned.");
            return ChangeOwner(w, c, cellId, FactionId.Neutral);
        }

        /// <summary>True when the faction has an active Fort or Headquarters building in the cell.</summary>
        /// <summary>
        /// Marks each cell whose owner has a working Headquarters or completed Fort there; only those cells' machines
        /// run. Called every factory tick and whenever anchors change.
        /// </summary>
        public static void RefreshFactoryOnline(WorldState w)
        {
            foreach (var cell in w.cells) cell.factoryOnline = false;
            foreach (var b in w.buildings)
            {
                if (!b.anchor || b.underConstruction || !WorldLookup.IsBuildingHeld(w, b)) continue;
                WorldLookup.GetCell(w, b.cellId).factoryOnline = true;
            }
            // A cell under siege (the target of a battle) stops producing until the battle ends; its anchor still defends.
            foreach (var e in w.encounters)
                if (e.active) { var target = WorldLookup.GetCell(w, e.targetCellId); if (target != null) target.factoryOnline = false; }
        }

        public static bool HasOwnAnchor(WorldState w, ContentCatalog c, int cellId, FactionId faction)
        {
            foreach (var b in w.buildings)
            {
                if (b.cellId != cellId || b.owner != faction || b.inactiveUntilTick != -1 || b.underConstruction) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && (def.kind == BuildingKind.Fort || def.kind == BuildingKind.Headquarters)) return true;
            }
            return false;
        }

        /// <summary>The live, non-travelling unit of the owner stationed in the cell, if any.</summary>
        public static CraftState StationedUnit(WorldState w, int cellId, FactionId owner)
        {
            CraftState best = null;
            foreach (var craft in w.crafts)
            {
                if (craft.cellId != cellId || craft.owner != owner || !WorldLookup.IsCraftLive(craft) || craft.worldPathCellIds.Count > 0) continue;
                if (best == null || craft.id < best.id) best = craft;
            }
            return best;
        }

        /// <summary>An owned cell is held by its HQ, an active Fort, or a stationed unit of its owner.</summary>
        public static bool IsCellHeld(WorldState w, ContentCatalog c, CellState cell)
        {
            if (cell.owner == FactionId.Neutral) return true;
            return HasOwnAnchor(w, c, cell.id, cell.owner) || StationedUnit(w, cell.id, cell.owner) != null;
        }

        /// <summary>
        /// Normal-tick territory upkeep: starts the abandon countdown for unheld cells (with a warning), clears it when
        /// a Fort or unit arrives, and abandons cells whose countdown has run out.
        /// </summary>
        public void TickUpkeep(WorldState w, ContentCatalog c)
        {
            int limit = c.rules.abandonTicks;
            foreach (var cell in w.cells.ToArray())
            {
                if (cell.owner == FactionId.Neutral) { cell.unheldSinceTick = -1; cell.fortLostTick = -1; continue; }
                // A Fort destroyed in battle must be rebuilt in time, units in the cell or not.
                if (cell.fortLostTick != -1)
                {
                    if (HasOwnAnchor(w, c, cell.id, cell.owner)) cell.fortLostTick = -1;
                    else if (w.tick - cell.fortLostTick >= c.rules.fortRebuildTicks)
                    {
                        var loser = cell.owner;
                        cell.fortLostTick = -1;
                        if (AbandonCell(w, c, cell.id).success)
                            WorldLookup.AddAlert(w, AlertKind.Territory, "Cell " + cell.id + " turned neutral: its Fort was not rebuilt in time.", cell.id, loser);
                        continue;
                    }
                }
                if (IsCellHeld(w, c, cell)) { cell.unheldSinceTick = -1; continue; }
                if (cell.unheldSinceTick == -1)
                {
                    cell.unheldSinceTick = w.tick;
                    WorldLookup.AddAlert(w, AlertKind.Territory, "Cell " + cell.id + " is unguarded. Build a Fort or station a unit there within "
                        + (limit * c.rules.tickMilliseconds / 1000) + "s or it will be lost.", cell.id, cell.owner);
                    continue;
                }
                if (w.tick - cell.unheldSinceTick < limit) continue;
                var owner = cell.owner;
                if (AbandonCell(w, c, cell.id).success)
                    WorldLookup.AddAlert(w, AlertKind.Territory, "Cell " + cell.id + " was abandoned. Its machines have stopped; reclaim it within "
                        + (c.rules.graceTicks * c.rules.tickMilliseconds / 60000) + " minutes to restore them.", cell.id, owner);
            }
        }

        /// <summary>Freezes tokens sitting on inactive edges and wakes tokens whose edge became active again.</summary>
        public static void RefreshTokenFreeze(WorldState w)
        {
            var belts = new Dictionary<int, BeltSegmentState>();
            foreach (var s in w.belts) belts[s.id] = s;
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                BeltSegmentState seg;
                if (!belts.TryGetValue(t.routeSegmentIds[t.segmentIndex], out seg)) continue;
                bool active = WorldLookup.IsBeltActive(w, seg);
                if (!active) t.status = TokenStatus.Frozen;
                else if (t.status == TokenStatus.Frozen) t.status = TokenStatus.Moving;
            }
        }

        /// <summary>Removes structures whose grace period has elapsed, plus tokens on removed edges.</summary>
        public void ExpireInactiveStructures(WorldState w, ContentCatalog c)
        {
            var removedBelts = new HashSet<int>();
            var removedBuildings = new HashSet<int>();
            foreach (var s in w.belts)
                if (s.inactiveUntilTick != -1 && w.tick >= s.inactiveUntilTick) removedBelts.Add(s.id);
            foreach (var b in w.buildings)
                if (b.inactiveUntilTick != -1 && w.tick >= b.inactiveUntilTick) removedBuildings.Add(b.id);
            if (removedBelts.Count == 0 && removedBuildings.Count == 0) return;
            RemoveStructures(w, removedBuildings, removedBelts);
        }

        /// <summary>Removes the given structures and every token or request that depends on them.</summary>
        public static void RemoveStructures(WorldState w, HashSet<int> buildingIds, HashSet<int> beltIds)
        {
            // Drone links lose their meaning when either pad goes: remove links touching a removed building's tile.
            if (buildingIds.Count > 0)
            {
                var removedTiles = new HashSet<int>();
                foreach (var b in w.buildings) if (buildingIds.Contains(b.id)) removedTiles.Add(BeltNetwork.VertexKey(b.cellId, b.x, b.y));
                foreach (var s in w.belts)
                    if (s.fromCellId != s.toCellId && !beltIds.Contains(s.id)
                        && (removedTiles.Contains(BeltNetwork.VertexKey(s.fromCellId, s.fromX, s.fromY)) || removedTiles.Contains(BeltNetwork.VertexKey(s.toCellId, s.toX, s.toY))))
                        beltIds = new HashSet<int>(beltIds) { s.id }; // a copy: the caller's set stays as given
            }
            w.belts.RemoveAll(s => beltIds.Contains(s.id));
            w.buildings.RemoveAll(b => buildingIds.Contains(b.id));
            foreach (var n in w.nodes)
                if (buildingIds.Contains(n.extractorBuildingId)) n.extractorBuildingId = -1;
            // Goods on a removed belt are lost. Goods heading for a removed building lose their destination and look
            // for another (see FactorySimulation's stranded loads) rather than vanishing.
            w.tokens.RemoveAll(t =>
                t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count
                || beltIds.Contains(t.routeSegmentIds[t.segmentIndex]));
            foreach (var t in w.tokens) if (buildingIds.Contains(t.targetBuildingId)) t.targetBuildingId = -1;
            w.pullRequests.RemoveAll(r => buildingIds.Contains(r.targetBuildingId));
        }

        /// <summary>
        /// Decides the match once a team has won, in one of two ways: it is the last team with a seat still in the match
        /// (every rival headquarters fell), or one of its seats held the trade rate (see TradeService). Every seat of
        /// the winning team wins, including any already out; the other seats concede and leave the match. If a person
        /// is among the winners the match goes on so they can keep building; otherwise it ends.
        /// </summary>
        public void EvaluateOutcome(WorldState w, RulesDefinition rules)
        {
            if (w.outcome == MatchOutcome.Decided || w.factions.Count == 0) return;
            FactionState rich = w.factions.Find(f => !f.suppressed && f.trade.won);
            int team;
            if (rich != null) team = WorldLookup.TeamOf(w, rich.id);
            else
            {
                var standing = new HashSet<int>();
                foreach (var f in w.factions) if (!f.suppressed) standing.Add(WorldLookup.TeamOf(w, f.id));
                if (standing.Count > 1) return;
                team = -2; // nobody, should every seat be out at once
                foreach (int t in standing) team = t;
            }
            w.outcome = MatchOutcome.Decided;
            w.winners.Clear();
            foreach (var f in w.factions) if (WorldLookup.TeamOf(w, f.id) == team) w.winners.Add(f.id);
            bool personWon = w.winners.Exists(id => !WorldLookup.GetFaction(w, id).aiControlled);
            foreach (var f in w.factions)
                if (!f.suppressed && !w.winners.Contains(f.id)) Eliminate(w, f.id); // the losers formally concede
            foreach (var e in w.encounters) if (e.active) StopBattle(w, e);
            w.mode = personWon && w.mode != MatchMode.Ended ? MatchMode.Factory : MatchMode.Ended;
            foreach (var f in w.factions)
            {
                bool won = w.winners.Contains(f.id);
                string how = rich == null ? (won ? "every rival headquarters has fallen" : "your side's headquarters have fallen")
                    : (rich.id == f.id ? "you" : WorldLookup.Who(w, f.id, rich.id)) + " sold " + rich.trade.rate.ToString("N0") + " credits a minute of "
                      + (rich.id == f.id ? "your" : "its") + " own goods";
                WorldLookup.AddAlert(w, won ? AlertKind.Victory : AlertKind.Defeat,
                    won ? (rich != null ? "Trade victory! " : "Victory! ") + char.ToUpperInvariant(how[0]) + how.Substring(1) + " and every rival has formally conceded."
                          + (w.mode == MatchMode.Factory ? " Keep building as long as you like." : "")
                        : "Defeat. " + char.ToUpperInvariant(how[0]) + how.Substring(1) + ". The map is lost.", -1, f.id);
            }
        }
    }
}
