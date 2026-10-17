using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Throughput Balancing and Reuse of Existing Production, one mechanism: a refinery or assembler working flat out
    /// (uptime at or above the profile's line over the last half-minute) is marked saturated, and the factory planner
    /// stops matching new demand to it. A chain that needs more of that good then builds another machine beside it
    /// (balancing the ratio), while machines with spare capacity keep being reused. It limits itself: once supply is
    /// shared, machines are no longer flat out. A cap on machines per recipe keeps it from overbuilding. Starts once
    /// the AI is earning; Easy reuses blindly, as before.
    /// </summary>
    public sealed class ThroughputBalancingSkill : IAiSkill
    {
        public string Name { get { return "Throughput balancing & reuse"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        readonly Dictionary<string, int> perRecipe = new Dictionary<string, int>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(12f); } // it reads a 30 s uptime average

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            var p = ctx.Profile;
            d.Saturated.Clear();
            if (p.Level < 1 || ctx.Faction.cumulativeSales < AIFactoryPlanner.ShipyardAfterSales)
            {
                d.ThroughputReason = p.Level < 1 ? "reusing machines as they are" : "not yet earning";
                Status = d.ThroughputReason;
                return;
            }
            float line = p.Level < 2 ? 0.95f : 0.85f; // Medium: only machines that never rest; Hard: busy ones too
            int cap = p.Level < 2 ? 3 : 5;               // machines per recipe it will build up to
            perRecipe.Clear();
            foreach (var b in w.buildings)
                if (b.owner == AiSide.Me && !string.IsNullOrEmpty(b.selectedRecipeId))
                {
                    int n;
                    perRecipe.TryGetValue(b.selectedRecipeId, out n);
                    perRecipe[b.selectedRecipeId] = n + 1;
                }
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || string.IsNullOrEmpty(b.selectedRecipeId)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) continue;
                if (perRecipe[b.selectedRecipeId] >= cap) continue;
                if (ctx.Services.Factory.Uptime(b.id) >= line) d.Saturated.Add(b.id);
            }
            d.ThroughputReason = d.Saturated.Count == 0 ? "no machine flat out" : d.Saturated.Count + " machines flat out: building more beside them";
            Status = d.ThroughputReason;
        }
    }

    /// <summary>
    /// Upgrade vs Expand: where the next new production chain starts. It stays on its headquarters asteroid (deepening
    /// the factory there) while that has room and the ores the chain needs; otherwise it starts the chain on another of
    /// its working asteroids that has more of them and more room. Easy always starts at home, as before, and so does
    /// every AI until it is earning.
    /// </summary>
    public sealed class UpgradeOrExpandSkill : IAiSkill
    {
        public string Name { get { return "Upgrade vs expand"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        readonly HashSet<string> ores = new HashSet<string>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(20f); } // only matters when a chain starts

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.NewChainCell = -1;
            // Easy, and everyone while starting out, builds at home first.
            if (ctx.Profile.Level < 1 || d.ProductOrder.Count == 0 || ctx.Faction.cumulativeSales < AIFactoryPlanner.ShipyardAfterSales)
            {
                d.GrowthReason = "new chains at home";
                Status = d.GrowthReason;
                return;
            }

            // The ores the next product's chain needs.
            ores.Clear();
            AddOres(c, d.ProductOrder[0], 0);
            int best = -1, bestScore = int.MinValue, home = -1;
            string why = "";
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me || !cell.factoryOnline) continue;
                if (cell.headquartersFor == AiSide.Me) home = cell.id;
                int oresHere = 0;
                foreach (var n in w.nodes)
                    if (n.cellId == cell.id && n.extractorBuildingId < 0 && ores.Contains(n.itemId)) oresHere++;
                int room = FreeTiles(ctx, cell.id);
                // Home is preferred while it has room: no Fort to protect, nearest everything.
                int score = oresHere * 20 + room / 10 + (cell.headquartersFor == AiSide.Me && room >= 60 ? 15 : 0);
                if (score > bestScore) { bestScore = score; best = cell.id; why = "cell " + cell.id + " (" + oresHere + " free ores it needs, " + room + " free tiles)"; }
            }
            d.NewChainCell = best;
            d.GrowthReason = best < 0 ? "no working asteroid" : (best == home ? "deepen at home: " : "start on another asteroid: ") + why;
            Status = d.GrowthReason;
        }

        void AddOres(ContentCatalog c, string itemId, int depth)
        {
            if (depth > 8) return;
            RecipeDefinition recipe = null;
            foreach (var r in c.recipes) if (r.outputItemId == itemId) { recipe = r; break; }
            if (recipe == null) { ores.Add(itemId); return; }
            foreach (var input in recipe.inputs) AddOres(c, input.itemId, depth + 1);
        }

        /// <summary>Tiles of a cell with no building or belt end on them (a rough measure of room).</summary>
        internal static int FreeTiles(AiContext ctx, int cellId)
        {
            var def = WorldLookup.GetCellDef(ctx.Map, cellId);
            if (def == null) return 0;
            int used = 0;
            foreach (var b in ctx.World.buildings) if (b.cellId == cellId) used++;
            foreach (var s in ctx.World.belts) if (s.fromCellId == cellId) used++;
            return System.Math.Max(0, def.gridWidth * def.gridHeight - used);
        }
    }
}
