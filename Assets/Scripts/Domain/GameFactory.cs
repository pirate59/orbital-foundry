using System;

namespace OrbitalFoundry
{
    /// <summary>Creates a fresh WorldState from validated content and a map. Only HQ buildings and starting forts are pre-placed.</summary>
    public static class GameFactory
    {
        /// <summary>Nearest tile to the cell centre that is not blocked, a node, or occupied.</summary>
        /// <summary>
        /// The free tile nearest the cell's centre with room for a building of <paramref name="footprint"/> tiles
        /// across (centred on it): every tile of that square inside the cell and clear of terrain, nodes and buildings.
        /// </summary>
        public static bool FindCentreTile(WorldState w, MapCellDefinition def, out int bx, out int by, int footprint = 1)
        {
            int cx = def.gridWidth / 2, cy = def.gridHeight / 2, half = Math.Max(0, footprint / 2);
            bx = by = -1;
            int best = int.MaxValue;
            for (int y = half; y < def.gridHeight - half; y++)
                for (int x = half; x < def.gridWidth - half; x++)
                {
                    int d = Math.Abs(x - cx) + Math.Abs(y - cy);
                    if (d >= best) continue;
                    bool clear = true;
                    for (int dy = -half; dy <= half && clear; dy++)
                        for (int dx = -half; dx <= half && clear; dx++)
                            clear = !GridRules.IsBlocked(def, x + dx, y + dy)
                                && WorldLookup.GetNodeAt(w, def.id, x + dx, y + dy) == null
                                && WorldLookup.GetBuildingAt(w, def.id, x + dx, y + dy) == null;
                    if (!clear) continue;
                    best = d; bx = x; by = y;
                }
            return bx >= 0;
        }

        public static WorldState CreateNew(ContentCatalog catalog, MapDefinition map)
        {
            return CreateNew(catalog, map, Environment.TickCount);
        }

        public static WorldState CreateNew(ContentCatalog catalog, MapDefinition map, int randomSeed)
        {
            CatalogRepository.ValidateMap(map, catalog);
            var rules = catalog.rules;
            var w = new WorldState
            {
                mapId = map.id,
                mode = MatchMode.Factory,
                outcome = MatchOutcome.None,
                tick = 0,
                randomSeed = randomSeed
            };

            w.factions.Add(new FactionState { id = FactionId.Player, currency = rules.startingCurrency, ion = rules.startingIon });
            w.factions.Add(new FactionState { id = FactionId.Ai, currency = rules.startingCurrency, ion = rules.startingIon });

            int maxId = 0;
            foreach (var def in map.cells)
            {
                w.cells.Add(new CellState
                {
                    id = def.id,
                    owner = def.startingOwner,
                    headquartersFor = def.headquartersFor,
                    defenderCraftId = -1,
                    lastOwnerChangeTick = 0
                });
                maxId = Math.Max(maxId, def.id);
                foreach (var n in def.nodes)
                {
                    w.nodes.Add(new ResourceNodeState
                    {
                        id = n.id,
                        cellId = def.id,
                        x = n.x,
                        y = n.y,
                        itemId = n.itemId,
                        rateTicks = n.rateTicks,
                        ticksUntilOutput = n.rateTicks,
                        extractorBuildingId = -1
                    });
                    maxId = Math.Max(maxId, n.id);
                }
            }
            w.nextId = maxId + 1;

            foreach (var s in map.startingCrafts)
            {
                var def = CatalogRepository.GetCraft(catalog, s.craftTypeId);
                var cell = WorldLookup.GetCell(w, s.cellId);
                if (def == null || cell == null) throw new CatalogException("Starting craft references a missing type or cell.");
                var craft = new CraftState
                {
                    id = WorldLookup.AllocateId(w),
                    owner = s.faction,
                    typeId = s.craftTypeId,
                    cellId = s.cellId,
                    currentHealth = def.maxHealth
                };
                w.crafts.Add(craft);
                if (s.assignedDefender)
                {
                    if (cell.defenderCraftId != -1) throw new CatalogException("Cell " + s.cellId + " would receive a second starting defender.");
                    if (cell.owner != s.faction) throw new CatalogException("Starting defender faction differs from cell " + s.cellId + " owner.");
                    cell.defenderCraftId = craft.id;
                }
            }

            // Every HQ cell has its Headquarters building; other starting territory begins fortified.
            foreach (var def in map.cells)
            {
                if (def.startingOwner == FactionId.Neutral) continue;
                string type = def.headquartersFor != FactionId.Neutral ? "headquarters" : "fort";
                if (CatalogRepository.GetBuilding(catalog, type) == null) throw new CatalogException("Catalog is missing the '" + type + "' building.");
                int x, y;
                if (!FindCentreTile(w, def, out x, out y, CatalogRepository.GetBuilding(catalog, type).footprint))
                    throw new CatalogException("Cell " + def.id + " has no free tile for its " + type + ".");
                w.buildings.Add(new BuildingState { id = WorldLookup.AllocateId(w), owner = def.startingOwner, typeId = type, cellId = def.id, x = x, y = y, anchor = true });
            }
            OwnershipService.RefreshFactoryOnline(w);

            w.ai.goal = AiGoal.BuildIncome;
            w.ai.nextPlanningTick = 0;
            w.ai.nextRaidTick = rules.raidIntervalTicks;
            w.encounter.active = false;
            w.encounter.turn = FactionId.Player;
            return w;
        }
    
        public const int TestStartFunds = 20000;

        /// <summary>
        /// Turns a new game into a mid-game test start: the player gets 20,000 credits and Ion, and one craft of every
        /// type parked in its headquarters cell.
        /// </summary>
        public static void MakeTestStart(WorldState w, ContentCatalog c)
        {
            var player = WorldLookup.GetFaction(w, FactionId.Player);
            if (player == null) return;
            player.currency = TestStartFunds;
            player.ion = TestStartFunds;
            int home = -1;
            foreach (var cell in w.cells) if (cell.headquartersFor == FactionId.Player) { home = cell.id; break; }
            if (home < 0) return;
            foreach (var craft in c.crafts)
                if (!w.crafts.Exists(k => k.owner == FactionId.Player && k.typeId == craft.id && k.cellId == home))
                    EconomyService.SpawnCraft(w, craft, FactionId.Player, home);
        }
}
}
