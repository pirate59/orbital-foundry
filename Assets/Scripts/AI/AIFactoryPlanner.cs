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
        }

        sealed class Candidate
        {
            public int CellId, X, Y, Score;
        }

        // Runtime-only search state. Persisted progress is the queued command list in AiState.
        string searchSignature = "";
        List<Candidate> candidates;
        readonly List<Candidate> best = new List<Candidate>();
        readonly HashSet<int> unroutable = new HashSet<int>();
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
            var yard = BuildShipyardTree(c, faction);
            return yard != null ? PlanTree(w, c, map, beltPlanner, network, faction, yard, "shipyard") : null;
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
            Resolve(w, c, root);

            // 1. Place the first missing machine, inputs before consumers.
            var missing = FirstMissing(root);
            if (missing != null)
            {
                if (!HasRawSource(w, c, root)) { LastReason = "No owned node for part of the " + label + " chain."; return null; }
                if (faction.currency < missing.Building.cost) return Result(false, Saving, "Saving for " + missing.Building.displayName + ".");
                return PlaceMachine(w, c, map, beltPlanner, root, missing);
            }

            // 2. Connect every producer-consumer link that has no belt route yet.
            var link = FirstUnconnected(w, network, root);
            if (link == null) return null;
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

        static Req BuildShipyardTree(ContentCatalog c, FactionState f)
        {
            var yard = BuildingFor(c, BuildingKind.Shipyard, null);
            if (yard == null) return null;
            var root = new Req { ItemId = "", Building = yard };
            var seen = new HashSet<string>();
            foreach (var craft in c.crafts)
            {
                foreach (var part in craft.components)
                {
                    if (!seen.Add(part.itemId)) continue;
                    var child = ItemReq(c, f, part.itemId, 0);
                    if (child == null) return null;
                    root.Inputs.Add(child);
                }
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

        static bool HasRawSource(WorldState w, ContentCatalog c, Req req)
        {
            if (req.Building.kind == BuildingKind.Extractor && req.BuildingId == -1)
            {
                bool any = false;
                foreach (var n in w.nodes)
                {
                    if (n.itemId != req.ItemId || n.extractorBuildingId != -1) continue;
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
                searchSignature = signature;
                candidates = EnumerateTiles(w, map);
                best.Clear();
                w.ai.plannerCursor = 0;
            }

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
            if (missing.Building.kind != BuildingKind.Extractor && WorldLookup.GetCell(w, cand.CellId).headquartersFor != FactionId.Ai) score += 15;
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
                if (b == null) continue;
                var plan = planner.PlanBetween(w, c, map, FactionId.Ai, b.cellId, b.x, b.y, cand.CellId, cand.X, cand.Y);
                if (!plan.success && plan.steps.Count == 0) return false;
            }
            if (consumer != null)
            {
                var b = WorldLookup.GetBuilding(w, consumer.BuildingId);
                if (b != null)
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
