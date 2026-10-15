using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Runtime-only index of directed belt edges. Rebuilt from WorldState lists; nothing here is serialized.
    /// Edge activity is evaluated at query time, so ownership changes are reflected without a rebuild.
    /// </summary>
    public sealed class BeltNetwork
    {
        readonly Dictionary<int, List<BeltSegmentState>> outgoing = new Dictionary<int, List<BeltSegmentState>>();
        readonly Dictionary<int, BeltSegmentState> byId = new Dictionary<int, BeltSegmentState>();
        WorldState indexedWorld;

        /// <summary>The map and content in play, for timing drone links between asteroids (set by GameServices).</summary>
        public MapDefinition Map;
        public ContentCatalog Catalog;
        int indexedBeltCount = -1;
        int indexedNextId = -1;

        public static int VertexKey(int cellId, int x, int y)
        {
            return cellId * 1024 + y * 32 + x;
        }

        // Inverse of VertexKey. Local coordinates must stay below 32.
        public static int VertexCell(int key) { return key / 1024; }
        public static int VertexX(int key) { return key % 32; }
        public static int VertexY(int key) { return (key % 1024) / 32; }

        public void Rebuild(WorldState w, MapDefinition map)
        {
            Rebuild(w);
        }

        public void Rebuild(WorldState w)
        {
            outgoing.Clear();
            byId.Clear();
            foreach (var s in w.belts)
            {
                byId[s.id] = s;
                s.linkTicks = LinkTicks(Map, Catalog, s);
                int key = VertexKey(s.fromCellId, s.fromX, s.fromY);
                List<BeltSegmentState> list;
                if (!outgoing.TryGetValue(key, out list)) { list = new List<BeltSegmentState>(); outgoing[key] = list; }
                list.Add(s);
            }
            foreach (var list in outgoing.Values) list.Sort((a, b) => a.id.CompareTo(b.id));
            indexedWorld = w;
            indexedBeltCount = w.belts.Count;
            indexedNextId = w.nextId;
        }

        /// <summary>Rebuilds when belts were added or removed since the last index (or a different world was loaded).</summary>
        public void EnsureCurrent(WorldState w)
        {
            if (indexedWorld != w || indexedBeltCount != w.belts.Count || (indexedNextId != w.nextId && HasUnindexedBelt(w)))
                Rebuild(w);
        }

        bool HasUnindexedBelt(WorldState w)
        {
            indexedNextId = w.nextId;
            foreach (var s in w.belts) if (!byId.ContainsKey(s.id)) return true;
            return false;
        }

        public bool IsEdgeActive(BeltSegmentState s, WorldState w)
        {
            return WorldLookup.IsBeltActive(w, s);
        }

        public BeltSegmentState GetSegment(WorldState w, int id)
        {
            EnsureCurrent(w);
            BeltSegmentState s;
            return byId.TryGetValue(id, out s) ? s : null;
        }

        /// <summary>Tokens currently occupying an edge. Counted directly so it can never drift from WorldState.</summary>
        public int CountTokensOnSegment(WorldState w, int segmentId)
        {
            int n = 0;
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex >= 0 && t.segmentIndex < t.routeSegmentIds.Count && t.routeSegmentIds[t.segmentIndex] == segmentId) n++;
            }
            return n;
        }

        /// <summary>
        /// The belt's line: the run of belts joined end to end through plain tiles, stopping at buildings and at
        /// junctions (a tile where belts split or merge). Ordered from the line's start to its end.
        /// </summary>
        public List<BeltSegmentState> LineOf(WorldState w, int beltId)
        {
            EnsureCurrent(w);
            var line = new List<BeltSegmentState>();
            var start = GetSegment(w, beltId);
            if (start == null) return line;
            var incoming = new Dictionary<int, List<BeltSegmentState>>();
            foreach (var s in w.belts)
            {
                int key = VertexKey(s.toCellId, s.toX, s.toY);
                List<BeltSegmentState> list;
                if (!incoming.TryGetValue(key, out list)) incoming[key] = list = new List<BeltSegmentState>();
                list.Add(s);
            }
            var buildingTiles = new HashSet<int>();
            foreach (var b in w.buildings) buildingTiles.Add(VertexKey(b.cellId, b.x, b.y));
            System.Func<int, bool> plain = key =>
            {
                List<BeltSegmentState> ins, outs;
                incoming.TryGetValue(key, out ins);
                outgoing.TryGetValue(key, out outs);
                return !buildingTiles.Contains(key) && ins != null && ins.Count == 1 && outs != null && outs.Count == 1;
            };
            var seen = new HashSet<int> { start.id };
            // Back to the line's start, then forward to its end.
            var cur = start;
            var before = new List<BeltSegmentState>();
            while (true)
            {
                int from = VertexKey(cur.fromCellId, cur.fromX, cur.fromY);
                if (!plain(from)) break;
                var prev = incoming[from][0];
                if (!seen.Add(prev.id)) break;
                before.Add(prev);
                cur = prev;
            }
            before.Reverse();
            line.AddRange(before);
            line.Add(start);
            cur = start;
            while (true)
            {
                int to = VertexKey(cur.toCellId, cur.toX, cur.toY);
                if (!plain(to)) break;
                var next = outgoing[to][0];
                if (!seen.Add(next.id)) break;
                line.Add(next);
                cur = next;
            }
            return line;
        }

        /// <summary>Ticks an item spends on this belt, from its speed tier.</summary>
        /// <summary>
        /// Building tiles that end a route. Drone Pads are not among them: goods pass through a pad onto its link.
        /// </summary>
        HashSet<int> heldPorts;

        /// <summary>Keeps one port list for every route search until <see cref="ReleasePorts"/>; buildings must not change meanwhile.</summary>
        public void HoldPorts(WorldState w) { heldPorts = null; heldPorts = PortTiles(w); }

        public void ReleasePorts() { heldPorts = null; }

        HashSet<int> PortTiles(WorldState w)
        {
            if (heldPorts != null) return heldPorts;
            var tiles = new HashSet<int>();
            foreach (var b in w.buildings)
                if (Catalog == null || !DroneLinks.IsPad(Catalog, b)) tiles.Add(VertexKey(b.cellId, b.x, b.y));
            return tiles;
        }

        public static int TravelTicks(WorldState w, ContentCatalog c, BeltSegmentState s)
        {
            var tiers = c.rules.beltTiers;
            int tier = s == null ? 0 : s.tier < 0 ? 0 : s.tier >= tiers.Count ? tiers.Count - 1 : s.tier;
            int ticks = tiers[tier].ticksPerSegment;
            if (s == null || s.linkTicks <= 0) return ticks;
            // Drone links fly their distance; faster tiers and Drone Engines research speed the drones up.
            int speed = w != null ? DroneLinks.SpeedPercent(c, WorldLookup.GetFaction(w, s.owner)) : 0;
            double flight = s.linkTicks * (double)ticks / tiers[0].ticksPerSegment * 100.0 / (100 + speed);
            return System.Math.Max(ticks, (int)System.Math.Ceiling(flight));
        }

        /// <summary>
        /// One-way flight time at base tier for a belt crossing between two cells' asteroids (0 for a belt within a
        /// cell): the distance between its two tiles times rules.droneTicksPerUnit.
        /// </summary>
        public static int LinkTicks(MapDefinition map, ContentCatalog c, BeltSegmentState s)
        {
            if (map == null || c == null || s.fromCellId == s.toCellId) return 0;
            if (WorldLookup.GetCellDef(map, s.fromCellId) == null || WorldLookup.GetCellDef(map, s.toCellId) == null) return 0;
            float distance = MapLayout.Distance(map, s.fromCellId, s.fromX, s.fromY, s.toCellId, s.toX, s.toY);
            return (int)System.Math.Ceiling(distance * c.rules.droneTicksPerUnit);
        }

        /// <summary>A drone link is busy until its drone has flown home after a delivery.</summary>
        public static bool DroneAway(WorldState w, BeltSegmentState s)
        {
            return s != null && s.linkTicks > 0 && w.tick < s.droneBackTick;
        }

        /// <summary>
        /// An item boards a link: the first one starts a trip (the drone loads for <see cref="DroneLinks.BoardingTicks"/>
        /// when it can carry more), later ones join it. Returns the item's ticks until it lands, the same for everyone
        /// on the trip. Ordinary belts just return their travel time.
        /// </summary>
        public int Board(WorldState w, ContentCatalog c, BeltSegmentState s)
        {
            int travel = TravelTicks(w, c, s);
            if (s == null || s.linkTicks <= 0) return travel;
            if (CountTokensOnSegment(w, s.id) == 0)
            {
                int capacity = DroneLinks.Capacity(c, WorldLookup.GetFaction(w, s.owner));
                s.tripStartTick = w.tick;
                s.tripArriveTick = w.tick + (capacity > 1 ? DroneLinks.BoardingTicks : 0) + travel;
                s.tripBoarded = 0;
                s.tripDelivered = 0;
            }
            s.tripBoarded++;
            return System.Math.Max(1, s.tripArriveTick - w.tick);
        }

        /// <summary>An item leaves a link at the far end; when the last of the trip is off, the drone flies home.</summary>
        public static void DroneDelivered(WorldState w, ContentCatalog c, BeltSegmentState s)
        {
            if (s == null || s.linkTicks <= 0) return;
            s.tripDelivered++;
            if (s.tripDelivered < s.tripBoarded) return;
            s.droneBackTick = w.tick + TravelTicks(w, c, s);
            s.tripStartTick = -1;
        }

        /// <summary>
        /// Items each belt still has to carry: tokens on it now or routed through it later. Routing adds to this as
        /// it dispatches, so parallel branches share the load.
        /// </summary>
        public static Dictionary<int, int> PendingLoad(WorldState w)
        {
            var load = new Dictionary<int, int>();
            foreach (var t in w.tokens)
                for (int i = t.segmentIndex < 0 ? 0 : t.segmentIndex; i < t.routeSegmentIds.Count; i++)
                {
                    int n;
                    load.TryGetValue(t.routeSegmentIds[i], out n);
                    load[t.routeSegmentIds[i]] = n + 1;
                }
            return load;
        }

        /// <summary>
        /// The quickest route once queues are counted: each belt costs its travel time multiplied by one plus the items
        /// already due to cross it. With equal belts this is the shortest route; when a branch is busy, a parallel one
        /// wins. Same port rules as <see cref="FindRouteFromVertex"/>; ties go to lower belt IDs.
        /// </summary>
        public BeltPathResult FindLeastLoadedRoute(WorldState w, ContentCatalog c, BuildingState source, BuildingState target, Dictionary<int, int> load)
        {
            EnsureCurrent(w);
            var result = new BeltPathResult();
            if (source == null || target == null || source.id == target.id) { result.reason = "No route."; return result; }
            int start = VertexKey(source.cellId, source.x, source.y);
            int goal = VertexKey(target.cellId, target.x, target.y);
            var buildingTiles = PortTiles(w);
            var best = new Dictionary<int, long> { { start, 0 } };
            var parentEdge = new Dictionary<int, BeltSegmentState>();
            var done = new HashSet<int>();
            var frontier = new SortedSet<long>();   // cost << 20 | vertex order, so ties pop in insertion order
            var order = new Dictionary<int, int>();
            var vertexByOrder = new List<int>();
            order[start] = 0; vertexByOrder.Add(start);
            frontier.Add(0);
            while (frontier.Count > 0)
            {
                long key = frontier.Min;
                frontier.Remove(key);
                int v = vertexByOrder[(int)(key & 0xFFFFF)];
                if (!done.Add(v)) continue;
                if (v == goal) break;
                if (v != start && buildingTiles.Contains(v)) continue; // other buildings are ports, not transit
                List<BeltSegmentState> edges;
                if (!outgoing.TryGetValue(v, out edges)) continue;
                foreach (var e in edges)
                {
                    if (e.owner != source.owner || !WorldLookup.IsBeltActive(w, e)) continue;
                    int to = VertexKey(e.toCellId, e.toX, e.toY);
                    if (done.Contains(to)) continue;
                    int queued;
                    load.TryGetValue(e.id, out queued);
                    long cost = best[v] + (long)TravelTicks(w, c, e) * (1 + queued);
                    long known;
                    if (best.TryGetValue(to, out known) && known <= cost) continue;
                    best[to] = cost;
                    parentEdge[to] = e;
                    if (!order.ContainsKey(to)) { order[to] = vertexByOrder.Count; vertexByOrder.Add(to); }
                    frontier.Add((cost << 20) | (long)order[to]);
                }
            }
            if (!parentEdge.ContainsKey(goal)) { result.reason = "No connected belt route."; return result; }
            int cur = goal;
            while (cur != start)
            {
                var e = parentEdge[cur];
                result.segmentIds.Add(e.id);
                cur = VertexKey(e.fromCellId, e.fromX, e.fromY);
            }
            result.segmentIds.Reverse();
            result.success = true;
            return result;
        }

        public bool HasCapacity(WorldState w, ContentCatalog catalog, int segmentId)
        {
            BeltSegmentState s;
            int onIt = CountTokensOnSegment(w, segmentId);
            if (byId.TryGetValue(segmentId, out s) && s.linkTicks > 0)
            {
                // A link takes items while its drone is home: a new trip, or joining one still loading, up to capacity.
                if (DroneAway(w, s)) return false;
                if (onIt == 0) return true;
                int capacity = DroneLinks.Capacity(catalog, WorldLookup.GetFaction(w, s.owner));
                return onIt < capacity && s.tripStartTick >= 0 && w.tick < s.tripStartTick + DroneLinks.BoardingTicks;
            }
            return onIt < catalog.rules.beltCapacityPerSegment;
        }

        public List<BeltSegmentState> Outgoing(WorldState w, int cellId, int x, int y)
        {
            EnsureCurrent(w);
            List<BeltSegmentState> list;
            return outgoing.TryGetValue(VertexKey(cellId, x, y), out list) ? list : Empty;
        }

        static readonly List<BeltSegmentState> Empty = new List<BeltSegmentState>();

        /// <summary>
        /// Every building tile the source's goods could reach, under the same rules as <see cref="FindRouteFromVertex"/>:
        /// active belts of the source's owner, and other buildings are ports where a path ends.
        /// </summary>
        public HashSet<int> ReachableTiles(WorldState w, BuildingState source)
        {
            EnsureCurrent(w);
            var buildingTiles = PortTiles(w);
            var reached = new HashSet<int>();
            int start = VertexKey(source.cellId, source.x, source.y);
            var visited = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                List<BeltSegmentState> edges;
                if (!outgoing.TryGetValue(v, out edges)) continue;
                foreach (var e in edges)
                {
                    if (e.owner != source.owner || !WorldLookup.IsBeltActive(w, e)) continue;
                    int to = VertexKey(e.toCellId, e.toX, e.toY);
                    if (!visited.Add(to)) continue;
                    if (buildingTiles.Contains(to)) { reached.Add(to); continue; }
                    queue.Enqueue(to);
                }
            }
            return reached;
        }

        public BeltPathResult FindShortestRoute(WorldState w, int sourceBuildingId, int targetBuildingId)
        {
            var source = WorldLookup.GetBuilding(w, sourceBuildingId);
            var target = WorldLookup.GetBuilding(w, targetBuildingId);
            if (source == null || target == null) return new BeltPathResult { success = false, reason = "Source or destination is missing." };
            if (source.id == target.id) return new BeltPathResult { success = false, reason = "Source and destination are the same building." };
            return FindRouteFromVertex(w, source.owner, source.cellId, source.x, source.y, target, null);
        }

        /// <summary>
        /// Directed BFS over active edges owned by <paramref name="owner"/>. Building tiles other than the target
        /// are ports, not transit vertices. Ties resolve by ascending belt ID because outgoing lists are sorted.
        /// </summary>
        public BeltPathResult FindRouteFromVertex(WorldState w, FactionId owner, int cellId, int x, int y, BuildingState target, BuildingState startBuilding)
        {
            EnsureCurrent(w);
            int start = VertexKey(cellId, x, y);
            int goal = VertexKey(target.cellId, target.x, target.y);
            var buildingTiles = PortTiles(w);
            var parentEdge = new Dictionary<int, BeltSegmentState>();
            var visited = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            bool found = start == goal;
            while (queue.Count > 0 && !found)
            {
                int v = queue.Dequeue();
                List<BeltSegmentState> edges;
                if (!outgoing.TryGetValue(v, out edges)) continue;
                foreach (var e in edges)
                {
                    if (e.owner != owner || !WorldLookup.IsBeltActive(w, e)) continue;
                    int to = VertexKey(e.toCellId, e.toX, e.toY);
                    if (visited.Contains(to)) continue;
                    visited.Add(to);
                    parentEdge[to] = e;
                    if (to == goal) { found = true; break; }
                    // Other buildings terminate a path: items never pass through an unrelated machine.
                    if (buildingTiles.Contains(to)) continue;
                    queue.Enqueue(to);
                }
            }
            var result = new BeltPathResult();
            if (!found || start == goal)
            {
                result.success = false;
                result.reason = start == goal ? "Already at destination." : "No connected belt route.";
                return result;
            }
            int cur = goal;
            while (cur != start)
            {
                var e = parentEdge[cur];
                result.segmentIds.Add(e.id);
                cur = VertexKey(e.fromCellId, e.fromX, e.fromY);
            }
            result.segmentIds.Reverse();
            result.success = true;
            return result;
        }
    }
}
