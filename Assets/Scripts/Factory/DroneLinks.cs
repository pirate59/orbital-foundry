using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Drone Pads link asteroids. A pad sends to one destination tile on another of its owner's asteroids within drone
    /// range (measured flat, across the field); a receiving pad is placed there if none stands on it. The link is a
    /// belt segment between the two pad tiles that only drones fly: one trip at a time, carrying up to the owner's
    /// drone capacity, at a speed set by its tier and drone research. Range, speed and capacity grow with research.
    /// </summary>
    public static class DroneLinks
    {
        /// <summary>Ticks a drone waits at the pad for more items once one boards (only when it can carry more than one).</summary>
        public const int BoardingTicks = 10;

        public static bool IsPad(ContentCatalog c, BuildingState b)
        {
            if (b == null) return false;
            var def = CatalogRepository.GetBuilding(c, b.typeId);
            return def != null && def.kind == BuildingKind.DronePad;
        }

        public static float Range(ContentCatalog c, FactionState f)
        {
            return c.rules.droneBaseRange + ResearchService.Bonus(c, f, ResearchService.DroneRange);
        }

        public static int Capacity(ContentCatalog c, FactionState f)
        {
            return Mathf.Max(1, c.rules.droneBaseCapacity + ResearchService.Bonus(c, f, ResearchService.DroneCapacity));
        }

        public static int SpeedPercent(ContentCatalog c, FactionState f)
        {
            return Mathf.Max(0, ResearchService.Bonus(c, f, ResearchService.DroneSpeed));
        }

        /// <summary>Flat distance across the field from a pad to a tile, the measure drone range uses.</summary>
        public static float FlatDistance(MapDefinition map, BuildingState pad, int cellId, int x, int y)
        {
            var a = MapLayout.TileCenter(map, pad.cellId, pad.x, pad.y);
            var b = MapLayout.TileCenter(map, cellId, x, y);
            return new Vector2(a.x - b.x, a.z - b.z).magnitude;
        }

        /// <summary>The link a pad sends along, or null.</summary>
        public static BeltSegmentState Outgoing(WorldState w, BuildingState pad)
        {
            return pad == null ? null : w.belts.Find(s => s.fromCellId == pad.cellId && s.fromX == pad.x && s.fromY == pad.y && s.fromCellId != s.toCellId);
        }

        /// <summary>Why a pad cannot link to a tile (null when it can), and the pad standing there already, if any.</summary>
        public static CommandResult CheckLink(WorldState w, ContentCatalog c, MapDefinition map, FactionId actor, BuildingState pad,
            int cellId, int x, int y, out BuildingState targetPad)
        {
            targetPad = null;
            if (pad == null || !IsPad(c, pad)) return WorldLookup.Fail("NOT_A_PAD", "Select one of your Drone Pads.");
            if (pad.owner != actor) return WorldLookup.Fail("NOT_OWNER", "That pad is not yours.");
            if (pad.underConstruction || !WorldLookup.IsBuildingActive(w, pad)) return WorldLookup.Fail("PAD_INACTIVE", "That pad is not working.");
            var def = WorldLookup.GetCellDef(map, cellId);
            var cell = WorldLookup.GetCell(w, cellId);
            if (def == null || cell == null || !GridRules.IsTileInside(def, x, y)) return WorldLookup.Fail("OFF_GRID", "Pick a tile on another asteroid.");
            if (cellId == pad.cellId) return WorldLookup.Fail("SAME_ASTEROID", "Drones fly between asteroids: pick a tile on another one.");
            if (cell.owner != actor) return WorldLookup.Fail("NOT_OWNER", "You can only link to asteroids you hold.");
            var faction = WorldLookup.GetFaction(w, actor);
            float range = Range(c, faction);
            float distance = FlatDistance(map, pad, cellId, x, y);
            if (distance > range)
                return WorldLookup.Fail("OUT_OF_RANGE", "Out of drone range (" + distance.ToString("0") + " of " + range.ToString("0") + " units). Research Drone Logistics for more.");
            var there = WorldLookup.GetBuildingAt(w, cellId, x, y);
            if (there != null)
            {
                if (!IsPad(c, there) || there.owner != actor) return WorldLookup.Fail("OCCUPIED", "Link to an empty tile or one of your Drone Pads.");
                targetPad = there;
                return null;
            }
            var padDef = CatalogRepository.GetBuilding(c, pad.typeId);
            var place = GridRules.CanPlaceBuilding(w, c, map, padDef, actor, cellId, x, y);
            if (!place.success) return place;
            if (faction.currency < padDef.cost) return WorldLookup.Fail("NO_MONEY", "A receiving pad costs " + padDef.cost + ".");
            return null;
        }

        /// <summary>Links a pad to a tile (placing a receiving pad if needed), replacing its old link; or unlinks it.</summary>
        public static CommandResult Link(WorldState w, ContentCatalog c, MapDefinition map, BeltNetwork network, EconomyService economy, GameCommand cmd)
        {
            var pad = WorldLookup.GetBuilding(w, cmd.buildingId);
            if (cmd.contentId == "unlink")
            {
                if (pad == null || !IsPad(c, pad) || pad.owner != cmd.actor) return WorldLookup.Fail("NOT_A_PAD", "Select one of your Drone Pads.");
                var old = Outgoing(w, pad);
                if (old == null) return WorldLookup.Fail("NO_LINK", "This pad has no link.");
                OwnershipService.RemoveStructures(w, new System.Collections.Generic.HashSet<int>(), new System.Collections.Generic.HashSet<int> { old.id });
                network.Rebuild(w);
                return WorldLookup.Ok("Link removed.");
            }
            BuildingState target;
            var refusal = CheckLink(w, c, map, cmd.actor, pad, cmd.toCellId, cmd.toX, cmd.toY, out target);
            if (refusal != null) return refusal;
            if (target == null)
            {
                var padDef = CatalogRepository.GetBuilding(c, pad.typeId);
                economy.Spend(WorldLookup.GetFaction(w, cmd.actor), padDef.cost);
                target = new BuildingState { id = WorldLookup.AllocateId(w), owner = cmd.actor, typeId = pad.typeId, cellId = cmd.toCellId, x = cmd.toX, y = cmd.toY };
                w.buildings.Add(target);
            }
            var existing = Outgoing(w, pad);
            if (existing != null)
            {
                if (existing.toCellId == target.cellId && existing.toX == target.x && existing.toY == target.y) return WorldLookup.Fail("ALREADY_LINKED", "Already linked there.");
                OwnershipService.RemoveStructures(w, new System.Collections.Generic.HashSet<int>(), new System.Collections.Generic.HashSet<int> { existing.id });
            }
            w.belts.Add(new BeltSegmentState
            {
                id = WorldLookup.AllocateId(w),
                owner = cmd.actor,
                fromCellId = pad.cellId, fromX = pad.x, fromY = pad.y,
                toCellId = target.cellId, toX = target.x, toY = target.y
            });
            network.Rebuild(w);
            return WorldLookup.Ok("Drone link set: cell " + pad.cellId + " to cell " + target.cellId + ".");
        }
    }
}
