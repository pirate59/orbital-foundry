using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Delivery Priority: high delivery priority for what must not wait: a shipyard building a ship, and a Fort
    /// zone under construction (the Fort holds the cell and brings Solar Array slots). Put back to normal when the
    /// ship is done; a finished Fort replaces its zone. Only priorities it raised are lowered again. Medium and Hard.
    /// </summary>
    public sealed class DeliveryPrioritySkill : IAiSkill
    {
        public string Name { get { return "Delivery priority"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        const int High = 1, Normal = 0;
        readonly HashSet<int> raised = new HashSet<int>();
        readonly List<int> done = new List<int>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(5f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            if (ctx.Profile.Level < 1) { d.PriorityReason = "leaves priorities alone"; Status = d.PriorityReason; return; }
            int yards = 0, zones = 0;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null) continue;
                bool urgent = (def.kind == BuildingKind.Shipyard && !string.IsNullOrEmpty(b.orderedCraftTypeId))
                    || (def.kind == BuildingKind.ConstructionZone && b.underConstruction);
                if (!urgent) continue;
                if (def.kind == BuildingKind.Shipyard) yards++; else zones++;
                if (b.priority < High && !Pending(d, b.id)) { d.PriorityCommands.Add(Order(b.id, High)); raised.Add(b.id); }
            }
            // Put back what is no longer urgent (a yard whose ship has launched).
            done.Clear();
            foreach (int id in raised)
            {
                var b = WorldLookup.GetBuilding(w, id);
                if (b == null) { done.Add(id); continue; }
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                bool stillUrgent = def != null && ((def.kind == BuildingKind.Shipyard && !string.IsNullOrEmpty(b.orderedCraftTypeId))
                    || (def.kind == BuildingKind.ConstructionZone && b.underConstruction));
                if (stillUrgent) continue;
                if (b.priority == High && !Pending(d, b.id)) d.PriorityCommands.Add(Order(b.id, Normal));
                done.Add(id);
            }
            foreach (int id in done) raised.Remove(id);
            d.PriorityReason = yards + zones == 0 ? "nothing urgent" : "high priority: " + yards + " shipyards building, " + zones + " Fort zones";
            Status = d.PriorityReason;
        }

        /// <summary>Another skill (Solar supply, which runs first) has already set this building's priority this think.</summary>
        static bool Pending(AiDecisions d, int buildingId)
        {
            foreach (var o in d.PriorityCommands) if (o.buildingId == buildingId) return true;
            return false;
        }

        static GameCommand Order(int buildingId, int level)
        {
            return new GameCommand { kind = CommandKind.SetPriority, actor = AiSide.Me, buildingId = buildingId, contentId = DeliveryPriority.Names[level + 2] };
        }
    }

    /// <summary>
    /// Storage Hub Use: a busy asteroid of the AI's (six or more machines) with no Storage gets one near the middle of
    /// its machines, fed from its most backed-up producers. Surplus then waits in the hub instead of stopping a
    /// machine, other machines can draw from it, and it is where research and solar goods collect. Only once it has
    /// earned its first shipyard; one hub a run, every half-minute. Medium and Hard.
    /// </summary>
    public sealed class StorageHubSkill : IAiSkill
    {
        public string Name { get { return "Storage hub use"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        /// <summary>Machines on an asteroid before it is worth a hub, and producers wired into a new hub.</summary>
        public const int MachinesForHub = 6, FeedsPerHub = 2;

        readonly List<BuildingState> producers = new List<BuildingState>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(30f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.HubCommands.Clear();
            if (ctx.Profile.Level < 1) { d.HubReason = "no hubs"; Status = d.HubReason; return; }
            // A mid-game step: the first shipyard comes before hubs.
            if (!HasShipyard(ctx)) { d.HubReason = "a shipyard first"; Status = d.HubReason; return; }
            BuildingDefinition storage = null;
            foreach (var def in c.buildings) if (def.kind == BuildingKind.Storage) storage = def;
            if (storage == null || ctx.Faction.currency < storage.cost + BudgetSkill.BaseCraftReserve) { d.HubReason = "no hub needed"; Status = d.HubReason; return; }

            foreach (var cell in w.cells)
            {
                if (cell.owner != AiSide.Me || !cell.factoryOnline) continue;
                int machines = 0, sx = 0, sy = 0;
                bool hasStorage = false;
                producers.Clear();
                foreach (var b in w.buildings)
                {
                    if (b.owner != AiSide.Me || b.cellId != cell.id) continue;
                    var def = CatalogRepository.GetBuilding(c, b.typeId);
                    if (def == null) continue;
                    if (def.kind == BuildingKind.Storage) { hasStorage = true; break; }
                    if (def.kind != BuildingKind.Extractor && def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler) continue;
                    machines++;
                    sx += b.x; sy += b.y;
                    if (ctx.Services.Factory.StallOf(w, c, b) == FactorySimulation.Stall.BackedUp) producers.Add(b);
                }
                if (hasStorage || machines < MachinesForHub) continue;
                int x, y;
                if (!FreeTile(ctx, storage, cell.id, sx / machines, sy / machines, out x, out y)) continue;
                d.HubCommands.Add(new GameCommand { kind = CommandKind.PlaceBuilding, actor = AiSide.Me, contentId = storage.id, fromCellId = cell.id, fromX = x, fromY = y });
                int fed = 0;
                foreach (var p in producers)
                {
                    if (fed >= FeedsPerHub) break;
                    var plan = ctx.Services.AiBelts.PlanBetween(w, c, ctx.Map, AiSide.Me, p.cellId, p.x, p.y, cell.id, x, y);
                    if (!plan.success || plan.steps.Count == 0) continue;
                    foreach (var st in plan.steps)
                        d.HubCommands.Add(new GameCommand
                        {
                            kind = CommandKind.PlaceBelt, actor = AiSide.Me,
                            fromCellId = st.fromCellId, fromX = st.fromX, fromY = st.fromY, toCellId = st.toCellId, toX = st.toX, toY = st.toY
                        });
                    fed++;
                }
                d.HubReason = "Storage hub for cell " + cell.id + " (" + machines + " machines, " + fed + " backed-up producers fed in)";
                Status = d.HubReason;
                return;
            }
            d.HubReason = "every busy asteroid has a Storage";
            Status = d.HubReason;
        }

        static bool HasShipyard(AiContext ctx)
        {
            foreach (var b in ctx.World.buildings)
            {
                if (b.owner != AiSide.Me || b.underConstruction) continue;
                var def = CatalogRepository.GetBuilding(ctx.Catalog, b.typeId);
                if (def != null && def.kind == BuildingKind.Shipyard) return true;
            }
            return false;
        }

        static bool FreeTile(AiContext ctx, BuildingDefinition def, int cellId, int cx, int cy, out int x, out int y)
        {
            for (int r = 1; r <= 6; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (System.Math.Abs(dx) != r && System.Math.Abs(dy) != r) continue;
                        x = cx + dx; y = cy + dy;
                        if (GridRules.CanPlaceBuilding(ctx.World, ctx.Catalog, ctx.Map, def, AiSide.Me, cellId, x, y).success) return true;
                    }
            x = y = -1;
            return false;
        }
    }

    /// <summary>
    /// Relayout and Dismantle: clears out what no longer earns its place. A dead-end belt (its far end reaches no
    /// building and no other belt) that has carried nothing for three minutes, and an orphan machine (nothing belted
    /// into it, barely working) idle for three minutes, are dismantled for half their cost back, freeing room and
    /// power. A few a run, every half-minute. Medium and Hard.
    /// </summary>
    public sealed class RelayoutSkill : IAiSkill
    {
        public string Name { get { return "Relayout / dismantle"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        /// <summary>How long (ticks) something must sit unused before it goes, and how many go a run.</summary>
        public const int UnusedTicks = 1800, BeltsPerRun = 8, MachinesPerRun = 2;

        readonly Dictionary<int, int> beltUsed = new Dictionary<int, int>();
        readonly Dictionary<int, int> idleSince = new Dictionary<int, int>();
        readonly HashSet<int> busyBelts = new HashSet<int>();
        readonly HashSet<int> beltEnds = new HashSet<int>();
        readonly HashSet<int> beltStarts = new HashSet<int>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(30f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.RelayoutCommands.Clear();
            if (ctx.Profile.Level < 1) { d.RelayoutReason = "keeps everything"; Status = d.RelayoutReason; return; }

            busyBelts.Clear();
            foreach (var t in w.tokens)
                for (int i = System.Math.Max(0, t.segmentIndex); i < t.routeSegmentIds.Count; i++) busyBelts.Add(t.routeSegmentIds[i]);
            beltEnds.Clear();
            beltStarts.Clear();
            foreach (var s in w.belts)
            {
                if (s.owner != AiSide.Me) continue;
                beltStarts.Add(BeltNetwork.VertexKey(s.fromCellId, s.fromX, s.fromY));
                beltEnds.Add(BeltNetwork.VertexKey(s.toCellId, s.toX, s.toY));
            }

            // Dead-end belts that have carried nothing for a while.
            int belts = 0;
            foreach (var s in w.belts)
            {
                if (s.owner != AiSide.Me) continue;
                int last;
                if (busyBelts.Contains(s.id) || !beltUsed.TryGetValue(s.id, out last)) { beltUsed[s.id] = w.tick; continue; }
                if (belts >= BeltsPerRun || w.tick - last < UnusedTicks) continue;
                int end = BeltNetwork.VertexKey(s.toCellId, s.toX, s.toY);
                bool leadsOn = beltStarts.Contains(end) || WorldLookup.GetBuildingAt(w, s.toCellId, s.toX, s.toY) != null
                    || GridRules.FootprintAt(w, c, s.toCellId, s.toX, s.toY) != null;
                if (leadsOn) continue;
                d.RelayoutCommands.Add(new GameCommand { kind = CommandKind.Dismantle, actor = AiSide.Me, beltId = s.id });
                beltUsed.Remove(s.id);
                belts++;
            }

            // Orphan machines: nothing belted in, and barely working, for a while.
            int machines = 0;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || b.underConstruction) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || (def.kind != BuildingKind.Refinery && def.kind != BuildingKind.Assembler)) { idleSince.Remove(b.id); continue; }
                float up = ctx.Services.Factory.Uptime(b.id);
                bool fedSomething = beltEnds.Contains(BeltNetwork.VertexKey(b.cellId, b.x, b.y));
                if (fedSomething || up < 0f || up > 0.05f) { idleSince.Remove(b.id); continue; }
                int since;
                if (!idleSince.TryGetValue(b.id, out since)) { idleSince[b.id] = w.tick; continue; }
                if (machines >= MachinesPerRun || w.tick - since < UnusedTicks) continue;
                d.RelayoutCommands.Add(new GameCommand { kind = CommandKind.Dismantle, actor = AiSide.Me, buildingId = b.id });
                idleSince.Remove(b.id);
                machines++;
            }
            d.RelayoutReason = belts + machines == 0 ? "nothing to clear" : "dismantling " + belts + " dead-end belts and " + machines + " orphan machines";
            Status = d.RelayoutReason;
        }
    }

    /// <summary>
    /// Storage Management: what its Storage buildings collect. A Storage starts with nothing on its list, as the
    /// player's does; the AI puts on it what it keeps goods in Storage for, Solar Array goods and the goods its next
    /// research levels cost, and takes off what it no longer needs (what is inside then drains as machines draw on
    /// it). The same list and commands as the player's. Every 10 seconds, at every difficulty.
    /// </summary>
    public sealed class StorageManagementSkill : IAiSkill
    {
        public string Name { get { return "Storage management"; } }
        public AiSkillArea Area { get { return AiSkillArea.Logistics; } }
        public string Status { get; private set; }

        readonly HashSet<string> wanted = new HashSet<string>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(10f); }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var f = ctx.Faction;
            var d = ctx.Decisions;
            wanted.Clear();
            foreach (var def in c.buildings)
                if (def.kind == BuildingKind.SolarArray) foreach (var g in def.goodsCost) wanted.Add(g.itemId);
            foreach (var node in c.research)
            {
                var next = ResearchService.NextPlannedLevel(f, node);
                if (next == null || (ResearchService.PlannedLevel(f, node.id) == 0 && !ResearchService.IsOpenPlanned(f, node))) continue;
                foreach (var g in next.goods) wanted.Add(g.itemId);
            }
            int storages = 0, changes = 0;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null || def.kind != BuildingKind.Storage) continue;
                storages++;
                foreach (var item in c.items)
                {
                    if (b.storedItemIds.Contains(item.id) == wanted.Contains(item.id)) continue;
                    d.InstantCommands.Add(new GameCommand { kind = CommandKind.ToggleDepotItem, actor = AiSide.Me, buildingId = b.id, contentId = item.id });
                    changes++;
                }
            }
            d.StorageReason = storages == 0 ? "no Storage" : storages + " Storages collect " + wanted.Count + " goods" + (changes > 0 ? " (" + changes + " changes)" : "");
            Status = d.StorageReason;
        }
    }
}
