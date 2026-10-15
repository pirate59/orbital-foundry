using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Where each cell's asteroid sits in space. Cells keep their grid neighbours, but every column and row of the
    /// map has its own gap, and each asteroid strays well off its lattice spot sideways (on both horizontal axes) and
    /// up or down, all from a seed taken from the map's ID. A relaxation pass then pushes apart any asteroids that came
    /// too close, so every pair keeps at least <see cref="MinClearance"/> of open space between their grids. Each new
    /// map scatters differently while a saved game always rebuilds the same field. The simulation uses it to time
    /// drone links between asteroids; the presentation uses it to place everything.
    /// </summary>
    public static class MapLayout
    {
        /// <summary>Range of the open space between one column (or row) of asteroids and the next, before scatter.</summary>
        public const float MinGap = 14f, MaxGap = 34f;
        /// <summary>Open space always left between two asteroids' grids (their rocky rims fit inside it).</summary>
        public const float MinClearance = 8f;
        /// <summary>How far each asteroid strays sideways on each horizontal axis, and up or down.</summary>
        const float Jitter = 12f;
        const float Lift = 5f;

        static readonly Dictionary<string, Dictionary<int, Vector3>> layouts = new Dictionary<string, Dictionary<int, Vector3>>();

        /// <summary>World position of the cell's (0, 0) tile corner, deck surface height in y.</summary>
        public static Vector3 Origin(MapDefinition map, MapCellDefinition def)
        {
            Dictionary<int, Vector3> layout;
            lock (layouts)
            {
                string id = map != null ? map.id ?? "" : "";
                if (!layouts.TryGetValue(id, out layout) || !layout.ContainsKey(def.id))
                {
                    layout = Build(map, id);
                    layouts[id] = layout;
                }
            }
            Vector3 o;
            return layout.TryGetValue(def.id, out o) ? o : Vector3.zero;
        }

        static Dictionary<int, Vector3> Build(MapDefinition map, string id)
        {
            var cells = map != null ? map.cells : new List<MapCellDefinition>();
            int seed = StableHash(id);
            var pos = new Vector3[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                var def = cells[i];
                float x = 0f, z = 0f;
                // Gaps differ row by row (and column by column), so columns and rows stagger rather than line up.
                for (int col = 0; col < def.mapX; col++) x += def.gridWidth + Mathf.Lerp(MinGap, MaxGap, Rand(seed, col, def.mapY, 1));
                for (int row = 0; row < def.mapY; row++) z += def.gridHeight + Mathf.Lerp(MinGap, MaxGap, Rand(seed, def.mapX, row, 2));
                x += (Rand(seed, def.mapX, def.mapY, 3) - 0.5f) * 2f * Jitter;
                z += (Rand(seed, def.mapX, def.mapY, 4) - 0.5f) * 2f * Jitter;
                float y = (Rand(seed, def.mapX, def.mapY, 5) - 0.5f) * 2f * Lift;
                pos[i] = new Vector3(x, y, z);
            }
            // Push apart any pair whose grids, padded by half the clearance each, overlap: along whichever horizontal
            // axis needs the smaller move, half each way. A few passes settle the whole field.
            float pad = MinClearance * 0.5f;
            for (int pass = 0; pass < 40; pass++)
            {
                bool moved = false;
                for (int i = 0; i < cells.Count; i++)
                    for (int j = i + 1; j < cells.Count; j++)
                    {
                        var a = cells[i];
                        var b = cells[j];
                        float overlapX = Mathf.Min(pos[i].x + a.gridWidth + pad, pos[j].x + b.gridWidth + pad) - Mathf.Max(pos[i].x - pad, pos[j].x - pad);
                        float overlapZ = Mathf.Min(pos[i].z + a.gridHeight + pad, pos[j].z + b.gridHeight + pad) - Mathf.Max(pos[i].z - pad, pos[j].z - pad);
                        if (overlapX <= 0f || overlapZ <= 0f) continue;
                        moved = true;
                        if (overlapX < overlapZ)
                        {
                            float dir = pos[i].x + a.gridWidth * 0.5f <= pos[j].x + b.gridWidth * 0.5f ? -1f : 1f;
                            pos[i].x += dir * (overlapX * 0.5f + 0.01f);
                            pos[j].x -= dir * (overlapX * 0.5f + 0.01f);
                        }
                        else
                        {
                            float dir = pos[i].z + a.gridHeight * 0.5f <= pos[j].z + b.gridHeight * 0.5f ? -1f : 1f;
                            pos[i].z += dir * (overlapZ * 0.5f + 0.01f);
                            pos[j].z -= dir * (overlapZ * 0.5f + 0.01f);
                        }
                    }
                if (!moved) break;
            }
            var layout = new Dictionary<int, Vector3>();
            for (int i = 0; i < cells.Count; i++) layout[cells[i].id] = pos[i];
            return layout;
        }

        public static Vector3 TileCenter(MapDefinition map, int cellId, float x, float y)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            return Origin(map, def) + new Vector3(x + 0.5f, 0f, y + 0.5f);
        }

        /// <summary>Straight-line distance a drone flies between two tiles.</summary>
        public static float Distance(MapDefinition map, int fromCell, int fromX, int fromY, int toCell, int toX, int toY)
        {
            return Vector3.Distance(TileCenter(map, fromCell, fromX, fromY), TileCenter(map, toCell, toX, toY));
        }

        /// <summary>A repeatable 0-1 value from integers only, so every platform lays a map out identically.</summary>
        static float Rand(int seed, int a, int b, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u;
                h ^= (uint)a * 0x85EBCA77u + 0x165667B1u;
                h = (h << 13 | h >> 19) * 0xC2B2AE3Du;
                h ^= (uint)b * 0x27D4EB2Fu + (uint)salt * 0x61C88647u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>FNV-1a over the map ID: stable across runs and platforms, unlike string.GetHashCode.</summary>
        static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                foreach (char ch in s) { h ^= ch; h *= 16777619u; }
                return (int)h;
            }
        }
    }
}
