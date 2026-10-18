using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>Cell-graph paths for tactical moves and normal-play world travel.</summary>
    public sealed class MovementRules
    {
        /// <summary>
        /// BFS path (excluding the start cell) of at most Movement steps inside the encounter's battle cells.
        /// Ground craft cannot pass cells holding opposing craft; airborne craft may overfly them. Neither may end there.
        /// Returns null when no legal path exists.
        /// </summary>
        public List<int> FindTacticalPath(WorldState w, MapDefinition map, ContentCatalog c, int craftId, int destinationCellId)
        {
            var craft = WorldLookup.GetCraft(w, craftId);
            if (craft == null || !WorldLookup.IsCraftLive(craft)) return null;
            var def = CatalogRepository.GetCraft(c, craft.typeId);
            if (def == null) return null;
            if (WorldLookup.GetCellDef(map, destinationCellId) == null || destinationCellId == craft.cellId) return null;
            var fight = WorldLookup.BattleOf(w, craftId);
            if (fight == null) return null;
            var battle = fight.battleCellIds;
            if (!battle.Contains(destinationCellId) || !battle.Contains(craft.cellId)) return null;
            if (WorldLookup.HasOpposingCraft(w, destinationCellId, craft.owner)) return null;

            var parent = new Dictionary<int, int> { { craft.cellId, -1 } };
            var depth = new Dictionary<int, int> { { craft.cellId, 0 } };
            var queue = new Queue<int>();
            queue.Enqueue(craft.cellId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (depth[cur] >= def.movement) continue;
                foreach (int n in WorldLookup.Neighbors(map, cur))
                {
                    if (parent.ContainsKey(n) || !battle.Contains(n)) continue;
                    bool occupied = WorldLookup.HasOpposingCraft(w, n, craft.owner);
                    if (occupied && !def.airborne) continue;
                    parent[n] = cur;
                    depth[n] = depth[cur] + 1;
                    if (n == destinationCellId) return Unwind(parent, n);
                    queue.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>
        /// Normal-play path (excluding the start) for <paramref name="faction"/>. Front-line movement: cells holding
        /// opposing craft are impassable and cannot be a destination, and cells the opponent holds cannot be flown
        /// through, only entered as the destination (an undefended one is claimed on arrival). Reaching an enemy
        /// headquarters means taking its territory from the edge inward. Travel to attack a defended cell uses
        /// <see cref="FindStagingPath"/>.
        /// </summary>
        public List<int> FindWorldPath(WorldState w, MapDefinition map, FactionId faction, int fromCellId, int targetCellId)
        {
            return FindWorldPath(w, map, faction, fromCellId, targetCellId, false);
        }

        /// <summary>
        /// As above; with <paramref name="withdrawing"/>, enemy-held cells may be crossed (ships flying home after a
        /// battle are never stranded behind the front).
        /// </summary>
        public List<int> FindWorldPath(WorldState w, MapDefinition map, FactionId faction, int fromCellId, int targetCellId, bool withdrawing)
        {
            if (WorldLookup.GetCellDef(map, fromCellId) == null || WorldLookup.GetCellDef(map, targetCellId) == null) return null;
            if (fromCellId == targetCellId) return new List<int>();
            if (WorldLookup.HasOpposingCraft(w, targetCellId, faction)) return null;
            var parent = new Dictionary<int, int> { { fromCellId, -1 } };
            var queue = new Queue<int>();
            queue.Enqueue(fromCellId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                foreach (int n in WorldLookup.Neighbors(map, cur))
                {
                    if (parent.ContainsKey(n)) continue;
                    if (WorldLookup.HasOpposingCraft(w, n, faction)) continue;
                    parent[n] = cur;
                    if (n == targetCellId) return Unwind(parent, n);
                    // The front line: an enemy-held cell is a destination, never a way through.
                    if (!withdrawing && EnemyHeld(w, n, faction)) continue;
                    queue.Enqueue(n);
                }
            }
            return null;
        }

        /// <summary>Shortest world path to a cell adjacent to <paramref name="targetCellId"/> from which an attack can begin.</summary>
        public List<int> FindStagingPath(WorldState w, MapDefinition map, FactionId faction, int fromCellId, int targetCellId)
        {
            if (WorldLookup.IsAdjacent(map, fromCellId, targetCellId)) return new List<int>();
            List<int> best = null;
            foreach (int staging in WorldLookup.Neighbors(map, targetCellId))
            {
                if (EnemyHeld(w, staging, faction)) continue; // attacks start from the front: a cell it does not hold for the enemy
                var path = FindWorldPath(w, map, faction, fromCellId, staging);
                if (path == null) continue;
                if (best == null || path.Count < best.Count) best = path;
            }
            return best;
        }

        /// <summary>Whether the cell is held by an enemy of <paramref name="faction"/> (another team).</summary>
        public static bool EnemyHeld(WorldState w, int cellId, FactionId faction)
        {
            var cell = WorldLookup.GetCell(w, cellId);
            return cell != null && WorldLookup.AreEnemies(w, faction, cell.owner);
        }

        static List<int> Unwind(Dictionary<int, int> parent, int end)
        {
            var path = new List<int>();
            for (int cur = end; parent[cur] != -1; cur = parent[cur]) path.Add(cur);
            path.Reverse();
            return path;
        }
    }
}
