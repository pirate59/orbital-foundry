using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Where each cell sits in the 3D world. The simulation's grid never changes; only presentation moves. Each cell
    /// is its own asteroid, spread apart with open space between neighbours and nudged sideways and up or down by a
    /// seeded offset, so the board reads as a scattered field while every cell keeps its neighbours.
    /// </summary>
    public static class BoardLayout
    {
        /// <summary>Open space between neighbouring asteroids before the scatter is added.</summary>
        public const float BeltGap = 10f;
        const float Scatter = 2.2f;
        const float Lift = 1.4f;

        static readonly Dictionary<long, Vector3> origins = new Dictionary<long, Vector3>();

        /// <summary>World position of the cell's (0, 0) tile corner, deck surface height in y.</summary>
        public static Vector3 Origin(MapCellDefinition def)
        {
            long key = ((long)def.mapX << 32) ^ (uint)def.mapY ^ ((long)def.gridWidth << 48);
            Vector3 o;
            if (origins.TryGetValue(key, out o)) return o;
            float dx = (Noise(def.mapX, def.mapY, 1) - 0.5f) * 2f * Scatter;
            float dz = (Noise(def.mapX, def.mapY, 2) - 0.5f) * 2f * Scatter;
            float dy = (Noise(def.mapX, def.mapY, 3) - 0.5f) * 2f * Lift;
            o = new Vector3(def.mapX * (def.gridWidth + BeltGap) + dx, dy, def.mapY * (def.gridHeight + BeltGap) + dz);
            origins[key] = o;
            return o;
        }

        static float Noise(int x, int y, int salt)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f + salt * 74.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>
        /// Height of the deck under a world position, counting each asteroid's rocky rim around its grid; false over
        /// open space.
        /// </summary>
        public static bool DeckHeightAt(MapDefinition map, float x, float z, out float y)
        {
            y = 0f;
            if (map == null) return false;
            const float rim = 2.5f;
            foreach (var def in map.cells)
            {
                var o = Origin(def);
                if (x < o.x - rim || z < o.z - rim || x > o.x + def.gridWidth + rim || z > o.z + def.gridHeight + rim) continue;
                y = o.y;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The cell and local tile the ray first meets on a deck surface, and the global grid tile the simulation
        /// uses for it. Misses (open space between asteroids) return false.
        /// </summary>
        public static bool Raycast(MapDefinition map, Ray ray, float maxDistance, out int gx, out int gy, out Vector3 hit)
        {
            gx = gy = 0;
            hit = Vector3.zero;
            if (map == null) return false;
            float best = float.MaxValue;
            bool found = false;
            foreach (var def in map.cells)
            {
                var o = Origin(def);
                var plane = new Plane(Vector3.up, o);
                float enter;
                if (!plane.Raycast(ray, out enter) || enter > maxDistance || enter >= best) continue;
                var p = ray.GetPoint(enter);
                float lx = p.x - o.x, lz = p.z - o.z;
                if (lx < 0 || lz < 0 || lx >= def.gridWidth || lz >= def.gridHeight) continue;
                best = enter;
                found = true;
                hit = p;
                gx = def.mapX * def.gridWidth + Mathf.FloorToInt(lx);
                gy = def.mapY * def.gridHeight + Mathf.FloorToInt(lz);
            }
            return found;
        }
    }
}
