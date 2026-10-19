using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// One normal-play factory tick in fixed order: construction, power, extraction, recipes, shipyards, pull
    /// requests, token movement and delivery, then depot sales. Never runs in Tactical or Ended mode.
    /// Power: active solar (Headquarters panels and Solar Arrays) supplies MW; each working machine draws MW. When
    /// demand exceeds supply, every machine of that faction advances only supply / demand of a step per tick.
    /// </summary>
    public sealed class FactorySimulation
    {
        /// <summary>Presentation-only notifications with the building that produced or sold the item.</summary>
        public event Action<BuildingState, string> ItemProduced;
        public event Action<BuildingState, string> ItemSold;
        // Machine speed this tick per faction (index = FactionId), from supply / demand.
        readonly float[] powerFactor = NewPowerFactors();

        static float[] NewPowerFactors()
        {
            var f = new float[WorldLookup.FactionSlots];
            for (int i = 0; i < f.Length; i++) f[i] = 1f;
            return f;
        }

        /// <summary>Ticks the uptime average spans (30 s at 10 ticks a second).</summary>
        public const int UptimeTicks = 300;
        // Runtime only, for the panel: each machine's share of recent ticks spent working (scaled by power).
        readonly Dictionary<int, float> uptime = new Dictionary<int, float>();
        WorldState uptimeWorld;

        /// <summary>
        /// The share (0-1) of the last <see cref="UptimeTicks"/> ticks a machine spent working: 1 is never paused for
        /// want of ingredients, a full store or power. -1 for buildings that are not machines or have no history yet.
        /// </summary>
        public float Uptime(int buildingId)
        {
            float u;
            return uptime.TryGetValue(buildingId, out u) ? u : -1f;
        }

        /// <summary>Samples whether each extractor, refinery, assembler and shipyard worked this tick.</summary>
        void SampleUptime(WorldState w, ContentCatalog c)
        {
            if (uptimeWorld != w) { uptime.Clear(); uptimeWorld = w; }
            const float step = 1f / UptimeTicks;
            foreach (var b in w.buildings)
            {
                if (b.underConstruction) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Extractor && def.kind != BuildingKind.Refinery
                    && def.kind != BuildingKind.Assembler && def.kind != BuildingKind.Shipyard)) continue;
                float sample = WorldLookup.IsBuildingActive(w, b) && WantsToWork(w, c, b, def) ? powerFactor[(int)b.owner] : 0f;
                float u;
                uptime[b.id] = uptime.TryGetValue(b.id, out u) ? u + (sample - u) * step : sample;
            }
            if (uptime.Count > w.buildings.Count + 16)
            {
                var live = new HashSet<int>();
                foreach (var b in w.buildings) live.Add(b.id);
                var gone = new List<int>();
                foreach (var id in uptime.Keys) if (!live.Contains(id)) gone.Add(id);
                foreach (var id in gone) uptime.Remove(id);
            }
        }

        public void Tick(WorldState w, ContentCatalog c, MapDefinition map, BeltNetwork network, PullRouter router, EconomyService economy)
        {
            if (w.mode != MatchMode.Factory) return;
            network.EnsureCurrent(w);
            long t = Perf.Begin();
            SampleUptime(w, c);
            Stats.Advance(w, c);
            TradeService.Advance(w, c);
            TickConstruction(w, c, map);
            Perf.End(Perf.Stage.Construction, t);
            t = Perf.Begin();
            OwnershipService.RefreshFactoryOnline(w);
            UpdatePower(w, c);
            Perf.End(Perf.Stage.Power, t);
            t = Perf.Begin();
            TickExtractors(w, c);
            Perf.End(Perf.Stage.Extractors, t);
            t = Perf.Begin();
            TickRecipes(w, c);
            TickShipyards(w, c);
            Perf.End(Perf.Stage.Recipes, t);
            t = Perf.Begin();
            router.RefreshRequests(w, c, network);
            Perf.End(Perf.Stage.Requests, t);
            t = Perf.Begin();
            router.DispatchOldest(w, c, network);
            Perf.End(Perf.Stage.Dispatch, t);
            t = Perf.Begin();
            TickTokens(w, c, network);
            Perf.End(Perf.Stage.Tokens, t);
            t = Perf.Begin();
            SellDepots(w, c, economy);
            Perf.End(Perf.Stage.Sales, t);
        }

        /// <summary>
        /// A construction zone with every material in stock deploys its building (a Fort) into the neighbouring cell
        /// it faces, then disappears. It waits, holding its materials, until its owner holds that cell.
        /// </summary>
        static void TickConstruction(WorldState w, ContentCatalog c, MapDefinition map)
        {
            foreach (var b in w.buildings.ToArray())
            {
                // The zone's own cell must be held and online, like any machine there.
                if (!b.underConstruction || !WorldLookup.IsBuildingHeld(w, b) || !WorldLookup.GetCell(w, b.cellId).factoryOnline) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || !HasAll(b, def.materials)) continue;
                var deploy = CatalogRepository.GetBuilding(c, def.deploysBuildingId);
                var target = WorldLookup.GetCell(w, b.targetCellId);
                var targetDef = WorldLookup.GetCellDef(map, b.targetCellId);
                if (deploy == null || target == null || targetDef == null || target.owner != b.owner) continue;
                if (OwnershipService.HasOwnAnchor(w, c, target.id, b.owner)) continue;
                int x, y;
                if (!GameFactory.FindCentreTile(w, targetDef, out x, out y)) continue;
                w.buildings.Add(new BuildingState { id = WorldLookup.AllocateId(w), owner = b.owner, typeId = deploy.id, cellId = target.id, x = x, y = y, anchor = true });
                w.pullRequests.RemoveAll(r => r.targetBuildingId == b.id);
                w.buildings.Remove(b);
                WorldLookup.AddAlert(w, AlertKind.Territory, deploy.displayName + " deployed into cell " + target.id + ". You can build there now.", target.id, b.owner);
            }
        }


        /// <summary>Totals each faction's supply and the draw of every machine that would work this tick.</summary>
        void UpdatePower(WorldState w, ContentCatalog c)
        {
            foreach (var f in w.factions) { f.powerSupply = 0; f.powerDemand = 0; }
            foreach (var b in w.buildings)
            {
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || !WorldLookup.IsBuildingActive(w, b)) continue;
                var f = WorldLookup.GetFaction(w, b.owner);
                if (f == null) continue;
                if (def.powerSupply > 0) f.powerSupply += ResearchService.SolarOutput(c, f, def.powerSupply);
                if (def.powerDraw > 0 && WantsToWork(w, c, b, def)) f.powerDemand += def.powerDraw;
            }
            for (int i = 0; i < powerFactor.Length; i++) powerFactor[i] = 1f;
            foreach (var f in w.factions)
                powerFactor[(int)f.id] = f.powerDemand <= f.powerSupply ? 1f : (float)f.powerSupply / f.powerDemand;
        }

        /// <summary>Machine speed for a faction this tick: 1 when supply covers demand.</summary>
        public float PowerFactor(FactionId faction) { return powerFactor[(int)faction]; }

        /// <summary>The last minute's production, use and sales, for the production overview.</summary>
        public readonly ProductionStats Stats = new ProductionStats();

        /// <summary>Why a machine is holding up its line, for the bottleneck markers.</summary>
        public enum Stall { None, NoRecipe, Starved, BackedUp, LowPower }

        /// <summary>
        /// Uptime below which a short wait counts as a bottleneck. Machines pause briefly between deliveries all the
        /// time; only one that has spent much of the last half-minute stopped is marked.
        /// </summary>
        public const float StallUptime = 0.6f;

        /// <summary>
        /// What is holding a working extractor, refinery, assembler or busy shipyard back: no recipe chosen, an input
        /// missing with none on the way (starved), nowhere for its output to go (backed up), or a power shortfall.
        /// None for anything running freely, stopped, under construction, or not a machine.
        /// </summary>
        public Stall StallOf(WorldState w, ContentCatalog c, BuildingState b)
        {
            if (b.underConstruction || !WorldLookup.IsBuildingActive(w, b)) return Stall.None;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return Stall.None;
            bool recipeMachine = def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler;
            if (recipeMachine && string.IsNullOrEmpty(b.selectedRecipeId)) return Stall.NoRecipe;
            if (!recipeMachine && def.kind != BuildingKind.Extractor && def.kind != BuildingKind.Shipyard) return Stall.None;
            if (def.kind == BuildingKind.Shipyard && CatalogRepository.GetCraft(c, b.orderedCraftTypeId) == null) return Stall.None; // idle yard
            float up = Uptime(b.id);
            if (WantsToWork(w, c, b, def))
                return powerFactor[(int)b.owner] < 0.95f && up >= 0f && up < StallUptime ? Stall.LowPower : Stall.None;
            if (up < 0f || up >= StallUptime) return Stall.None;
            if (def.kind == BuildingKind.Extractor) return Stall.BackedUp; // only waits for room to put its ore
            if (recipeMachine)
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                if (recipe == null) return Stall.None;
                var faction = WorldLookup.GetFaction(w, b.owner);
                if (b.recipeProgressTicks >= ResearchService.CraftTicks(c, faction, b.typeId, recipe) || IsStorageFull(w, c, b)) return Stall.BackedUp;
                foreach (var need in recipe.inputs)
                    if (WorldLookup.CountItem(b.inventory, need.itemId) < need.quantity && Inbound(w, b.id, need.itemId) == 0) return Stall.Starved;
                return Stall.None;
            }
            var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
            foreach (var need in craft.components)
                if (WorldLookup.CountItem(b.inventory, need.itemId) < need.quantity && Inbound(w, b.id, need.itemId) == 0) return Stall.Starved;
            return Stall.None;
        }

        /// <summary>
        /// True when an active machine would advance this tick: an extractor counting down or with buffer room, a
        /// recipe machine with its inputs and progress to make, a shipyard with its parts. Holding machines draw nothing.
        /// </summary>
        public static bool WantsToWork(WorldState w, ContentCatalog c, BuildingState b, BuildingDefinition def)
        {
            switch (def.kind)
            {
                case BuildingKind.Extractor:
                {
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    if (node == null) return false;
                    int output = ResearchService.ExtractionOutput(c, WorldLookup.GetFaction(w, b.owner), node.itemId);
                    return node.ticksUntilOutput > 0 || node.itemId == c.rules.ionItemId
                        || WorldLookup.TotalItems(b.inventory) + output <= ResearchService.ExtractorCapacity(def, output);
                }
                case BuildingKind.Refinery:
                case BuildingKind.Assembler:
                {
                    if (string.IsNullOrEmpty(b.selectedRecipeId) || !def.allowedRecipeIds.Contains(b.selectedRecipeId)) return false;
                    var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    return ResearchService.IsRecipeUnlocked(c, faction, recipe) && HasInputs(b, recipe)
                        && b.recipeProgressTicks < ResearchService.CraftTicks(c, faction, b.typeId, recipe);
                }
                case BuildingKind.Shipyard:
                {
                    var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                    if (craft != null) return HasComponents(b, craft) && b.recipeProgressTicks < craft.buildTicks;
                    var damaged = RepairTarget(w, c, b);
                    var d = damaged != null ? CatalogRepository.GetCraft(c, damaged.typeId) : null;
                    return d != null && HasAll(b, d.repairParts); // repairing a docked craft
                }
                default: return false;
            }
        }

        /// <summary>Banks this tick's share of power; true when a whole work step is available.</summary>
        bool Step(BuildingState b)
        {
            float factor = powerFactor[(int)b.owner];
            if (factor >= 1f) { b.powerCharge = 0f; return true; }
            b.powerCharge += factor;
            if (b.powerCharge < 1f) return false;
            b.powerCharge -= 1f;
            return true;
        }

        void TickExtractors(WorldState w, ContentCatalog c)
        {
            foreach (var node in w.nodes)
            {
                if (node.extractorBuildingId == -1) continue;
                var b = WorldLookup.GetBuilding(w, node.extractorBuildingId);
                if (b == null) { node.extractorBuildingId = -1; continue; }
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (node.ticksUntilOutput > 0)
                {
                    if (!Step(b)) continue;
                    node.ticksUntilOutput--;
                    if (node.ticksUntilOutput > 0) continue;
                }
                var owner = WorldLookup.GetFaction(w, b.owner);
                if (node.itemId == c.rules.ionItemId)
                {
                    // Ion goes straight to the owner's stockpile and never enters the extractor or a belt.
                    int ion = ResearchService.IonAmount(c, owner, c.rules.ionPerExtraction);
                    if (owner != null) owner.ion = EconomyService.SaturatingAdd(owner.ion, ion);
                    Stats.Add(b.owner, ProductionStats.Kind.Made, node.itemId, ion);
                    node.ticksUntilOutput = ResearchService.NextExtractionTicks(c, owner, node.itemId, node.rateTicks, ref node.extractionCarry);
                    continue;
                }
                // Hold at zero until the whole cycle's output fits; output as soon as room opens.
                int output = ResearchService.ExtractionOutput(c, owner, node.itemId);
                if (def == null || WorldLookup.TotalItems(b.inventory) + output > ResearchService.ExtractorCapacity(def, output)) continue;
                WorldLookup.AddItem(b.inventory, node.itemId, output);
                Stats.Add(b.owner, ProductionStats.Kind.Made, node.itemId, output);
                node.ticksUntilOutput = ResearchService.NextExtractionTicks(c, owner, node.itemId, node.rateTicks, ref node.extractionCarry);
            }
        }

        void TickRecipes(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) continue;
                if (string.IsNullOrEmpty(b.selectedRecipeId) || !def.allowedRecipeIds.Contains(b.selectedRecipeId)) continue;
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                var faction = WorldLookup.GetFaction(w, b.owner);
                if (!ResearchService.IsRecipeUnlocked(c, faction, recipe)) continue;
                if (!HasInputs(b, recipe)) continue;

                int craftTicks = ResearchService.CraftTicks(c, faction, b.typeId, recipe);
                if (b.recipeProgressTicks < craftTicks)
                {
                    if (!Step(b)) continue;
                    b.recipeProgressTicks++;
                    if (b.recipeProgressTicks < craftTicks) continue;
                }

                // Hold the finished cycle until its product's bucket has room; ingredients have buckets of their own.
                if (!ExitRoom(b, def, recipe)) continue;

                foreach (var i in recipe.inputs) { WorldLookup.RemoveItem(b.inventory, i.itemId, i.quantity); Stats.Add(b.owner, ProductionStats.Kind.Used, i.itemId, i.quantity); }
                WorldLookup.AddItem(b.inventory, recipe.outputItemId, recipe.outputQuantity);
                Stats.Add(b.owner, ProductionStats.Kind.Made, recipe.outputItemId, recipe.outputQuantity);
                TradeService.RecordMade(w, b.owner, recipe.outputItemId, recipe.outputQuantity);
                if (ItemProduced != null) ItemProduced(b, recipe.outputItemId);
                b.recipeProgressTicks = 0;
            }
        }

        /// <summary>
        /// A shipyard with an order builds while every component is in stock, then launches the craft in its cell.
        /// With no order it repairs the most damaged of its owner's craft docked in its cell, one HP per delivery of
        /// that craft's repair parts.
        /// </summary>
        void TickShipyards(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (string.IsNullOrEmpty(b.orderedCraftTypeId))
                {
                    TickRepair(w, c, b);
                    continue;
                }
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                if (craft == null) { b.orderedCraftTypeId = ""; continue; }
                if (!HasComponents(b, craft)) continue;
                if (b.recipeProgressTicks < craft.buildTicks)
                {
                    if (!Step(b)) continue;
                    b.recipeProgressTicks++;
                    if (b.recipeProgressTicks < craft.buildTicks) continue;
                }
                foreach (var part in craft.components) { WorldLookup.RemoveItem(b.inventory, part.itemId, part.quantity); Stats.Add(b.owner, ProductionStats.Kind.Used, part.itemId, part.quantity); }
                b.recipeProgressTicks = 0;
                b.orderedCraftTypeId = "";
                EconomyService.SpawnCraft(w, craft, b.owner, b.cellId);
                WorldLookup.AddAlert(w, AlertKind.Info, craft.displayName + " launched from your shipyard in cell " + b.cellId + ".", b.cellId, b.owner);
            }
        }

        void TickRepair(WorldState w, ContentCatalog c, BuildingState yard)
        {
            var def = CatalogRepository.GetBuilding(c, yard.typeId);
            if (def == null || def.kind != BuildingKind.Shipyard || !WorldLookup.IsBuildingActive(w, yard)) return;
            var craft = RepairTarget(w, c, yard);
            var craftDef = craft != null ? CatalogRepository.GetCraft(c, craft.typeId) : null;
            if (craftDef == null || !HasAll(yard, craftDef.repairParts)) return;
            if (yard.recipeProgressTicks < craftDef.repairTicks)
            {
                if (!Step(yard)) return;
                yard.recipeProgressTicks++;
                if (yard.recipeProgressTicks < craftDef.repairTicks) return;
            }
            foreach (var part in craftDef.repairParts) { WorldLookup.RemoveItem(yard.inventory, part.itemId, part.quantity); Stats.Add(yard.owner, ProductionStats.Kind.Used, part.itemId, part.quantity); }
            yard.recipeProgressTicks = 0;
            craft.currentHealth = System.Math.Min(craftDef.maxHealth, craft.currentHealth + 1);
            if (craft.currentHealth == craftDef.maxHealth)
                WorldLookup.AddAlert(w, AlertKind.Info, craftDef.displayName + " fully repaired at your shipyard in cell " + yard.cellId + ".", yard.cellId, craft.owner);
        }

        /// <summary>
        /// The craft an idle shipyard repairs: the most damaged (by share of HP) of its owner's craft docked in its cell,
        /// that is standing there rather than passing through, with repair parts defined; null when there is none.
        /// </summary>
        public static CraftState RepairTarget(WorldState w, ContentCatalog c, BuildingState yard)
        {
            CraftState best = null;
            float worst = 1f;
            foreach (var k in w.crafts)
            {
                if (k.owner != yard.owner || k.cellId != yard.cellId || !WorldLookup.IsCraftLive(k) || k.worldPathCellIds.Count > 0) continue;
                var d = CatalogRepository.GetCraft(c, k.typeId);
                if (d == null || d.repairParts.Count == 0 || k.currentHealth >= d.maxHealth) continue;
                float share = k.currentHealth / (float)d.maxHealth;
                if (best == null || share < worst || (share == worst && k.id < best.id)) { best = k; worst = share; }
            }
            return best;
        }

        /// <summary>
        /// The cell of the faction's working shipyard closest to the craft by route (its own cell when one is
        /// there already), or -1 when none can be reached.
        /// </summary>
        public static int NearestRepairYard(WorldState w, ContentCatalog c, MapDefinition map, MovementRules movement, CraftState craft)
        {
            int best = -1, bestLength = int.MaxValue;
            foreach (var b in w.buildings)
            {
                if (b.owner != craft.owner || b.underConstruction || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.kind != BuildingKind.Shipyard) continue;
                if (b.cellId == craft.cellId) return b.cellId; // already docked
                var path = movement.FindWorldPath(w, map, craft.owner, craft.cellId, b.cellId);
                if (path != null && path.Count < bestLength) { best = b.cellId; bestLength = path.Count; }
            }
            return best;
        }

        /// <summary>What a shipyard wants delivered now: its order's parts, else one HP of repairs for a docked craft.</summary>
        public static List<IngredientDefinition> ShipyardNeeds(WorldState w, ContentCatalog c, BuildingState yard)
        {
            var craft = CatalogRepository.GetCraft(c, yard.orderedCraftTypeId);
            if (craft != null) return craft.components;
            var damaged = RepairTarget(w, c, yard);
            var d = damaged != null ? CatalogRepository.GetCraft(c, damaged.typeId) : null;
            return d != null ? d.repairParts : NoNeeds;
        }

        static readonly List<IngredientDefinition> NoNeeds = new List<IngredientDefinition>();

        /// <summary>
        /// The items a building is currently waiting to receive: its selected recipe's inputs, or its shipyard order's
        /// parts. Extractors and depots have none.
        /// </summary>
        public static List<IngredientDefinition> CurrentNeeds(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return NoNeeds;
            if (b.underConstruction) return def.materials;
            if (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler)
            {
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                return recipe != null && def.allowedRecipeIds.Contains(recipe.id) ? recipe.inputs : NoNeeds;
            }
            if (def.kind == BuildingKind.Shipyard) return ShipyardNeeds(w, c, b);
            return NoNeeds;
        }

        /// <summary>
        /// True when a machine's storage is full, so it cannot go on until something takes its output: for a refinery
        /// or assembler, its product's bucket has no room for another cycle; otherwise stock plus items on the way fill it.
        /// </summary>
        public static bool IsStorageFull(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return false;
            var recipe = RecipeOf(c, b, def);
            if (recipe != null) return !ExitRoom(b, def, recipe);
            int inbound = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == b.id) inbound += t.Units;
            return WorldLookup.TotalItems(b.inventory) + inbound >= def.inventoryCapacity;
        }

        /// <summary>
        /// The recipe whose buckets a refinery or assembler keeps (see <see cref="BucketCapacity"/>): the one selected,
        /// once built. Null for every other building, whose whole inventory is one store bounded by inventoryCapacity.
        /// </summary>
        public static RecipeDefinition RecipeOf(ContentCatalog c, BuildingState b, BuildingDefinition def)
        {
            if (def == null || b.underConstruction || (def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) return null;
            if (string.IsNullOrEmpty(b.selectedRecipeId) || !def.allowedRecipeIds.Contains(b.selectedRecipeId)) return null;
            return CatalogRepository.GetRecipe(c, b.selectedRecipeId);
        }

        /// <summary>
        /// A refinery or assembler keeps a bucket for each item, like a Satisfactory machine's slots: one per ingredient
        /// and one for its product, each holding inventoryCapacity units (and always two cycles' worth). Ingredients
        /// never crowd each other out, and finished goods never take the room promised to loads still on their way.
        /// Leftovers from an earlier recipe sit in buckets of their own.
        /// </summary>
        public static int BucketCapacity(BuildingDefinition def, RecipeDefinition recipe, string itemId)
        {
            int perCycle = recipe.outputItemId == itemId ? recipe.outputQuantity : 0;
            foreach (var i in recipe.inputs) if (i.itemId == itemId) perCycle += i.quantity;
            return System.Math.Max(def.inventoryCapacity, 2 * perCycle);
        }

        /// <summary>True when the product's bucket can take another cycle's output.</summary>
        public static bool ExitRoom(BuildingState b, BuildingDefinition def, RecipeDefinition recipe)
        {
            return WorldLookup.CountItem(b.inventory, recipe.outputItemId) + recipe.outputQuantity <= BucketCapacity(def, recipe, recipe.outputItemId);
        }

        /// <summary>
        /// True when a load can be put away on arrival: into its own bucket in a refinery or assembler, and into the
        /// one shared inventory of any other building.
        /// </summary>
        public static bool HasRoomFor(ContentCatalog c, BuildingState b, BuildingDefinition def, string itemId, int units)
        {
            var recipe = RecipeOf(c, b, def);
            if (recipe == null) return WorldLookup.TotalItems(b.inventory) + units <= def.inventoryCapacity;
            return WorldLookup.CountItem(b.inventory, itemId) + units <= BucketCapacity(def, recipe, itemId);
        }

        /// <summary>Items of this type currently travelling to the building.</summary>
        public static int Inbound(WorldState w, int buildingId, string itemId)
        {
            int n = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == buildingId && t.itemId == itemId) n += t.Units;
            return n;
        }

        public static bool HasAll(BuildingState b, List<IngredientDefinition> goods)
        {
            foreach (var g in goods)
                if (WorldLookup.CountItem(b.inventory, g.itemId) < g.quantity) return false;
            return true;
        }

        public static bool HasComponents(BuildingState b, CraftDefinition craft)
        {
            foreach (var part in craft.components)
                if (WorldLookup.CountItem(b.inventory, part.itemId) < part.quantity) return false;
            return true;
        }

        public static bool HasInputs(BuildingState b, RecipeDefinition recipe)
        {
            foreach (var i in recipe.inputs)
                if (WorldLookup.CountItem(b.inventory, i.itemId) < i.quantity) return false;
            return true;
        }

        static void TickTokens(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            var removals = new List<ItemTokenState>();
            var stranded = new List<ItemTokenState>();
            var ordered = new List<ItemTokenState>(w.tokens);
            // Items nearest their destination move first, so space they free downstream is there for whoever is
            // behind them; at a merge, an item already waiting goes before a newer arrival. Then oldest first.
            ordered.Sort((a, b) =>
            {
                int ra = a.routeSegmentIds.Count - a.segmentIndex, rb = b.routeSegmentIds.Count - b.segmentIndex;
                if (ra != rb) return ra.CompareTo(rb);
                bool wa = a.status == TokenStatus.Waiting, wb = b.status == TokenStatus.Waiting;
                if (wa != wb) return wa ? -1 : 1;
                return a.id.CompareTo(b.id);
            });
            foreach (var t in ordered)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) { removals.Add(t); continue; }
                var current = network.GetSegment(w, t.routeSegmentIds[t.segmentIndex]);
                if (current == null) { removals.Add(t); continue; }
                if (!WorldLookup.IsBeltActive(w, current)) { t.status = TokenStatus.Frozen; continue; }
                if (t.status == TokenStatus.Frozen) t.status = TokenStatus.Moving;

                if (t.ticksUntilNextSegment > 0) t.ticksUntilNextSegment--;
                if (t.ticksUntilNextSegment > 0) continue;

                // A load with no destination (its building went, or its route was cut) looks for a way on.
                if (t.strandedSinceTick >= 0 || WorldLookup.GetBuilding(w, t.targetBuildingId) == null)
                {
                    if (!Redirect(w, c, network, t, current, stranded))
                    {
                        if (w.tick - t.strandedSinceTick >= StrandedTicks) removals.Add(t); // lost
                        continue;
                    }
                }

                if (t.segmentIndex == t.routeSegmentIds.Count - 1)
                {
                    var target = WorldLookup.GetBuilding(w, t.targetBuildingId);
                    if (target == null) continue; // just redirected onto this belt's end: next tick
                    var def = CatalogRepository.GetBuilding(c, target.typeId);
                    if (!WorldLookup.CanReceive(w, target) || def == null || !HasRoomFor(c, target, def, t.itemId, t.Units))
                    {
                        t.status = TokenStatus.Waiting;
                        continue;
                    }
                    WorldLookup.AddItem(target.inventory, t.itemId, t.Units);
                    BeltNetwork.DroneDelivered(w, c, current);
                    removals.Add(t);
                    continue;
                }

                var next = network.GetSegment(w, t.routeSegmentIds[t.segmentIndex + 1]);
                if (next == null)
                {
                    // A later edge was removed: a new way to the same building, or any other that can use the load.
                    if (!Redirect(w, c, network, t, current, stranded)) continue;
                    next = t.segmentIndex + 1 < t.routeSegmentIds.Count ? network.GetSegment(w, t.routeSegmentIds[t.segmentIndex + 1]) : null;
                    if (next == null) continue;
                }
                if (!WorldLookup.IsBeltActive(w, next) || !network.HasCapacity(w, c, next.id))
                {
                    t.status = TokenStatus.Waiting;
                    continue;
                }
                BeltNetwork.DroneDelivered(w, c, current);
                int ticks = network.Board(w, c, next); // before moving on, so a new trip sees an empty link
                t.segmentIndex++;
                t.ticksUntilNextSegment = ticks;
                t.status = TokenStatus.Moving;
            }
            foreach (var t in removals) w.tokens.Remove(t);
            if (stranded.Count > 0)
            {
                // One alert for everything stranded this tick.
                int units = 0;
                foreach (var t in stranded) units += t.Units;
                var first = network.GetSegment(w, stranded[0].routeSegmentIds[stranded[0].segmentIndex]);
                var item = CatalogRepository.GetItem(c, stranded[0].itemId);
                string what = stranded.Count == 1 ? units + " " + (item != null ? item.displayName : stranded[0].itemId) : units + " goods";
                // Each seat with goods stranded on its belts hears of it once.
                var told = new HashSet<FactionId>();
                foreach (var t in stranded)
                {
                    var belt = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                    if (belt == null || !told.Add(belt.owner)) continue;
                    WorldLookup.AddAlert(w, AlertKind.BlockedRoute, what + " stranded on a belt with no way on. Reconnect it within "
                        + (StrandedTicks * c.rules.tickMilliseconds / 1000) + "s or they are lost.", first != null ? first.toCellId : -1, belt.owner);
                }
            }
        }

        /// <summary>How long a load can sit stranded, still looking for a way on, before it is lost (30 s).</summary>
        public const int StrandedTicks = 300;
        /// <summary>A stranded load looks for a way on this often, rather than every tick.</summary>
        const int StrandedRetryTicks = 10;

        /// <summary>
        /// Finds a load a way on from the end of its current belt: first a new route to its own building, then a
        /// route to the nearest reachable building of its owner's that can take it. False when there is none: the load
        /// is then stranded (no building counts it as on its way), noted in <paramref name="newlyStranded"/> the first time.
        /// </summary>
        static bool Redirect(WorldState w, ContentCatalog c, BeltNetwork network, ItemTokenState t, BeltSegmentState current, List<ItemTokenState> newlyStranded)
        {
            if (t.strandedSinceTick >= 0 && (w.tick - t.strandedSinceTick) % StrandedRetryTicks != 0) return false;
            var target = WorldLookup.GetBuilding(w, t.targetBuildingId);
            if (target != null && Replan(w, network, t, current, target)) { t.strandedSinceTick = -1; return true; }
            foreach (var other in network.BuildingsReachableFrom(w, current.owner, current.toCellId, current.toX, current.toY))
            {
                if (other.owner != current.owner || !PullRouter.CanTake(w, c, other, t.itemId, t.Units)) continue;
                if (!Replan(w, network, t, current, other)) continue;
                t.targetBuildingId = other.id;
                t.strandedSinceTick = -1;
                t.status = TokenStatus.Moving;
                return true;
            }
            if (t.strandedSinceTick < 0)
            {
                t.strandedSinceTick = w.tick;
                newlyStranded.Add(t);
            }
            t.targetBuildingId = -1; // no longer on its way anywhere, so its old building orders again
            t.status = TokenStatus.Waiting;
            return false;
        }

        static bool Replan(WorldState w, BeltNetwork network, ItemTokenState t, BeltSegmentState current, BuildingState target)
        {
            if (target == null) return false;
            if (current.toCellId == target.cellId && current.toX == target.x && current.toY == target.y)
            {
                // Already arriving there: the route ends with this belt.
                t.routeSegmentIds = t.routeSegmentIds.GetRange(0, t.segmentIndex + 1);
                return true;
            }
            var path = network.FindRouteFromVertex(w, current.owner, current.toCellId, current.toX, current.toY, target, null);
            if (!path.success) return false;
            var route = new List<int>();
            for (int i = 0; i <= t.segmentIndex; i++) route.Add(t.routeSegmentIds[i]);
            route.AddRange(path.segmentIds);
            t.routeSegmentIds = route;
            return true;
        }

        void SellDepots(WorldState w, ContentCatalog c, EconomyService economy)
        {
            foreach (var b in w.buildings)
            {
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.kind != BuildingKind.SaleDepot || b.inventory.Count == 0) continue;
                var faction = WorldLookup.GetFaction(w, b.owner);
                for (int i = b.inventory.Count - 1; i >= 0; i--)
                {
                    var stack = b.inventory[i];
                    var item = CatalogRepository.GetItem(c, stack.itemId);
                    if (item == null || !item.sellable) continue;
                    int earned = economy.Sell(w, c, faction, item, stack.quantity);
                    Stats.Add(b.owner, ProductionStats.Kind.Sold, stack.itemId, stack.quantity);
                    TradeService.RecordSold(w, b.owner, stack.itemId, stack.quantity);
                    Stats.AddCredits(b.owner, earned);
                    if (ItemSold != null) ItemSold(b, stack.itemId);
                    b.inventory.RemoveAt(i);
                }
            }
        }
    }
}
