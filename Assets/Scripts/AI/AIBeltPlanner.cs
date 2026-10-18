using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// A* over legal grid vertices in the planning faction's active cells. Existing same-direction edges of that faction
    /// are reused at no currency cost, which naturally produces splits and merges. Returns each missing directed edge
    /// in build order. Never consults authored layouts.
    /// A dedicated route (<see cref="PlanDedicated"/>) carries one item only: it reuses belts that carry nothing else and
    /// never joins a line that carries anything else, so no belt mixes goods (no "sushi").
    /// </summary>
    public sealed class AIBeltPlanner
    {
        const int NewEdgeCost = 10;
        const int ReuseEdgeCost = 1;
        const int CongestionCost = 2;

        public BeltPlanResult Plan(WorldState w, ContentCatalog c, MapDefinition map, int sourceBuildingId, int destinationBuildingId)
        {
            var source = WorldLookup.GetBuilding(w, sourceBuildingId);
            var dest = WorldLookup.GetBuilding(w, destinationBuildingId);
            if (source == null || dest == null) return Fail("Source or destination building is missing.");
            if (source.owner != dest.owner) return Fail("Source and destination belong to different factions.");
            return PlanBetween(w, c, map, source.owner, source.cellId, source.x, source.y, dest.cellId, dest.x, dest.y);
        }

        /// <summary>
        /// A route for <paramref name="itemId"/> alone between two buildings: it may share belts already carrying only
        /// that item, and keeps clear of every tile a belt carrying anything else touches.
        /// </summary>
        public BeltPlanResult PlanDedicated(WorldState w, ContentCatalog c, MapDefinition map, int sourceBuildingId, int destinationBuildingId, string itemId)
        {
            var source = WorldLookup.GetBuilding(w, sourceBuildingId);
            var dest = WorldLookup.GetBuilding(w, destinationBuildingId);
            if (source == null || dest == null) return Fail("Source or destination building is missing.");
            if (source.owner != dest.owner) return Fail("Source and destination belong to different factions.");
            return Search(w, c, map, source.owner, source.cellId, source.x, source.y, dest.cellId, dest.x, dest.y, false, itemId);
        }

        struct Vertex
        {
            public int CellId, X, Y, GX, GY;
        }

        public BeltPlanResult PlanBetween(WorldState w, ContentCatalog c, MapDefinition map, FactionId faction,
            int fromCellId, int fromX, int fromY, int toCellId, int toX, int toY)
        {
            return PlanBetween(w, c, map, faction, fromCellId, fromX, fromY, toCellId, toX, toY, false);
        }

        /// <summary>
        /// As above; with <paramref name="separateLane"/> the route is a new, parallel lane: it reuses no existing belt
        /// and crosses no tile a belt already touches (other than its two ends), so it never merges into another line.
        /// </summary>
        public BeltPlanResult PlanBetween(WorldState w, ContentCatalog c, MapDefinition map, FactionId faction,
            int fromCellId, int fromX, int fromY, int toCellId, int toX, int toY, bool separateLane)
        {
            return Search(w, c, map, faction, fromCellId, fromX, fromY, toCellId, toX, toY, separateLane, null);
        }

        /// <summary>As above; with <paramref name="onlyItem"/> the route is dedicated to that item (see <see cref="PlanDedicated"/>).</summary>
        public BeltPlanResult PlanBetween(WorldState w, ContentCatalog c, MapDefinition map, FactionId faction,
            int fromCellId, int fromX, int fromY, int toCellId, int toX, int toY, string onlyItem)
        {
            return Search(w, c, map, faction, fromCellId, fromX, fromY, toCellId, toX, toY, false, onlyItem);
        }

        BeltPlanResult Search(WorldState w, ContentCatalog c, MapDefinition map, FactionId faction,
            int fromCellId, int fromX, int fromY, int toCellId, int toX, int toY, bool separateLane, string onlyItem)
        {
            if (WorldLookup.IsSuppressed(w, faction)) return Fail("Faction is suppressed.");
            var fromDef = WorldLookup.GetCellDef(map, fromCellId);
            var toDef = WorldLookup.GetCellDef(map, toCellId);
            if (fromDef == null || toDef == null) return Fail("Endpoint cell is missing.");
            if (fromCellId != toCellId) return Fail("Belts cannot cross between asteroids.");

            int startKey = BeltNetwork.VertexKey(fromCellId, fromX, fromY);
            int goalKey = BeltNetwork.VertexKey(toCellId, toX, toY);
            int goalGX = GridRules.GlobalX(toDef, toX), goalGY = GridRules.GlobalY(toDef, toY);

            // Index existing edges by (from,to) key so reuse and lane conflicts are O(1).
            var edges = new Dictionary<long, BeltSegmentState>();
            var tokensOn = new Dictionary<int, int>();
            var beltTiles = separateLane ? new HashSet<int>() : null;
            foreach (var s in w.belts)
            {
                int a = BeltNetwork.VertexKey(s.fromCellId, s.fromX, s.fromY), b = BeltNetwork.VertexKey(s.toCellId, s.toX, s.toY);
                edges[EdgeKey(a, b)] = s;
                if (beltTiles != null) { beltTiles.Add(a); beltTiles.Add(b); }
            }
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                int id = t.routeSegmentIds[t.segmentIndex];
                int n; tokensOn.TryGetValue(id, out n); tokensOn[id] = n + 1;
            }
            var buildingTiles = new HashSet<int>();
            foreach (var b in w.buildings)
            {
                buildingTiles.Add(BeltNetwork.VertexKey(b.cellId, b.x, b.y));
                // A big building's whole footprint is off-limits to belts.
                var bdef = CatalogRepository.GetBuilding(c, b.typeId);
                foreach (var t in GridRules.ShipyardExtraTiles(c, b))
                    buildingTiles.Add(BeltNetwork.VertexKey(b.cellId, t[0], t[1]));
                if (bdef == null || bdef.footprint <= 1) continue;
                int half = bdef.footprint / 2;
                for (int dy = -half; dy <= half; dy++)
                    for (int dx = -half; dx <= half; dx++)
                        buildingTiles.Add(BeltNetwork.VertexKey(b.cellId, b.x + dx, b.y + dy));
            }
            // A dedicated route: what each belt and each belt tile carries, so it can keep to its own item.
            Dictionary<int, HashSet<string>> carried = null;
            Dictionary<int, HashSet<string>> tileCarries = null;
            if (onlyItem != null)
            {
                carried = BeltItems(w, c, faction);
                tileCarries = new Dictionary<int, HashSet<string>>();
                foreach (var seg in w.belts)
                {
                    HashSet<string> items;
                    if (!carried.TryGetValue(seg.id, out items)) continue;
                    foreach (int k in new[] { BeltNetwork.VertexKey(seg.fromCellId, seg.fromX, seg.fromY), BeltNetwork.VertexKey(seg.toCellId, seg.toX, seg.toY) })
                    {
                        HashSet<string> at;
                        if (!tileCarries.TryGetValue(k, out at)) tileCarries[k] = at = new HashSet<string>();
                        at.UnionWith(items);
                    }
                }
            }
            var nodeTiles = new HashSet<int>();
            foreach (var n in w.nodes) nodeTiles.Add(BeltNetwork.VertexKey(n.cellId, n.x, n.y));

            int maxNodes = 0;
            foreach (var cell in map.cells) maxNodes += cell.gridWidth * cell.gridHeight;

            var gScore = new Dictionary<int, int> { { startKey, 0 } };
            var cameFrom = new Dictionary<int, int>();
            var closed = new HashSet<int>();
            var open = new SortedSet<(int f, int h, int key)>();
            var vertices = new Dictionary<int, Vertex>();
            var startV = new Vertex { CellId = fromCellId, X = fromX, Y = fromY, GX = GridRules.GlobalX(fromDef, fromX), GY = GridRules.GlobalY(fromDef, fromY) };
            vertices[startKey] = startV;
            int h0 = Manhattan(startV.GX, startV.GY, goalGX, goalGY);
            open.Add((h0, h0, startKey));

            bool found = false;
            int expansions = 0;
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                int cur = top.key;
                if (closed.Contains(cur)) continue;
                closed.Add(cur);
                if (cur == goalKey) { found = true; break; }
                if (++expansions > maxNodes) break;
                // Building tiles other than the start are ports, never transit vertices.
                if (cur != startKey && buildingTiles.Contains(cur)) continue;

                var v = vertices[cur];
                for (int dir = 0; dir < 4; dir++)
                {
                    int ngx = v.GX + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                    int ngy = v.GY + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                    int ncell, nx, ny;
                    if (!GridRules.TryGlobalToLocal(map, ngx, ngy, out ncell, out nx, out ny)) continue;
                    int nkey = BeltNetwork.VertexKey(ncell, nx, ny);
                    if (closed.Contains(nkey)) continue;
                    // Belts end at an asteroid's edge (only drone pads cross between asteroids).
                    if (ncell != v.CellId) continue;
                    if (!IsPassable(w, map, faction, ncell, nx, ny, nkey, goalKey, buildingTiles, nodeTiles)) continue;

                    int stepCost;
                    BeltSegmentState existing;
                    if (beltTiles != null && nkey != goalKey && beltTiles.Contains(nkey)) continue; // keep the lane separate
                    if (tileCarries != null && nkey != goalKey && !OnlyCarries(tileCarries, nkey, onlyItem)) continue; // would join another line
                    if (edges.TryGetValue(EdgeKey(cur, nkey), out existing))
                    {
                        if (separateLane) continue;
                        // Only an active edge of our own faction can be reused; anything else occupies the lane.
                        if (existing.owner != faction || !WorldLookup.IsBeltActive(w, existing)) continue;
                        if (carried != null && !OnlyCarries(carried, existing.id, onlyItem)) continue;
                        int load; tokensOn.TryGetValue(existing.id, out load);
                        stepCost = ReuseEdgeCost + load * CongestionCost;
                    }
                    else
                    {
                        if (edges.ContainsKey(EdgeKey(nkey, cur))) continue; // opposite direction on the same lane
                        stepCost = NewEdgeCost;
                    }
                    int tentative = gScore[cur] + stepCost;
                    int old;
                    if (gScore.TryGetValue(nkey, out old) && old <= tentative) continue;
                    gScore[nkey] = tentative;
                    cameFrom[nkey] = cur;
                    vertices[nkey] = new Vertex { CellId = ncell, X = nx, Y = ny, GX = ngx, GY = ngy };
                    int h = Manhattan(ngx, ngy, goalGX, goalGY) * ReuseEdgeCost;
                    open.Add((tentative + h, h, nkey));
                }
            }
            if (!found) return Fail("No legal belt route exists between those tiles.");

            var result = new BeltPlanResult { success = true };
            var chain = new List<int>();
            for (int k = goalKey; k != startKey; k = cameFrom[k]) chain.Add(k);
            chain.Add(startKey);
            chain.Reverse();
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                if (edges.ContainsKey(EdgeKey(chain[i], chain[i + 1]))) continue;
                var a = vertices[chain[i]];
                var b = vertices[chain[i + 1]];
                result.steps.Add(new BeltStep { fromCellId = a.CellId, fromX = a.X, fromY = a.Y, toCellId = b.CellId, toX = b.X, toY = b.Y });
            }
            result.totalCost = result.steps.Count * c.rules.beltCost;
            var faction_ = WorldLookup.GetFaction(w, faction);
            if (faction_ != null && result.totalCost > faction_.currency)
            {
                result.success = false;
                result.reason = "Route needs " + result.totalCost + " currency for belts.";
            }
            return result;
        }

        static bool OnlyCarries(Dictionary<int, HashSet<string>> carries, int key, string itemId)
        {
            HashSet<string> items;
            if (!carries.TryGetValue(key, out items)) return true;
            foreach (var i in items) if (i != itemId) return false;
            return true;
        }

        /// <summary>
        /// The items each of a faction's belts can carry: what every building that hands goods out (an extractor's ore,
        /// a machine's product, whatever a storage holds) could send down the belts leaving it, followed through the
        /// network to the next building. A belt with more than one item is a mixed ("sushi") line.
        /// </summary>
        public static Dictionary<int, HashSet<string>> BeltItems(WorldState w, ContentCatalog c, FactionId faction)
        {
            var outgoing = new Dictionary<int, List<BeltSegmentState>>();
            foreach (var seg in w.belts)
            {
                if (seg.owner != faction) continue;
                int from = BeltNetwork.VertexKey(seg.fromCellId, seg.fromX, seg.fromY);
                List<BeltSegmentState> list;
                if (!outgoing.TryGetValue(from, out list)) outgoing[from] = list = new List<BeltSegmentState>();
                list.Add(seg);
            }
            var buildingAt = new HashSet<int>();
            // Drone pads pass goods through to their link; every other building takes them in.
            foreach (var b in w.buildings) if (!DroneLinks.IsPad(c, b)) buildingAt.Add(BeltNetwork.VertexKey(b.cellId, b.x, b.y));
            var result = new Dictionary<int, HashSet<string>>();
            var products = new List<string>();
            var queue = new Queue<int>();
            var seen = new HashSet<int>();
            foreach (var b in w.buildings)
            {
                if (b.owner != faction) continue;
                Products(w, c, b, products);
                if (products.Count == 0) continue;
                int start = BeltNetwork.VertexKey(b.cellId, b.x, b.y);
                if (!outgoing.ContainsKey(start)) continue;
                queue.Clear();
                seen.Clear();
                queue.Enqueue(start);
                seen.Add(start);
                while (queue.Count > 0)
                {
                    int at = queue.Dequeue();
                    List<BeltSegmentState> leaving;
                    if (!outgoing.TryGetValue(at, out leaving)) continue;
                    foreach (var seg in leaving)
                    {
                        HashSet<string> items;
                        if (!result.TryGetValue(seg.id, out items)) result[seg.id] = items = new HashSet<string>();
                        items.UnionWith(products);
                        int next = BeltNetwork.VertexKey(seg.toCellId, seg.toX, seg.toY);
                        // Goods stop at the next building: it takes them in rather than passing them on.
                        if (buildingAt.Contains(next) || !seen.Add(next)) continue;
                        queue.Enqueue(next);
                    }
                }
            }
            return result;
        }

        /// <summary>What a building hands out onto belts.</summary>
        static void Products(WorldState w, ContentCatalog c, BuildingState b, List<string> into)
        {
            into.Clear();
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            if (def == null) return;
            switch (def.kind)
            {
                case BuildingKind.Extractor:
                    var node = WorldLookup.GetNode(w, b.nodeId);
                    if (node != null && node.itemId != c.rules.ionItemId) into.Add(node.itemId);
                    break;
                case BuildingKind.Refinery:
                case BuildingKind.Assembler:
                    var recipe = FactorySimulation.RecipeOf(c, b, def);
                    if (recipe != null) into.Add(recipe.outputItemId);
                    break;
                case BuildingKind.Storage:
                    into.AddRange(b.storedItemIds);
                    break;
            }
        }

        static bool IsPassable(WorldState w, MapDefinition map, FactionId faction, int cellId, int x, int y, int key, int goalKey,
            HashSet<int> buildingTiles, HashSet<int> nodeTiles)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            if (cell == null || cell.owner != faction) return false;
            var def = WorldLookup.GetCellDef(map, cellId);
            if (GridRules.IsBlocked(def, x, y)) return false;
            if (key == goalKey) return true;
            if (buildingTiles.Contains(key)) return false;
            if (nodeTiles.Contains(key)) return false;
            return true;
        }

        static long EdgeKey(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }

        static int Manhattan(int ax, int ay, int bx, int by)
        {
            return Math.Abs(ax - bx) + Math.Abs(ay - by);
        }

        static BeltPlanResult Fail(string reason)
        {
            return new BeltPlanResult { success = false, reason = reason };
        }
    }
}
