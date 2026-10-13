using System;

namespace OrbitalFoundry
{
    /// <summary>Creates a fresh WorldState from validated content and a map. No buildings or belts are pre-placed.</summary>
    public static class GameFactory
    {
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

            w.factions.Add(new FactionState { id = FactionId.Player, currency = rules.startingCurrency, saleMultiplierPercent = 100 });
            w.factions.Add(new FactionState { id = FactionId.Ai, currency = rules.startingCurrency, saleMultiplierPercent = 100 });

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

            w.ai.goal = AiGoal.BuildIncome;
            w.ai.nextPlanningTick = 0;
            w.ai.nextRaidTick = rules.raidIntervalTicks;
            w.encounter.active = false;
            w.encounter.turn = FactionId.Player;
            return w;
        }
    }
}
