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

        public bool HasCapacity(WorldState w, ContentCatalog catalog, int segmentId)
        {
            return CountTokensOnSegment(w, segmentId) < catalog.rules.beltCapacityPerSegment;
        }

        public List<BeltSegmentState> Outgoing(WorldState w, int cellId, int x, int y)
        {
            EnsureCurrent(w);
            List<BeltSegmentState> list;
            return outgoing.TryGetValue(VertexKey(cellId, x, y), out list) ? list : Empty;
        }

        static readonly List<BeltSegmentState> Empty = new List<BeltSegmentState>();

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
            var buildingTiles = new HashSet<int>();
            foreach (var b in w.buildings) buildingTiles.Add(VertexKey(b.cellId, b.x, b.y));
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
