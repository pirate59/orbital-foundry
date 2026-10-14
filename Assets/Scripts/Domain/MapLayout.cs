using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Where each cell's asteroid sits in space. Cells keep their grid neighbours, but every column and row of the
    /// map has its own gap, each asteroid is jittered sideways and lifted or sunk, all from a seed taken from the map's
    /// ID, so each new map scatters differently while a saved game always rebuilds the same field. Neighbouring
    /// grids always keep at least <see cref="MinGap"/> minus twice the jitter of open space between them. The simulation
    /// uses it to time drone links between asteroids; the presentation uses it to place everything.
    /// </summary>
    public static class MapLayout
    {
        /// <summary>Range of the open space between one column (or row) of asteroids and the next.</summary>
        public const float MinGap = 16f, MaxGap = 30f;
        /// <summary>How far each asteroid strays sideways, and up or down, from its place in the lattice.</summary>
        const float Jitter = 4f;
        const float Lift = 5f;

        static readonly Dictionary<string, Vector3> origins = new Dictionary<string, Vector3>();

        /// <summary>World position of the cell's (0, 0) tile corner, deck surface height in y.</summary>
        public static Vector3 Origin(MapDefinition map, MapCellDefinition def)
        {
            string id = map != null ? map.id ?? "" : "";
            string key = id + ":" + def.mapX + ":" + def.mapY;
            Vector3 o;
            lock (origins)
            {
                if (origins.TryGetValue(key, out o)) return o;
                int seed = StableHash(id);
                float x = 0f, z = 0f;
                for (int c = 0; c < def.mapX; c++) x += def.gridWidth + Mathf.Lerp(MinGap, MaxGap, Rand(seed, c, -1, 1));
                for (int r = 0; r < def.mapY; r++) z += def.gridHeight + Mathf.Lerp(MinGap, MaxGap, Rand(seed, -1, r, 2));
                x += (Rand(seed, def.mapX, def.mapY, 3) - 0.5f) * 2f * Jitter;
                z += (Rand(seed, def.mapX, def.mapY, 4) - 0.5f) * 2f * Jitter;
                float y = (Rand(seed, def.mapX, def.mapY, 5) - 0.5f) * 2f * Lift;
                o = new Vector3(x, y, z);
                origins[key] = o;
            }
            return o;
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
