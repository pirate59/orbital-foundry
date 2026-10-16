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
        // Machine speed this tick per faction (index = FactionId), from supply / demand.
        readonly float[] powerFactor = { 1f, 1f, 1f };

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
            router.RefreshRequests(w, c);
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
                if (b.owner == FactionId.Player)
                    WorldLookup.AddAlert(w, AlertKind.Territory, deploy.displayName + " deployed into cell " + target.id + ". You can build there now.", target.id);
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
                    return craft != null && HasComponents(b, craft) && b.recipeProgressTicks < craft.buildTicks;
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
                    if (owner != null) owner.ion = EconomyService.SaturatingAdd(owner.ion, ResearchService.IonAmount(c, owner, c.rules.ionPerExtraction));
                    node.ticksUntilOutput = ResearchService.NextExtractionTicks(c, owner, node.itemId, node.rateTicks, ref node.extractionCarry);
                    continue;
                }
                // Hold at zero until the whole cycle's output fits; output as soon as room opens.
                int output = ResearchService.ExtractionOutput(c, owner, node.itemId);
                if (def == null || WorldLookup.TotalItems(b.inventory) + output > ResearchService.ExtractorCapacity(def, output)) continue;
                WorldLookup.AddItem(b.inventory, node.itemId, output);
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

                int inputTotal = 0;
                foreach (var i in recipe.inputs) inputTotal += i.quantity;
                int after = WorldLookup.TotalItems(b.inventory) - inputTotal + recipe.outputQuantity;
                if (after > def.inventoryCapacity) continue; // hold completed progress until output room opens

                foreach (var i in recipe.inputs) WorldLookup.RemoveItem(b.inventory, i.itemId, i.quantity);
                WorldLookup.AddItem(b.inventory, recipe.outputItemId, recipe.outputQuantity);
                b.recipeProgressTicks = 0;
            }
        }

        /// <summary>A shipyard with an order builds while every component is in stock, then launches the craft in its cell.</summary>
        void TickShipyards(WorldState w, ContentCatalog c)
        {
            foreach (var b in w.buildings)
            {
                if (string.IsNullOrEmpty(b.orderedCraftTypeId) || !WorldLookup.IsBuildingActive(w, b)) continue;
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                if (craft == null) { b.orderedCraftTypeId = ""; continue; }
                if (!HasComponents(b, craft)) continue;
                if (b.recipeProgressTicks < craft.buildTicks)
                {
                    if (!Step(b)) continue;
                    b.recipeProgressTicks++;
                    if (b.recipeProgressTicks < craft.buildTicks) continue;
                }
                foreach (var part in craft.components) WorldLookup.RemoveItem(b.inventory, part.itemId, part.quantity);
                b.recipeProgressTicks = 0;
                b.orderedCraftTypeId = "";
                EconomyService.SpawnCraft(w, craft, b.owner, b.cellId);
                if (b.owner == FactionId.Player)
                    WorldLookup.AddAlert(w, AlertKind.Info, craft.displayName + " launched from your shipyard in cell " + b.cellId + ".", b.cellId);
            }
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
            if (def.kind == BuildingKind.Shipyard)
            {
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                return craft != null ? craft.components : NoNeeds;
            }
            return NoNeeds;
        }

        /// <summary>
        /// True when a machine's storage is full (stock plus items on the way), so it cannot request more inputs until
        /// something takes its output.
        /// </summary>
        public static bool IsStorageFull(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return false;
            int inbound = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == b.id) inbound += t.Units;
            return WorldLookup.TotalItems(b.inventory) + inbound >= def.inventoryCapacity;
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
                    if (!WorldLookup.CanReceive(w, target) || def == null || WorldLookup.TotalItems(target.inventory) + t.Units > def.inventoryCapacity)
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
                if (stranded.Exists(t => WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]) != null && WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]).owner == FactionId.Player))
                    WorldLookup.AddAlert(w, AlertKind.BlockedRoute, what + " stranded on a belt with no way on. Reconnect it within "
                        + (StrandedTicks * c.rules.tickMilliseconds / 1000) + "s or they are lost.", first != null ? first.toCellId : -1);
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

        static void SellDepots(WorldState w, ContentCatalog c, EconomyService economy)
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
                    economy.Sell(w, c, faction, item, stack.quantity);
                    b.inventory.RemoveAt(i);
                }
            }
        }
    }
}
