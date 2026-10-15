using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Demand-driven logistics: consumers create one-unit requests; the oldest serviceable request is matched
    /// to a stocked, reachable source and dispatched as a visible token along the shortest directed route.
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
        /// Depots and storages keep at most this many inbound-or-requested units of each product: enough to keep a
        /// belt line busy, so belt speed and parallel lanes decide throughput rather than the order size.
        /// </summary>
        public const int DepotOutstandingPerItem = 8;


        /// <summary>A request must wait this many ticks before the UI reports it as blocked.</summary>
        public const int BlockedReportTicks = 80;

        public void RefreshRequests(WorldState w, ContentCatalog c)
        {
            // Drop stale requests first.
            for (int i = w.pullRequests.Count - 1; i >= 0; i--)
            {
                var r = w.pullRequests[i];
                var b = WorldLookup.GetBuilding(w, r.targetBuildingId);
                if (b == null || !StillWants(w, c, b, r.itemId)) w.pullRequests.RemoveAt(i);
            }

            foreach (var b in w.buildings)
            {
                if (!WorldLookup.CanReceive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def == null) continue;
                if (b.underConstruction)
                {
                    // Construction sites pull their materials like a shipyard pulls a craft's parts.
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id) + RequestedTotal(w, b.id);
                    foreach (var m in def.materials)
                    {
                        int have = WorldLookup.CountItem(b.inventory, m.itemId) + Inbound(w, b.id, m.itemId) + Requested(w, b.id, m.itemId);
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
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id) + RequestedTotal(w, b.id);
                    foreach (var input in recipe.inputs)
                    {
                        int have = WorldLookup.CountItem(b.inventory, input.itemId) + Inbound(w, b.id, input.itemId) + Requested(w, b.id, input.itemId);
                        for (int want = input.quantity * cycles; have < want && committed < def.inventoryCapacity; have++, committed++)
                            AddRequest(w, b.id, input.itemId, have >= input.quantity);
                    }
                }
                else if (def.kind == BuildingKind.Shipyard)
                {
                    var craft = CatalogRepository.GetCraft(c, b.orderedCraftTypeId);
                    if (craft == null) continue;
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id) + RequestedTotal(w, b.id);
                    foreach (var part in craft.components)
                    {
                        int have = WorldLookup.CountItem(b.inventory, part.itemId) + Inbound(w, b.id, part.itemId) + Requested(w, b.id, part.itemId);
                        for (int k = 0; k < part.quantity - have && committed < def.inventoryCapacity; k++, committed++)
                            AddRequest(w, b.id, part.itemId);
                    }
                }
                else if (def.kind == BuildingKind.SaleDepot)
                {
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    // Depots sell every tick, so only stock and items already on their way count against capacity;
                    // unanswerable requests for unconnected goods must not crowd out goods that are connected.
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id);
                    foreach (var item in c.items)
                    {
                        if (!item.sellable || !ResearchService.IsItemUnlocked(c, faction, item.id) || b.blockedItemIds.Contains(item.id)) continue;
                        int outstanding = Inbound(w, b.id, item.id) + Requested(w, b.id, item.id);
                        for (; outstanding < DepotOutstandingPerItem && committed < def.inventoryCapacity; outstanding++)
                            AddRequest(w, b.id, item.id);
                    }
                }
                else if (def.kind == BuildingKind.Storage)
                {
                    // Storage collects surplus of every item on its accept list, until full (stock plus items on the way).
                    var faction = WorldLookup.GetFaction(w, b.owner);
                    int committed = WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id);
                    foreach (var item in c.items)
                    {
                        if (!ResearchService.IsItemUnlocked(c, faction, item.id) || b.blockedItemIds.Contains(item.id)) continue;
                        // committed already counts goods on their way, so only open requests are added to it.
                        int requested = Requested(w, b.id, item.id);
                        int outstanding = Inbound(w, b.id, item.id) + requested;
                        for (; outstanding < DepotOutstandingPerItem && committed + requested < def.inventoryCapacity; outstanding++, requested++)
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

        static bool StillWants(WorldState w, ContentCatalog c, BuildingState b, string itemId)
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
                return !b.blockedItemIds.Contains(itemId) && WorldLookup.TotalItems(b.inventory) + InboundTotal(w, b.id) < def.inventoryCapacity;
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
        }

        static int Inbound(WorldState w, int buildingId, string itemId)
        {
            int n = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == buildingId && t.itemId == itemId) n++;
            return n;
        }

        static int InboundTotal(WorldState w, int buildingId)
        {
            int n = 0;
            foreach (var t in w.tokens) if (t.targetBuildingId == buildingId) n++;
            return n;
        }

        static int Requested(WorldState w, int buildingId, string itemId)
        {
            int n = 0;
            foreach (var r in w.pullRequests) if (r.targetBuildingId == buildingId && r.itemId == itemId) n += r.quantity;
            return n;
        }

        static int RequestedTotal(WorldState w, int buildingId)
        {
            int n = 0;
            foreach (var r in w.pullRequests) if (r.targetBuildingId == buildingId) n += r.quantity;
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

        public void DispatchOldest(WorldState w, ContentCatalog c, BeltNetwork network)
        {
            BlockedReasonByBuilding.Clear();
            ItemReasons.Clear();
            if (w.pullRequests.Count == 0) return;
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
            var ordered = new List<PullRequestState>(w.pullRequests);
            ordered.Sort((a, b) =>
            {
                int da = isDepot[a.targetBuildingId] ? 1 : 0, db = isDepot[b.targetBuildingId] ? 1 : 0;
                bool firstA = da == 0 && !a.fill, firstB = db == 0 && !b.fill;
                if (firstA != firstB) return firstA ? -1 : 1; // a machine's first batch beats any priority
                int pa = priority[a.targetBuildingId], pb = priority[b.targetBuildingId];
                if (pa != pb) return pb.CompareTo(pa);
                if (da != db) return da.CompareTo(db);
                if (a.fill != b.fill) return a.fill ? 1 : -1; // every machine's first batch before any top-up
                return a.createdTick != b.createdTick ? a.createdTick.CompareTo(b.createdTick) : a.id.CompareTo(b.id);
            });

            var sources = new List<BuildingState>(w.buildings);
            sources.Sort((a, b) => a.id.CompareTo(b.id));

            // Reachability is cached per source/target; the route actually taken is chosen against current belt load.
            var routeCache = new Dictionary<long, BeltPathResult>();
            var load = BeltNetwork.PendingLoad(w);
            // Belts with an item queued on them: depots and storages hold off on those so they never back a line up
            // in front of a machine's delivery.
            var backedUp = new HashSet<int>();
            foreach (var t in w.tokens)
                if (t.status == TokenStatus.Waiting && t.segmentIndex >= 0 && t.segmentIndex < t.routeSegmentIds.Count) backedUp.Add(t.routeSegmentIds[t.segmentIndex]);
            foreach (var req in ordered)
            {
                var target = WorldLookup.GetBuilding(w, req.targetBuildingId);
                if (target == null || !WorldLookup.CanReceive(w, target)) continue;

                BuildingState bestSource = null;
                BeltPathResult bestRoute = null;
                int bestCost = int.MaxValue;
                bool anyStock = false, anyRoute = false;
                foreach (var s in sources)
                {
                    if (s.id == target.id || s.owner != target.owner) continue;
                    // Storages never feed other storages, so items cannot bounce between them.
                    if (isStorage.Contains(target.id) && IsStorage(c, s)) continue;
                    if (!WorldLookup.IsBuildingActive(w, s)) continue;
                    if (!Offers(w, c, s, req.itemId)) continue;
                    if (WorldLookup.CountItem(s.inventory, req.itemId) <= 0) continue;
                    anyStock = true;
                    long key = ((long)s.id << 32) | (uint)target.id;
                    BeltPathResult route;
                    if (!routeCache.TryGetValue(key, out route))
                    {
                        route = network.FindShortestRoute(w, s.id, target.id);
                        routeCache[key] = route;
                    }
                    if (!route.success) continue;
                    anyRoute = true;
                    var quickest = network.FindLeastLoadedRoute(w, c, s, target, load);
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
                        if (InboundTotal(w, target.id) > 0) continue;
                        if (!BlockedReasonByBuilding.ContainsKey(target.id))
                            BlockedReasonByBuilding[target.id] = isStorage.Contains(target.id)
                                ? "Idle: nothing connected is producing surplus to store."
                                : "Idle: no connected machine or extractor is delivering goods to sell.";
                        continue;
                    }
                    var item = CatalogRepository.GetItem(c, req.itemId);
                    string name = item != null ? item.displayName : req.itemId;
                    string reason = !anyStock ? "Waiting for " + name + ": no connected source has stock."
                        : !anyRoute ? "Waiting for " + name + ": no belt route from a source."
                        : "Waiting for " + name + ": the first belt is full.";
                    if (!BlockedReasonByBuilding.ContainsKey(target.id)) BlockedReasonByBuilding[target.id] = reason;
                    continue;
                }

                WorldLookup.RemoveItem(bestSource.inventory, req.itemId, 1);
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
                    status = TokenStatus.Moving
                });
                w.pullRequests.Remove(req);
            }
        }
    }
}
