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
                var plan = ctx.Services.AiBelts.PlanBetween(w, c, ctx.Map, AiSide.Me, b.cellId, b.x, b.y, b.cellId, x, y);
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

        static bool FreeTileNear(AiContext ctx, BuildingDefinition depot, BuildingState b, out int x, out int y)
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
