using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Presentation helpers over <see cref="MapLayout"/>: each cell is its own asteroid, spread apart and scattered
    /// while keeping its neighbours. Finds the deck under a ray or a point.
    /// </summary>
    public static class BoardLayout
    {
        /// <summary>World position of the cell's (0, 0) tile corner, deck surface height in y (see <see cref="MapLayout"/>).</summary>
        public static Vector3 Origin(MapCellDefinition def)
        {
            return MapLayout.Origin(def);
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
