using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Demand-driven logistics: consumers create one-unit requests; the oldest serviceable request is matched
    /// to a stocked, reachable source and dispatched as a visible token along the shortest directed route. Raw
    /// goods travel in stacks: one token answers up to rules.rawStackSize of a building's requests for the item.
    /// </summary>
    public sealed class PullRouter
    {
        /// <summary>Most recent reason each building's request could not be served. Runtime-only, for the UI.</summary>
        public readonly Dictionary<int, string> BlockedReasonByBuilding = new Dictionary<int, string>();

        /// <summary>Why each building's request for an item was not served this tick (building → item → reason). Runtime-only.</summary>
        public readonly Dictionary<int, Dictionary<string, string>> ItemReasons = new Dictionary<int, Dictionary<string, string>>();

        public string GetItemReason(int buildingId, string itemId)
        {
            Dictionary<string, string> byItem;
            string reason;
            return ItemReasons.TryGetValue(buildingId, out byItem) && byItem.TryGetValue(itemId, out reason) ? reason : null;
        }

        /// <summary>
        /// Depots and storages keep at most this many belt loads of each product inbound or requested: enough to keep
        /// a belt line busy, so belt speed and parallel lanes decide throughput rather than the order size.
        /// </summary>
        public const int DepotOutstandingPerItem = 8;

        /// <summary>Units of an item a depot or storage keeps inbound or requested: eight belt loads.</summary>
        public static int DepotOutstanding(ContentCatalog c, FactionState f, string itemId) { return DepotOutstandingPerItem * ResearchService.StackSize(c, f, itemId); }


        /// <summary>A request must wait this many ticks before the UI reports it as blocked.</summary>
        public const int BlockedReportTicks = 80;

        // Units on their way to, and asked for by, each building: counted once a tick rather than by scanning every
        // token and request for every question, which grows with the factory squared.
        readonly Dictionary<(int, string), int> inboundBy = new Dictionary<(int, string), int>();
        readonly Dictionary<(int, string), int> requestedBy = new Dictionary<(int, string), int>();
        readonly Dictionary<int, int> inboundTotal = new Dictionary<int, int>();
        readonly Dictionary<int, int> requestedTotal = new Dictionary<int, int>();

        void CountOutstanding(WorldState w)
        {
            inboundBy.Clear();
            requestedBy.Clear();
            inboundTotal.Clear();
            requestedTotal.Clear();
            foreach (var t in w.tokens)
            {
                Add(inboundBy, (t.targetBuildingId, t.itemId), t.Units);
                Add(inboundTotal, t.targetBuildingId, t.Units);
            }
            foreach (var r in w.pullRequests)
            {
                Add(requestedBy, (r.targetBuildingId, r.itemId), r.quantity);
                Add(requestedTotal, r.targetBuildingId, r.quantity);
            }
        }

        static void Add<TKey>(Dictionary<TKey, int> table, TKey key, int n)
        {
            int v;
            table.TryGetValue(key, out v);
            table[key] = v + n;
        }

        static int Get<TKey>(Dictionary<TKey, int> table, TKey key)
        {
            int v;
            return table.TryGetValue(key, out v) ? v : 0;
        }

        int Inbound(int buildingId, string itemId) { return Get(inboundBy, (buildingId, itemId)); }
        int InboundTotal(int buildingId) { return Get(inboundTotal, buildingId); }
        int Requested(int buildingId, string itemId) { return Get(requestedBy, (buildingId, itemId)); }
        int RequestedTotal(int buildingId) { return Get(requestedTotal, buildingId); }

        // Which items each faction has unlocked, worked out once a tick for the depots and storages that ask.
        readonly Dictionary<(FactionId, string), bool> unlocked = new Dictionary<(FactionId, string), bool>();

        bool Unlocked(ContentCatalog c, FactionState f, string itemId)
        {
            bool v;
            if (!unlocked.TryGetValue((f.id, itemId), out v)) unlocked[(f.id, itemId)] = v = ResearchService.IsItemUnlocked(c, f, itemId);
            return v;
        }

        // ---------------------------------------------------------------- reach and supply, kept while the network stands

        /// <summary>How often (ticks) the supply index is refreshed when nothing in the network changed.</summary>
        public const int SupplyRefreshTicks = 20;

        // Where each source's goods can reach (building tiles) and which exits an extractor may feed, kept until the
        // belts, the buildings or which of them work change: route reach only depends on those.
        readonly Dictionary<int, HashSet<int>> reachCache = new Dictionary<int, HashSet<int>>();
        readonly Dictionary<int, HashSet<int>> exitsCache = new Dictionary<int, HashSet<int>>();
        long topologyKey = long.MinValue;
        WorldState topologyWorld;

        // Goods that could arrive at each building tile: from machines and extractors, and from Storage. A Storage or
        // Sale Depot only asks for goods listed here, so unconnected goods never fill the request list.
        readonly Dictionary<int, HashSet<string>> madeFor = new Dictionary<int, HashSet<string>>();
        readonly Dictionary<int, HashSet<string>> storedFor = new Dictionary<int, HashSet<string>>();
        readonly List<string> sourceItems = new List<string>();
        int supplyTick = int.MinValue;
        bool supplyBuilt;
        // Depots and storages with nothing they could be sent, and since when (for the "Idle" note).
        readonly Dictionary<int, int> idleSince = new Dictionary<int, int>();
        readonly List<int> idleGone = new List<int>();

        static long TopologyKey(WorldState w, ContentCatalog c)
        {
            unchecked
            {
                long h = w.belts.Count * 1000003L + w.buildings.Count;
                foreach (var s in w.belts) h = h * 31 + s.id * 4 + (int)s.owner + (WorldLookup.IsBeltActive(w, s) ? 2 : 0);
                foreach (var b in w.buildings) h = h * 31 + b.id * 2 + (WorldLookup.IsBuildingActive(w, b) ? 1 : 0);
                foreach (var f in w.factions) h = h * 31 + ResearchService.Chutes(c, f);
                return h;
            }
        }

        /// <summary>Drops cached reach when the network changed since it was worked out; true when it did.</summary>
        bool CheckTopology(WorldState w, ContentCatalog c)
        {
            long key = TopologyKey(w, c);
            if (key == topologyKey && topologyWorld == w) return false;
            topologyKey = key;
            topologyWorld = w;
            reachCache.Clear();
            exitsCache.Clear();
            return true;
        }

        HashSet<int> Exits(WorldState w, ContentCatalog c, BeltNetwork network, BuildingState s)
        {
            HashSet<int> exits;
            if (!exitsCache.TryGetValue(s.id, out exits)) exitsCache[s.id] = exits = ExitsFor(w, c, network, s);
            return exits;
        }

        HashSet<int> Reach(WorldState w, ContentCatalog c, BeltNetwork network, BuildingState s)
        {
            HashSet<int> tiles;
            if (!reachCache.TryGetValue(s.id, out tiles))
            {
                long t0 = Perf.Begin();
                reachCache[s.id] = tiles = network.ReachableTiles(w, s, Exits(w, c, network, s));
                Perf.End(Perf.Stage.Reach, t0);
            }
            return tiles;
        }

        /// <summary>
        /// Works out, for every building tile, the goods some source could send there: what each extractor mines and
        /// each machine makes or holds, and what each Storage holds. Refreshed every <see cref="SupplyRefreshTicks"/>
        /// and at once when the network changes.
        /// </summary>
        void RefreshSupply(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            bool changed = CheckTopology(w, c);
            if (!changed && supplyBuilt && w.tick >= supplyTick && w.tick - supplyTick < SupplyRefreshTicks) return;
            supplyTick = w.tick;
            supplyBuilt = true;
            foreach (var set in madeFor.Values) set.Clear();
            foreach (var set in storedFor.Values) set.Clear();
            network.HoldPorts(w);
            try
            {
                foreach (var s in w.buildings)
                {
                    if (!WorldLookup.IsBuildingActive(w, s)) continue;
                    var def = CatalogRepository.GetBuilding(c, s.typeId);
                    if (def == null) continue;
                    sourceItems.Clear();
                    bool store = def.kind == BuildingKind.Storage;
                    if (def.kind == BuildingKind.Extractor)
                    {
                        var node = WorldLookup.GetNode(w, s.nodeId);
                        if (node != null && node.itemId != c.rules.ionItemId) sourceItems.Add(node.itemId);
                    }
                    else if (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler)
                    {
                        var recipe = ActiveRecipe(w, c, s, def);
                        if (recipe != null) sourceItems.Add(recipe.outputItemId);
                    }
                    else if (!store) continue;
                    foreach (var stack in s.inventory)
                        if (stack.quantity > 0 && !sourceItems.Contains(stack.itemId) && Offers(w, c, s, stack.itemId)) sourceItems.Add(stack.itemId);
                    if (sourceItems.Count == 0) continue;
                    var into = store ? storedFor : madeFor;
                    foreach (int tile in Reach(w, c, network, s))
                    {
                        HashSet<string> set;
                        if (!into.TryGetValue(tile, out set)) into[tile] = set = new HashSet<string>();
                        foreach (var item in sourceItems) set.Add(item);
                    }
                }
            }
            finally { network.ReleasePorts(); }
        }

        /// <summary>Whether some connected source could send this good to the building (a Storage never feeds another).</summary>
        bool Suppliable(BuildingState b, bool fromStorageToo, string itemId)
        {
            if (!supplyBuilt) return true; // no network given: ask for everything, as before
            int tile = BeltNetwork.VertexKey(b.cellId, b.x, b.y);
            HashSet<string> set;
            if (madeFor.TryGetValue(tile, out set) && set.Contains(itemId)) return true;
            return fromStorageToo && storedFor.TryGetValue(tile, out set) && set.Contains(itemId);
        }

        /// <summary>Records since when a Storage or Sale Depot has had nothing connected to send it (cleared once it has).</summary>
        void NoteIdle(WorldState w, BuildingState b, bool supplied)
        {
            if (supplied || InboundTotal(b.id) > 0) { idleSince.Remove(b.id); return; }
            if (!idleSince.ContainsKey(b.id)) idleSince[b.id] = w.tick;
        }

        /// <summary>
        /// A Storage or Sale Depot that nothing connected could send anything to, for a while, is idle. It has no
        /// requests to fail, so it is reported here rather than by dispatch.
        /// </summary>
        void ReportIdle(WorldState w, ContentCatalog c)
        {
            idleGone.Clear();
            foreach (var kv in idleSince)
            {
                var b = WorldLookup.GetBuilding(w, kv.Key);
                if (b == null) { idleGone.Add(kv.Key); continue; }
                if (w.tick - kv.Value < BlockedReportTicks || !WorldLookup.IsBuildingActive(w, b)) continue;
                BlockedReasonByBuilding[b.id] = IsStorage(c, b)
                    ? (b.storedItemIds.Count == 0 ? "Idle: no goods chosen to store. Press Settings to pick them." : "Idle: nothing connected is producing surplus of the goods it stores.")
                    : "Idle: no connected machine or extractor is delivering goods to sell.";
            }
            foreach (int id in idleGone) idleSince.Remove(id);
        }

        readonly Dictionary<int, BuildingState> refreshById = new Dictionary<int, BuildingState>();

        public void RefreshRequests(WorldState w, ContentCatalog c)
        {
            RefreshRequests(w, c, null);
        }

        /// <summary>
        /// Brings every building's open requests up to date. With the belt network, Storage and Sale Depots only ask
        /// for goods something connected could send them.
        /// </summary>
        public void RefreshRequests(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            unlocked.Clear();
            if (network != null) RefreshSupply(w, c, network);
            CountOutstanding(w);
            var byId = refreshById;
            byId.Clear();
            foreach (var x in w.buildings) byId[x.id] = x;
            // Drop stale requests first.
            for (int i = w.pullRequests.Count - 1; i >= 0; i--)
            {
                var r = w.pullRequests[i];
                BuildingState b;
                if (!byId.TryGetValue(r.targetBuildingId, out b) || !StillWants(w, c, b, r.itemId))
                {
                    w.pullRequests.RemoveAt(i);
                    Add(requestedBy, (r.targetBuildingId, r.itemId), -r.quantity);
                    Add(requestedTotal, r.targetBuildingId, -r.quantity);
                }
            }

            foreach (var b in w.buildings)
            {
                if (!WorldLookup.CanReceive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null) continue;
                if (b.underConstruction)
                {
                    // Construction sites pull their materials like a shipyard pulls a craft's parts.
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) + RequestedTotal(b.id);
                    foreach (var m in def.materials)
                    {
                        int have = WorldLookup.CountItem(b.inventory, m.itemId) + Inbound(b.id, m.itemId) + Requested(b.id, m.itemId);
                        for (int k = 0; k < m.quantity - have && committed < def.inventoryCapacity; k++, committed++)
                            AddRequest(w, b.id, m.itemId);
                    }
                }
                else if (def.kind == BuildingKind.Refinery || def.kind == BuildingKind.Assembler)
                {
                    // Fill each ingredient's own bucket with as many whole cycles as it holds, so one ingredient never
                    // takes the room another needs. The first cycle's worth is priority; the rest are top-ups served
                    // after every machine's first batch.
                    var recipe = ActiveRecipe(w, c, b, def);
                    if (recipe == null) continue;
                    foreach (var input in recipe.inputs)
                    {
                        int bucket = FactorySimulation.BucketCapacity(def, recipe, input.itemId);
                        int want = bucket / input.quantity * input.quantity;
                        int have = WorldLookup.CountItem(b.inventory, input.itemId) + Inbound(b.id, input.itemId) + Requested(b.id, input.itemId);
                        for (; have < want; have++)
                            AddRequest(w, b.id, input.itemId, have >= input.quantity);
                    }
                }
                else if (def.kind == BuildingKind.Shipyard)
                {
                    // An order's parts, or (idle) one HP of repairs for a docked craft.
                    var needs = FactorySimulation.ShipyardNeeds(w, c, b);
                    if (needs.Count == 0) continue;
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) + RequestedTotal(b.id);
                    foreach (var part in needs)
                    {
                        int have = WorldLookup.CountItem(b.inventory, part.itemId) + Inbound(b.id, part.itemId) + Requested(b.id, part.itemId);
                        for (int k = 0; k < part.quantity - have && committed < def.inventoryCapacity; k++, committed++)
                            AddRequest(w, b.id, part.itemId);
                    }
                }
                else if (def.kind == BuildingKind.SaleDepot)
                {
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    // Depots sell every tick, so only stock and items already on their way count against capacity;
                    // unanswerable requests for unconnected goods must not crowd out goods that are connected.
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id);
                    // Room for a few full loads in flight even once Bulk Crates make raw loads big.
                    int capacity = System.Math.Max(def.inventoryCapacity, 4 * ResearchService.StackSize(c, faction, "ore"));
                    bool any = false;
                    foreach (var item in c.items)
                    {
                        if (!item.sellable || !Unlocked(c, faction, item.id) || b.blockedItemIds.Contains(item.id)) continue;
                        if (!Suppliable(b, true, item.id)) continue;
                        any = true;
                        int outstanding = Inbound(b.id, item.id) + Requested(b.id, item.id);
                        for (int most = DepotOutstanding(c, faction, item.id); outstanding < most && committed < capacity; outstanding++)
                            AddRequest(w, b.id, item.id);
                    }
                    NoteIdle(w, b, any);
                }
                else if (def.kind == BuildingKind.Storage)
                {
                    // Storage collects surplus of every item on its list (none until chosen), until full (stock plus items on the way).
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id);
                    bool any = false;
                    foreach (var item in c.items)
                    {
                        if (!Unlocked(c, faction, item.id) || !b.storedItemIds.Contains(item.id)) continue;
                        if (!Suppliable(b, false, item.id)) continue;
                        any = true;
                        // committed already counts goods on their way, so only open requests are added to it.
                        int requested = Requested(b.id, item.id);
                        int outstanding = Inbound(b.id, item.id) + requested;
                        for (int most = DepotOutstanding(c, faction, item.id); outstanding < most && committed + requested < def.inventoryCapacity; outstanding++, requested++)
                            AddRequest(w, b.id, item.id);
                    }
                    NoteIdle(w, b, any || committed >= def.inventoryCapacity);
                }
            }
        }

        static int RouteCost(WorldState w, ContentCatalog c, BeltNetwork network, BeltPathResult route, Dictionary<int, int> load)
        {
            int cost = 0;
            foreach (int id in route.segmentIds)
            {
                int queued;
                load.TryGetValue(id, out queued);
                cost += BeltNetwork.TravelTicks(w, c, network.GetSegment(w, id)) * (1 + queued);
            }
            return cost;
        }

        static RecipeDefinition ActiveRecipe(WorldState w, ContentCatalog c, BuildingState b, BuildingDefinition def)
        {
            if (string.IsNullOrEmpty(b.selectedRecipeId) || !def.allowedRecipeIds.Contains(b.selectedRecipeId)) return null;
            var recipe = CatalogRepository.GetRecipe(c, b.selectedRecipeId);
            var faction = WorldLookup.GetFaction(w, b.owner);
            return ResearchService.IsRecipeUnlocked(c, faction, recipe) ? recipe : null;
        }

        bool StillWants(WorldState w, ContentCatalog c, BuildingState b, string itemId)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return false;
            if (b.underConstruction)
            {
                foreach (var m in def.materials) if (m.itemId == itemId) return true;
                return false;
            }
            if (def.kind == BuildingKind.SaleDepot)
            {
                var item = CatalogRepository.GetItem(c, itemId);
                return item != null && item.sellable && !b.blockedItemIds.Contains(itemId) && Suppliable(b, true, itemId);
            }
            // A full storage stops asking (its stock and goods on their way fill it), so it is not reported idle.
            if (def.kind == BuildingKind.Storage)
                return b.storedItemIds.Contains(itemId) && Suppliable(b, false, itemId) && WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) < def.inventoryCapacity;
            if (def.kind == BuildingKind.Shipyard)
            {
                foreach (var part in FactorySimulation.ShipyardNeeds(w, c, b)) if (part.itemId == itemId) return true;
                return false;
            }
            var recipe = ActiveRecipe(w, c, b, def);
            if (recipe == null) return false;
            foreach (var input in recipe.inputs) if (input.itemId == itemId) return true;
            return false;
        }

        void AddRequest(WorldState w, int buildingId, string itemId, bool fill = false)
        {
            w.pullRequests.Add(new PullRequestState
            {
                id = WorldLookup.AllocateId(w),
                targetBuildingId = buildingId,
                itemId = itemId,
                quantity = 1,
                createdTick = w.tick,
                fill = fill
            });
            Add(requestedBy, (buildingId, itemId), 1);
            Add(requestedTotal, buildingId, 1);
        }

        /// <summary>Units on their way to a building right now, including any dispatched this tick.</summary>
        static int ScanInboundTotal(WorldState w, int buildingId)
        {
            int n = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == buildingId) n += t.Units;
            return n;
        }

        /// <summary>
        /// Whether a building would take <paramref name="units"/> of an item it was not expecting (a load that lost its
        /// way): a machine whose recipe uses it, a shipyard or construction zone that needs it, a Sale Depot that sells
        /// it, or a Storage that accepts it, with room for the load.
        /// </summary>
        public static bool CanTake(WorldState w, ContentCatalog c, BuildingState b, string itemId, int units)
        {
            if (b == null || !WorldLookup.CanReceive(w, b)) return false;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return false;
            bool wanted;
            if (b.underConstruction) wanted = def.materials.Exists(m => m.itemId == itemId);
            else switch (def.kind)
            {
                case BuildingKind.Refinery:
                case BuildingKind.Assembler:
                    var recipe = ActiveRecipe(w, c, b, def);
                    wanted = recipe != null && recipe.inputs.Exists(i => i.itemId == itemId);
                    break;
                case BuildingKind.Shipyard:
                    wanted = FactorySimulation.ShipyardNeeds(w, c, b).Exists(p => p.itemId == itemId);
                    break;
                case BuildingKind.SaleDepot:
                    var item = CatalogRepository.GetItem(c, itemId);
                    wanted = item != null && item.sellable && !b.blockedItemIds.Contains(itemId);
                    break;
                case BuildingKind.Storage:
                    wanted = b.storedItemIds.Contains(itemId);
                    break;
                default:
                    wanted = false;
                    break;
            }
            if (!wanted) return false;
            var buckets = FactorySimulation.RecipeOf(c, b, def);
            if (buckets != null)
                return WorldLookup.CountItem(b.inventory, itemId) + FactorySimulation.Inbound(w, b.id, itemId) + units <= FactorySimulation.BucketCapacity(def, buckets, itemId);
            int capacity = def.kind == BuildingKind.SaleDepot ? System.Math.Max(def.inventoryCapacity, units) : def.inventoryCapacity;
            return WorldLookup.TotalItems(b.inventory) + ScanInboundTotal(w, b.id) + units <= capacity;
        }

        static bool IsStorage(ContentCatalog c, BuildingState b)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            return def != null && def.kind == BuildingKind.Storage;
        }

        /// <summary>True when this building may hand out <paramref name="itemId"/>: extractors their node item, processors their kind's recipe outputs.</summary>
        public static bool Offers(WorldState w, ContentCatalog c, BuildingState b, string itemId)
        {
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return false;
            switch (def.kind)
            {
                case BuildingKind.Extractor:
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    return node != null && node.itemId == itemId;
                case BuildingKind.Refinery:
                case BuildingKind.Assembler:
                    // Never what fills its own ingredient buckets: those stay to be used here.
                    var own = FactorySimulation.RecipeOf(c, b, def);
                    if (own != null) foreach (var i in own.inputs) if (i.itemId == itemId) return false;
                    foreach (var r in c.recipes)
                        if (r.buildingKind == def.kind && r.outputItemId == itemId) return true;
                    return false;
                case BuildingKind.Storage:
                    return true; // a storage hands out anything it holds
                default:
                    return false;
            }
        }

        /// <summary>True when some belt leaving the source's tile could take another load now.</summary>
        static bool RoomOut(WorldState w, ContentCatalog c, BeltNetwork network, BuildingState source, HashSet<int> exits)
        {
            foreach (var e in network.Outgoing(w, source.cellId, source.x, source.y))
                if (e.owner == source.owner && (exits == null || exits.Contains(e.id)) && network.HasCapacity(w, c, e.id)) return true;
            return false;
        }

        /// <summary>
        /// The exit belts an extractor may feed: its oldest (lowest id) belts leaving its tile, as many as its
        /// owner's chutes allow (1, more with Multi-Chute research); null for any other building (all exits).
        /// </summary>
        static HashSet<int> ExitsFor(WorldState w, ContentCatalog c, BeltNetwork network, BuildingState source)
        {
            var def = CatalogRepository.GetBuilding(c, source.typeId);
            if (def == null || def.kind != BuildingKind.Extractor) return null;
            var mine = new List<int>();
            foreach (var e in network.Outgoing(w, source.cellId, source.x, source.y)) if (e.owner == source.owner) mine.Add(e.id);
            mine.Sort();
            int chutes = ResearchService.Chutes(c, WorldLookup.GetFaction(w, source.owner));
            if (mine.Count > chutes) mine.RemoveRange(chutes, mine.Count - chutes);
            return new HashSet<int>(mine);
        }

        static string WaitingReason(ContentCatalog c, string itemId, bool anyStock, bool anyRoute)
        {
            var item = CatalogRepository.GetItem(c, itemId);
            string name = item != null ? item.displayName : itemId;
            return !anyStock ? "Waiting for " + name + ": no connected source has stock."
                : !anyRoute ? "Waiting for " + name + ": no belt route from a source."
                : "Waiting for " + name + ": the first belt is full.";
        }

        public void DispatchOldest(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            BlockedReasonByBuilding.Clear();
            foreach (int id in reasonsUsed)
            {
                Dictionary<string, string> byItem;
                if (ItemReasons.TryGetValue(id, out byItem)) byItem.Clear();
            }
            reasonsUsed.Clear();
            ReportIdle(w, c);
            if (w.pullRequests.Count == 0) return;
            // No building is placed or removed while goods are dispatched, so route searches share one port list.
            network.HoldPorts(w);
            try { Dispatch(w, c, network); }
            finally { network.ReleasePorts(); }
        }

        // Dispatch's working tables, kept between ticks and cleared rather than built anew, so a tick allocates little.
        readonly Dictionary<int, int> priority = new Dictionary<int, int>();
        readonly Dictionary<int, bool> isDepot = new Dictionary<int, bool>();
        readonly HashSet<int> isStorage = new HashSet<int>();
        readonly Dictionary<int, int> youngestDepotRequest = new Dictionary<int, int>();
        (long rank, int created, int id, PullRequestState r)[] keyed = new (long, int, int, PullRequestState)[64];
        readonly List<PullRequestState> ordered = new List<PullRequestState>();
        readonly List<BuildingState> sources = new List<BuildingState>();
        readonly Dictionary<int, BuildingState> sourceById = new Dictionary<int, BuildingState>();
        readonly Dictionary<string, List<BuildingState>> offering = new Dictionary<string, List<BuildingState>>();
        static readonly List<BuildingState> None = new List<BuildingState>();
        readonly HashSet<int> full = new HashSet<int>();
        readonly Dictionary<(int, string, bool), (bool stock, bool route)> failed = new Dictionary<(int, string, bool), (bool stock, bool route)>();
        readonly Dictionary<int, int> load = new Dictionary<int, int>();
        readonly HashSet<int> backedUp = new HashSet<int>();
        readonly HashSet<PullRequestState> stacked = new HashSet<PullRequestState>();
        readonly Dictionary<(string, bool, bool), string> waitingText = new Dictionary<(string, bool, bool), string>();
        // Buildings whose item reasons were written this tick; their tables are emptied (not dropped) next tick.
        readonly HashSet<int> reasonsUsed = new HashSet<int>();

        sealed class RankOrder : IComparer<(long rank, int created, int id, PullRequestState r)>
        {
            public int Compare((long rank, int created, int id, PullRequestState r) a, (long rank, int created, int id, PullRequestState r) b)
            {
                return a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.created != b.created ? a.created.CompareTo(b.created) : a.id.CompareTo(b.id);
            }
        }
        static readonly RankOrder ByRank = new RankOrder();
        static readonly System.Comparison<BuildingState> ById = (a, b) => a.id.CompareTo(b.id);

        string Waiting(ContentCatalog c, string itemId, bool anyStock, bool anyRoute)
        {
            string text;
            if (!waitingText.TryGetValue((itemId, anyStock, anyRoute), out text)) waitingText[(itemId, anyStock, anyRoute)] = text = WaitingReason(c, itemId, anyStock, anyRoute);
            return text;
        }

        void Dispatch(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            CheckTopology(w, c);
            // Order of service: every machine's first batch; then delivery priority (highest first); then machines and
            // shipyards before depots and storages, so those only take surplus; then top-ups after first batches; then
            // oldest first. Order only matters where requests compete for the same stock, so a priority never reaches
            // past the buildings it competes with. isDepot marks the surplus tier: sale depots and storages.
            priority.Clear();
            isDepot.Clear();
            isStorage.Clear();
            youngestDepotRequest.Clear();
            foreach (var r in w.pullRequests)
            {
                if (!isDepot.ContainsKey(r.targetBuildingId))
                {
                    var tb = WorldLookup.GetBuilding(w, r.targetBuildingId);
                    priority[r.targetBuildingId] = tb != null ? DeliveryPriority.Clamp(tb.priority) : 0;
                    var td = tb != null ? CatalogRepository.GetBuilding(c, tb.typeId) : null;
                    isDepot[r.targetBuildingId] = td != null && (td.kind == BuildingKind.SaleDepot || td.kind == BuildingKind.Storage);
                    if (td != null && td.kind == BuildingKind.Storage) isStorage.Add(r.targetBuildingId);
                }
                if (isDepot[r.targetBuildingId])
                {
                    int y;
                    youngestDepotRequest[r.targetBuildingId] = youngestDepotRequest.TryGetValue(r.targetBuildingId, out y) ? System.Math.Max(y, r.createdTick) : r.createdTick;
                }
            }
            long t2 = Perf.Begin();
            // Each request's place in that order, packed into one number (then age and id), so sorting compares numbers.
            int count = w.pullRequests.Count;
            if (keyed.Length < count) keyed = new (long, int, int, PullRequestState)[System.Math.Max(count, keyed.Length * 2)];
            for (int k = 0; k < count; k++)
            {
                var r = w.pullRequests[k];
                int depot = isDepot[r.targetBuildingId] ? 1 : 0;
                int first = depot == 0 && !r.fill ? 0 : 1;   // a machine's first batch beats any priority
                int pri = 8 - priority[r.targetBuildingId];   // highest priority first
                keyed[k] = (((long)first << 12) | ((long)pri << 4) | ((long)depot << 1) | (r.fill ? 1L : 0L), r.createdTick, r.id, r);
            }
            System.Array.Sort(keyed, 0, count, ByRank);
            ordered.Clear();
            for (int k = 0; k < count; k++) { ordered.Add(keyed[k].r); keyed[k].r = null; }
            Perf.End(Perf.Stage.Sort, t2);

            sources.Clear();
            sources.AddRange(w.buildings);
            sources.Sort(ById);
            var byId = sourceById;
            byId.Clear();
            foreach (var b in sources) byId[b.id] = b;
            // Sources by the items they hold and may hand out, in id order, so each request looks only at those.
            foreach (var list in offering.Values) list.Clear();
            foreach (var s in sources)
            {
                if (!WorldLookup.IsBuildingActive(w, s)) continue;
                foreach (var stack in s.inventory)
                {
                    if (stack.quantity <= 0 || !Offers(w, c, s, stack.itemId)) continue;
                    List<BuildingState> list;
                    if (!offering.TryGetValue(stack.itemId, out list)) offering[stack.itemId] = list = new List<BuildingState>();
                    if (list.Count == 0 || list[list.Count - 1] != s) list.Add(s);
                }
            }
            var none = None;
            full.Clear();
            // A building's requests for one item (first batch or top-up) that found no source stay unserved for the
            // rest of the tick: stock and belt room only shrink as goods are sent. The rest of that group is skipped.
            failed.Clear();

            // The route actually taken is chosen against current belt load.
            BeltNetwork.PendingLoad(w, load);
            // Belts with an item queued on them: depots and storages hold off on those so they never back a line up
            // in front of a machine's delivery.
            backedUp.Clear();
            foreach (var t in w.tokens)
                if (t.status == TokenStatus.Waiting && t.segmentIndex >= 0 && t.segmentIndex < t.routeSegmentIds.Count) backedUp.Add(t.routeSegmentIds[t.segmentIndex]);
            // Requests already riding in another request's stack this tick.
            stacked.Clear();
            foreach (var req in ordered)
            {
                if (stacked.Contains(req)) continue;
                var group = (req.targetBuildingId, req.itemId, req.fill);
                (bool stock, bool route) known;
                if (failed.TryGetValue(group, out known))
                {
                    // The same answer as the group's first request. Only an older machine request can still need its
                    // wait reported; a depot's wait is judged once per depot.
                    if (isDepot[req.targetBuildingId] || BlockedReasonByBuilding.ContainsKey(req.targetBuildingId)
                        || w.tick - req.createdTick < BlockedReportTicks) continue;
                    BlockedReasonByBuilding[req.targetBuildingId] = Waiting(c, req.itemId, known.stock, known.route);
                    continue;
                }
                BuildingState target;
                if (!byId.TryGetValue(req.targetBuildingId, out target) || !WorldLookup.CanReceive(w, target)) continue;

                BuildingState bestSource = null;
                BeltPathResult bestRoute = null;
                int bestCost = int.MaxValue;
                bool anyStock = false, anyRoute = false;
                List<BuildingState> candidates;
                if (!offering.TryGetValue(req.itemId, out candidates)) candidates = none;
                foreach (var s in candidates)
                {
                    if (s.id == target.id || s.owner != target.owner) continue;
                    // Storages never feed other storages, so items cannot bounce between them.
                    if (isStorage.Contains(target.id) && IsStorage(c, s)) continue;
                    if (!WorldLookup.IsBuildingActive(w, s)) continue;
                    if (!Offers(w, c, s, req.itemId)) continue;
                    if (WorldLookup.CountItem(s.inventory, req.itemId) <= 0) continue;
                    anyStock = true;
                    var exits = Exits(w, c, network, s);
                    var tiles = Reach(w, c, network, s);
                    if (!tiles.Contains(BeltNetwork.VertexKey(target.cellId, target.x, target.y))) continue;
                    anyRoute = true;
                    // Every route starts on one of the belts leaving the source; with all of them full there is no
                    // point searching (belt room only shrinks during dispatch, so a full source stays full this tick).
                    if (full.Contains(s.id)) continue;
                    if (!RoomOut(w, c, network, s, exits)) { full.Add(s.id); continue; }
                    long t1 = Perf.Begin();
                    var quickest = network.FindLeastLoadedRoute(w, c, s, target, load, exits);
                    Perf.End(Perf.Stage.Search, t1);
                    if (!quickest.success || !network.HasCapacity(w, c, quickest.segmentIds[0])) continue;
                    // Depot orders and machine top-ups are optional: they never queue onto a backed-up line. A building
                    // given a High priority does queue, so a busy moment on its branch does not hand its goods to a rival.
                    if ((isDepot[target.id] || req.fill) && priority[target.id] <= 0 && quickest.segmentIds.Exists(backedUp.Contains)) continue;
                    int cost = RouteCost(w, c, network, quickest, load);
                    if (bestRoute == null || cost < bestCost)
                    {
                        bestSource = s;
                        bestRoute = quickest;
                        bestCost = cost;
                    }
                }

                if (bestSource == null)
                {
                    failed[group] = (anyStock, anyRoute);
                    {
                        Dictionary<string, string> byItem;
                        if (!ItemReasons.TryGetValue(target.id, out byItem)) ItemReasons[target.id] = byItem = new Dictionary<string, string>();
                        reasonsUsed.Add(target.id);
                        byItem[req.itemId] = !anyStock ? "nothing that makes it has any in stock" : !anyRoute ? "no belt connects a producer to this building" : "the belt out of the producer is busy";
                    }
                    // Only explain requests that have waited noticeably; short gaps are normal flow. A depot asks for every
                    // sellable item, so it is only "blocked" when none of its requests has been served for a while.
                    int waitedSince = isDepot[target.id] ? youngestDepotRequest[target.id] : req.createdTick;
                    if (w.tick - waitedSince < BlockedReportTicks) continue;
                    if (isDepot[target.id])
                    {
                        // Goods already on their way mean it is being served, whatever else it is waiting for.
                        if (ScanInboundTotal(w, target.id) > 0) continue;
                        if (!BlockedReasonByBuilding.ContainsKey(target.id))
                            BlockedReasonByBuilding[target.id] = isStorage.Contains(target.id)
                                ? "Idle: nothing connected is producing surplus to store."
                                : "Idle: no connected machine or extractor is delivering goods to sell.";
                        continue;
                    }
                    if (!BlockedReasonByBuilding.ContainsKey(target.id)) BlockedReasonByBuilding[target.id] = Waiting(c, req.itemId, anyStock, anyRoute);
                    continue;
                }

                // Raw goods ride in stacks: the same building's other open requests for this item share the slot,
                // up to the stack size and the source's stock.
                int units = 1;
                int most = System.Math.Min(ResearchService.StackSize(c, WorldLookup.GetFaction(w, target.owner), req.itemId), WorldLookup.CountItem(bestSource.inventory, req.itemId));
                foreach (var other in ordered)
                {
                    if (units >= most) break;
                    if (other == req || other.targetBuildingId != req.targetBuildingId || other.itemId != req.itemId || !stacked.Add(other)) continue;
                    w.pullRequests.Remove(other);
                    units++;
                }
                WorldLookup.RemoveItem(bestSource.inventory, req.itemId, units);
                foreach (int seg in bestRoute.segmentIds)
                {
                    int n;
                    load.TryGetValue(seg, out n);
                    load[seg] = n + 1;
                }
                int boardTicks = network.Board(w, c, network.GetSegment(w, bestRoute.segmentIds[0]));
                w.tokens.Add(new ItemTokenState
                {
                    id = WorldLookup.AllocateId(w),
                    itemId = req.itemId,
                    sourceBuildingId = bestSource.id,
                    targetBuildingId = target.id,
                    routeSegmentIds = new List<int>(bestRoute.segmentIds),
                    segmentIndex = 0,
                    ticksUntilNextSegment = boardTicks,
                    status = TokenStatus.Moving,
                    quantity = units
                });
                w.pullRequests.Remove(req);
            }
        }
    }
}
