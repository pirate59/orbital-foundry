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

            if (cell.headquartersFor == FactionId.Player && newOwner == FactionId.Ai)
            {
                w.mode = MatchMode.Ended;
                w.outcome = MatchOutcome.PlayerDefeat;
                w.encounter.active = false;
                WorldLookup.AddAlert(w, AlertKind.Defeat, "Your headquarters has fallen. The map is lost.", cellId);
                return WorldLookup.Ok("Player headquarters captured.");
            }
            if (cell.headquartersFor == FactionId.Ai && newOwner == FactionId.Player)
            {
                SuppressAi(w);
                WorldLookup.AddAlert(w, AlertKind.Victory, "AI headquarters captured! Its craft withdraw and its factories stop.", cellId);
            }
            EvaluateOutcome(w, c.rules);
            return WorldLookup.Ok();
        }

        static void SuppressAi(WorldState w)
        {
            var ai = WorldLookup.GetFaction(w, FactionId.Ai);
            if (ai != null) ai.suppressed = true;
            foreach (var craft in w.crafts)
            {
                if (craft.owner != FactionId.Ai) continue;
                craft.withdrawn = true;
                craft.worldPathCellIds.Clear();
                craft.worldDestinationCellId = -1;
                WorldLookup.ClearDefenderSlot(w, craft.id);
            }
            w.captureHolds.RemoveAll(h => h.faction == FactionId.Ai);
            w.pullRequests.RemoveAll(r =>
            {
                var b = WorldLookup.GetBuilding(w, r.targetBuildingId);
                return b == null || b.owner == FactionId.Ai;
            });
            w.ai.queuedCommands.Clear();
            w.ai.goal = AiGoal.None;
            w.ai.raidCraftId = -1;
            w.ai.raidTargetCellId = -1;
            w.ai.raidWarningShown = false;
            if (w.encounter.active)
            {
                w.encounter.active = false;
                w.encounter.pendingDefenderCellId = -1;
                if (w.mode == MatchMode.Tactical) w.mode = MatchMode.Factory;
                w.captureHolds.RemoveAll(h => h.tactical);
            }
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
                if (cell.owner == FactionId.Neutral) { cell.unheldSinceTick = -1; continue; }
                if (IsCellHeld(w, c, cell)) { cell.unheldSinceTick = -1; continue; }
                if (cell.unheldSinceTick == -1)
                {
                    cell.unheldSinceTick = w.tick;
                    if (cell.owner == FactionId.Player)
                        WorldLookup.AddAlert(w, AlertKind.Territory, "Cell " + cell.id + " is unguarded. Build a Fort or station a unit there within "
                            + (limit * c.rules.tickMilliseconds / 1000) + "s or it will be lost.", cell.id);
                    continue;
                }
                if (w.tick - cell.unheldSinceTick < limit) continue;
                var owner = cell.owner;
                if (AbandonCell(w, c, cell.id).success && owner == FactionId.Player)
                    WorldLookup.AddAlert(w, AlertKind.Territory, "Cell " + cell.id + " was abandoned. Its machines have stopped; reclaim it within "
                        + (c.rules.graceTicks * c.rules.tickMilliseconds / 60000) + " minutes to restore them.", cell.id);
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

        public void EvaluateOutcome(WorldState w, RulesDefinition rules)
        {
            if (w.outcome == MatchOutcome.PlayerDefeat) return;
            foreach (var cell in w.cells)
            {
                if (cell.headquartersFor == FactionId.Player && cell.owner == FactionId.Ai)
                {
                    w.outcome = MatchOutcome.PlayerDefeat;
                    w.mode = MatchMode.Ended;
                    return;
                }
            }
            if (w.outcome == MatchOutcome.AiConceded) return;
            var ai = WorldLookup.GetFaction(w, FactionId.Ai);
            var player = WorldLookup.GetFaction(w, FactionId.Player);
            if (ai != null && player != null && ai.suppressed && player.cumulativeSales >= rules.playerSalesGoal)
            {
                w.outcome = MatchOutcome.AiConceded;
                if (w.mode != MatchMode.Ended) w.mode = MatchMode.Factory;
                WorldLookup.AddAlert(w, AlertKind.Victory, "Victory! The AI has formally conceded. Keep building as long as you like.", -1);
            }
        }
    }
}
