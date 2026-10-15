using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Random maps for new games. The cell layout, owners, Headquarters and starting craft come from a template map;
    /// asteroids are generated from a seed, so the same seed always gives the same map and a save only
    /// needs the map ID. New games use the 4x4 "field-SEED" maps (template map_c); older "random-SEED" saves keep
    /// their 4x2 layout (template map_a). Guarantees:
    ///   - both Headquarters cells get the same kit (2 Iron, 1 Copper, 1 Ice, 1 Ion, 1 Silicon), no rich nodes;
    ///   - other starting cells get Iron, Copper, Ion and Ice;
    ///   - neutral cells get commons, each rare resource at least twice and each extra-rare resource exactly once;
    ///   - asteroids keep off the cell border and the central build area, apart from each other.
    /// </summary>
    public static class MapGenerator
    {
        public const string Prefix = "field-";
        public const string TemplateMapId = "map_c";
        /// <summary>The earlier 4x2 random maps, still rebuilt for saves made on them.</summary>
        public const string LegacyPrefix = "random-";
        public const string LegacyTemplateMapId = "map_a";

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

        public static bool IsGenerated(string mapId)
        {
            return mapId != null && (mapId.StartsWith(Prefix, StringComparison.Ordinal) || mapId.StartsWith(LegacyPrefix, StringComparison.Ordinal));
        }

        public static string IdFor(int seed) { return Prefix + seed; }

        /// <summary>The seed and template of a generated map ID.</summary>
        public static bool TryParseSeed(string mapId, out int seed, out string templateId)
        {
            seed = 0;
            templateId = null;
            if (mapId == null) return false;
            string prefix = mapId.StartsWith(Prefix, StringComparison.Ordinal) ? Prefix : mapId.StartsWith(LegacyPrefix, StringComparison.Ordinal) ? LegacyPrefix : null;
            if (prefix == null || !int.TryParse(mapId.Substring(prefix.Length), out seed)) return false;
            templateId = prefix == Prefix ? TemplateMapId : LegacyTemplateMapId;
            return true;
        }

        public static bool TryParseSeed(string mapId, out int seed)
        {
            string template;
            return TryParseSeed(mapId, out seed, out template);
        }

        /// <summary>Builds a new map from the template's layout and the seed.</summary>
        public static MapDefinition Generate(MapDefinition template, int seed)
        {
            var rng = new Random(seed);
            var map = new MapDefinition
            {
                id = (template.id == LegacyTemplateMapId ? LegacyPrefix : Prefix) + seed,
                displayName = "Frontier " + (Math.Abs(seed) % 10000).ToString("0000")
            };
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
    }
}
