using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Procedural factory planning over the current map. For each sellable product the AI can make, it derives the
    /// recipe tree, matches existing machines, searches open legal tiles for missing machines (a bounded number of
    /// candidates per tick), then routes missing belt links with <see cref="AIBeltPlanner"/>. It only emits ordinary
    /// GameCommands; there are no authored layouts or fixed coordinates.
    /// </summary>
    public sealed class AIFactoryPlanner
    {
        public const string Searching = "SEARCHING";
        public const string Idle = "IDLE";
        public const string Saving = "SAVING";

        sealed class Req
        {
            public string ItemId;
            public BuildingDefinition Building;
            public RecipeDefinition Recipe;     // null for extractors and the depot
            public readonly List<Req> Inputs = new List<Req>();
            public int BuildingId = -1;
            public int Fixed = -1;              // a specific building (a construction site) rather than any match
            public int Cell = -1;               // the asteroid its machine goes on: the chain's, or a remote one for flown-in ore
        }

        sealed class Candidate
        {
            public int CellId, X, Y, Score;
        }

        // Runtime-only search state. Persisted progress is the queued command list in AiState.
        string searchSignature = "";
        List<Candidate> candidates;
        /// <summary>Last tick the running site search was advanced, and how long it keeps its claim without that.</summary>
        int searchTouchedTick = -1000;
        const int SearchClaimTicks = 5;
        readonly List<Candidate> best = new List<Candidate>();
        readonly HashSet<int> unroutable = new HashSet<int>();

        /// <summary>
        /// Belts stop at an asteroid's edge and the AI does not fly drones, so each chain is built on one asteroid. This
        /// is the asteroid each chain (by label) is on; the headquarters first, moving on when a chain cannot be
        /// finished there.
        /// </summary>
        readonly Dictionary<string, int> chainCells = new Dictionary<string, int>();
        /// <summary>The asteroid the chain being planned is confined to, or -1 (anywhere) outside chain planning.</summary>
        int planCell = -1;
        /// <summary>Whether the AI has researched Drone Pads, so a chain may fly ore in from its other asteroids.</summary>
        bool dronesReady;
        float droneRange;
        readonly Dictionary<int, int> routeFailures = new Dictionary<int, int>();

        public string LastReason = "";

        public void Reconsider(WorldState w, string reason)
        {
            searchSignature = "";
            candidates = null;
            best.Clear();
            w.ai.plannerCursor = 0;
            w.ai.queuedCommands.Clear();
            LastReason = "Replanning: " + reason;
        }

        public FactoryPlanResult PlanNext(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network)
        {
            var faction = WorldLookup.GetFaction(w, FactionId.Ai);
            if (faction == null || faction.suppressed) return Result(false, "SUPPRESSED");
            network.EnsureCurrent(w);
            ForgetMissing(w);
            wantsDrones = false;

            // Holding territory comes first: fortify every claimed AI cell so its unit can move on.
            var fortStep = PlanFort(w, c, map, faction);
            if (fortStep != null) return fortStep;
            var buildStep = PlanConstruction(w, c, map, beltPlanner, network, faction);
            if (buildStep != null) return buildStep;

            // Research runs on Ion, so tap an Ion node early.
            var ionStep = PlanIon(w, c, map, faction);
            if (ionStep != null) return ionStep;

            // The simplest product comes first; the shipyard follows it, then the remaining products.
            var products = Products(c, faction);
            for (int i = 0; i < products.Count; i++)
            {
                var root = BuildTree(w, c, faction, products[i]);
                if (root != null)
                {
                    var step = PlanTree(w, c, map, beltPlanner, network, faction, root, products[i]);
                    if (step != null) return step;
                    // The chain's ore is only on other asteroids: drones come before anything else.
                    if (wantsDrones)
                    {
                        var droneStep = PlanDroneResearch(c, faction);
                        if (droneStep != null) return droneStep;
                    }
                }
                if (i == 0)
                {
                    // With the first income chain running: power for what comes next, then the shipyard.
                    var powerStep = PlanPower(w, c, map, beltPlanner, network, faction);
                    if (powerStep != null) return powerStep;
                    var yardStep = PlanShipyard(w, c, map, beltPlanner, network, faction);
                    if (yardStep != null) return yardStep;
                }
            }
            if (products.Count == 0)
            {
                var yardStep = PlanShipyard(w, c, map, beltPlanner, network, faction);
                if (yardStep != null) return yardStep;
            }

            // 3. With income flowing and every buildable chain in place, research. New unlocks become new products.
            if (faction.cumulativeSales > 0)
            {
                var research = PlanResearch(c, faction);
                if (research != null) return research;
            }
            return Result(false, Idle, "Factory plan complete.");
        }

        FactoryPlanResult PlanShipyard(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network, FactionState faction)
        {
            // Military craft are manufactured from factory goods, so a shipyard only follows real income.
            if (!(c.rules.aiExpands || c.rules.aiRaids || c.rules.aiDefends) || faction.cumulativeSales < ShipyardAfterSales) return null;
            // One tree per craft: plan the supply line of each craft whose parts the AI can make, skipping the rest.
            foreach (var craft in c.crafts)
            {
                var tree = BuildCraftTree(c, faction, craft);
                if (tree == null || !PartsUnlocked(c, faction, craft)) continue;
                var step = PlanTree(w, c, map, beltPlanner, network, faction, tree, craft.displayName);
                if (step != null) return step;
            }
            return null;
        }

        static bool PartsUnlocked(ContentCatalog c, FactionState f, CraftDefinition craft)
        {
            foreach (var part in craft.components)
                if (!ResearchService.IsItemUnlocked(c, f, part.itemId)) return false;
            return true;
        }

        /// <summary>
        /// True when the AI could supply this craft's parts: every part is researched and every raw resource in its
        /// tree is already extracted or available on a free node in a cell whose factory is online.
        /// </summary>
        public bool CanSupply(WorldState w, ContentCatalog c, FactionState f, CraftDefinition craft)
        {
            if (craft == null || !PartsUnlocked(c, f, craft)) return false;
            var tree = BuildCraftTree(c, f, craft);
            if (tree == null) return false;
            Resolve(w, c, tree);
            return HasRawSource(w, c, tree);
        }

        /// <summary>The AI adds solar once its machines draw this share of its supply.</summary>
        public const int PowerHeadroomPercent = 90;

        /// <summary>
        /// Solar power: when demand nears supply and an online cell has a free array slot, place a Solar Array once a
        /// Storage holds its goods; until then, plan a Storage fed by belt with those goods (Copper Bars and Silicon
        /// Ingots from the AI's own refineries).
        /// </summary>
        FactoryPlanResult PlanPower(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network, FactionState faction)
        {
            var array = BuildingFor(c, BuildingKind.SolarArray, null);
            var storage = BuildingFor(c, BuildingKind.Storage, null);
            if (array == null || storage == null || array.powerSupply <= 0) return null;
            if (faction.powerDemand * 100 < faction.powerSupply * PowerHeadroomPercent) return null;
            int tileCell, tileX, tileY;
            if (!FindArrayTile(w, c, map, array, out tileCell, out tileX, out tileY)) return null;

            if (EconomyService.HasStoredGoods(w, c, FactionId.Ai, array.goodsCost))
            {
                if (faction.currency < array.cost) return Result(false, Saving, "Saving for a Solar Array.");
                var r = Result(true, "", "Adding a Solar Array in cell " + tileCell + ".");
                r.estimatedCost = array.cost;
                r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = array.id, fromCellId = tileCell, fromX = tileX, fromY = tileY });
                return r;
            }
            var root = new Req { ItemId = "", Building = storage };
            foreach (var g in array.goodsCost)
            {
                var child = ItemReq(c, faction, g.itemId, 0);
                if (child == null) return null;
                root.Inputs.Add(child);
            }
            return PlanTree(w, c, map, beltPlanner, network, faction, root, "solar power");
        }

        /// <summary>A free tile for a Solar Array in an online AI cell with a free slot, away from belts and nearest its centre.</summary>
        static bool FindArrayTile(WorldState w, ContentCatalog c, MapDefinition map, BuildingDefinition array, out int cellId, out int bx, out int by)
        {
            cellId = bx = by = -1;
            int best = int.MaxValue;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Ai || !cell.factoryOnline) continue;
                var def = WorldLookup.GetCellDef(map, cell.id);
                for (int y = 0; y < def.gridHeight; y++)
                    for (int x = 0; x < def.gridWidth; x++)
                    {
                        int d = Math.Abs(x - def.gridWidth / 2) + Math.Abs(y - def.gridHeight / 2) + (cell.headquartersFor == FactionId.Ai ? 0 : 20);
                        if (d >= best || d < 2 || TouchesBelt(w, cell.id, x, y)) continue;
                        if (!GridRules.CanPlaceBuilding(w, c, map, array, FactionId.Ai, cell.id, x, y).success) continue;
                        best = d; cellId = cell.id; bx = x; by = y;
                    }
            }
            return cellId >= 0;
        }

        /// <summary>Credits and Ion the AI keeps in hand after paying for research.</summary>
        public const int ResearchCreditReserve = 60, ResearchIonReserve = 40;

        /// <summary>
        /// Buys the cheapest research level the AI can reach, keeping a reserve; waits for funds when the cheapest is
        /// out of reach. The AI sees the same web as the player and discovers it the same way.
        /// </summary>
        FactoryPlanResult PlanResearch(ContentCatalog c, FactionState faction)
        {
            ResearchNodeDefinition best = null;
            ResearchLevelDefinition bestLevel = null;
            foreach (var node in c.research)
            {
                var next = ResearchService.NextLevel(faction, node);
                if (next == null || (ResearchService.Level(faction, node.id) == 0 && !ResearchService.IsOpen(faction, node))) continue;
                int price = next.creditCost + ResearchService.IonPrice(c, faction, next) * 2;
                if (best == null || price < bestLevel.creditCost + ResearchService.IonPrice(c, faction, bestLevel) * 2) { best = node; bestLevel = next; }
            }
            if (best == null) return null;
            if (faction.currency < bestLevel.creditCost + ResearchCreditReserve || faction.ion < ResearchService.IonPrice(c, faction, bestLevel) + ResearchIonReserve)
                return Result(false, Saving, "Saving to research " + best.displayName + ".");
            var r = Single(new GameCommand { kind = CommandKind.Research, actor = FactionId.Ai, contentId = best.id }, bestLevel.creditCost);
            LastReason = "Researching " + best.displayName + ".";
            return r;
        }

        /// <summary>Lowest Ion stockpile at which the AI taps another Ion node.</summary>
        const int IonReserve = 40;

        /// <summary>Places an extractor on a free owned Ion node when the AI has none, or when its stockpile runs low.</summary>
        FactoryPlanResult PlanIon(WorldState w, ContentCatalog c, MapDefinition map, FactionState faction)
        {
            string ion = c.rules.ionItemId;
            if (string.IsNullOrEmpty(ion)) return null;
            bool tapped = false;
            ResourceNodeState free = null;
            foreach (var n in w.nodes)
            {
                if (n.itemId != ion) continue;
                var cell = WorldLookup.GetCell(w, n.cellId);
                if (cell == null || cell.owner != FactionId.Ai) continue;
                if (n.extractorBuildingId != -1) tapped = true;
                else if (free == null && cell.factoryOnline) free = n;
            }
            if (tapped && faction.ion >= IonReserve) return null;
            if (free == null) return null;
            var extractor = BuildingFor(c, BuildingKind.Extractor, null);
            if (extractor == null || !GridRules.CanPlaceBuilding(w, c, map, extractor, FactionId.Ai, free.cellId, free.x, free.y).success) return null;
            if (faction.currency < extractor.cost) return Result(false, Saving, "Saving for an Ion extractor.");
            var r = Result(true, "", "Tapping Ion in cell " + free.cellId + ".");
            r.estimatedCost = extractor.cost;
            r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = extractor.id, fromCellId = free.cellId, fromX = free.x, fromY = free.y });
            return r;
        }

        /// <summary>
        /// Holding territory: for each claimed AI cell without a Fort, place a Fort Construction Zone on the border of a
        /// neighbouring AI cell whose factory is online, facing the claimed cell. Supplying it is planned separately.
        /// </summary>
        FactoryPlanResult PlanFort(WorldState w, ContentCatalog c, MapDefinition map, FactionState faction)
        {
            BuildingDefinition zone = null;
            foreach (var d in c.buildings) if (d.kind == BuildingKind.ConstructionZone) zone = d;
            if (zone == null) return null;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Ai || cell.headquartersFor != FactionId.Neutral || OwnershipService.HasOwnAnchor(w, c, cell.id, FactionId.Ai)) continue;
                if (w.buildings.Exists(b => b.owner == FactionId.Ai && b.typeId == zone.id && b.targetCellId == cell.id)) continue;
                if (faction.currency < zone.cost) return Result(false, Saving, "Saving for a Fort zone facing cell " + cell.id + ".");
                foreach (var home in w.cells)
                {
                    if (home.owner != FactionId.Ai || !home.factoryOnline) continue;
                    var def = WorldLookup.GetCellDef(map, home.id);
                    for (int y = 0; y < def.gridHeight; y++)
                        for (int x = 0; x < def.gridWidth; x++)
                        {
                            if (GridRules.BorderNeighbour(map, home.id, x, y) != cell.id || TouchesBelt(w, home.id, x, y)) continue;
                            if (!GridRules.CanPlaceBuilding(w, c, map, zone, FactionId.Ai, home.id, x, y).success) continue;
                            var r = Result(true, "", "Building a Fort zone facing cell " + cell.id + ".");
                            r.estimatedCost = zone.cost;
                            r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = zone.id, fromCellId = home.id, fromX = x, fromY = y });
                            return r;
                        }
                }
            }
            return null;
        }

        /// <summary>Feeds each AI construction site: its materials are planned as a tree rooted at that site.</summary>
        FactoryPlanResult PlanConstruction(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network, FactionState faction)
        {
            foreach (var site in w.buildings.ToArray())
            {
                if (site.owner != FactionId.Ai || !site.underConstruction) continue;
                var def = CatalogRepository.GetBuilding(c, site.typeId);
                if (def == null) continue;
                var root = new Req { ItemId = "", Building = def, Fixed = site.id };
                bool ok = true;
                foreach (var m in def.materials)
                {
                    var child = ItemReq(c, faction, m.itemId, 0);
                    if (child == null) { ok = false; break; }
                    root.Inputs.Add(child);
                }
                if (!ok) continue;
                var step = PlanTree(w, c, map, beltPlanner, network, faction, root, def.displayName);
                if (step != null) return step;
            }
            return null;
        }

        /// <summary>Lifetime AI sales required before it builds a shipyard.</summary>
        public const int ShipyardAfterSales = 150;

        /// <summary>Returns the next step for one tree (place a machine or route a link), or null when the tree is complete or blocked.</summary>
        FactoryPlanResult PlanTree(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network,
            FactionState faction, Req root, string label)
        {
            planCell = ChainCell(w, root, label);
            lastMap = map;
            dronesReady = DronesUnlocked(c, faction);
            droneRange = DroneLinks.Range(c, faction);
            try
            {
                return PlanTreeOnCell(w, c, map, beltPlanner, network, faction, root, label);
            }
            finally
            {
                planCell = -1;
            }
        }

        /// <summary>The asteroid a chain is built on: a fixed site's own, else the one chosen before, else the headquarters.</summary>
        int ChainCell(WorldState w, Req root, string label)
        {
            var fixedSite = root.Fixed != -1 ? WorldLookup.GetBuilding(w, root.Fixed) : null;
            if (fixedSite != null) return fixedSite.cellId;
            int cell;
            if (chainCells.TryGetValue(label, out cell) && IsAiFactoryCell(w, cell)) return cell;
            var cells = AiFactoryCells(w);
            cell = cells.Count > 0 ? cells[0] : -1;
            chainCells[label] = cell;
            return cell;
        }

        /// <summary>The chain cannot be finished on its asteroid: try the next of the AI's working asteroids.</summary>
        void MoveChain(WorldState w, Req root, string label)
        {
            if (root.Fixed != -1) return;
            var cells = AiFactoryCells(w);
            if (cells.Count < 2) return;
            int i = cells.IndexOf(planCell);
            chainCells[label] = cells[(i + 1) % cells.Count];
        }

        static bool IsAiFactoryCell(WorldState w, int cellId)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            return cell != null && cell.owner == FactionId.Ai && cell.factoryOnline;
        }

        /// <summary>The AI's asteroids where it can build, headquarters first, then by ID.</summary>
        static List<int> AiFactoryCells(WorldState w)
        {
            var cells = new List<int>();
            foreach (var cell in w.cells)
                if (cell.owner == FactionId.Ai && cell.factoryOnline) cells.Add(cell.id);
            cells.Sort((a, b) =>
            {
                bool ha = WorldLookup.GetCell(w, a).headquartersFor == FactionId.Ai, hb = WorldLookup.GetCell(w, b).headquartersFor == FactionId.Ai;
                return ha != hb ? (ha ? -1 : 1) : a.CompareTo(b);
            });
            return cells;
        }

        FactoryPlanResult PlanTreeOnCell(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network,
            FactionState faction, Req root, string label)
        {
            Resolve(w, c, root);
            AssignCells(w, map, root);

            // 1. Place the first missing machine, inputs before consumers.
            var missing = FirstMissing(root);
            if (missing != null)
            {
                if (!HasRawSource(w, c, root))
                {
                    LastReason = "No owned node for part of the " + label + " chain on asteroid " + planCell + ".";
                    MoveChain(w, root, label);
                    return null;
                }
                if (faction.currency < missing.Building.cost) return Result(false, Saving, "Saving for " + missing.Building.displayName + ".");
                var placed = PlaceMachine(w, c, map, beltPlanner, root, missing);
                if (placed != null && placed.reason == "NO_SITE") MoveChain(w, root, label);
                return placed;
            }

            // 2. Connect every producer-consumer link that has no belt route yet.
            var link = FirstUnconnected(w, network, root);
            if (link == null) return null;
            var linkSource = WorldLookup.GetBuilding(w, link.Item1.BuildingId);
            var linkTarget = WorldLookup.GetBuilding(w, link.Item2.BuildingId);
            if (linkSource != null && linkTarget != null && linkSource.cellId != linkTarget.cellId)
                return PlanDroneRoute(w, c, map, beltPlanner, network, faction, linkSource, linkTarget, label);
            var plan = beltPlanner.Plan(w, c, map, link.Item1.BuildingId, link.Item2.BuildingId);
            if (plan.success && plan.steps.Count > 0)
            {
                var r = Result(true, "", "Routing belts for " + label + ".");
                r.productItemId = root.ItemId;
                r.estimatedCost = plan.totalCost;
                foreach (var st in plan.steps)
                {
                    r.commands.Add(new GameCommand
                    {
                        kind = CommandKind.PlaceBelt, actor = FactionId.Ai,
                        fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY,
                        toCellId = st.toCellId, toX = st.toX, toY = st.toY
                    });
                }
                return r;
            }
            if (plan.steps.Count > 0 && !plan.success && plan.totalCost > faction.currency)
                return Result(false, Saving, plan.reason);
            // Unroutable: abandon the consumer (or a repeatedly failing source) so a new site is chosen.
            int src = link.Item1.BuildingId, dst = link.Item2.BuildingId;
            int fails;
            routeFailures.TryGetValue(src, out fails);
            routeFailures[src] = fails + 1;
            unroutable.Add(fails >= 1 ? src : dst);
            return Result(false, "NO_ROUTE", "No belt route between machines; choosing a new site.");
        }

        /// <summary>A shipyard fed with one craft's parts.</summary>
        static Req BuildCraftTree(ContentCatalog c, FactionState f, CraftDefinition craft)
        {
            var yard = BuildingFor(c, BuildingKind.Shipyard, null);
            if (yard == null) return null;
            var root = new Req { ItemId = "", Building = yard };
            foreach (var part in craft.components)
            {
                var child = ItemReq(c, f, part.itemId, 0);
                if (child == null) return null;
                root.Inputs.Add(child);
            }
            return root;
        }

        // ---------------------------------------------------------------- recipe tree

        static List<string> Products(ContentCatalog c, FactionState f)
        {
            var list = new List<ItemDefinition>();
            // Products are finished goods: sellable, craftable, and not an ingredient of another recipe.
            var ingredients = new HashSet<string>();
            foreach (var r in c.recipes) foreach (var i in r.inputs) ingredients.Add(i.itemId);
            foreach (var item in c.items)
                if (item.sellable && !ingredients.Contains(item.id) && ResearchService.IsItemUnlocked(c, f, item.id) && ProducingRecipe(c, f, item.id) != null) list.Add(item);
            // Simplest chain first (fewest machines), then the more valuable product.
            var size = new Dictionary<string, int>();
            foreach (var item in list) size[item.id] = TreeSize(c, f, item.id, 0);
            list.Sort((a, b) => size[a.id] != size[b.id] ? size[a.id].CompareTo(size[b.id])
                : a.baseSaleValue != b.baseSaleValue ? b.baseSaleValue.CompareTo(a.baseSaleValue) : string.CompareOrdinal(a.id, b.id));
            var ids = new List<string>();
            foreach (var i in list) ids.Add(i.id);
            return ids;
        }

        /// <summary>Machines in an item's production tree, extractors included.</summary>
        static int TreeSize(ContentCatalog c, FactionState f, string itemId, int depth)
        {
            var recipe = ProducingRecipe(c, f, itemId);
            if (recipe == null || depth > 8) return 1;
            int n = 1;
            foreach (var input in recipe.inputs) n += TreeSize(c, f, input.itemId, depth + 1);
            return n;
        }

        static RecipeDefinition ProducingRecipe(ContentCatalog c, FactionState f, string itemId)
        {
            foreach (var r in c.recipes)
                if (r.outputItemId == itemId && ResearchService.IsRecipeUnlocked(c, f, r)) return r;
            return null;
        }

        static BuildingDefinition BuildingFor(ContentCatalog c, BuildingKind kind, string recipeId)
        {
            foreach (var b in c.buildings)
                if (b.kind == kind && (recipeId == null || b.allowedRecipeIds.Contains(recipeId))) return b;
            return null;
        }

        static Req BuildTree(WorldState w, ContentCatalog c, FactionState f, string product)
        {
            var depot = BuildingFor(c, BuildingKind.SaleDepot, null);
            if (depot == null) return null;
            var root = new Req { ItemId = product, Building = depot };
            var producer = ItemReq(c, f, product, 0);
            if (producer == null) return null;
            root.Inputs.Add(producer);
            return root;
        }

        static Req ItemReq(ContentCatalog c, FactionState f, string itemId, int depth)
        {
            if (depth > 8) return null;
            var recipe = ProducingRecipe(c, f, itemId);
            if (recipe == null)
            {
                var extractor = BuildingFor(c, BuildingKind.Extractor, null);
                return extractor == null ? null : new Req { ItemId = itemId, Building = extractor };
            }
            var building = BuildingFor(c, recipe.buildingKind, recipe.id);
            if (building == null) return null;
            var req = new Req { ItemId = itemId, Building = building, Recipe = recipe };
            foreach (var input in recipe.inputs)
            {
                var child = ItemReq(c, f, input.itemId, depth + 1);
                if (child == null) return null;
                req.Inputs.Add(child);
            }
            return req;
        }

        void Resolve(WorldState w, ContentCatalog c, Req req)
        {
            foreach (var child in req.Inputs) Resolve(w, c, child);
            req.BuildingId = -1;
            if (req.Fixed != -1)
            {
                if (WorldLookup.GetBuilding(w, req.Fixed) != null) req.BuildingId = req.Fixed;
                return;
            }
            var sorted = new List<BuildingState>(w.buildings);
            sorted.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var b in sorted)
            {
                if (b.owner != FactionId.Ai || b.typeId != req.Building.id || unroutable.Contains(b.id)) continue;
                // Belts cannot reach another asteroid; only extractors in drone reach can supply a chain from one.
                if (planCell >= 0 && b.cellId != planCell
                    && !(req.Building.kind == BuildingKind.Extractor && dronesReady && lastMap != null && InDroneReach(lastMap, b.cellId, planCell))) continue;
                if (!WorldLookup.IsBuildingActive(w, b)) continue;
                if (req.Building.kind == BuildingKind.Extractor)
                {
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    if (node == null || node.itemId != req.ItemId) continue;
                }
                else if (req.Recipe != null && b.selectedRecipeId != req.Recipe.id) continue;
                req.BuildingId = b.id;
                return;
            }
        }

        static Req FirstMissing(Req req)
        {
            foreach (var child in req.Inputs)
            {
                var m = FirstMissing(child);
                if (m != null) return m;
            }
            return req.BuildingId == -1 ? req : null;
        }

        static Tuple<Req, Req> FirstUnconnected(WorldState w, BeltNetwork network, Req req)
        {
            foreach (var child in req.Inputs)
            {
                var deeper = FirstUnconnected(w, network, child);
                if (deeper != null) return deeper;
                if (!network.FindShortestRoute(w, child.BuildingId, req.BuildingId).success) return Tuple.Create(child, req);
            }
            return null;
        }

        bool HasRawSource(WorldState w, ContentCatalog c, Req req)
        {
            if (req.Building.kind == BuildingKind.Extractor && req.BuildingId == -1)
            {
                bool any = false;
                foreach (var n in w.nodes)
                {
                    if (n.itemId != req.ItemId || n.extractorBuildingId != -1) continue;
                    if (planCell >= 0 && n.cellId != planCell && !(dronesReady && InDroneReachNoMap(w, n.cellId)))
                    {
                        // Ore on another of its asteroids that drones cannot reach yet: research drones, or more range.
                        if (IsAiFactoryCell(w, n.cellId)) wantsDrones = true;
                        continue;
                    }
                    var cell = WorldLookup.GetCell(w, n.cellId);
                    // Extractors can only go where the AI's factory is online (its HQ or a completed Fort).
                    if (cell != null && cell.owner == FactionId.Ai && cell.factoryOnline) { any = true; break; }
                }
                if (!any) return false;
            }
            foreach (var child in req.Inputs) if (!HasRawSource(w, c, child)) return false;
            return true;
        }

        void ForgetMissing(WorldState w)
        {
            unroutable.RemoveWhere(id => WorldLookup.GetBuilding(w, id) == null);
        }

        // ---------------------------------------------------------------- site search

        FactoryPlanResult PlaceMachine(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, Req root, Req missing)
        {
            string signature = missing.Building.id + ":" + (missing.Recipe != null ? missing.Recipe.id : missing.ItemId);
            if (signature != searchSignature || candidates == null)
            {
                // One site search runs at a time. While another chain is still advancing its search, wait for it
                // instead of restarting it, or two chains taking turns would reset each other forever.
                if (candidates != null && searchSignature != "" && w.tick - searchTouchedTick <= SearchClaimTicks)
                    return Result(false, Searching, "Waiting to search sites for " + missing.Building.displayName + ".");
                searchSignature = signature;
                candidates = EnumerateTiles(w, map);
                best.Clear();
                w.ai.plannerCursor = 0;
            }

            searchTouchedTick = w.tick;
            var consumer = FindConsumer(root, missing);
            int budget = Math.Max(1, c.rules.aiCandidateChecksPerTick);
            while (budget-- > 0 && w.ai.plannerCursor < candidates.Count)
            {
                var cand = candidates[w.ai.plannerCursor++];
                int score;
                if (!ScoreSite(w, c, map, missing, consumer, cand, out score)) continue;
                cand.Score = score;
                best.Add(cand);
                best.Sort((a, b) => a.Score != b.Score ? a.Score.CompareTo(b.Score) : a.CellId != b.CellId ? a.CellId.CompareTo(b.CellId) : (a.Y * 32 + a.X).CompareTo(b.Y * 32 + b.X));
                if (best.Count > 6) best.RemoveAt(best.Count - 1);
            }
            if (w.ai.plannerCursor < candidates.Count)
            {
                LastReason = "Searching sites for " + missing.Building.displayName + " (" + w.ai.plannerCursor + "/" + candidates.Count + ").";
                return Result(false, Searching, LastReason);
            }

            // Search finished: take the best site whose belt links are routable.
            searchSignature = "";
            candidates = null;
            w.ai.plannerCursor = 0;
            foreach (var cand in best)
            {
                if (!LinksRoutable(w, c, map, beltPlanner, missing, consumer, cand)) continue;
                var r = Result(true, "", "Placing " + missing.Building.displayName + ".");
                r.productItemId = root.ItemId;
                r.estimatedCost = missing.Building.cost;
                r.commands.Add(new GameCommand
                {
                    kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = missing.Building.id,
                    fromCellId = cand.CellId, fromX = cand.X, fromY = cand.Y
                });
                if (missing.Recipe != null && DefaultRecipe(c, WorldLookup.GetFaction(w, FactionId.Ai), missing.Building) != missing.Recipe.id)
                {
                    // buildingId is filled in by AIController from the placed tile before submission.
                    r.commands.Add(new GameCommand
                    {
                        kind = CommandKind.SelectRecipe, actor = FactionId.Ai, contentId = missing.Recipe.id,
                        fromCellId = cand.CellId, fromX = cand.X, fromY = cand.Y
                    });
                }
                best.Clear();
                return r;
            }
            best.Clear();
            // Nothing could be routed to the machines this one would reuse (they may be boxed in): stop reusing them,
            // so the next plan builds fresh producers with room around them. Belts cannot leave the asteroid to
            // find a way round.
            bool marked = false;
            foreach (var input in missing.Inputs)
                if (input.BuildingId != -1) { unroutable.Add(input.BuildingId); marked = true; }
            if (!marked && consumer != null && consumer.BuildingId != -1 && consumer.Fixed == -1) unroutable.Add(consumer.BuildingId);
            LastReason = "No legal site for " + missing.Building.displayName + ".";
            return Result(false, "NO_SITE", LastReason);
        }

        static string DefaultRecipe(ContentCatalog c, FactionState f, BuildingDefinition def)
        {
            foreach (var rid in def.allowedRecipeIds)
                if (ResearchService.IsRecipeUnlocked(c, f, CatalogRepository.GetRecipe(c, rid))) return rid;
            return "";
        }

        static Req FindConsumer(Req node, Req target)
        {
            foreach (var child in node.Inputs)
            {
                if (child == target) return node;
                var deeper = FindConsumer(child, target);
                if (deeper != null) return deeper;
            }
            return null;
        }

        static List<Candidate> EnumerateTiles(WorldState w, MapDefinition map)
        {
            var list = new List<Candidate>();
            var cells = new List<MapCellDefinition>(map.cells);
            cells.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var def in cells)
            {
                var cell = WorldLookup.GetCell(w, def.id);
                if (cell == null || cell.owner != FactionId.Ai) continue;
                for (int y = 0; y < def.gridHeight; y++)
                    for (int x = 0; x < def.gridWidth; x++)
                        list.Add(new Candidate { CellId = def.id, X = x, Y = y });
            }
            return list;
        }

        // ---------------------------------------------------------------- drones between asteroids

        static BuildingDefinition PadDef(ContentCatalog c)
        {
            foreach (var d in c.buildings) if (d.kind == BuildingKind.DronePad) return d;
            return null;
        }

        static bool DronesUnlocked(ContentCatalog c, FactionState f)
        {
            var pad = PadDef(c);
            return pad != null && ResearchService.IsBuildingUnlocked(c, f, pad.id);
        }

        /// <summary>Flat distance across the open space between two asteroids' grids (0 when they overlap).</summary>
        static float GridGap(MapDefinition map, int a, int b)
        {
            var da = WorldLookup.GetCellDef(map, a);
            var db = WorldLookup.GetCellDef(map, b);
            if (da == null || db == null) return float.MaxValue;
            var oa = MapLayout.Origin(map, da);
            var ob = MapLayout.Origin(map, db);
            float dx = Math.Max(0f, Math.Max(ob.x - (oa.x + da.gridWidth), oa.x - (ob.x + db.gridWidth)));
            float dz = Math.Max(0f, Math.Max(ob.z - (oa.z + da.gridHeight), oa.z - (ob.z + db.gridHeight)));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Whether pads on facing edges of two asteroids would be within drone range (with a tile to spare).</summary>
        bool InDroneReach(MapDefinition map, int a, int b)
        {
            return a == b || GridGap(map, a, b) + 2f <= droneRange;
        }

        /// <summary>Set when a chain's ore exists only on other AI asteroids that drones cannot reach yet.</summary>
        bool wantsDrones;

        /// <summary>
        /// Researches toward the level that unlocks Drone Pads (prerequisites first), then its range levels, saving for
        /// them if need be. Null when there is nothing left to research there.
        /// </summary>
        FactoryPlanResult PlanDroneResearch(ContentCatalog c, FactionState faction)
        {
            var pad = PadDef(c);
            var gate = pad != null ? ResearchService.BuildingGate(c, pad.id) : null;
            if (gate == null) return null;
            // Unlock the pads first; after that, the same node's further levels add range.
            var node = NextToward(c, faction, gate, 0);
            if (node == null) return null;
            if (ResearchService.CheckPurchase(c, faction, node) != null)
                return Result(false, Saving, "Saving to research " + node.displayName + " toward Drone Pads.");
            LastReason = "Researching " + node.displayName + " toward Drone Pads.";
            return Single(new GameCommand { kind = CommandKind.Research, actor = FactionId.Ai, contentId = node.id }, ResearchService.NextLevel(faction, node).creditCost);
        }

        /// <summary>The research node to buy next on the way to <paramref name="target"/>: the target once open, else an unmet prerequisite.</summary>
        static ResearchNodeDefinition NextToward(ContentCatalog c, FactionState f, ResearchNodeDefinition target, int depth)
        {
            if (target == null || depth > 12) return null;
            if (ResearchService.IsOpen(f, target)) return ResearchService.NextLevel(f, target) != null ? target : null;
            foreach (var p in target.prerequisites)
                if (ResearchService.Level(f, p.nodeId) < p.minimumLevel)
                    return NextToward(c, f, CatalogRepository.GetResearch(c, p.nodeId), depth + 1);
            return null;
        }

        /// <summary>The map of the chain being planned (for reach checks in helpers that are not given it).</summary>
        MapDefinition lastMap;
        bool InDroneReachNoMap(WorldState w, int cellId)
        {
            return lastMap != null && IsAiFactoryCell(w, cellId) && InDroneReach(lastMap, cellId, planCell);
        }

        /// <summary>
        /// Gives each unbuilt machine its asteroid: the chain's, except an extractor whose ore has run out there,
        /// which goes to the nearest other working asteroid in drone reach with a free node of that ore.
        /// </summary>
        void AssignCells(WorldState w, MapDefinition map, Req req)
        {
            var built = WorldLookup.GetBuilding(w, req.BuildingId);
            req.Cell = built != null ? built.cellId : planCell;
            if (built == null && req.Building.kind == BuildingKind.Extractor && planCell >= 0 && !FreeNode(w, req.ItemId, planCell) && dronesReady)
            {
                float best = float.MaxValue;
                foreach (int cell in AiFactoryCells(w))
                {
                    if (cell == planCell || !FreeNode(w, req.ItemId, cell) || !InDroneReach(map, cell, planCell)) continue;
                    float gap = GridGap(map, cell, planCell);
                    if (gap < best) { best = gap; req.Cell = cell; }
                }
            }
            foreach (var child in req.Inputs) AssignCells(w, map, child);
        }

        static bool FreeNode(WorldState w, string itemId, int cellId)
        {
            return w.nodes.Exists(n => n.cellId == cellId && n.itemId == itemId && n.extractorBuildingId == -1);
        }

        /// <summary>
        /// Connects a producer on one asteroid to a consumer on another, one step at a time: a sending pad on the
        /// producer's asteroid (on the edge facing the consumer), a drone link to a receiving pad by the consumer,
        /// then belts from the producer to the sending pad and from the receiving pad to the consumer. Links already
        /// flying between the two asteroids are reused.
        /// </summary>
        FactoryPlanResult PlanDroneRoute(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network,
            FactionState faction, BuildingState src, BuildingState dst, string label)
        {
            var padDef = PadDef(c);
            if (!dronesReady || padDef == null)
            {
                unroutable.Add(src.id);
                return Result(false, "NO_ROUTE", "Needs Drone Pads to reach asteroid " + src.cellId + "; choosing a new site.");
            }
            BuildingState send = null, recv = null, unlinked = null;
            foreach (var b in w.buildings)
            {
                if (b.owner != FactionId.Ai || b.cellId != src.cellId || !DroneLinks.IsPad(c, b)) continue;
                var out1 = DroneLinks.Outgoing(w, b);
                if (out1 == null) { if (unlinked == null) unlinked = b; continue; }
                if (out1.toCellId != dst.cellId) continue;
                send = b;
                recv = WorldLookup.GetBuildingAt(w, out1.toCellId, out1.toX, out1.toY);
                break;
            }
            if (send == null)
            {
                if (unlinked == null)
                {
                    // Place a sending pad on the producer's asteroid, as near the consumer's asteroid as possible.
                    var srcDef = WorldLookup.GetCellDef(map, src.cellId);
                    int bx = -1, by = -1;
                    float bestScore = float.MaxValue;
                    for (int y = 0; y < srcDef.gridHeight; y++)
                        for (int x = 0; x < srcDef.gridWidth; x++)
                        {
                            if (TouchesBelt(w, src.cellId, x, y)) continue;
                            var ok = GridRules.CanPlaceBuilding(w, c, map, padDef, FactionId.Ai, src.cellId, x, y);
                            if (!ok.success && ok.code != "NO_MONEY") continue;
                            var at = MapLayout.TileCenter(map, src.cellId, x, y);
                            float score = FlatToGrid(map, at, dst.cellId) * 10f + Math.Abs(x - src.x) + Math.Abs(y - src.y);
                            if (score < bestScore) { bestScore = score; bx = x; by = y; }
                        }
                    if (bx < 0) { unroutable.Add(src.id); return Result(false, "NO_ROUTE", "No room for a drone pad on asteroid " + src.cellId + "."); }
                    if (faction.currency < padDef.cost) return Result(false, Saving, "Saving for a Drone Pad.");
                    var r = Result(true, "", "Placing a Drone Pad for " + label + ".");
                    r.estimatedCost = padDef.cost;
                    r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = padDef.id, fromCellId = src.cellId, fromX = bx, fromY = by });
                    return r;
                }
                // Link the pad to the reachable tile nearest it on the consumer's asteroid.
                var dstDef = WorldLookup.GetCellDef(map, dst.cellId);
                int tx = -1, ty = -1;
                float bestTarget = float.MaxValue;
                for (int y = 0; y < dstDef.gridHeight; y++)
                    for (int x = 0; x < dstDef.gridWidth; x++)
                    {
                        if (TouchesBelt(w, dst.cellId, x, y)) continue;
                        BuildingState existing;
                        if (DroneLinks.CheckLink(w, c, map, FactionId.Ai, unlinked, dst.cellId, x, y, out existing) != null) continue;
                        float score = DroneLinks.FlatDistance(map, unlinked, dst.cellId, x, y) * 10f + Math.Abs(x - dst.x) + Math.Abs(y - dst.y);
                        if (score < bestTarget) { bestTarget = score; tx = x; ty = y; }
                    }
                if (tx < 0) { unroutable.Add(src.id); return Result(false, "NO_ROUTE", "Asteroid " + dst.cellId + " is out of drone reach."); }
                if (faction.currency < padDef.cost) return Result(false, Saving, "Saving for a receiving pad.");
                var link = Result(true, "", "Linking drones from asteroid " + src.cellId + " to " + dst.cellId + ".");
                link.estimatedCost = padDef.cost;
                link.commands.Add(new GameCommand { kind = CommandKind.LinkDronePad, actor = FactionId.Ai, buildingId = unlinked.id, toCellId = dst.cellId, toX = tx, toY = ty });
                return link;
            }
            if (recv == null) { unroutable.Add(src.id); return Result(false, "NO_ROUTE", "Drone link has no receiving pad."); }
            // Belt each leg that is not yet connected: producer to sending pad, then receiving pad to consumer.
            foreach (var leg in new[] { Tuple.Create(src, send), Tuple.Create(recv, dst) })
            {
                if (network.FindShortestRoute(w, leg.Item1.id, leg.Item2.id).success) continue;
                var plan = beltPlanner.Plan(w, c, map, leg.Item1.id, leg.Item2.id);
                if (plan.success && plan.steps.Count > 0)
                {
                    var r = Result(true, "", "Routing belts to the drone pads for " + label + ".");
                    r.estimatedCost = plan.totalCost;
                    foreach (var st in plan.steps)
                        r.commands.Add(new GameCommand
                        {
                            kind = CommandKind.PlaceBelt, actor = FactionId.Ai,
                            fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY,
                            toCellId = st.toCellId, toX = st.toX, toY = st.toY
                        });
                    return r;
                }
                if (plan.steps.Count > 0 && !plan.success && plan.totalCost > faction.currency) return Result(false, Saving, plan.reason);
                unroutable.Add(src.id);
                return Result(false, "NO_ROUTE", "No belt route to the drone pads; choosing a new site.");
            }
            return null;
        }

        /// <summary>Flat distance from a point to the nearest edge of an asteroid's grid.</summary>
        static float FlatToGrid(MapDefinition map, UnityEngine.Vector3 p, int cellId)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            var o = MapLayout.Origin(map, def);
            float dx = Math.Max(0f, Math.Max(o.x - p.x, p.x - (o.x + def.gridWidth)));
            float dz = Math.Max(0f, Math.Max(o.z - p.z, p.z - (o.z + def.gridHeight)));
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        bool ScoreSite(WorldState w, ContentCatalog c, MapDefinition map, Req missing, Req consumer, Candidate cand, out int score)
        {
            score = 0;
            var check = GridRules.CanPlaceBuilding(w, c, map, missing.Building, FactionId.Ai, cand.CellId, cand.X, cand.Y);
            if (!check.success && check.code != "NO_MONEY") return false;
            if (missing.Building.kind == BuildingKind.Extractor)
            {
                var node = WorldLookup.GetNodeAt(w, cand.CellId, cand.X, cand.Y);
                if (node == null || node.itemId != missing.ItemId) return false;
            }
            if (TouchesBelt(w, cand.CellId, cand.X, cand.Y)) return false;

            int links = missing.Inputs.Count + (consumer != null ? 1 : 0);
            int free = FreeNeighbours(w, map, cand);
            if (free < Math.Max(1, Math.Min(links, 3))) return false;

            int wanted = missing.Cell >= 0 ? missing.Cell : planCell;
            if (wanted >= 0 && cand.CellId != wanted) return false; // the chain's asteroid, or the remote one picked for its ore

            var def = WorldLookup.GetCellDef(map, cand.CellId);
            int gx = GridRules.GlobalX(def, cand.X), gy = GridRules.GlobalY(def, cand.Y);
            int distance = 0, partners = 0;
            foreach (var input in missing.Inputs) AddPartner(w, map, input.BuildingId, gx, gy, ref distance, ref partners);
            if (consumer != null) AddPartner(w, map, consumer.BuildingId, gx, gy, ref distance, ref partners);
            if (partners == 0)
            {
                // Anchor near the AI's existing machines, or its headquarters.
                int ax, ay;
                Anchor(w, map, out ax, out ay);
                distance = Math.Abs(gx - ax) + Math.Abs(gy - ay);
            }
            score = distance * 10 + (4 - free) * 3;
            // Prefer the headquarters cell: it is the last to fall, so machines there survive border raids.

            return true;
        }

        static void AddPartner(WorldState w, MapDefinition map, int buildingId, int gx, int gy, ref int distance, ref int partners)
        {
            var b = WorldLookup.GetBuilding(w, buildingId);
            if (b == null) return;
            var def = WorldLookup.GetCellDef(map, b.cellId);
            int d = Math.Abs(GridRules.GlobalX(def, b.x) - gx) + Math.Abs(GridRules.GlobalY(def, b.y) - gy);
            distance += Math.Max(d, 2); // leave a lane between machines
            partners++;
        }

        static void Anchor(WorldState w, MapDefinition map, out int ax, out int ay)
        {
            int sx = 0, sy = 0, n = 0;
            foreach (var b in w.buildings)
            {
                if (b.owner != FactionId.Ai || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = WorldLookup.GetCellDef(map, b.cellId);
                sx += GridRules.GlobalX(def, b.x);
                sy += GridRules.GlobalY(def, b.y);
                n++;
            }
            if (n > 0) { ax = sx / n; ay = sy / n; return; }
            foreach (var cell in w.cells)
            {
                if (cell.headquartersFor != FactionId.Ai) continue;
                var def = WorldLookup.GetCellDef(map, cell.id);
                ax = GridRules.GlobalX(def, def.gridWidth / 2);
                ay = GridRules.GlobalY(def, def.gridHeight / 2);
                return;
            }
            ax = ay = 0;
        }

        static bool TouchesBelt(WorldState w, int cellId, int x, int y)
        {
            foreach (var s in w.belts)
            {
                if (s.fromCellId == cellId && s.fromX == x && s.fromY == y) return true;
                if (s.toCellId == cellId && s.toX == x && s.toY == y) return true;
            }
            return false;
        }

        static int FreeNeighbours(WorldState w, MapDefinition map, Candidate cand)
        {
            var def = WorldLookup.GetCellDef(map, cand.CellId);
            int gx = GridRules.GlobalX(def, cand.X), gy = GridRules.GlobalY(def, cand.Y);
            int free = 0;
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = gx + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                int ny = gy + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                int cellId, lx, ly;
                if (!GridRules.TryGlobalToLocal(map, nx, ny, out cellId, out lx, out ly)) continue;
                var cell = WorldLookup.GetCell(w, cellId);
                if (cell == null || cell.owner != FactionId.Ai) continue;
                if (GridRules.IsBlocked(WorldLookup.GetCellDef(map, cellId), lx, ly)) continue;
                if (WorldLookup.GetBuildingAt(w, cellId, lx, ly) != null || WorldLookup.GetNodeAt(w, cellId, lx, ly) != null) continue;
                free++;
            }
            return free;
        }

        static bool LinksRoutable(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner planner, Req missing, Req consumer, Candidate cand)
        {
            foreach (var input in missing.Inputs)
            {
                var b = WorldLookup.GetBuilding(w, input.BuildingId);
                if (b == null || b.cellId != cand.CellId) continue; // another asteroid: drones carry that leg
                var plan = planner.PlanBetween(w, c, map, FactionId.Ai, b.cellId, b.x, b.y, cand.CellId, cand.X, cand.Y);
                if (!plan.success && plan.steps.Count == 0) return false;
            }
            if (consumer != null)
            {
                var b = WorldLookup.GetBuilding(w, consumer.BuildingId);
                if (b != null && b.cellId == cand.CellId)
                {
                    var plan = planner.PlanBetween(w, c, map, FactionId.Ai, cand.CellId, cand.X, cand.Y, b.cellId, b.x, b.y);
                    if (!plan.success && plan.steps.Count == 0) return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- results

        FactoryPlanResult Result(bool success, string code, string message = "")
        {
            LastReason = string.IsNullOrEmpty(message) ? code : message;
            return new FactoryPlanResult { success = success, reason = success ? "" : code };
        }

        FactoryPlanResult Single(GameCommand cmd, int cost)
        {
            var r = Result(true, "", "Investing.");
            r.estimatedCost = cost;
            r.commands.Add(cmd);
            return r;
        }
    }
}
