using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Needs and Gaps Analysis: what the AI is short of, for the rest of the economy skills to act on. Ion against the
    /// next research and ship; power headroom against the next machine; ores its target products need that it does
    /// not mine; ship parts it cannot make yet. Ores it lacks raise the value of cells that have them.
    /// </summary>
    public sealed class NeedsAnalysisSkill : IAiSkill
    {
        public string Name { get { return "Needs & gaps analysis"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        /// <summary>A machine's typical draw: the headroom the next one needs.</summary>
        public const int NextMachineMw = 3;

        readonly HashSet<string> mined = new HashSet<string>();
        readonly List<string> gaps = new List<string>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(3f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var f = ctx.Faction;
            var d = ctx.Decisions;
            gaps.Clear();

            // Ion: the next research level and the next ship, with a little over.
            int ionWanted = 0;
            var target = string.IsNullOrEmpty(d.ResearchTarget) ? null : CatalogRepository.GetResearch(c, d.ResearchTarget);
            var level = target != null ? ResearchService.NextLevel(f, target) : null;
            if (level != null) ionWanted += ResearchService.IonPrice(c, f, level);
            if ((c.rules.aiExpands || c.rules.aiRaids) && d.BuildOrder.Count > 0)
            {
                var craft = CatalogRepository.GetCraft(c, d.BuildOrder[0]);
                if (craft != null) ionWanted += craft.ionCost;
            }
            ionWanted += AIFactoryPlanner.ResearchIonReserve;
            // Only once it is earning does a shortfall send it after more Ion: before that, credits go to income.
            bool earning = f.cumulativeSales >= AIFactoryPlanner.ShipyardAfterSales;
            d.IonWanted = earning ? ionWanted : 0;
            if (f.ion < ionWanted) gaps.Add("Ion " + f.ion + "/" + ionWanted);

            // Power headroom for the next machine.
            d.PowerSpare = f.powerSupply - f.powerDemand;
            if (d.PowerSpare < NextMachineMw) gaps.Add("power " + d.PowerSpare + " MW spare");

            // Ores the wanted products need that it does not mine.
            mined.Clear();
            foreach (var n in w.nodes)
            {
                var b = n.extractorBuildingId >= 0 ? WorldLookup.GetBuilding(w, n.extractorBuildingId) : null;
                if (b != null && b.owner == AiSide.Me) mined.Add(n.itemId);
            }
            d.MissingOres.Clear();
            int looked = 0;
            foreach (var product in d.ProductOrder)
            {
                if (looked++ >= 3) break;
                AddOres(c, product, d.MissingOres, 0);
            }
            d.MissingOres.RemoveWhere(mined.Contains);
            if (d.MissingOres.Count > 0) gaps.Add("ores: " + string.Join(", ", d.MissingOres));

            // Ship parts it cannot make yet.
            d.PartsShort = false;
            if ((c.rules.aiExpands || c.rules.aiRaids) && d.BuildOrder.Count > 0)
            {
                var craft = CatalogRepository.GetCraft(c, d.BuildOrder[0]);
                if (craft != null && !(AiSide.Planner ?? ctx.Services.AiPlanner).CanSupply(w, c, f, craft)) { d.PartsShort = true; gaps.Add(craft.displayName + " parts"); }
            }
            d.NeedsReason = gaps.Count == 0 ? "no gaps" : "short of " + string.Join("; ", gaps);
            Status = d.NeedsReason;
        }

        static void AddOres(ContentCatalog c, string itemId, HashSet<string> into, int depth)
        {
            if (depth > 8) return;
            RecipeDefinition recipe = null;
            foreach (var r in c.recipes) if (r.outputItemId == itemId) { recipe = r; break; }
            if (recipe == null) { into.Add(itemId); return; }
            foreach (var input in recipe.inputs) AddOres(c, input.itemId, into, depth + 1);
        }
    }

    /// <summary>
    /// Product Selection / Value Targeting: the order in which the factory planner builds product chains. Until it is
    /// earning, the simplest chain first (the planner's own order). After that, by credits a minute per megawatt, the
    /// best use of its power; Hard also puts first the parts its next ship needs. Easy keeps the planner's order.
    /// </summary>
    public sealed class ProductSelectionSkill : IAiSkill, IAiEventSkill
    {
        int researchSeen = -1;

        /// <summary>Research finished since the last run: new products may be open.</summary>
        public bool Triggered(AiContext ctx)
        {
            int levels = 0;
            foreach (var r in ctx.Faction.research) levels += r.level;
            if (levels == researchSeen) return false;
            researchSeen = levels;
            return true;
        }

        public string Name { get { return "Product selection / value targeting"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Every(20f); } // and when research finishes

        public void Think(AiContext ctx)
        {
            var c = ctx.Catalog;
            var f = ctx.Faction;
            var d = ctx.Decisions;
            d.ProductOrder.Clear();
            var products = AIFactoryPlanner.ProductsFor(c, f);
            if (ctx.Profile.Level < 1 || f.cumulativeSales < AIFactoryPlanner.ShipyardAfterSales)
            {
                d.ProductOrder.AddRange(products); // the simplest chain first, as the planner would
                d.ProductReason = f.cumulativeSales < AIFactoryPlanner.ShipyardAfterSales ? "simplest first while starting out" : "simplest first";
                Status = d.ProductReason;
                return;
            }
            products.Sort((a, b) =>
            {
                float va = AiEconomics.CreditsPerMwMinute(c, f, a), vb = AiEconomics.CreditsPerMwMinute(c, f, b);
                return va != vb ? vb.CompareTo(va) : string.CompareOrdinal(a, b);
            });
            // Hard: parts for the next ship come first.
            if (ctx.Profile.Level >= 2 && d.BuildOrder.Count > 0)
            {
                var craft = CatalogRepository.GetCraft(c, d.BuildOrder[0]);
                if (craft != null)
                    for (int i = craft.components.Count - 1; i >= 0; i--)
                    {
                        string part = craft.components[i].itemId;
                        if (products.Remove(part)) products.Insert(0, part);
                    }
            }
            d.ProductOrder.AddRange(products);
            d.ProductReason = d.ProductOrder.Count > 0
                ? "best per MW: " + d.ProductOrder[0] + " (" + AiEconomics.CreditsPerMwMinute(c, f, d.ProductOrder[0]).ToString("0") + "/min/MW)"
                : "nothing it can make yet";
            Status = d.ProductReason;
        }
    }

    /// <summary>
    /// Research Goals and Prioritisation: what to research toward. Medium unlocks the most valuable product it cannot
    /// make yet; Hard first unlocks the parts its next ship needs, and takes machine-speed and sale-price research
    /// when nothing is locked. Easy buys the cheapest level, as before (no target).
    /// </summary>
    public sealed class ResearchGoalSkill : IAiSkill, IAiEventSkill
    {
        bool queueWasBusy;

        /// <summary>The research queue just emptied: time to choose the next goal.</summary>
        public bool Triggered(AiContext ctx)
        {
            bool busy = ctx.Faction.researchQueue.Count > 0;
            bool emptied = queueWasBusy && !busy;
            queueWasBusy = busy;
            return emptied;
        }

        public string Name { get { return "Research goals"; } }
        public AiSkillArea Area { get { return AiSkillArea.Research; } }
        public string Status { get; private set; }

        static readonly string[] Boosts = { "process_optimisation", "heavy_industry", "brokerage_contract", "trade_network" };

        public int EveryThinks(AiContext ctx) { return ctx.Every(15f); } // and when the research queue empties

        public void Think(AiContext ctx)
        {
            var c = ctx.Catalog;
            var f = ctx.Faction;
            var d = ctx.Decisions;
            d.ResearchTarget = "";
            if (ctx.Profile.Level < 1) { d.ResearchReason = "cheapest first"; Status = d.ResearchReason; return; }

            // Hard: unlock the next ship's parts first.
            if (ctx.Profile.Level >= 2 && d.PartsShort && d.BuildOrder.Count > 0)
            {
                var craft = CatalogRepository.GetCraft(c, d.BuildOrder[0]);
                if (craft != null)
                    foreach (var part in craft.components)
                    {
                        var gate = GateFor(c, f, part.itemId);
                        if (gate != null) { Choose(d, gate, "unlocks " + part.itemId + " for the " + craft.displayName); Status = d.ResearchReason; return; }
                    }
            }

            // Its next products need ores no single asteroid of its has, but its asteroids have between them: drones.
            var drones = DroneGateNeeded(ctx);
            if (drones != null) { Choose(d, drones, "Drone Pads: its ores are on different asteroids"); Status = d.ResearchReason; return; }

            // The most valuable product it cannot make yet.
            string bestItem = null;
            ResearchNodeDefinition bestGate = null;
            float bestValue = 0f;
            foreach (var item in c.items)
            {
                if (!item.sellable || ResearchService.IsItemUnlocked(c, f, item.id)) continue;
                var gate = GateFor(c, f, item.id);
                if (gate == null) continue;
                float value = AiEconomics.CreditsPerMwMinute(c, f, item.id);
                if (value > bestValue) { bestValue = value; bestItem = item.id; bestGate = gate; }
            }
            if (bestGate != null) { Choose(d, bestGate, "unlocks " + bestItem + " (" + bestValue.ToString("0") + "/min/MW)"); Status = d.ResearchReason; return; }

            // Everything unlocked: speed and prices (Hard).
            if (ctx.Profile.Level >= 2)
                foreach (var id in Boosts)
                {
                    var node = CatalogRepository.GetResearch(c, id);
                    if (node != null && ResearchService.NextLevel(f, node) != null) { Choose(d, node, "economy boost"); Status = d.ResearchReason; return; }
                }
            d.ResearchReason = "cheapest first";
            Status = d.ResearchReason;
        }

        readonly HashSet<string> ores = new HashSet<string>();
        readonly HashSet<string> spread = new HashSet<string>();

        /// <summary>
        /// The research that unlocks Drone Pads, when one of its next three products cannot be made on any one of its
        /// working asteroids but could be from several. Null when drones are unlocked or would not help.
        /// </summary>
        ResearchNodeDefinition DroneGateNeeded(AiContext ctx)
        {
            var c = ctx.Catalog;
            BuildingDefinition pad = null;
            foreach (var def in c.buildings) if (def.kind == BuildingKind.DronePad) pad = def;
            if (pad == null || ResearchService.IsBuildingUnlocked(c, ctx.Faction, pad.id)) return null;
            int cells = 0;
            foreach (var cell in ctx.World.cells) if (cell.owner == AiSide.Me && cell.factoryOnline) cells++;
            if (cells < 2) return null;
            for (int i = 0; i < ctx.Decisions.ProductOrder.Count && i < 3; i++)
            {
                ores.Clear();
                AddOres(c, ctx.Decisions.ProductOrder[i], 0);
                if (ores.Count < 2) continue;
                bool oneCell = false;
                spread.Clear();
                foreach (var cell in ctx.World.cells)
                {
                    if (cell.owner != AiSide.Me || !cell.factoryOnline) continue;
                    int here = 0;
                    foreach (var ore in ores)
                        if (ctx.World.nodes.Exists(n => n.cellId == cell.id && n.itemId == ore)) { here++; spread.Add(ore); }
                    if (here == ores.Count) { oneCell = true; break; }
                }
                if (!oneCell && spread.Count == ores.Count) return ResearchService.BuildingGate(c, pad.id);
            }
            return null;
        }

        void AddOres(ContentCatalog c, string itemId, int depth)
        {
            if (depth > 8) return;
            RecipeDefinition recipe = null;
            foreach (var r in c.recipes) if (r.outputItemId == itemId) { recipe = r; break; }
            if (recipe == null) { ores.Add(itemId); return; }
            foreach (var input in recipe.inputs) AddOres(c, input.itemId, depth + 1);
        }

        static void Choose(AiDecisions d, ResearchNodeDefinition node, string why)
        {
            d.ResearchTarget = node.id;
            d.ResearchReason = node.displayName + " (" + why + ")";
        }

        /// <summary>The not-yet-owned research that unlocks the recipe making an item, or null.</summary>
        static ResearchNodeDefinition GateFor(ContentCatalog c, FactionState f, string itemId)
        {
            foreach (var r in c.recipes)
            {
                if (r.outputItemId != itemId) continue;
                var gate = ResearchService.RecipeGate(c, r.id);
                if (gate != null && ResearchService.NextLevel(f, gate) != null && ResearchService.Level(f, gate.id) == 0) return gate;
            }
            return null;
        }
    }

    /// <summary>
    /// Bottleneck Detection and Repair: reads the same stall check behind the player's bottleneck markers for the
    /// AI's refineries, assemblers and shipyards (extractors are always held back by belt speed, by design). Short of
    /// power sets power first; a machine starved for a minute asks the planner to replan, at most once a minute, since
    /// a broken or missing link is what a replan fixes. Backed-up machines are counted, not replanned. Easy ignores
    /// bottlenecks.
    /// </summary>
    public sealed class BottleneckRepairSkill : IAiSkill
    {
        public string Name { get { return "Bottleneck detection & repair"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        /// <summary>How long (ticks) a machine must stay stuck before the AI acts, and the gap between replans.</summary>
        public const int StuckTicks = 600, ReplanGapTicks = 600;

        readonly Dictionary<int, int> stuckSince = new Dictionary<int, int>();
        readonly List<int> gone = new List<int>();
        int lastReplanTick = -ReplanGapTicks;

        public int EveryThinks(AiContext ctx) { return ctx.Every(10f); } // it reads a 30 s uptime average

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.LowPowerMachines = d.StarvedMachines = d.BackedUpMachines = 0;
            d.ReplanNow = false;
            if (ctx.Profile.Level < 1) { d.BottleneckReason = "not watching bottlenecks"; Status = d.BottleneckReason; return; }
            int longest = -1, longestSince = int.MaxValue;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me) continue;
                var bdef = CatalogRepository.GetBuilding(c, b.typeId);
                if (bdef == null || bdef.kind == BuildingKind.Extractor) continue;
                var stall = ctx.Services.Factory.StallOf(w, c, b);
                if (stall == FactorySimulation.Stall.LowPower) d.LowPowerMachines++;
                if (stall == FactorySimulation.Stall.Starved) d.StarvedMachines++;
                if (stall == FactorySimulation.Stall.BackedUp) d.BackedUpMachines++;
                if (stall == FactorySimulation.Stall.Starved)
                {
                    int since;
                    if (!stuckSince.TryGetValue(b.id, out since)) stuckSince[b.id] = since = w.tick;
                    if (since < longestSince) { longestSince = since; longest = b.id; }
                }
                else stuckSince.Remove(b.id);
            }
            gone.Clear();
            foreach (var id in stuckSince.Keys) if (WorldLookup.GetBuilding(w, id) == null) gone.Add(id);
            foreach (var id in gone) stuckSince.Remove(id);

            if (longest >= 0 && w.tick - longestSince >= StuckTicks && w.tick - lastReplanTick >= ReplanGapTicks)
            {
                d.ReplanNow = true;
                lastReplanTick = w.tick;
                var b = WorldLookup.GetBuilding(w, longest);
                d.ReplanReason = (b != null ? b.typeId : "machine") + " " + longest + " stuck for " + ((w.tick - longestSince) / 10) + "s";
            }
            d.BottleneckReason = d.StarvedMachines + " starved, " + d.BackedUpMachines + " backed up, " + d.LowPowerMachines + " short of power"
                + (d.ReplanNow ? "; replanning (" + d.ReplanReason + ")" : "");
            Status = d.BottleneckReason;
        }
    }

    /// <summary>
    /// Power Management, planned ahead: keeps headroom for the machines still to come rather than adding solar after
    /// a brownout. Power goes first when the spare is below the profile's margin (Medium 3 MW, Hard 6 MW) or any
    /// machine is short of power. When demand is well over supply it holds off new product machines until power
    /// catches up. Easy adds solar only when the factory is nearly at its limit, as before.
    /// </summary>
    public sealed class PowerPlanningSkill : IAiSkill
    {
        public string Name { get { return "Power planned ahead"; } }
        public AiSkillArea Area { get { return AiSkillArea.Economy; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Profile.AssessEveryThinks; }

        public void Think(AiContext ctx)
        {
            var f = ctx.Faction;
            var d = ctx.Decisions;
            var p = ctx.Profile;
            d.PowerMarginMw = p.Level < 1 ? 0 : p.Level < 2 ? 3 : 6;
            int spare = f.powerSupply - f.powerDemand;
            d.PowerFirst = d.PowerMarginMw > 0 && (spare < d.PowerMarginMw || d.LowPowerMachines > 0);
            // Demand over 110% of supply: more machines would only slow every machine further (Medium and Hard).
            d.HoldNewMachines = d.PowerMarginMw > 0 && f.powerDemand * 10 > f.powerSupply * 11;
            d.PowerReason = d.HoldNewMachines ? "holding new machines: demand " + f.powerDemand + " MW over supply " + f.powerSupply
                : d.PowerFirst ? "power first: " + spare + " MW spare, keeping " + d.PowerMarginMw : spare + " MW spare";
            Status = d.PowerReason;
        }
    }
}
