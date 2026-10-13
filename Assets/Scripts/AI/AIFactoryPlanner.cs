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

            // Every machine spends Ion, so tap an Ion node before anything else.
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
                    var yardStep = PlanShipyard(w, c, map, beltPlanner, network, faction);
                    if (yardStep != null) return yardStep;
                }
            }
            if (products.Count == 0)
            {
                var yardStep = PlanShipyard(w, c, map, beltPlanner, network, faction);
                if (yardStep != null) return yardStep;
            }

            // 3. With income flowing, invest in returns and the better product.
            if (faction.cumulativeSales > 0)
            {
                foreach (var u in c.upgrades)
                {
                    if (faction.purchasedUpgradeIds.Contains(u.id)) continue;
                    if (faction.currency >= u.cost + 60) return Single(new GameCommand { kind = CommandKind.BuyUpgrade, actor = FactionId.Ai, contentId = u.id }, u.cost);
                    return Result(false, Saving, "Saving for " + u.displayName + ".");
                }
                foreach (var t in c.techs)
                {
                    if (faction.unlockedTechIds.Contains(t.id)) continue;
                    if (faction.currency >= t.cost + 250) return Single(new GameCommand { kind = CommandKind.UnlockTech, actor = FactionId.Ai, contentId = t.id }, t.cost);
                }
            }
            return Result(false, Idle, "Factory plan complete.");
        }

        FactoryPlanResult PlanShipyard(WorldState w, ContentCatalog c, MapDefinition map, AIBeltPlanner beltPlanner, BeltNetwork network, FactionState faction)
        {
            // Military craft are manufactured from factory goods, so a shipyard only follows real income.
            if (!(c.rules.aiExpands || c.rules.aiRaids) || faction.cumulativeSales < ShipyardAfterSales) return null;
            var yard = BuildShipyardTree(c, faction);
            return yard != null ? PlanTree(w, c, map, beltPlanner, network, faction, yard, "shipyard") : null;
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
                else if (free == null) free = n;
            }
            if (free == null || (tapped && faction.ion >= IonReserve)) return null;
            var extractor = BuildingFor(c, BuildingKind.Extractor, null);
            if (extractor == null || !GridRules.CanPlaceBuilding(w, c, map, extractor, FactionId.Ai, free.cellId, free.x, free.y).success) return null;
            if (faction.currency < extractor.cost) return Result(false, Saving, "Saving for an Ion extractor.");
            var r = Result(true, "", "Tapping Ion in cell " + free.cellId + ".");
            r.estimatedCost = extractor.cost;
            r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = extractor.id, fromCellId = free.cellId, fromX = free.x, fromY = free.y });
            return r;
        }

        FactoryPlanResult PlanFort(WorldState w, ContentCatalog c, MapDefinition map, FactionState faction)
        {
            var fort = BuildingFor(c, BuildingKind.Fort, null);
            if (fort == null) return null;
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Ai || cell.headquartersFor != FactionId.Neutral || OwnershipService.HasOwnAnchor(w, c, cell.id, FactionId.Ai)) continue;
                if (faction.currency < fort.cost) return Result(false, Saving, "Saving for a Fort in cell " + cell.id + ".");
                if (faction.ion < fort.ionCost) return Result(false, Saving, "Waiting for Ion for a Fort in cell " + cell.id + ".");
                var def = WorldLookup.GetCellDef(map, cell.id);
                // Nearest legal tile to the centre, without touching belts.
                int bestX = -1, bestY = -1, best = int.MaxValue;
                for (int y = 0; y < def.gridHeight; y++)
                    for (int x = 0; x < def.gridWidth; x++)
                    {
                        int d = Math.Abs(x - def.gridWidth / 2) + Math.Abs(y - def.gridHeight / 2);
                        if (d >= best || TouchesBelt(w, cell.id, x, y)) continue;
                        if (!GridRules.CanPlaceBuilding(w, c, map, fort, FactionId.Ai, cell.id, x, y).success) continue;
                        best = d; bestX = x; bestY = y;
                    }
                if (bestX < 0) continue;
                var r = Result(true, "", "Fortifying cell " + cell.id + ".");
                r.estimatedCost = fort.cost;
                r.commands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Ai, contentId = fort.id, fromCellId = cell.id, fromX = bestX, fromY = bestY });
                return r;
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
                if (faction.ion < missing.Building.ionCost) return Result(false, Saving, "Waiting for Ion for " + missing.Building.displayName + ".");
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
                if (item.sellable && !ingredients.Contains(item.id) && EconomyService.IsItemUnlocked(c, f, item.id) && ProducingRecipe(c, f, item.id) != null) list.Add(item);
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
                if (r.outputItemId == itemId && EconomyService.IsRecipeUnlocked(c, f, r)) return r;
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
                    if (cell != null && cell.owner == FactionId.Ai) { any = true; break; }
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
                if (EconomyService.IsRecipeUnlocked(c, f, CatalogRepository.GetRecipe(c, rid))) return rid;
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
