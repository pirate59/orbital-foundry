namespace OrbitalFoundry
{
    /// <summary>
    /// Placement legality for buildings and directed belt edges. Global tile coordinates are
    /// (mapX * gridWidth + x, mapY * gridHeight + y); a belt edge joins two orthogonally neighbouring global tiles.
    /// </summary>
    public static class GridRules
    {
        public static bool IsCellAdjacent(MapDefinition map, int a, int b)
        {
            return WorldLookup.IsAdjacent(map, a, b);
        }

        public static bool IsTileInside(MapCellDefinition cell, int x, int y)
        {
            return cell != null && x >= 0 && y >= 0 && x < cell.gridWidth && y < cell.gridHeight;
        }

        public static int GlobalX(MapCellDefinition cell, int x) { return cell.mapX * cell.gridWidth + x; }
        public static int GlobalY(MapCellDefinition cell, int y) { return cell.mapY * cell.gridHeight + y; }

        /// <summary>Returns the cell ID containing a global tile, or -1.</summary>
        public static int GetCellForGlobalTile(MapDefinition map, int gx, int gy)
        {
            foreach (var c in map.cells)
            {
                int lx = gx - c.mapX * c.gridWidth;
                int ly = gy - c.mapY * c.gridHeight;
                if (IsTileInside(c, lx, ly)) return c.id;
            }
            return -1;
        }

        public static bool TryGlobalToLocal(MapDefinition map, int gx, int gy, out int cellId, out int x, out int y)
        {
            cellId = GetCellForGlobalTile(map, gx, gy);
            x = y = 0;
            if (cellId < 0) return false;
            var c = WorldLookup.GetCellDef(map, cellId);
            x = gx - c.mapX * c.gridWidth;
            y = gy - c.mapY * c.gridHeight;
            return true;
        }

        /// <summary>
        /// The big building whose footprint (wider than its own tile) covers this tile, or null. Nothing may be built
        /// or belted inside it.
        /// </summary>
        public static BuildingState FootprintAt(WorldState w, ContentCatalog c, int cellId, int x, int y)
        {
            if (c == null) return null;
            foreach (var b in w.buildings)
            {
                if (b.cellId != cellId) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.footprint <= 1) continue;
                int half = def.footprint / 2;
                if (System.Math.Abs(x - b.x) <= half && System.Math.Abs(y - b.y) <= half) return b;
            }
            return null;
        }

        public static bool IsBlocked(MapCellDefinition cell, int x, int y)
        {
            foreach (var t in cell.blockedTiles) if (t.x == x && t.y == y) return true;
            return false;
        }

        /// <summary>True when the tile is both entered and left by belt edges, i.e. a building there would cut a belt line.</summary>
        public static bool IsBeltTransitTile(WorldState w, int cellId, int x, int y)
        {
            bool incoming = false, outgoing = false;
            foreach (var s in w.belts)
            {
                if (s.toCellId == cellId && s.toX == x && s.toY == y) incoming = true;
                if (s.fromCellId == cellId && s.fromX == x && s.fromY == y) outgoing = true;
            }
            return incoming && outgoing;
        }

        public static CommandResult CanPlaceBuilding(WorldState w, ContentCatalog catalog, MapDefinition map, BuildingDefinition def,
            FactionId faction, int cellId, int x, int y)
        {
            if (def == null) return WorldLookup.Fail("UNKNOWN_BUILDING", "That building type does not exist.");
            if (def.width != 1 || def.height != 1) return WorldLookup.Fail("BAD_FOOTPRINT", "Only 1x1 buildings are supported.");
            if (faction == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Neutral cannot build.");
            if (WorldLookup.IsSuppressed(w, faction)) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer build.");
            var cellDef = WorldLookup.GetCellDef(map, cellId);
            var cell = WorldLookup.GetCell(w, cellId);
            if (cellDef == null || cell == null) return WorldLookup.Fail("BAD_CELL", "That cell does not exist.");
            if (cell.owner != faction) return WorldLookup.Fail("NOT_OWNER", "You can only build in cells you own.");
            if (!IsTileInside(cellDef, x, y)) return WorldLookup.Fail("OFF_GRID", "That tile is outside the cell.");
            if (IsBlocked(cellDef, x, y)) return WorldLookup.Fail("BLOCKED_TILE", "That tile is blocked by terrain.");
            if (WorldLookup.GetBuildingAt(w, cellId, x, y) != null) return WorldLookup.Fail("OCCUPIED", "Another structure already occupies that tile.");
            var node = WorldLookup.GetNodeAt(w, cellId, x, y);
            if (def.kind == BuildingKind.Extractor)
            {
                if (node == null) return WorldLookup.Fail("NEEDS_NODE", "Extractors must be built on a resource node.");
                if (node.extractorBuildingId != -1) return WorldLookup.Fail("NODE_USED", "That node already has an extractor.");
            }
            else if (node != null)
            {
                return WorldLookup.Fail("NODE_TILE", "Only extractors can be built on resource nodes.");
            }
            if (IsBeltTransitTile(w, cellId, x, y)) return WorldLookup.Fail("BELT_TRANSIT", "A belt passes through that tile; a building there would cut it.");
            if (def.kind == BuildingKind.Headquarters) return WorldLookup.Fail("NOT_BUILDABLE", "Headquarters cannot be built.");
            if (def.kind == BuildingKind.Fort)
                return WorldLookup.Fail("NOT_BUILDABLE", "Forts are deployed from a Fort Construction Zone on the border of a cell you hold.");
            var big = FootprintAt(w, catalog, cellId, x, y);
            if (big != null) return WorldLookup.Fail("FOOTPRINT", "The " + CatalogRepository.GetBuilding(catalog, big.typeId).displayName + " fills that space.");
            if (!OwnershipService.HasOwnAnchor(w, catalog, cellId, faction))
            {
                // Expansion: a new cell's factory comes online only once a Fort has been deployed into it.
                return WorldLookup.Fail("NEEDS_FORT", "This cell needs a Fort first. Build a Fort Construction Zone on the border of a neighbouring cell you hold.");
            }
            if (def.kind == BuildingKind.ConstructionZone)
            {
                int target = BorderNeighbour(map, cellId, x, y);
                if (target < 0) return WorldLookup.Fail("NOT_BORDER", "Place the zone on an edge tile facing the cell the Fort will deploy into (not a corner).");
                if (OwnershipService.HasOwnAnchor(w, catalog, target, faction)) return WorldLookup.Fail("ALREADY_HELD", "Cell " + target + " already has your Fort.");
                foreach (var b in w.buildings)
                    if (b.owner == faction && b.typeId == def.id && b.targetCellId == target)
                        return WorldLookup.Fail("ALREADY_HELD", "A zone is already building a Fort for cell " + target + ".");
            }
            if (def.kind == BuildingKind.SolarArray)
            {
                int slots = 0, used = 0;
                foreach (var b in w.buildings)
                {
                    if (b.cellId != cellId || b.owner != faction) continue;
                    var other = CatalogRepository.GetBuilding(catalog, b.typeId);
                    if (other == null) continue;
                    if (b.anchor && !b.underConstruction) slots += other.arraySlots;
                    if (other.kind == BuildingKind.SolarArray) used++;
                }
                if (used >= slots)
                    return WorldLookup.Fail("NO_ARRAY_SLOT", "This cell's Solar Array slots are full (" + slots + "). Hold another cell with a Fort for more.");
            }
            var faction_ = WorldLookup.GetFaction(w, faction);
            if (faction_ == null || faction_.currency < def.cost) return WorldLookup.Fail("NO_MONEY", "Not enough currency (" + def.cost + " needed).");
            return WorldLookup.Ok();
        }

        /// <summary>
        /// The neighbouring cell an edge tile faces, or -1 for an interior tile, a corner facing two cells, or an
        /// edge on the map's rim.
        /// </summary>
        public static int BorderNeighbour(MapDefinition map, int cellId, int x, int y)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            if (def == null) return -1;
            int gx = GlobalX(def, x), gy = GlobalY(def, y), found = -1;
            foreach (var d in new[] { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } })
            {
                int other = GetCellForGlobalTile(map, gx + d[0], gy + d[1]);
                if (other < 0 || other == cellId) continue;
                if (found != -1 && found != other) return -1; // a corner between two neighbours
                found = other;
            }
            return found;
        }

        public static CommandResult CanPlaceBelt(WorldState w, MapDefinition map, FactionId faction, BeltStep step, ContentCatalog catalog = null)
        {
            if (step == null) return WorldLookup.Fail("MALFORMED", "No belt step given.");
            if (faction == FactionId.Neutral) return WorldLookup.Fail("NEUTRAL_ACTOR", "Neutral cannot build.");
            if (WorldLookup.IsSuppressed(w, faction)) return WorldLookup.Fail("SUPPRESSED", "This faction can no longer build.");
            var fromDef = WorldLookup.GetCellDef(map, step.fromCellId);
            var toDef = WorldLookup.GetCellDef(map, step.toCellId);
            var fromCell = WorldLookup.GetCell(w, step.fromCellId);
            var toCell = WorldLookup.GetCell(w, step.toCellId);
            if (fromDef == null || toDef == null || fromCell == null || toCell == null) return WorldLookup.Fail("BAD_CELL", "A belt endpoint is in a missing cell.");
            if (!IsTileInside(fromDef, step.fromX, step.fromY) || !IsTileInside(toDef, step.toX, step.toY))
                return WorldLookup.Fail("OFF_GRID", "A belt endpoint is outside its cell.");
            if (fromCell.owner != faction || toCell.owner != faction)
                return WorldLookup.Fail("NOT_OWNER", "Belts can only be laid inside cells you own.");
            int dx = GlobalX(toDef, step.toX) - GlobalX(fromDef, step.fromX);
            int dy = GlobalY(toDef, step.toY) - GlobalY(fromDef, step.fromY);
            if (!((dx == 0 && (dy == 1 || dy == -1)) || (dy == 0 && (dx == 1 || dx == -1))))
                return WorldLookup.Fail("NOT_ADJACENT", "Belt steps must join orthogonally neighbouring tiles.");
            if (step.fromCellId != step.toCellId)
                return WorldLookup.Fail("USE_DRONE_PAD", "Belts end at the asteroid's edge. Use Drone Pads (research Drone Logistics) to send goods to another asteroid.");
            if (IsBlocked(fromDef, step.fromX, step.fromY) || IsBlocked(toDef, step.toX, step.toY))
                return WorldLookup.Fail("BLOCKED_TILE", "Belts cannot cross blocked terrain.");
            var big = FootprintAt(w, catalog, step.fromCellId, step.fromX, step.fromY) ?? FootprintAt(w, catalog, step.toCellId, step.toX, step.toY);
            if (big != null) return WorldLookup.Fail("FOOTPRINT", "Belts cannot cross the " + CatalogRepository.GetBuilding(catalog, big.typeId).displayName + ".");

            var r1 = CheckEndpoint(w, faction, step.fromCellId, step.fromX, step.fromY);
            if (!r1.success) return r1;
            var r2 = CheckEndpoint(w, faction, step.toCellId, step.toX, step.toY);
            if (!r2.success) return r2;

            foreach (var s in w.belts)
            {
                bool same = s.fromCellId == step.fromCellId && s.fromX == step.fromX && s.fromY == step.fromY
                         && s.toCellId == step.toCellId && s.toX == step.toX && s.toY == step.toY;
                if (same) return WorldLookup.Fail("DUPLICATE_EDGE", "That belt step already exists.");
                bool opposite = s.fromCellId == step.toCellId && s.fromX == step.toX && s.fromY == step.toY
                             && s.toCellId == step.fromCellId && s.toX == step.fromX && s.toY == step.fromY;
                if (opposite) return WorldLookup.Fail("OPPOSITE_EDGE", "A belt already runs the other way along this lane.");
            }
            return WorldLookup.Ok();
        }

        static CommandResult CheckEndpoint(WorldState w, FactionId faction, int cellId, int x, int y)
        {
            var b = WorldLookup.GetBuildingAt(w, cellId, x, y);
            if (b != null)
            {
                if (b.owner != faction) return WorldLookup.Fail("FOREIGN_STRUCTURE", "Belts cannot connect to another faction's structure.");
                return WorldLookup.Ok();
            }
            if (WorldLookup.GetNodeAt(w, cellId, x, y) != null)
                return WorldLookup.Fail("NODE_TILE", "Belts cannot cross a resource node without an extractor on it.");
            return WorldLookup.Ok();
        }
    }
}
