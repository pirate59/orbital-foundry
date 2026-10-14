using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Where each cell's asteroid sits in space. Cells keep their grid neighbours but are spread apart by
    /// <see cref="Gap"/> and nudged sideways and up or down by an offset seeded from the cell's grid position, so the
    /// same map always lays out the same way. The simulation uses it to time drone links between asteroids; the
    /// presentation uses it to place everything.
    /// </summary>
    public static class MapLayout
    {
        /// <summary>Open space between neighbouring asteroids before the scatter is added.</summary>
        public const float Gap = 22f;
        const float Scatter = 3f;
        const float Lift = 1.6f;

        static readonly Dictionary<long, Vector3> origins = new Dictionary<long, Vector3>();

        /// <summary>World position of the cell's (0, 0) tile corner, deck surface height in y.</summary>
        public static Vector3 Origin(MapCellDefinition def)
        {
            long key = ((long)def.mapX << 32) ^ (uint)def.mapY ^ ((long)def.gridWidth << 48);
            Vector3 o;
            lock (origins)
            {
                if (origins.TryGetValue(key, out o)) return o;
                float dx = (Noise(def.mapX, def.mapY, 1) - 0.5f) * 2f * Scatter;
                float dz = (Noise(def.mapX, def.mapY, 2) - 0.5f) * 2f * Scatter;
                float dy = (Noise(def.mapX, def.mapY, 3) - 0.5f) * 2f * Lift;
                o = new Vector3(def.mapX * (def.gridWidth + Gap) + dx, dy, def.mapY * (def.gridHeight + Gap) + dz);
                origins[key] = o;
            }
            return o;
        }

        public static Vector3 TileCenter(MapDefinition map, int cellId, float x, float y)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            return Origin(def) + new Vector3(x + 0.5f, 0f, y + 0.5f);
        }

        /// <summary>Straight-line distance a drone flies between two tiles.</summary>
        public static float Distance(MapDefinition map, int fromCell, int fromX, int fromY, int toCell, int toX, int toY)
        {
            return Vector3.Distance(TileCenter(map, fromCell, fromX, fromY), TileCenter(map, toCell, toX, toY));
        }

        static float Noise(int x, int y, int salt)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f + salt * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }
    }
}
