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
        public static int DepotOutstanding(ContentCatalog c, string itemId) { return DepotOutstandingPerItem * CatalogRepository.StackSize(c, itemId); }


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

        public void RefreshRequests(WorldState w, ContentCatalog c)
        {
            unlocked.Clear();
            CountOutstanding(w);
            var byId = new Dictionary<int, BuildingState>(w.buildings.Count);
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
                    // Fill the machine: as many cycles of ingredients as fit, leaving room for a cycle's extra output.
                    // The first cycle's worth is priority; the rest are top-ups served after every machine's first batch.
                    var recipe = ActiveRecipe(w, c, b, def);
                    if (recipe == null) continue;
                    int perCycle = 0;
                    foreach (var input in recipe.inputs) perCycle += input.quantity;
                    int room = def.inventoryCapacity - System.Math.Max(0, recipe.outputQuantity - perCycle);
                    int cycles = System.Math.Max(1, room / System.Math.Max(1, perCycle));
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) + RequestedTotal(b.id);
                    foreach (var input in recipe.inputs)
                    {
                        int have = WorldLookup.CountItem(b.inventory, input.itemId) + Inbound(b.id, input.itemId) + Requested(b.id, input.itemId);
                        for (int want = input.quantity * cycles; have < want && committed < def.inventoryCapacity; have++, committed++)
                            AddRequest(w, b.id, input.itemId, have >= input.quantity);
                    }
                }
                else if (def.kind == BuildingKind.Shipyard)
                {
                    var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                    if (craft == null) continue;
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) + RequestedTotal(b.id);
                    foreach (var part in craft.components)
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
                    foreach (var item in c.items)
                    {
                        if (!item.sellable || !Unlocked(c, faction, item.id) || b.blockedItemIds.Contains(item.id)) continue;
                        int outstanding = Inbound(b.id, item.id) + Requested(b.id, item.id);
                        for (int most = DepotOutstanding(c, item.id); outstanding < most && committed < def.inventoryCapacity; outstanding++)
                            AddRequest(w, b.id, item.id);
                    }
                }
                else if (def.kind == BuildingKind.Storage)
                {
                    // Storage collects surplus of every item on its accept list, until full (stock plus items on the way).
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id);
                    foreach (var item in c.items)
                    {
                        if (!Unlocked(c, faction, item.id) || b.blockedItemIds.Contains(item.id)) continue;
                        // committed already counts goods on their way, so only open requests are added to it.
                        int requested = Requested(b.id, item.id);
                        int outstanding = Inbound(b.id, item.id) + requested;
                        for (int most = DepotOutstanding(c, item.id); outstanding < most && committed + requested < def.inventoryCapacity; outstanding++, requested++)
                            AddRequest(w, b.id, item.id);
                    }
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
                return item != null && item.sellable && !b.blockedItemIds.Contains(itemId);
            }
            // A full storage stops asking (its stock and goods on their way fill it), so it is not reported idle.
            if (def.kind == BuildingKind.Storage)
                return !b.blockedItemIds.Contains(itemId) && WorldLookup.TotalItems(b.inventory) + InboundTotal(b.id) < def.inventoryCapacity;
            if (def.kind == BuildingKind.Shipyard)
            {
                var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                if (craft == null) return false;
                foreach (var part in craft.components) if (part.itemId == itemId) return true;
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
        static bool RoomOut(WorldState w, ContentCatalog c, BeltNetwork network, BuildingState source)
        {
            foreach (var e in network.Outgoing(w, source.cellId, source.x, source.y))
                if (e.owner == source.owner && network.HasCapacity(w, c, e.id)) return true;
            return false;
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
            ItemReasons.Clear();
            if (w.pullRequests.Count == 0) return;
            // No building is placed or removed while goods are dispatched, so route searches share one port list.
            network.HoldPorts(w);
            try { Dispatch(w, c, network); }
            finally { network.ReleasePorts(); }
        }

        void Dispatch(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            // Order of service: every machine's first batch; then delivery priority (highest first); then machines and
            // shipyards before depots and storages, so those only take surplus; then top-ups after first batches; then
            // oldest first. Order only matters where requests compete for the same stock, so a priority never reaches
            // past the buildings it competes with. isDepot marks the surplus tier: sale depots and storages.
            var priority = new Dictionary<int, int>();
            var isDepot = new Dictionary<int, bool>();
            var isStorage = new HashSet<int>();
            var youngestDepotRequest = new Dictionary<int, int>();
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
            var keyed = new (long rank, int created, int id, PullRequestState r)[w.pullRequests.Count];
            for (int k = 0; k < keyed.Length; k++)
            {
                var r = w.pullRequests[k];
                int depot = isDepot[r.targetBuildingId] ? 1 : 0;
                int first = depot == 0 && !r.fill ? 0 : 1;   // a machine's first batch beats any priority
                int pri = 8 - priority[r.targetBuildingId];   // highest priority first
                keyed[k] = (((long)first << 12) | ((long)pri << 4) | ((long)depot << 1) | (r.fill ? 1L : 0L), r.createdTick, r.id, r);
            }
            System.Array.Sort(keyed, (a, b) =>
                a.rank != b.rank ? a.rank.CompareTo(b.rank) : a.created != b.created ? a.created.CompareTo(b.created) : a.id.CompareTo(b.id));
            var ordered = new List<PullRequestState>(keyed.Length);
            foreach (var k in keyed) ordered.Add(k.r);
            Perf.End(Perf.Stage.Sort, t2);

            var sources = new List<BuildingState>(w.buildings);
            sources.Sort((a, b) => a.id.CompareTo(b.id));
            var byId = new Dictionary<int, BuildingState>(sources.Count);
            foreach (var b in sources) byId[b.id] = b;
            // Sources by the items they hold and may hand out, in id order, so each request looks only at those.
            var offering = new Dictionary<string, List<BuildingState>>();
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
            var none = new List<BuildingState>();
            var full = new HashSet<int>();
            // Building tiles each source can reach, worked out once a tick.
            var reach = new Dictionary<int, HashSet<int>>();
            // A building's requests for one item (first batch or top-up) that found no source stay unserved for the
            // rest of the tick: stock and belt room only shrink as goods are sent. The rest of that group is skipped.
            var failed = new Dictionary<(int, string, bool), (bool stock, bool route)>();

            // The route actually taken is chosen against current belt load.
            var load = BeltNetwork.PendingLoad(w);
            // Belts with an item queued on them: depots and storages hold off on those so they never back a line up
            // in front of a machine's delivery.
            var backedUp = new HashSet<int>();
            foreach (var t in w.tokens)
                if (t.status == TokenStatus.Waiting && t.segmentIndex >= 0 && t.segmentIndex < t.routeSegmentIds.Count) backedUp.Add(t.routeSegmentIds[t.segmentIndex]);
            // Requests already riding in another request's stack this tick.
            var stacked = new HashSet<PullRequestState>();
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
                    BlockedReasonByBuilding[req.targetBuildingId] = WaitingReason(c, req.itemId, known.stock, known.route);
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
                    HashSet<int> tiles;
                    if (!reach.TryGetValue(s.id, out tiles))
                    {
                        long t0 = Perf.Begin();
                        reach[s.id] = tiles = network.ReachableTiles(w, s);
                        Perf.End(Perf.Stage.Reach, t0);
                    }
                    if (!tiles.Contains(BeltNetwork.VertexKey(target.cellId, target.x, target.y))) continue;
                    anyRoute = true;
                    // Every route starts on one of the belts leaving the source; with all of them full there is no
                    // point searching (belt room only shrinks during dispatch, so a full source stays full this tick).
                    if (full.Contains(s.id)) continue;
                    if (!RoomOut(w, c, network, s)) { full.Add(s.id); continue; }
                    long t1 = Perf.Begin();
                    var quickest = network.FindLeastLoadedRoute(w, c, s, target, load);
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
                    if (!BlockedReasonByBuilding.ContainsKey(target.id)) BlockedReasonByBuilding[target.id] = WaitingReason(c, req.itemId, anyStock, anyRoute);
                    continue;
                }

                // Raw goods ride in stacks: the same building's other open requests for this item share the slot,
                // up to the stack size and the source's stock.
                int units = 1;
                int most = System.Math.Min(CatalogRepository.StackSize(c, req.itemId), WorldLookup.CountItem(bestSource.inventory, req.itemId));
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
