using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>Validates loaded saves and worlds without mutating them.</summary>
    public static class StateValidator
    {
        /// <summary>Oldest content version whose saves can still be played.</summary>
        public const int MinimumCatalogVersion = 6;

        public static CommandResult ValidateSave(SaveEnvelope envelope, ContentCatalog c, MapDefinition map)
        {
            if (envelope == null) return Bad("Save is empty.");
            if (envelope.schemaVersion > SaveService.SchemaVersion)
                return WorldLookup.Fail("NEWER_SAVE", "This save was made by a newer version of the game (schema " + envelope.schemaVersion + ").");
            if (envelope.schemaVersion < 1)
                return WorldLookup.Fail("SCHEMA_MISMATCH", "Unsupported save schema " + envelope.schemaVersion + ".");
            // Older saves load as-is: fields added since (e.g. shipyard orders) take their empty defaults.
            if (envelope.catalogVersion < MinimumCatalogVersion)
                return WorldLookup.Fail("MAP_CHANGED", "This save is from an older version of the game (before solar power and Fort construction). Start a new game.");
            if (envelope.catalogVersion > c.version)
                return WorldLookup.Fail("CATALOG_MISMATCH", "Save content version " + envelope.catalogVersion + " is newer than game content " + c.version + ".");
            return ValidateWorld(envelope.world, c, map);
        }

        public static CommandResult ValidateWorld(WorldState w, ContentCatalog c, MapDefinition map)
        {
            if (w == null) return Bad("World state is missing.");
            if (w.mapId != map.id) return WorldLookup.Fail("MAP_MISMATCH", "Save belongs to map '" + w.mapId + "'.");
            if (w.factions == null || w.cells == null || w.nodes == null || w.buildings == null || w.belts == null || w.tokens == null
                || w.pullRequests == null || w.crafts == null || w.captureHolds == null || w.encounters == null || w.alerts == null || w.winners == null)
                return Bad("A world list is missing.");
            if (w.tick < 0 || w.nextId < 0) return Bad("Negative tick or ID counter.");
            if (w.outcome == MatchOutcome.Decided && w.winners.Count == 0) return Bad("A decided match has no winners.");

            var ids = new HashSet<int>();

            // Factions
            // One faction per seat with a headquarters on the map, in seat order.
            var seats = GameFactory.DefaultSeats(map);
            if (w.factions.Count != seats.Count) return Bad("Faction list is invalid.");
            for (int i = 0; i < seats.Count; i++)
                if (w.factions[i] == null || w.factions[i].id != seats[i].seat) return Bad("Faction list is invalid.");
            foreach (var f in w.factions)
            {
                if (f.ai == null || f.ai.queuedCommands == null || f.trade == null) return Bad("A seat's AI state or trade ledger is missing.");
                foreach (var cmd in f.ai.queuedCommands)
                {
                    if (cmd == null || cmd.actor != f.id) return Bad("An AI queue holds another seat's command.");
                    if (!System.Enum.IsDefined(typeof(CommandKind), cmd.kind)) return Bad("An AI queue holds an unknown command.");
                }
                if (f.currency < 0 || f.ion < 0 || f.cumulativeSales < 0) return Bad("Negative currency, ion or sales.");
                if (f.research == null) return Bad("Faction research list is missing.");
                foreach (var r in f.research)
                {
                    var node = r != null ? CatalogRepository.GetResearch(c, r.nodeId) : null;
                    if (node == null || r.level < 1 || r.level > node.levels.Count) return Bad("Invalid research progress.");
                }
            }

            // Cells must match the map exactly.
            if (w.cells.Count != map.cells.Count) return Bad("Cell count does not match the map.");
            foreach (var cell in w.cells)
            {
                var def = WorldLookup.GetCellDef(map, cell.id);
                if (def == null) return Bad("Unknown cell " + cell.id + ".");
                if (cell.headquartersFor != def.headquartersFor) return Bad("Headquarters mismatch in cell " + cell.id + ".");
                if (!ids.Add(cell.id)) return Bad("Duplicate cell ID " + cell.id + ".");
            }

            // The map layout must match: saves from an earlier map version are rejected rather than mixed.
            int mapNodes = 0;
            foreach (var def in map.cells) mapNodes += def.nodes.Count;
            if (w.nodes.Count != mapNodes)
                return WorldLookup.Fail("MAP_CHANGED", "This save was made on an older layout of the map. Start a new game to play the current map.");
            foreach (var n in w.nodes)
            {
                var cellDef = WorldLookup.GetCellDef(map, n.cellId);
                var nodeDef = cellDef != null ? cellDef.nodes.Find(d => d.id == n.id) : null;
                if (nodeDef == null || nodeDef.x != n.x || nodeDef.y != n.y || nodeDef.itemId != n.itemId)
                    return WorldLookup.Fail("MAP_CHANGED", "This save was made on an older layout of the map. Start a new game to play the current map.");
            }
            foreach (var n in w.nodes)
            {
                if (!ids.Add(n.id)) return Bad("Duplicate ID " + n.id + ".");
                var def = WorldLookup.GetCellDef(map, n.cellId);
                if (def == null || !GridRules.IsTileInside(def, n.x, n.y)) return Bad("Node " + n.id + " is off the map.");
                if (CatalogRepository.GetItem(c, n.itemId) == null || n.rateTicks <= 0) return Bad("Node " + n.id + " is invalid.");
                if (n.extractorBuildingId != -1)
                {
                    var b = WorldLookup.GetBuilding(w, n.extractorBuildingId);
                    if (b == null || b.nodeId != n.id) return Bad("Node " + n.id + " references a missing extractor.");
                }
            }

            foreach (var b in w.buildings)
            {
                if (!ids.Add(b.id)) return Bad("Duplicate ID " + b.id + ".");
                if (b.owner == FactionId.Neutral) return Bad("Building " + b.id + " has no owner.");
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null) return Bad("Building " + b.id + " has unknown type.");
                var cellDef = WorldLookup.GetCellDef(map, b.cellId);
                if (cellDef == null || !GridRules.IsTileInside(cellDef, b.x, b.y)) return Bad("Building " + b.id + " is off the map.");
                if (b.inventory == null) return Bad("Building " + b.id + " inventory is missing.");
                foreach (var s in b.inventory)
                    if (s == null || s.quantity <= 0 || CatalogRepository.GetItem(c, s.itemId) == null) return Bad("Building " + b.id + " has an invalid stack.");
                int capacity = def.inventoryCapacity;
                if (def.kind == BuildingKind.Extractor)
                {
                    // Yield research makes cycles bigger, and the buffer grows to hold two of them.
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    if (node != null) capacity = ResearchService.ExtractorCapacity(def, ResearchService.ExtractionOutput(c, WorldLookup.GetFaction(w, b.owner), node.itemId));
                }
                var buckets = FactorySimulation.RecipeOf(c, b, def);
                if (buckets != null)
                {
                    // A refinery or assembler: a bucket for each item.
                    foreach (var st in b.inventory)
                        if (WorldLookup.CountItem(b.inventory, st.itemId) > FactorySimulation.BucketCapacity(def, buckets, st.itemId))
                            return Bad("Building " + b.id + " holds more " + st.itemId + " than its bucket.");
                }
                else if (WorldLookup.TotalItems(b.inventory) > capacity) return Bad("Building " + b.id + " inventory exceeds capacity.");
                if (!string.IsNullOrEmpty(b.selectedRecipeId) && !def.allowedRecipeIds.Contains(b.selectedRecipeId)) return Bad("Building " + b.id + " has an invalid recipe.");
                if (!string.IsNullOrEmpty(b.orderedCraftTypeId) && (def.kind != BuildingKind.Shipyard || CatalogRepository.GetCraft(c, b.orderedCraftTypeId) == null))
                    return Bad("Building " + b.id + " has an invalid craft order.");
                if (def.kind == BuildingKind.Extractor)
                {
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    if (node == null || node.extractorBuildingId != b.id) return Bad("Extractor " + b.id + " is not linked to its node.");
                }
            }

            foreach (var s in w.belts)
            {
                if (!ids.Add(s.id)) return Bad("Duplicate ID " + s.id + ".");
                var a = WorldLookup.GetCellDef(map, s.fromCellId);
                var b = WorldLookup.GetCellDef(map, s.toCellId);
                if (a == null || b == null || !GridRules.IsTileInside(a, s.fromX, s.fromY) || !GridRules.IsTileInside(b, s.toX, s.toY))
                    return Bad("Belt " + s.id + " has an invalid endpoint.");
                int dx = GridRules.GlobalX(b, s.toX) - GridRules.GlobalX(a, s.fromX);
                int dy = GridRules.GlobalY(b, s.toY) - GridRules.GlobalY(a, s.fromY);
                // Drone links fly between two pads on different asteroids; every other belt joins neighbouring tiles.
                bool droneLink = s.fromCellId != s.toCellId
                    && DroneLinks.IsPad(c, WorldLookup.GetBuildingAt(w, s.fromCellId, s.fromX, s.fromY))
                    && DroneLinks.IsPad(c, WorldLookup.GetBuildingAt(w, s.toCellId, s.toX, s.toY));
                if (!droneLink && System.Math.Abs(dx) + System.Math.Abs(dy) != 1) return Bad("Belt " + s.id + " is not between neighbouring tiles.");
            }

            var segmentLoad = new Dictionary<int, int>();
            foreach (var t in w.tokens)
            {
                if (!ids.Add(t.id)) return Bad("Duplicate ID " + t.id + ".");
                if (t.routeSegmentIds == null || t.routeSegmentIds.Count == 0) return Bad("Token " + t.id + " has no route.");
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) return Bad("Token " + t.id + " route index is out of range.");
                if (CatalogRepository.GetItem(c, t.itemId) == null) return Bad("Token " + t.id + " carries an unknown item.");
                if (WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]) == null) return Bad("Token " + t.id + " sits on a missing belt.");
                int n; segmentLoad.TryGetValue(t.routeSegmentIds[t.segmentIndex], out n);
                segmentLoad[t.routeSegmentIds[t.segmentIndex]] = n + 1;
            }
            foreach (var kv in segmentLoad)
                if (kv.Value > c.rules.beltCapacityPerSegment) return Bad("Belt " + kv.Key + " exceeds its capacity.");

            foreach (var r in w.pullRequests)
            {
                if (!ids.Add(r.id)) return Bad("Duplicate ID " + r.id + ".");
                if (WorldLookup.GetBuilding(w, r.targetBuildingId) == null || CatalogRepository.GetItem(c, r.itemId) == null || r.quantity <= 0)
                    return Bad("Request " + r.id + " is invalid.");
            }

            foreach (var craft in w.crafts)
            {
                if (!ids.Add(craft.id)) return Bad("Duplicate ID " + craft.id + ".");
                if (craft.owner == FactionId.Neutral || CatalogRepository.GetCraft(c, craft.typeId) == null) return Bad("Craft " + craft.id + " is invalid.");
                if (craft.currentHealth <= 0) return Bad("Craft " + craft.id + " has no health.");
                if (craft.currentHealth > CatalogRepository.GetCraft(c, craft.typeId).maxHealth) return Bad("Craft " + craft.id + " health exceeds maximum.");
                if (WorldLookup.GetCellDef(map, craft.cellId) == null) return Bad("Craft " + craft.id + " is off the map.");
                if (craft.worldPathCellIds == null) return Bad("Craft " + craft.id + " path is missing.");
                foreach (int cid in craft.worldPathCellIds) if (WorldLookup.GetCellDef(map, cid) == null) return Bad("Craft " + craft.id + " path is invalid.");
                if (craft.worldPathCellIds.Count > 0 && (craft.worldPathIndex < 0 || craft.worldPathIndex >= craft.worldPathCellIds.Count))
                    return Bad("Craft " + craft.id + " path index is out of range.");
            }

            foreach (var cell in w.cells)
            {
                if (cell.defenderCraftId == -1) continue;
                var d = WorldLookup.GetCraft(w, cell.defenderCraftId);
                if (d == null || d.cellId != cell.id || d.owner != cell.owner) return Bad("Cell " + cell.id + " defender is inconsistent.");
            }

            foreach (var h in w.captureHolds)
                if (WorldLookup.GetCell(w, h.cellId) == null || WorldLookup.GetCraft(w, h.craftId) == null) return Bad("Capture hold is invalid.");

            // Battles run alongside play; the world is never frozen in a battle mode any more.
            if (w.mode == MatchMode.Tactical) return Bad("The match is in the old frozen battle mode.");
            var battleCells = new HashSet<int>();
            foreach (var e in w.encounters)
            {
                if (e == null || e.battleCellIds == null || e.participantCraftIds == null || e.actedCraftIds == null || e.sides == null || e.strikes == null)
                    return Bad("Battle lists are missing.");
                if (!e.active) continue;
                if (e.sides.Count < 1 || e.strikes.Count != e.sides.Count || !e.sides.Contains(e.turn)) return Bad("Battle " + e.id + " turn order is invalid.");
                foreach (int id in e.battleCellIds) if (!battleCells.Add(id)) return Bad("Two battles share cell " + id + ".");
                if (WorldLookup.GetCell(w, e.targetCellId) == null) return Bad("Encounter target is invalid.");
                // Every ship in the battle acts once a turn, so the pool is at most the ships taking part.
                if (e.actionsRemaining < 0 || e.actionsRemaining > e.participantCraftIds.Count) return Bad("Encounter action pool is invalid.");
                foreach (int id in e.battleCellIds) if (WorldLookup.GetCell(w, id) == null) return Bad("Encounter cell is invalid.");
                if (e.pendingDefenderCellId != -1 && WorldLookup.GetCell(w, e.pendingDefenderCellId) == null) return Bad("Pending defender cell is invalid.");
            }

            if (w.alerts == null) return Bad("Alert list is missing.");
            if (w.nextId <= MaxId(w)) return Bad("ID counter is behind existing entities.");
            return WorldLookup.Ok();
        }

        static int MaxId(WorldState w)
        {
            int max = 0;
            foreach (var x in w.cells) max = System.Math.Max(max, x.id);
            foreach (var x in w.nodes) max = System.Math.Max(max, x.id);
            foreach (var x in w.buildings) max = System.Math.Max(max, x.id);
            foreach (var x in w.belts) max = System.Math.Max(max, x.id);
            foreach (var x in w.tokens) max = System.Math.Max(max, x.id);
            foreach (var x in w.pullRequests) max = System.Math.Max(max, x.id);
            foreach (var x in w.crafts) max = System.Math.Max(max, x.id);
            foreach (var x in w.alerts) max = System.Math.Max(max, x.id);
            return max;
        }

        static CommandResult Bad(string message)
        {
            return WorldLookup.Fail("INVALID_STATE", message);
        }
    }
}
