using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>Raised when content or map data cannot be loaded or fails validation.</summary>
    public sealed class CatalogException : Exception
    {
        public CatalogException(string message) : base(message) { }
    }

    /// <summary>
    /// Loads and validates editable game content. Runtime dictionaries are built once per parsed
    /// catalog and cached against the catalog instance; only the list-based contracts are serialized.
    /// </summary>
    public static class CatalogRepository
    {
        public const int SupportedCatalogVersion = 1;
        public const int CellGridSize = 6;

        sealed class CatalogIndex
        {
            public readonly Dictionary<string, ItemDefinition> Items = new Dictionary<string, ItemDefinition>();
            public readonly Dictionary<string, RecipeDefinition> Recipes = new Dictionary<string, RecipeDefinition>();
            public readonly Dictionary<string, BuildingDefinition> Buildings = new Dictionary<string, BuildingDefinition>();
            public readonly Dictionary<string, CraftDefinition> Crafts = new Dictionary<string, CraftDefinition>();
            public readonly Dictionary<string, UpgradeDefinition> Upgrades = new Dictionary<string, UpgradeDefinition>();
            public readonly Dictionary<string, TechDefinition> Techs = new Dictionary<string, TechDefinition>();
        }

        static readonly ConditionalWeakTable<ContentCatalog, CatalogIndex> Indexes = new ConditionalWeakTable<ContentCatalog, CatalogIndex>();

        public static ContentCatalog LoadContent()
        {
            var asset = Resources.Load<TextAsset>("catalog");
            if (asset == null) throw new CatalogException("Content file Resources/catalog.json is missing.");
            return ParseContent(asset.text);
        }

        public static MapDefinition LoadMap(string mapId)
        {
            return LoadMap(mapId, null);
        }

        /// <summary>Loads a map and, when a catalog is supplied, validates its references against it.</summary>
        public static MapDefinition LoadMap(string mapId, ContentCatalog catalog)
        {
            if (string.IsNullOrEmpty(mapId)) throw new CatalogException("No map ID was given.");
            var asset = Resources.Load<TextAsset>("maps/" + mapId);
            if (asset == null) throw new CatalogException("Map file Resources/maps/" + mapId + ".json is missing.");
            var map = ParseMap(asset.text);
            if (map.id != mapId) throw new CatalogException("Map file " + mapId + " declares id '" + map.id + "'.");
            if (catalog != null) ValidateMap(map, catalog);
            return map;
        }

        public static ContentCatalog ParseContent(string json)
        {
            ContentCatalog catalog;
            try { catalog = JsonUtility.FromJson<ContentCatalog>(json); }
            catch (Exception e) { throw new CatalogException("catalog.json is malformed: " + e.Message); }
            if (catalog == null) throw new CatalogException("catalog.json is empty.");
            ValidateContent(catalog);
            return catalog;
        }

        public static MapDefinition ParseMap(string json)
        {
            MapDefinition map;
            try { map = JsonUtility.FromJson<MapDefinition>(json); }
            catch (Exception e) { throw new CatalogException("Map JSON is malformed: " + e.Message); }
            if (map == null) throw new CatalogException("Map JSON is empty.");
            return map;
        }

        // ---------------------------------------------------------------- lookups

        public static ItemDefinition GetItem(ContentCatalog c, string id) { return Find(Index(c).Items, id); }
        public static RecipeDefinition GetRecipe(ContentCatalog c, string id) { return Find(Index(c).Recipes, id); }
        public static BuildingDefinition GetBuilding(ContentCatalog c, string id) { return Find(Index(c).Buildings, id); }
        public static CraftDefinition GetCraft(ContentCatalog c, string id) { return Find(Index(c).Crafts, id); }
        public static UpgradeDefinition GetUpgrade(ContentCatalog c, string id) { return Find(Index(c).Upgrades, id); }
        public static TechDefinition GetTech(ContentCatalog c, string id) { return Find(Index(c).Techs, id); }

        public static bool TryGetItem(ContentCatalog c, string id, out ItemDefinition def) { def = GetItem(c, id); return def != null; }
        public static bool TryGetRecipe(ContentCatalog c, string id, out RecipeDefinition def) { def = GetRecipe(c, id); return def != null; }
        public static bool TryGetBuilding(ContentCatalog c, string id, out BuildingDefinition def) { def = GetBuilding(c, id); return def != null; }
        public static bool TryGetCraft(ContentCatalog c, string id, out CraftDefinition def) { def = GetCraft(c, id); return def != null; }

        static T Find<T>(Dictionary<string, T> table, string id) where T : class
        {
            if (string.IsNullOrEmpty(id)) return null;
            T value;
            return table.TryGetValue(id, out value) ? value : null;
        }

        static CatalogIndex Index(ContentCatalog c)
        {
            if (c == null) throw new CatalogException("Content catalog is not loaded.");
            CatalogIndex index;
            if (Indexes.TryGetValue(c, out index)) return index;
            index = BuildIndex(c);
            Indexes.Add(c, index);
            return index;
        }

        static CatalogIndex BuildIndex(ContentCatalog c)
        {
            var index = new CatalogIndex();
            AddAll(index.Items, c.items, x => x.id, "item");
            AddAll(index.Recipes, c.recipes, x => x.id, "recipe");
            AddAll(index.Buildings, c.buildings, x => x.id, "building");
            AddAll(index.Crafts, c.crafts, x => x.id, "craft");
            AddAll(index.Upgrades, c.upgrades, x => x.id, "upgrade");
            AddAll(index.Techs, c.techs, x => x.id, "tech");
            return index;
        }

        static void AddAll<T>(Dictionary<string, T> table, List<T> list, Func<T, string> key, string label) where T : class
        {
            if (list == null) throw new CatalogException("Catalog " + label + " list is missing.");
            foreach (var entry in list)
            {
                if (entry == null) throw new CatalogException("Catalog contains a null " + label + ".");
                string id = key(entry);
                if (string.IsNullOrEmpty(id)) throw new CatalogException("A " + label + " has an empty ID.");
                if (table.ContainsKey(id)) throw new CatalogException("Duplicate " + label + " ID '" + id + "'.");
                table.Add(id, entry);
            }
        }

        // ---------------------------------------------------------------- content validation

        public static void ValidateContent(ContentCatalog c)
        {
            if (c.version != SupportedCatalogVersion)
                throw new CatalogException("Catalog version " + c.version + " is not supported (expected " + SupportedCatalogVersion + ").");
            if (c.rules == null) throw new CatalogException("Catalog rules are missing.");
            var r = c.rules;
            RequirePositive(r.tickMilliseconds, "rules.tickMilliseconds");
            RequirePositive(r.graceTicks, "rules.graceTicks");
            RequirePositive(r.normalCaptureTicks, "rules.normalCaptureTicks");
            RequirePositive(r.tacticalActionsPerSide, "rules.tacticalActionsPerSide");
            RequirePositive(r.worldMoveTicksPerCell, "rules.worldMoveTicksPerCell");
            RequirePositive(r.beltTravelTicksPerSegment, "rules.beltTravelTicksPerSegment");
            RequirePositive(r.beltCapacityPerSegment, "rules.beltCapacityPerSegment");
            RequirePositive(r.autosaveEveryTicks, "rules.autosaveEveryTicks");
            RequirePositive(r.maxCatchUpTicksPerFrame, "rules.maxCatchUpTicksPerFrame");
            RequirePositive(r.aiCandidateChecksPerTick, "rules.aiCandidateChecksPerTick");
            RequirePositive(r.playerSalesGoal, "rules.playerSalesGoal");
            RequireNonNegative(r.startingCurrency, "rules.startingCurrency");
            RequireNonNegative(r.beltCost, "rules.beltCost");
            RequireNonNegative(r.raidWarningTicks, "rules.raidWarningTicks");
            RequirePositive(r.raidIntervalTicks, "rules.raidIntervalTicks");

            // Builds the index and rejects duplicate or empty IDs.
            Indexes.Remove(c);
            Index(c);

            foreach (var item in c.items)
            {
                RequireNonNegative(item.baseSaleValue, "item " + item.id + " baseSaleValue");
                if (item.sellable && item.baseSaleValue <= 0) throw new CatalogException("Sellable item " + item.id + " needs a positive sale value.");
            }
            foreach (var recipe in c.recipes)
            {
                if (recipe.inputs == null || recipe.inputs.Count == 0) throw new CatalogException("Recipe " + recipe.id + " has no inputs.");
                foreach (var input in recipe.inputs)
                {
                    if (input == null || GetItem(c, input.itemId) == null) throw new CatalogException("Recipe " + recipe.id + " uses a missing ingredient.");
                    RequirePositive(input.quantity, "recipe " + recipe.id + " input quantity");
                }
                if (GetItem(c, recipe.outputItemId) == null) throw new CatalogException("Recipe " + recipe.id + " outputs missing item '" + recipe.outputItemId + "'.");
                RequirePositive(recipe.outputQuantity, "recipe " + recipe.id + " outputQuantity");
                RequirePositive(recipe.craftTicks, "recipe " + recipe.id + " craftTicks");
                if (recipe.buildingKind != BuildingKind.Refinery && recipe.buildingKind != BuildingKind.Assembler)
                    throw new CatalogException("Recipe " + recipe.id + " must run in a refinery or assembler.");
                if (!string.IsNullOrEmpty(recipe.requiredTechId) && GetTech(c, recipe.requiredTechId) == null)
                    throw new CatalogException("Recipe " + recipe.id + " requires missing tech '" + recipe.requiredTechId + "'.");
            }
            foreach (var b in c.buildings)
            {
                RequireNonNegative(b.cost, "building " + b.id + " cost");
                if (b.width != 1 || b.height != 1) throw new CatalogException("Building " + b.id + " must be 1x1 in catalog version 1.");
                RequirePositive(b.inventoryCapacity, "building " + b.id + " inventoryCapacity");
                if (b.allowedRecipeIds == null) throw new CatalogException("Building " + b.id + " has no allowedRecipeIds list.");
                foreach (var rid in b.allowedRecipeIds)
                {
                    var recipe = GetRecipe(c, rid);
                    if (recipe == null) throw new CatalogException("Building " + b.id + " allows missing recipe '" + rid + "'.");
                    if (recipe.buildingKind != b.kind) throw new CatalogException("Recipe " + rid + " does not run in building kind " + b.kind + ".");
                }
            }
            foreach (var k in c.crafts)
            {
                string p = "craft " + k.id + " ";
                RequireNonNegative(k.cost, p + "cost");
                RequirePositive(k.movement, p + "movement");
                RequireNonNegative(k.range, p + "range");
                RequireNonNegative(k.attackDamage, p + "attackDamage");
                RequireNonNegative(k.rangedAttackDamage, p + "rangedAttackDamage");
                RequireNonNegative(k.airAttackDamage, p + "airAttackDamage");
                RequireNonNegative(k.defenceDamage, p + "defenceDamage");
                RequireNonNegative(k.rangedDefenceDamage, p + "rangedDefenceDamage");
                RequireNonNegative(k.airDefenceDamage, p + "airDefenceDamage");
                RequirePositive(k.maxHealth, p + "maxHealth");
                RequireNonNegative(k.armour, p + "armour");
                RequireNonNegative(k.defenderArmourBonus, p + "defenderArmourBonus");
                RequireNonNegative(k.defenderCounterBonus, p + "defenderCounterBonus");
            }
            foreach (var u in c.upgrades)
            {
                RequireNonNegative(u.cost, "upgrade " + u.id + " cost");
                RequirePositive(u.saleMultiplierPercent, "upgrade " + u.id + " saleMultiplierPercent");
            }
            foreach (var t in c.techs)
            {
                RequireNonNegative(t.cost, "tech " + t.id + " cost");
                if (!string.IsNullOrEmpty(t.unlockedItemId) && GetItem(c, t.unlockedItemId) == null)
                    throw new CatalogException("Tech " + t.id + " unlocks missing item '" + t.unlockedItemId + "'.");
                if (!string.IsNullOrEmpty(t.unlockedRecipeId) && GetRecipe(c, t.unlockedRecipeId) == null)
                    throw new CatalogException("Tech " + t.id + " unlocks missing recipe '" + t.unlockedRecipeId + "'.");
            }
        }

        // ---------------------------------------------------------------- map validation

        public static void ValidateMap(MapDefinition map, ContentCatalog c)
        {
            if (map == null) throw new CatalogException("Map is missing.");
            if (map.cells == null || map.cells.Count == 0) throw new CatalogException("Map " + map.id + " has no cells.");
            if (map.startingCrafts == null) throw new CatalogException("Map " + map.id + " has no startingCrafts list.");
            var cellIds = new HashSet<int>();
            var coords = new HashSet<long>();
            var nodeIds = new HashSet<int>();
            int playerHq = 0, aiHq = 0;
            foreach (var cell in map.cells)
            {
                if (cell == null) throw new CatalogException("Map " + map.id + " has a null cell.");
                if (!cellIds.Add(cell.id)) throw new CatalogException("Duplicate cell ID " + cell.id + ".");
                if (cell.id < 0) throw new CatalogException("Cell IDs must not be negative.");
                if (!coords.Add(((long)cell.mapX << 32) | (uint)cell.mapY)) throw new CatalogException("Two cells share map position " + cell.mapX + "," + cell.mapY + ".");
                if (cell.mapX < 0 || cell.mapY < 0) throw new CatalogException("Cell " + cell.id + " has a negative map position.");
                if (cell.gridWidth != CellGridSize || cell.gridHeight != CellGridSize)
                    throw new CatalogException("Cell " + cell.id + " must be " + CellGridSize + "x" + CellGridSize + ".");
                if (cell.headquartersFor == FactionId.Player) { playerHq++; if (cell.startingOwner != FactionId.Player) throw new CatalogException("Player HQ cell must start player-owned."); }
                if (cell.headquartersFor == FactionId.Ai) { aiHq++; if (cell.startingOwner != FactionId.Ai) throw new CatalogException("AI HQ cell must start AI-owned."); }
                if (cell.blockedTiles == null || cell.nodes == null) throw new CatalogException("Cell " + cell.id + " is missing blockedTiles or nodes.");
                var used = new HashSet<int>();
                foreach (var t in cell.blockedTiles)
                {
                    if (t == null || !InGrid(cell, t.x, t.y)) throw new CatalogException("Cell " + cell.id + " has an off-grid blocked tile.");
                    if (!used.Add(t.y * 64 + t.x)) throw new CatalogException("Cell " + cell.id + " has overlapping blocked tiles at " + t.x + "," + t.y + ".");
                }
                foreach (var n in cell.nodes)
                {
                    if (n == null || !InGrid(cell, n.x, n.y)) throw new CatalogException("Cell " + cell.id + " has an off-grid resource node.");
                    if (!used.Add(n.y * 64 + n.x)) throw new CatalogException("Node " + n.id + " overlaps another node or blocked tile.");
                    if (!nodeIds.Add(n.id)) throw new CatalogException("Duplicate node ID " + n.id + ".");
                    if (GetItem(c, n.itemId) == null) throw new CatalogException("Node " + n.id + " produces missing item '" + n.itemId + "'.");
                    RequirePositive(n.rateTicks, "node " + n.id + " rateTicks");
                }
            }
            if (playerHq != 1 || aiHq != 1) throw new CatalogException("Map " + map.id + " needs exactly one HQ for each side.");
            var defended = new HashSet<int>();
            foreach (var s in map.startingCrafts)
            {
                if (s == null) throw new CatalogException("Map has a null starting craft.");
                if (GetCraft(c, s.craftTypeId) == null) throw new CatalogException("Starting craft type '" + s.craftTypeId + "' does not exist.");
                var cell = map.cells.Find(x => x.id == s.cellId);
                if (cell == null) throw new CatalogException("Starting craft references missing cell " + s.cellId + ".");
                if (s.faction == FactionId.Neutral) throw new CatalogException("Starting craft must belong to a faction.");
                if (s.assignedDefender)
                {
                    if (cell.startingOwner != s.faction) throw new CatalogException("Starting defender in cell " + s.cellId + " is not owned by the cell owner.");
                    if (!defended.Add(s.cellId)) throw new CatalogException("Cell " + s.cellId + " has two starting defenders.");
                }
            }
        }

        static bool InGrid(MapCellDefinition cell, int x, int y)
        {
            return x >= 0 && y >= 0 && x < cell.gridWidth && y < cell.gridHeight;
        }

        static void RequirePositive(int value, string label)
        {
            if (value <= 0) throw new CatalogException(label + " must be positive (was " + value + ").");
        }

        static void RequireNonNegative(int value, string label)
        {
            if (value < 0) throw new CatalogException(label + " must not be negative (was " + value + ").");
        }
    }
}
