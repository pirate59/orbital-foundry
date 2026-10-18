using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Belt Refactoring: a quick review of one piece of the AI's layout each run, round the machines that send goods
    /// out on belts, deciding whether it should be laid out better.
    /// - Multi-lane: an extractor held back by its one belt, with more chutes than lanes, gets a second, separate lane
    ///   to the machine it feeds (Medium and Hard).
    /// - Detour: a route much longer than the distance it covers gets a direct, separate lane beside it; deliveries take
    ///   the quicker route on their own (Hard).
    /// It only lays belts it can pay for and leaves working lines in place. Easy never refactors.
    /// </summary>
    public sealed class BeltRefactorSkill : IAiSkill
    {
        public string Name { get { return "Belt refactoring (multi-lane, detours)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        /// <summary>A route this many times its distance (and at least MinDetourBelts long) is a detour worth fixing.</summary>
        public const int DetourFactor = 2, MinDetourBelts = 8;

        readonly List<BuildingState> pieces = new List<BuildingState>();
        int cursor;

        public int EveryThinks(AiContext ctx) { return ctx.Every(ctx.Profile.Level < 2 ? 45f : 30f); } // one piece reviewed per run

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.RefactorCommands.Clear();
            if (ctx.Profile.Level < 1) { d.RefactorReason = "not reviewing layout"; Status = d.RefactorReason; return; }

            // The pieces to review: AI extractors and machines with belts leaving them.
            pieces.Clear();
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Extractor && def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) continue;
                if (ctx.Services.Belts.Outgoing(w, b.cellId, b.x, b.y).Count == 0) continue;
                pieces.Add(b);
            }
            if (pieces.Count == 0) { d.RefactorReason = "nothing laid out yet"; Status = d.RefactorReason; return; }
            pieces.Sort((a, b) => a.id.CompareTo(b.id));
            var piece = pieces[cursor++ % pieces.Count];
            Review(ctx, piece);
            Status = d.RefactorReason;
        }

        void Review(AiContext ctx, BuildingState piece)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            var def = CatalogRepository.GetBuilding(c, piece.typeId);
            var consumer = FirstConsumer(ctx, piece);
            if (consumer == null) { d.RefactorReason = "reviewed " + piece.typeId + " " + piece.id + ": feeds nothing yet"; return; }

            // Multi-lane: an extractor held back by its belt, with a chute to spare.
            if (def.kind == BuildingKind.Extractor)
            {
                int lanes = 0;
                foreach (var e in ctx.Services.Belts.Outgoing(w, piece.cellId, piece.x, piece.y)) if (e.owner == AiSide.Me) lanes++;
                int chutes = ResearchService.Chutes(c, ctx.Faction);
                bool heldBack = ctx.Services.Factory.StallOf(w, c, piece) == FactorySimulation.Stall.BackedUp;
                if (heldBack && lanes < chutes && TryLane(ctx, piece, consumer, "second lane from extractor " + piece.id + " to " + consumer.typeId + " " + consumer.id))
                    return;
            }

            // Detour (Hard): a route far longer than the distance it covers.
            if (ctx.Profile.Level >= 2)
            {
                var route = ctx.Services.Belts.FindShortestRoute(w, piece.id, consumer.id);
                int distance = GridDistance(ctx, piece, consumer);
                if (route.success && route.segmentIds.Count >= MinDetourBelts && route.segmentIds.Count >= DetourFactor * distance + 4)
                {
                    var plan = ctx.Services.AiBelts.PlanBetween(w, c, ctx.Map, AiSide.Me, piece.cellId, piece.x, piece.y, consumer.cellId, consumer.x, consumer.y, true);
                    if (plan.success && plan.steps.Count > 0 && plan.steps.Count * 2 <= route.segmentIds.Count
                        && TryLane(ctx, piece, consumer, "detour of " + route.segmentIds.Count + " belts replaced by a direct lane", plan))
                        return;
                }
            }
            d.RefactorReason = "reviewed " + piece.typeId + " " + piece.id + ": layout is fine";
        }

        /// <summary>Plans a separate lane (or uses the given plan) and, if affordable, sets it as the refactoring to carry out.</summary>
        static bool TryLane(AiContext ctx, BuildingState from, BuildingState to, string why, BeltPlanResult plan = null)
        {
            var w = ctx.World;
            var d = ctx.Decisions;
            if (plan == null) plan = ctx.Services.AiBelts.PlanBetween(w, ctx.Catalog, ctx.Map, AiSide.Me, from.cellId, from.x, from.y, to.cellId, to.x, to.y, true);
            if (!plan.success || plan.steps.Count == 0) return false;
            int cost = plan.steps.Count * ctx.Catalog.rules.beltCost;
            if (ctx.Faction.currency < cost + BudgetSkill.BaseCraftReserve) return false;
            foreach (var st in plan.steps)
                d.RefactorCommands.Add(new GameCommand
                {
                    kind = CommandKind.PlaceBelt, actor = AiSide.Me,
                    fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY
                });
            d.RefactorReason = why + " (" + plan.steps.Count + " belts)";
            return true;
        }

        /// <summary>The first building (nearest along the belts) the piece's goods reach, other than itself.</summary>
        public static BuildingState FirstConsumer(AiContext ctx, BuildingState piece)
        {
            foreach (var b in ctx.Services.Belts.BuildingsReachableFrom(ctx.World, AiSide.Me, piece.cellId, piece.x, piece.y))
                if (b.id != piece.id && b.owner == AiSide.Me) return b;
            return null;
        }

        static int GridDistance(AiContext ctx, BuildingState a, BuildingState b)
        {
            var da = WorldLookup.GetCellDef(ctx.Map, a.cellId);
            var db = WorldLookup.GetCellDef(ctx.Map, b.cellId);
            return System.Math.Abs(GridRules.GlobalX(da, a.x) - GridRules.GlobalX(db, b.x)) + System.Math.Abs(GridRules.GlobalY(da, a.y) - GridRules.GlobalY(db, b.y));
        }
    }

    /// <summary>
    /// Sale Depot Management: a machine backed up with a sellable product and no depot or Storage its belts reach gets an
    /// outlet: a Sale Depot on a free tile nearby, belted from the machine. Depots are served after machines, so this
    /// only sells the surplus that was stuck, and the machine starts working again. One outlet a run; Medium and Hard.
    /// </summary>
    public sealed class DepotOutletSkill : IAiSkill
    {
        public string Name { get { return "Sale depot management (outlets)"; } }

        /// <summary>Machines checked a run (the rest on later runs), so a big factory costs no more a run.</summary>
        public const int MachinesPerRun = 12;
        int cursor;
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Every(25f); } // stuck machines stay stuck

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.OutletCommands.Clear();
            if (ctx.Profile.Level < 1) { d.OutletReason = "not adding outlets"; Status = d.OutletReason; return; }
            var depot = default(BuildingDefinition);
            foreach (var def in c.buildings) if (def.kind == BuildingKind.SaleDepot) depot = def;
            if (depot == null || ctx.Faction.currency < depot.cost + BudgetSkill.BaseCraftReserve) { d.OutletReason = "no outlet needed"; Status = d.OutletReason; return; }
            int stuck = 0, looked = 0;
            int start = cursor;
            for (int n = 0; n < w.buildings.Count && looked < MachinesPerRun; n++)
            {
                var b = w.buildings[(start + n) % w.buildings.Count];
                cursor = (start + n + 1) % w.buildings.Count;
                if (b.owner != AiSide.Me || string.IsNullOrEmpty(b.selectedRecipeId)) continue;
                looked++;
                if (ctx.Services.Factory.StallOf(w, c, b) != FactorySimulation.Stall.BackedUp) continue;
                var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
                var item = recipe != null ? CatalogRepository.GetItem(c, recipe.outputItemId) : null;
                if (item == null || !item.sellable) continue;
                stuck++;
                if (HasOutlet(ctx, b)) continue;
                int x, y;
                if (!FreeTileNear(ctx, depot, b, out x, out y)) continue;
                // A dedicated AI keeps the outlet's line to this product alone.
                var plan = ctx.Services.AiBelts.PlanBetween(w, c, ctx.Map, AiSide.Me, b.cellId, b.x, b.y, b.cellId, x, y,
                    ctx.Profile.DedicatedBelts ? recipe.outputItemId : null);
                if (!plan.success || plan.steps.Count == 0) continue;
                d.OutletCommands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = AiSide.Me, contentId = depot.id, fromCellId = b.cellId, fromX = x, fromY = y });
                foreach (var st in plan.steps)
                    d.OutletCommands.Add(new GameCommand
                    {
                        kind = CommandKind.PlaceBelt, actor = AiSide.Me,
                        fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY
                    });
                d.OutletReason = "outlet for " + item.displayName + " from " + b.typeId + " " + b.id;
                Status = d.OutletReason;
                return;
            }
            d.OutletReason = stuck == 0 ? "no machine stuck with goods to sell" : stuck + " stuck machines all have an outlet";
            Status = d.OutletReason;
        }

        static bool HasOutlet(AiContext ctx, BuildingState b)
        {
            foreach (var reached in ctx.Services.Belts.BuildingsReachableFrom(ctx.World, AiSide.Me, b.cellId, b.x, b.y))
            {
                var def = CatalogRepository.GetBuilding(ctx.Catalog, reached.typeId);
                if (reached.id != b.id && def != null && (def.kind == BuildingKind.SaleDepot || def.kind == BuildingKind.Storage)) return true;
            }
            return false;
        }

        internal static bool FreeTileNear(AiContext ctx, BuildingDefinition depot, BuildingState b, out int x, out int y)
        {
            for (int r = 2; r <= 4; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                        x = b.x + dx; y = b.y + dy;
                        if (GridRules.CanPlaceBuilding(ctx.World, ctx.Catalog, ctx.Map, depot, AiSide.Me, b.cellId, x, y).success) return true;
                    }
            x = y = -1;
            return false;
        }
    }

    /// <summary>
    /// Belt Style: carries out the AI's belt intent (<see cref="AiProfile.BeltStyle"/>) on lines already laid. The factory
    /// planner lays new links in that style; this skill reviews a few producers a run for goods sharing a mixed line.
    /// - Dedicated: a producer whose goods run on a mixed line gets a line of its own to the machine that uses them,
    ///   sharing only belts that carry its goods alone. Deliveries then take whichever route is quicker.
    /// - Sushi: mixed lines are fine, but surplus must keep moving: a producer backed up on a mixed line with goods to
    ///   sell gets a Sale Depot on that line (a Storage does not count: surplus is sold, not kept).
    /// One change a run, only when it can be paid for; Medium and Hard (Easy lays its belts as they come).
    /// </summary>
    public sealed class BeltStyleSkill : IAiSkill
    {
        public string Name { get { return "Belt style (dedicated lines or managed sushi)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        /// <summary>Producers reviewed a run (the rest on later runs).</summary>
        public const int ProducersPerRun = 12;
        int cursor;
        readonly List<BuildingState> producers = new List<BuildingState>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(30f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.BeltStyleCommands.Clear();
            bool dedicated = ctx.Profile.DedicatedBelts;
            if (ctx.Profile.Level < 1) { Report(d, "laying belts as they come"); return; }
            if (ctx.Faction.cumulativeSales < AIFactoryPlanner.ShipyardAfterSales) { Report(d, "not yet earning: sharing belts to get going"); return; }

            var carried = AIBeltPlanner.BeltItems(w, c, AiSide.Me);
            int mixed = 0;
            foreach (var items in carried.Values) if (items.Count > 1) mixed++;
            string style = dedicated ? "dedicated" : "sushi";
            if (mixed == 0) { Report(d, style + ": no mixed belts"); return; }

            producers.Clear();
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && (def.kind == BuildingKind.Extractor || def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler)) producers.Add(b);
            }
            producers.Sort((a, b) => a.id.CompareTo(b.id));
            for (int n = 0; n < producers.Count && n < ProducersPerRun; n++)
            {
                var p = producers[(cursor + n) % producers.Count];
                string item = ProductOf(w, c, p);
                if (item == null || !OnMixedLine(ctx, carried, p)) continue;
                if (dedicated ? Untangle(ctx, p, item) : AddOverflow(ctx, p, item))
                {
                    cursor = (cursor + n + 1) % producers.Count;
                    Status = d.BeltStyleReason;
                    return;
                }
            }
            cursor = producers.Count > 0 ? (cursor + ProducersPerRun) % producers.Count : 0;
            Report(d, style + ": " + mixed + " mixed belts, " + (dedicated ? "none to untangle now" : "their surplus has outlets"));
        }

        void Report(AiDecisions d, string why) { d.BeltStyleReason = why; Status = why; }

        /// <summary>Dedicated: a line of the producer's own to the nearest machine along its belts that uses its goods.</summary>
        static bool Untangle(AiContext ctx, BuildingState p, string item)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            BuildingState user = null;
            foreach (var b in ctx.Services.Belts.BuildingsReachableFrom(w, AiSide.Me, p.cellId, p.x, p.y))
            {
                if (b.id == p.id || b.cellId != p.cellId) continue;
                if (FactorySimulation.CurrentNeeds(w, c, b).Exists(n => n.itemId == item)) { user = b; break; }
            }
            if (user == null) return false;
            var plan = ctx.Services.AiBelts.PlanDedicated(w, c, ctx.Map, p.id, user.id, item);
            // No steps: it already has a line of its own there. Not found: no room for one.
            if (!plan.success || plan.steps.Count == 0) return false;
            if (ctx.Faction.currency < plan.totalCost + BudgetSkill.BaseCraftReserve) return false;
            AddBelts(ctx.Decisions.BeltStyleCommands, plan);
            ctx.Decisions.BeltStyleReason = "own line for " + ItemName(c, item) + " from " + p.typeId + " " + p.id + " to " + user.typeId + " " + user.id
                + " (" + plan.steps.Count + " belts)";
            return true;
        }

        /// <summary>Sushi: a backed-up producer on a mixed line gets a Sale Depot, so its surplus is sold and the line moves.</summary>
        static bool AddOverflow(AiContext ctx, BuildingState p, string item)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var goods = CatalogRepository.GetItem(c, item);
            if (goods == null || !goods.sellable) return false;
            if (ctx.Services.Factory.StallOf(w, c, p) != FactorySimulation.Stall.BackedUp) return false;
            foreach (var b in ctx.Services.Belts.BuildingsReachableFrom(w, AiSide.Me, p.cellId, p.x, p.y))
            {
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.kind == BuildingKind.SaleDepot) return false; // it has an outlet already
            }
            BuildingDefinition depot = null;
            foreach (var def in c.buildings) if (def.kind == BuildingKind.SaleDepot) depot = def;
            if (depot == null || ctx.Faction.currency < depot.cost + BudgetSkill.BaseCraftReserve) return false;
            int x, y;
            if (!DepotOutletSkill.FreeTileNear(ctx, depot, p, out x, out y)) return false;
            var plan = ctx.Services.AiBelts.PlanBetween(w, c, ctx.Map, AiSide.Me, p.cellId, p.x, p.y, p.cellId, x, y);
            if (!plan.success || plan.steps.Count == 0) return false;
            if (ctx.Faction.currency < depot.cost + plan.totalCost + BudgetSkill.BaseCraftReserve) return false;
            ctx.Decisions.BeltStyleCommands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = AiSide.Me, contentId = depot.id, fromCellId = p.cellId, fromX = x, fromY = y });
            AddBelts(ctx.Decisions.BeltStyleCommands, plan);
            ctx.Decisions.BeltStyleReason = "overflow depot for " + ItemName(c, item) + " backed up on a shared line at " + p.typeId + " " + p.id;
            return true;
        }

        /// <summary>Whether a belt leaving the producer carries anything besides its own goods.</summary>
        static bool OnMixedLine(AiContext ctx, Dictionary<int, HashSet<string>> carried, BuildingState p)
        {
            foreach (var e in ctx.Services.Belts.Outgoing(ctx.World, p.cellId, p.x, p.y))
            {
                HashSet<string> items;
                if (e.owner == AiSide.Me && carried.TryGetValue(e.id, out items) && items.Count > 1) return true;
            }
            return false;
        }

        static string ProductOf(WorldState w, ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def != null && def.kind == BuildingKind.Extractor)
            {
                var node = WorldLookup.GetNode(w, b.nodeId);
                return node != null && node.itemId != c.rules.ionItemId ? node.itemId : null;
            }
            var recipe = FactorySimulation.RecipeOf(c, b, def);
            return recipe != null ? recipe.outputItemId : null;
        }

        static void AddBelts(List<GameCommand> into, BeltPlanResult plan)
        {
            foreach (var st in plan.steps)
                into.Add(new GameCommand
                {
                    kind = CommandKind.PlaceBelt, actor = AiSide.Me,
                    fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY
                });
        }

        static string ItemName(ContentCatalog c, string id)
        {
            var item = CatalogRepository.GetItem(c, id);
            return item != null ? item.displayName : id;
        }
    }

    /// <summary>
    /// Rebuild after Loss: remembers each cell the AI held with machines on it and then lost, and how much was there.
    /// Taking those cells back counts for more (the Attack Target and Expansion Target skills read it), and once a
    /// cell is the AI's again the factory planner rebuilds its chains there. Medium and Hard.
    /// </summary>
    public sealed class LostGroundSkill : IAiSkill
    {
        public string Name { get { return "Rebuild after loss"; } }
        public AiSkillArea Area { get { return AiSkillArea.Territory; } }
        public string Status { get; private set; }

        readonly Dictionary<int, int> machinesIn = new Dictionary<int, int>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(5f); } // cells change hands rarely

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var d = ctx.Decisions;
            if (ctx.Profile.Level < 1) { d.LostCells.Clear(); d.LostReason = "not tracking losses"; Status = d.LostReason; return; }
            foreach (var cell in w.cells)
            {
                if (cell.owner == AiSide.Me)
                {
                    int n = 0;
                    foreach (var b in w.buildings) if (b.owner == AiSide.Me && b.cellId == cell.id) n++;
                    machinesIn[cell.id] = n;
                    d.LostCells.Remove(cell.id); // held (again)
                }
                else
                {
                    int had;
                    if (machinesIn.TryGetValue(cell.id, out had) && had > 0 && !d.LostCells.ContainsKey(cell.id)) d.LostCells[cell.id] = had;
                    machinesIn.Remove(cell.id);
                }
            }
            d.LostReason = d.LostCells.Count == 0 ? "nothing lost" : "lost " + d.LostCells.Count + " cells with machines; taking them back counts for more";
            Status = d.LostReason;
        }
    }
}
