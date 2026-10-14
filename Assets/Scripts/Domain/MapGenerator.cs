using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Random maps for new games. The cell layout, owners, Headquarters and starting craft come from a template map;
    /// asteroids and scenery are generated from a seed, so the same seed always gives the same map and a save only
    /// needs the map ID ("random-SEED"). Guarantees:
    ///   - both Headquarters cells get the same kit (2 Iron, 1 Copper, 1 Ice, 1 Ion, 1 Silicon), no rich nodes;
    ///   - other starting cells get Iron, Copper, Ion and Ice;
    ///   - neutral cells get commons, each rare resource at least twice and each extra-rare resource exactly once;
    ///   - asteroids keep off the cell border and the central build area, apart from each other;
    ///   - blocking scenery never cuts an asteroid, the centre or a border crossing off from the rest of the cell;
    ///   - scenery counts scale with cell area.
    /// </summary>
    public static class MapGenerator
    {
        public const string Prefix = "random-";
        public const string TemplateMapId = "map_a";

        public static readonly string[] BlockingKinds = { "moonlet", "wreck" };
        public static readonly string[] FlatKinds = { "nebula", "debris", "dust" };

        static readonly string[] HqKit = { "ore", "ore", "copper_ore", "ice", "crystal", "silicon_ore" };
        static readonly string[] OutpostKit = { "ore", "copper_ore", "crystal", "ice" };
        static readonly string[] Rares = { "titanium_ore", "silicon_ore", "sulphur" };
        static readonly string[] ExtraRares = { "unobtanium_ore", "complex_carbon", "gold_ore" };

        static readonly Dictionary<string, int> BaseRate = new Dictionary<string, int>
        {
            { "ore", 10 }, { "copper_ore", 12 }, { "ice", 15 }, { "crystal", 15 },
            { "titanium_ore", 20 }, { "silicon_ore", 20 }, { "sulphur", 20 },
            { "unobtanium_ore", 40 }, { "complex_carbon", 40 }, { "gold_ore", 40 },
        };

        public static bool IsGenerated(string mapId) { return mapId != null && mapId.StartsWith(Prefix, StringComparison.Ordinal); }

        public static string IdFor(int seed) { return Prefix + seed; }

        public static bool TryParseSeed(string mapId, out int seed)
        {
            seed = 0;
            return IsGenerated(mapId) && int.TryParse(mapId.Substring(Prefix.Length), out seed);
        }

        /// <summary>Builds a new map from the template's layout and the seed.</summary>
        public static MapDefinition Generate(MapDefinition template, int seed)
        {
            var rng = new Random(seed);
            var map = new MapDefinition { id = IdFor(seed), displayName = "Frontier " + (Math.Abs(seed) % 10000).ToString("0000") };
            foreach (var s in template.startingCrafts)
                map.startingCrafts.Add(new StartCraftDefinition { faction = s.faction, craftTypeId = s.craftTypeId, cellId = s.cellId, assignedDefender = s.assignedDefender });

            // Which neutral cells get which rare and extra-rare resources.
            var neutral = new List<int>();
            foreach (var t in template.cells) if (t.startingOwner == FactionId.Neutral) neutral.Add(t.id);
            var extras = new Dictionary<int, List<string>>();
            foreach (int id in neutral) extras[id] = new List<string>();
            var shuffledNeutral = Shuffled(rng, neutral);
            for (int i = 0; i < ExtraRares.Length && i < shuffledNeutral.Count; i++) extras[shuffledNeutral[i]].Add(ExtraRares[i]);
            foreach (var rare in Rares)
            {
                // Each rare in two different neutral cells.
                var picks = Shuffled(rng, neutral);
                for (int i = 0; i < 2 && i < picks.Count; i++) extras[picks[i]].Add(rare);
            }

            int nextNodeId = 101;
            foreach (var t in template.cells)
            {
                var cell = new MapCellDefinition
                {
                    id = t.id, mapX = t.mapX, mapY = t.mapY, gridWidth = t.gridWidth, gridHeight = t.gridHeight,
                    startingOwner = t.startingOwner, headquartersFor = t.headquartersFor
                };
                var items = new List<string>();
                bool fair = t.headquartersFor != FactionId.Neutral;
                if (fair) items.AddRange(HqKit);
                else if (t.startingOwner != FactionId.Neutral) items.AddRange(OutpostKit);
                else
                {
                    items.Add("ore");
                    if (rng.Next(2) == 0) items.Add("ore");
                    items.Add("copper_ore");
                    items.Add("ice");
                    items.Add("crystal");
                    items.AddRange(extras[t.id]);
                }
                PlaceNodes(rng, cell, items, fair, ref nextNodeId);
                PlaceScenery(rng, cell);
                map.cells.Add(cell);
            }
            return map;
        }

        static List<int> Shuffled(Random rng, List<int> list)
        {
            var copy = new List<int>(list);
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = copy[i]; copy[i] = copy[j]; copy[j] = tmp;
            }
            return copy;
        }

        /// <summary>The central build area kept clear for the Headquarters or a deployed Fort.</summary>
        static bool InCentre(MapCellDefinition cell, int x, int y)
        {
            return Math.Abs(x - cell.gridWidth / 2) <= 2 && Math.Abs(y - cell.gridHeight / 2) <= 2;
        }

        static bool OnBorderRing(MapCellDefinition cell, int x, int y)
        {
            return x == 0 || y == 0 || x == cell.gridWidth - 1 || y == cell.gridHeight - 1;
        }

        static void PlaceNodes(Random rng, MapCellDefinition cell, List<string> items, bool fair, ref int nextId)
        {
            foreach (var item in items)
            {
                for (int attempt = 0; attempt < 400; attempt++)
                {
                    int x = 1 + rng.Next(cell.gridWidth - 2), y = 1 + rng.Next(cell.gridHeight - 2);
                    if (InCentre(cell, x, y) || OnBorderRing(cell, x, y)) continue;
                    bool crowded = false;
                    foreach (var n in cell.nodes) if (Math.Max(Math.Abs(n.x - x), Math.Abs(n.y - y)) < 3) { crowded = true; break; }
                    if (crowded) continue;
                    int rate = BaseRate[item];
                    // Commons outside the Headquarters cells are sometimes rich (faster).
                    bool common = item == "ore" || item == "copper_ore" || item == "ice" || item == "crystal";
                    if (!fair && common && rng.Next(4) == 0) rate = Math.Max(5, rate / 2 + 1);
                    cell.nodes.Add(new ResourceNodeDefinition { id = nextId++, x = x, y = y, itemId = item, rateTicks = rate });
                    break;
                }
            }
        }

        static void PlaceScenery(Random rng, MapCellDefinition cell)
        {
            var taken = new HashSet<int>();
            foreach (var n in cell.nodes) taken.Add(n.y * 64 + n.x);
            // Scenery scales with cell area (15x15 = 225 tiles is the reference).
            float area = cell.gridWidth * cell.gridHeight / 225f;
            int blocking = (int)Math.Round((1 + rng.Next(3)) * area);
            for (int i = 0, attempts = 0; i < blocking && attempts < 200; attempts++)
            {
                int x = 1 + rng.Next(cell.gridWidth - 2), y = 1 + rng.Next(cell.gridHeight - 2);
                if (InCentre(cell, x, y) || taken.Contains(y * 64 + x)) continue;
                bool nearNode = false;
                foreach (var n in cell.nodes) if (Math.Max(Math.Abs(n.x - x), Math.Abs(n.y - y)) <= 1) { nearNode = true; break; }
                if (nearNode) continue;
                cell.blockedTiles.Add(new TilePoint { x = x, y = y });
                if (!StaysConnected(cell)) { cell.blockedTiles.RemoveAt(cell.blockedTiles.Count - 1); continue; }
                taken.Add(y * 64 + x);
                cell.scenery.Add(new SceneryDefinition { kind = BlockingKinds[rng.Next(BlockingKinds.Length)], x = x, y = y, variant = rng.Next(4), blocking = true });
                i++;
            }
            int flat = (int)Math.Round((4 + rng.Next(4)) * area);
            for (int i = 0, attempts = 0; i < flat && attempts < 200; attempts++)
            {
                int x = rng.Next(cell.gridWidth), y = rng.Next(cell.gridHeight);
                if (taken.Contains(y * 64 + x) || (x == cell.gridWidth / 2 && y == cell.gridHeight / 2)) continue;
                taken.Add(y * 64 + x);
                cell.scenery.Add(new SceneryDefinition { kind = FlatKinds[rng.Next(FlatKinds.Length)], x = x, y = y, variant = rng.Next(4), blocking = false });
                i++;
            }
        }

        /// <summary>
        /// Every open tile can reach the centre, so every asteroid has an open neighbour to belt from, and every
        /// border tile that is open can carry a belt into the next cell.
        /// </summary>
        public static bool StaysConnected(MapCellDefinition cell)
        {
            int w = cell.gridWidth, h = cell.gridHeight;
            var blocked = new bool[w, h];
            foreach (var t in cell.blockedTiles) blocked[t.x, t.y] = true;
            var seen = new bool[w, h];
            var queue = new Queue<int>();
            int cx = w / 2, cy = h / 2;
            if (blocked[cx, cy]) return false;
            seen[cx, cy] = true;
            queue.Enqueue(cy * w + cx);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue(), x = p % w, y = p / w;
                foreach (var d in new[] { 1, -1 })
                {
                    Visit(x + d, y, w, h, blocked, seen, queue);
                    Visit(x, y + d, w, h, blocked, seen, queue);
                }
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (!blocked[x, y] && !seen[x, y]) return false;
            return true;
        }

        static void Visit(int x, int y, int w, int h, bool[,] blocked, bool[,] seen, Queue<int> queue)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || blocked[x, y] || seen[x, y]) return;
            seen[x, y] = true;
            queue.Enqueue(y * w + x);
        }
    }
}
