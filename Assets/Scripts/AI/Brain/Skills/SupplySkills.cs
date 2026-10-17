using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Cross-platform Supply Strategy: what crosses between the AI's asteroids. A refinery turns ten ore into one bar,
    /// so when a chain's ore has to come from another asteroid it is cheaper to refine it there and fly the bars:
    /// the remote asteroid becomes a refining outpost and the chain's own asteroid keeps the assembly. This skill picks
    /// the outposts (working asteroids with ore and room for refineries); the planner then places a chain's refinery
    /// beside its flown-in ore there, and reuses refineries already working there. Hard accepts more crowded
    /// outposts. Easy flies raw ore, as before. Needs Drone Pads.
    /// </summary>
    public sealed class CrossPlatformSupplySkill : IAiSkill, IAiEventSkill
    {
        bool hadDrones;

        /// <summary>Drone Pads just unlocked: pick the outposts before the planner flies its first ore.</summary>
        public bool Triggered(AiContext ctx)
        {
            bool drones = DronesUnlocked(ctx);
            bool fired = drones && !hadDrones;
            hadDrones = drones;
            return fired;
        }

        static bool DronesUnlocked(AiContext ctx)
        {
            foreach (var def in ctx.Catalog.buildings)
                if (def.kind == BuildingKind.DronePad) return ResearchService.IsBuildingUnlocked(ctx.Catalog, ctx.Faction, def.id);
            return false;
        }

        public string Name { get { return "Cross-platform supply"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        /// <summary>Free tiles an asteroid needs to take refineries for other chains (Medium, Hard).</summary>
        public const int OutpostRoomMedium = 40, OutpostRoomHard = 20;

        readonly HashSet<string> raw = new HashSet<string>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(20f); } // and the moment drones unlock

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.SmeltCells.Clear();
            d.SupplyCommands.Clear();
            int rawInFlight = RawInFlight(w, c);
            string flying = rawInFlight > 0 ? " (" + rawInFlight + " raw ore in flight)" : "";
            if (ctx.Profile.Level < 1) { d.SupplyReason = "flies raw ore" + flying; Status = d.SupplyReason; return; }
            if (!DronesUnlocked(ctx))
            {
                d.SupplyReason = "no drones yet: each asteroid makes its own chains";
                Status = d.SupplyReason;
                return;
            }

            int room = ctx.Profile.Level >= 2 ? OutpostRoomHard : OutpostRoomMedium;
            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me || !cell.factoryOnline) continue;
                bool ore = false;
                foreach (var n in w.nodes) if (n.cellId == cell.id) { ore = true; break; }
                if (ore && UpgradeOrExpandSkill.FreeTiles(ctx, cell.id) >= room) d.SmeltCells.Add(cell.id);
            }
            d.SupplyReason = d.SmeltCells.Count == 0 ? "no asteroid with room to refine for others" + flying
                : "refines at the ore on " + d.SmeltCells.Count + " asteroids, flies bars" + flying;

            // A refinery built before this, still fed raw ore flown in from an outpost: take it down so the planner
            // rebuilds it at the ore. One at a time, and only with the credits to rebuild it.
            var moving = RawFedRefinery(ctx);
            if (moving != null)
            {
                d.SupplyCommands.Add(new GameCommand { kind = CommandKind.Dismantle, actor = AiSide.Me, buildingId = moving.id });
                d.SupplyReason += "; moving refinery " + moving.id + " to its ore";
            }
            Status = d.SupplyReason;
        }

        /// <summary>Raw ore seen on its way to a refinery (refinery id to times seen), to move only steady flights.</summary>
        readonly Dictionary<int, int> rawFed = new Dictionary<int, int>();
        readonly List<int> fedNow = new List<int>();
        // Asteroid and recipe of each refinery moved: never twice, should the planner have had to build it back at home.
        readonly HashSet<string> moved = new HashSet<string>();

        /// <summary>A refinery that keeps being fed ore flown in from a refining outpost, if the AI can afford to move it.</summary>
        BuildingState RawFedRefinery(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            fedNow.Clear();
            foreach (var t in w.tokens)
            {
                if (!raw.Contains(t.itemId) || t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                bool flown = false;
                for (int i = 0; i < t.routeSegmentIds.Count && !flown; i++)
                {
                    var s = WorldLookup.GetBelt(w, t.routeSegmentIds[i]);
                    flown = s != null && s.fromCellId != s.toCellId;
                }
                if (!flown) continue;
                var target = WorldLookup.GetBuilding(w, t.targetBuildingId);
                var source = WorldLookup.GetBuilding(w, t.sourceBuildingId);
                if (target == null || source == null || target.owner != AiSide.Me || target.cellId == source.cellId) continue;
                if (!ctx.Decisions.SmeltCells.Contains(source.cellId)) continue;
                var def = CatalogRepository.GetBuilding(c, target.typeId);
                if (def == null || def.kind != BuildingKind.Refinery || fedNow.Contains(target.id)) continue;
                fedNow.Add(target.id);
            }
            // Forget refineries no longer fed that way; count the ones still are.
            var gone = new List<int>();
            foreach (var id in rawFed.Keys) if (!fedNow.Contains(id)) gone.Add(id);
            foreach (var id in gone) rawFed.Remove(id);
            foreach (var id in fedNow) { int n; rawFed.TryGetValue(id, out n); rawFed[id] = n + 1; }
            foreach (var id in fedNow)
            {
                if (rawFed[id] < SteadyRuns) continue;
                var b = WorldLookup.GetBuilding(w, id);
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                string key = b.cellId + ":" + b.selectedRecipeId;
                if (moved.Contains(key)) continue;
                if (ctx.Faction.currency < def.cost + BudgetSkill.BaseCraftReserve) return null;
                rawFed.Remove(id);
                moved.Add(key);
                return b;
            }
            return null;
        }

        /// <summary>Runs in a row a refinery must be seen fed flown-in ore before it is moved.</summary>
        public const int SteadyRuns = 2;

        /// <summary>Raw ore (items no recipe makes) on a drone flight between asteroids just now.</summary>
        int RawInFlight(WorldState w, ContentCatalog c)
        {
            if (raw.Count == 0)
            {
                var made = new HashSet<string>();
                foreach (var r in c.recipes) made.Add(r.outputItemId);
                foreach (var item in c.items) if (!made.Contains(item.id)) raw.Add(item.id);
            }
            int n = 0;
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count || !raw.Contains(t.itemId)) continue;
                var s = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                if (s != null && s.owner == AiSide.Me && s.fromCellId != s.toCellId) n += System.Math.Max(1, t.quantity);
            }
            return n;
        }
    }
}
