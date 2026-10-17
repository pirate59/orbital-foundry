using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// A craft is shown as a fleet: one ship for every <see cref="CraftDefinition.hpPerShip"/> HP it has left
    /// (rounded up), or a single ship when that is 0, flying in a wedge behind a leader. The world view and the
    /// battle scene both lay their ships out from here.
    /// </summary>
    public static class Fleet
    {
        /// <summary>Ships shown for a craft with this much HP left: none once it is destroyed.</summary>
        public static int Ships(CraftDefinition def, int health)
        {
            if (health <= 0) return 0;
            if (def == null || def.hpPerShip <= 0) return 1;
            return Mathf.Max(1, Mathf.CeilToInt(health / (float)def.hpPerShip));
        }

        /// <summary>The most ships drawn for one craft (0 for no limit): Low graphics draws a single ship.</summary>
        public static int MaxShown;

        /// <summary>Ships drawn for a craft: its fleet, held to <see cref="MaxShown"/>.</summary>
        public static int ShownShips(CraftDefinition def, int health)
        {
            int n = Ships(def, health);
            return MaxShown > 0 ? Mathf.Min(n, MaxShown) : n;
        }

        /// <summary>The formation's size as drawn: the full fleet, held to <see cref="MaxShown"/>.</summary>
        public static int ShownFullFleet(CraftDefinition def)
        {
            int n = FullFleet(def);
            return MaxShown > 0 ? Mathf.Min(n, MaxShown) : n;
        }

        /// <summary>Ships in a craft's fleet at full health.</summary>
        public static int FullFleet(CraftDefinition def)
        {
            return def != null ? Mathf.Max(1, Ships(def, def.maxHealth)) : 1;
        }

        /// <summary>A ship's length relative to a Mobile Guard ship.</summary>
        public static float Scale(CraftDefinition def)
        {
            return def != null && def.shipScale > 0f ? def.shipScale : 1f;
        }

        /// <summary>How much wider and taller a ship is drawn than its model's own proportions.</summary>
        public static float Girth(CraftDefinition def)
        {
            return def != null && def.shipGirth > 0f ? def.shipGirth : 1f;
        }

        /// <summary>
        /// Slots for <paramref name="count"/> ships in a wedge, in ship spacings: x across (0 on the centre line),
        /// y back from the leader at 0. Row r holds r + 1 ships; a part-filled last row is centred. Slot 0 is the
        /// leader, so a fleet losing ships loses them from the back.
        /// </summary>
        public static Vector2[] Wedge(int count)
        {
            var slots = new Vector2[Mathf.Max(0, count)];
            int placed = 0;
            for (int row = 0; placed < slots.Length; row++)
            {
                int inRow = Mathf.Min(row + 1, slots.Length - placed);
                for (int i = 0; i < inRow; i++)
                    slots[placed++] = new Vector2(i - (inRow - 1) * 0.5f, row);
            }
            return slots;
        }
    }
}
