using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Draws WorldState with flat ground sprites and camera-facing billboards. Holds no authoritative state: every
    /// visual is diffed by entity ID from the world and reused between refreshes.
    /// </summary>
    public sealed class WorldView
    {
        const float BillboardHeight = 0.45f;
        const float AirborneHeight = 1.4f;
        const float TokenHeight = 0.22f;

        readonly SpaceSpriteLibrary sprites;
        readonly Transform root;
        Camera camera;
        MapDefinition map;

        readonly Dictionary<int, SpriteRenderer> cellFloors = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> cellGrids = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> controlPoints = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> hqStars = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> overlays = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> nodes = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, BuildingVisual> buildings = new Dictionary<int, BuildingVisual>();
        readonly Dictionary<int, SpriteRenderer> belts = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> tokens = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, CraftVisual> crafts = new Dictionary<int, CraftVisual>();
        readonly Dictionary<int, SpriteRenderer> holds = new Dictionary<int, SpriteRenderer>();
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        SpriteRenderer hoverTile;
        SpriteRenderer selectionRing;

        sealed class BuildingVisual
        {
            public SpriteRenderer Body, Alert;
        }

        sealed class CraftVisual
        {
            public SpriteRenderer Body, Shadow, DefenderRing, HpBack, HpFill;
            public Vector3 Target;
        }

        public WorldView(SpaceSpriteLibrary sprites, Transform root)
        {
            this.sprites = sprites;
            this.root = root;
        }

        public static Vector3 TileCenter(MapDefinition map, int cellId, float x, float y)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            return new Vector3(def.mapX * def.gridWidth + x + 0.5f, 0f, def.mapY * def.gridHeight + y + 0.5f);
        }

        public static Vector3 CellCenter(MapDefinition map, int cellId)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            return new Vector3(def.mapX * def.gridWidth + def.gridWidth * 0.5f, 0f, def.mapY * def.gridHeight + def.gridHeight * 0.5f);
        }

        public static Bounds MapBounds(MapDefinition map)
        {
            var b = new Bounds(CellCenter(map, map.cells[0].id), Vector3.zero);
            foreach (var c in map.cells)
            {
                b.Encapsulate(new Vector3(c.mapX * c.gridWidth, 0, c.mapY * c.gridHeight));
                b.Encapsulate(new Vector3((c.mapX + 1) * c.gridWidth, 0, (c.mapY + 1) * c.gridHeight));
            }
            return b;
        }

        // ---------------------------------------------------------------- creation helpers

        SpriteRenderer Make(string name, Sprite sprite, int order, bool flat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (flat) go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            return sr;
        }

        static void Destroy(Component c)
        {
            if (c != null) Object.Destroy(c.gameObject);
        }

        public void BuildMap(MapDefinition map, WorldState w, Camera camera)
        {
            Clear();
            this.map = map;
            this.camera = camera;
            foreach (var def in map.cells)
            {
                var center = CellCenter(map, def.id);
                var floor = Make("Cell " + def.id, sprites.GetCellSprite(FactionId.Neutral), -100, true);
                floor.transform.position = center + Vector3.down * 0.02f;
                floor.transform.localScale = new Vector3(def.gridWidth, def.gridHeight, 1f);
                cellFloors[def.id] = floor;

                var grid = Make("Grid " + def.id, sprites.GetGridSprite(), -95, true);
                grid.transform.position = center + Vector3.down * 0.01f;
                grid.transform.localScale = new Vector3(def.gridWidth, def.gridHeight, 1f);
                cellGrids[def.id] = grid;

                var overlay = Make("Overlay " + def.id, sprites.GetSquareSprite(), -94, true);
                overlay.transform.position = center;
                overlay.transform.localScale = new Vector3(def.gridWidth - 0.1f, def.gridHeight - 0.1f, 1f);
                overlay.enabled = false;
                overlays[def.id] = overlay;

                var cp = Make("Control " + def.id, sprites.GetControlPointSprite(), -60, true);
                cp.transform.position = center + Vector3.up * 0.01f;
                cp.transform.localScale = Vector3.one * 1.1f;
                controlPoints[def.id] = cp;

                if (def.headquartersFor != FactionId.Neutral)
                {
                    var star = Make("HQ " + def.id, sprites.GetStarSprite(), 5, false);
                    star.transform.position = center + Vector3.up * 1.2f;
                    star.transform.localScale = Vector3.one * 0.7f;
                    hqStars[def.id] = star;
                }

                foreach (var t in def.blockedTiles)
                {
                    var rock = Make("Rock", sprites.GetBlockedSprite(), -80, true);
                    rock.transform.position = TileCenter(map, def.id, t.x, t.y) + Vector3.up * 0.005f;
                    rock.color = new Color(0.35f, 0.32f, 0.4f);
                }
            }
            hoverTile = Make("Hover", sprites.GetSquareSprite(), -40, true);
            hoverTile.transform.localScale = Vector3.one * 0.92f;
            hoverTile.enabled = false;
            selectionRing = Make("Selection", sprites.GetRingSprite(), -39, true);
            selectionRing.enabled = false;
        }

        public void Clear()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
            cellFloors.Clear(); cellGrids.Clear(); controlPoints.Clear(); hqStars.Clear(); overlays.Clear();
            nodes.Clear(); buildings.Clear(); belts.Clear(); tokens.Clear(); crafts.Clear(); holds.Clear(); pool.Clear();
        }

        // ---------------------------------------------------------------- refresh

        public void Refresh(WorldState w, ContentCatalog c, int revision, PullRouter router, UiState ui, float tickFraction)
        {
            if (map == null) return;
            RefreshCells(w);
            RefreshNodes(w, c);
            RefreshBuildings(w, c, router);
            RefreshBelts(w);
            RefreshTokens(w, c, tickFraction);
            RefreshCrafts(w, c, tickFraction);
            RefreshHolds(w);
            RefreshSelection(w, ui);
            FaceCamera();
        }

        void RefreshCells(WorldState w)
        {
            foreach (var cell in w.cells)
            {
                SpriteRenderer floor;
                if (!cellFloors.TryGetValue(cell.id, out floor)) continue;
                var col = SpaceSpriteLibrary.FactionColor(cell.owner);
                floor.color = new Color(col.r * 0.22f + 0.04f, col.g * 0.22f + 0.04f, col.b * 0.22f + 0.07f, 1f);
                cellGrids[cell.id].color = new Color(col.r, col.g, col.b, 0.55f);
                bool contested = w.captureHolds.Exists(h => h.cellId == cell.id);
                controlPoints[cell.id].color = contested ? Color.Lerp(col, Color.white, Mathf.PingPong(Time.time * 2f, 1f)) : new Color(col.r, col.g, col.b, 0.8f);
                SpriteRenderer star;
                if (hqStars.TryGetValue(cell.id, out star))
                {
                    var hqCol = SpaceSpriteLibrary.FactionColor(cell.headquartersFor);
                    star.color = cell.owner == cell.headquartersFor ? hqCol : new Color(0.5f, 0.5f, 0.5f, 0.6f);
                }
            }
        }

        void RefreshNodes(WorldState w, ContentCatalog c)
        {
            foreach (var n in w.nodes)
            {
                SpriteRenderer sr;
                if (!nodes.TryGetValue(n.id, out sr))
                {
                    sr = Make("Node " + n.id, sprites.GetNodeSprite(n.itemId), -70, true);
                    sr.transform.position = TileCenter(map, n.cellId, n.x, n.y) + Vector3.up * 0.01f;
                    sr.transform.localScale = Vector3.one * 0.95f;
                    var item = CatalogRepository.GetItem(c, n.itemId);
                    sr.color = SpaceSpriteLibrary.Hex(item != null ? item.colorHex : "", Color.magenta);
                    nodes[n.id] = sr;
                }
            }
        }

        void RefreshBuildings(WorldState w, ContentCatalog c, PullRouter router)
        {
            var seen = new HashSet<int>();
            foreach (var b in w.buildings)
            {
                seen.Add(b.id);
                BuildingVisual v;
                if (!buildings.TryGetValue(b.id, out v))
                {
                    v = new BuildingVisual
                    {
                        Body = Make("Building " + b.id, sprites.GetBuildingSprite(b.typeId), 0, false),
                        Alert = Make("Alert " + b.id, sprites.GetAlertSprite(), 2, false)
                    };
                    v.Body.transform.position = TileCenter(map, b.cellId, b.x, b.y) + Vector3.up * BillboardHeight;
                    v.Body.transform.localScale = Vector3.one * 0.95f;
                    v.Alert.transform.position = v.Body.transform.position + Vector3.up * 0.65f;
                    v.Alert.transform.localScale = Vector3.one * 0.4f;
                    buildings[b.id] = v;
                }
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                var baseCol = SpaceSpriteLibrary.Hex(def != null ? def.colorHex : "", Color.magenta);
                var faction = SpaceSpriteLibrary.FactionColor(b.owner);
                var col = Color.Lerp(baseCol, faction, 0.25f);
                bool active = WorldLookup.IsBuildingActive(w, b);
                v.Body.color = active ? col : new Color(col.r * 0.35f, col.g * 0.35f, col.b * 0.35f, 0.7f);
                bool blocked = active && b.owner == FactionId.Player && router.BlockedReasonByBuilding.ContainsKey(b.id);
                v.Alert.enabled = blocked || (!active && b.owner == FactionId.Player);
                v.Alert.color = active ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f);
            }
            RemoveMissing(buildings, seen, v => { Destroy(v.Body); Destroy(v.Alert); });
        }

        void RefreshBelts(WorldState w)
        {
            var seen = new HashSet<int>();
            foreach (var s in w.belts)
            {
                seen.Add(s.id);
                SpriteRenderer sr;
                if (!belts.TryGetValue(s.id, out sr))
                {
                    sr = Make("Belt " + s.id, sprites.GetBeltSprite(), -50, true);
                    var a = TileCenter(map, s.fromCellId, s.fromX, s.fromY);
                    var b = TileCenter(map, s.toCellId, s.toX, s.toY);
                    sr.transform.position = (a + b) * 0.5f + Vector3.up * 0.02f;
                    var dir = b - a;
                    float yaw = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
                    sr.transform.rotation = Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, 0f, yaw);
                    sr.transform.localScale = new Vector3(0.9f, 0.6f, 1f);
                    belts[s.id] = sr;
                }
                var col = SpaceSpriteLibrary.FactionColor(s.owner);
                bool active = WorldLookup.IsBeltActive(w, s);
                sr.color = active ? new Color(col.r * 0.75f + 0.2f, col.g * 0.75f + 0.2f, col.b * 0.75f + 0.2f, 0.95f) : new Color(0.4f, 0.4f, 0.4f, 0.45f);
            }
            RemoveMissing(belts, seen, Destroy);
        }

        void RefreshTokens(WorldState w, ContentCatalog c, float tickFraction)
        {
            var seen = new HashSet<int>();
            int travel = c.rules.beltTravelTicksPerSegment;
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                var seg = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                if (seg == null) continue;
                seen.Add(t.id);
                SpriteRenderer sr;
                if (!tokens.TryGetValue(t.id, out sr))
                {
                    sr = Make("Token " + t.id, sprites.GetTokenSprite(t.itemId), 1, false);
                    sr.transform.localScale = Vector3.one * 0.42f;
                    var item = CatalogRepository.GetItem(c, t.itemId);
                    sr.color = SpaceSpriteLibrary.Hex(item != null ? item.colorHex : "", Color.magenta);
                    tokens[t.id] = sr;
                }
                float remaining = t.ticksUntilNextSegment;
                if (t.status == TokenStatus.Moving && remaining > 0) remaining = Mathf.Max(0f, remaining - tickFraction);
                float p = t.status == TokenStatus.Moving ? 1f - remaining / travel : (t.ticksUntilNextSegment <= 0 ? 1f : 1f - (float)t.ticksUntilNextSegment / travel);
                var a = TileCenter(map, seg.fromCellId, seg.fromX, seg.fromY);
                var b = TileCenter(map, seg.toCellId, seg.toX, seg.toY);
                // Stop short of the vertex so waiting items do not overlap the next machine.
                sr.transform.position = Vector3.Lerp(a, b, Mathf.Clamp01(p) * 0.85f + 0.075f) + Vector3.up * TokenHeight;
                var baseCol = sr.color;
                baseCol.a = t.status == TokenStatus.Frozen ? 0.45f : 1f;
                sr.color = baseCol;
            }
            RemoveMissing(tokens, seen, Destroy);
        }

        void RefreshCrafts(WorldState w, ContentCatalog c, float tickFraction)
        {
            var seen = new HashSet<int>();
            var slotByCell = new Dictionary<int, int>();
            craftSpots.Clear();
            var ordered = new List<CraftState>(w.crafts);
            ordered.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var craft in ordered)
            {
                if (craft.withdrawn || craft.currentHealth <= 0) continue;
                seen.Add(craft.id);
                CraftVisual v;
                var def = CatalogRepository.GetCraft(c, craft.typeId);
                if (!crafts.TryGetValue(craft.id, out v))
                {
                    v = new CraftVisual
                    {
                        Body = Make("Craft " + craft.id, sprites.GetCraftSprite(craft.typeId), 3, false),
                        Shadow = Make("Shadow " + craft.id, sprites.GetShadowSprite(), -30, true),
                        DefenderRing = Make("DefRing " + craft.id, sprites.GetRingSprite(), -31, true),
                        HpBack = Make("HpBack " + craft.id, sprites.GetSquareSprite(), 4, false),
                        HpFill = Make("HpFill " + craft.id, sprites.GetSquareSprite(), 5, false)
                    };
                    v.Body.transform.localScale = Vector3.one * 0.9f;
                    v.HpBack.color = new Color(0, 0, 0, 0.7f);
                    crafts[craft.id] = v;
                }
                var factionCol = SpaceSpriteLibrary.FactionColor(craft.owner);
                var typeCol = SpaceSpriteLibrary.Hex(def != null ? def.colorHex : "", Color.magenta);
                v.Body.color = Color.Lerp(typeCol, factionCol, 0.45f);
                v.DefenderRing.color = factionCol;
                v.DefenderRing.enabled = WorldLookup.IsDefender(w, craft);

                int slot;
                slotByCell.TryGetValue(craft.cellId, out slot);
                slotByCell[craft.cellId] = slot + 1;
                Vector3 pos = CraftGround(w, craft.cellId, slot);
                if (craft.worldPathCellIds.Count > 0 && craft.worldPathIndex < craft.worldPathCellIds.Count)
                {
                    float total = Mathf.Max(1, c.rules.worldMoveTicksPerCell);
                    float t = 1f - Mathf.Max(0f, craft.worldTicksUntilNextCell - (w.mode == MatchMode.Factory ? tickFraction : 0f)) / total;
                    pos = Vector3.Lerp(CellCenter(map, craft.cellId), CellCenter(map, craft.worldPathCellIds[craft.worldPathIndex]), Mathf.Clamp01(t)) + new Vector3(0.6f, 0, -0.6f);
                }
                float height = def != null && def.airborne ? AirborneHeight + Mathf.Sin(Time.time * 2f + craft.id) * 0.08f : BillboardHeight;
                v.Body.transform.position = pos + Vector3.up * height;
                v.Shadow.transform.position = pos + Vector3.up * 0.015f;
                v.Shadow.transform.localScale = def != null && def.airborne ? Vector3.one * 0.7f : Vector3.one * 0.9f;
                v.DefenderRing.transform.position = pos + Vector3.up * 0.018f;
                v.DefenderRing.transform.localScale = Vector3.one * 1.1f;

                float hp = def != null ? Mathf.Clamp01((float)craft.currentHealth / def.maxHealth) : 1f;
                var barCenter = v.Body.transform.position + Vector3.up * 0.6f;
                v.HpBack.transform.position = barCenter;
                v.HpBack.transform.localScale = new Vector3(0.8f, 0.1f, 1f);
                v.HpFill.transform.localScale = new Vector3(0.78f * hp, 0.07f, 1f);
                v.HpFill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 1f, 0.45f), hp);
                v.Target = barCenter;
                v.HpFill.transform.position = barCenter;
            }
            RemoveMissing(crafts, seen, v => { Destroy(v.Body); Destroy(v.Shadow); Destroy(v.DefenderRing); Destroy(v.HpBack); Destroy(v.HpFill); });
        }

        readonly Dictionary<int, List<Vector3>> craftSpots = new Dictionary<int, List<Vector3>>();

        /// <summary>
        /// Craft stand on the free tiles nearest the control point, so they never hide resource nodes or machines
        /// (tiles under belts are used only when nothing else is free).
        /// </summary>
        Vector3 CraftGround(WorldState w, int cellId, int slot)
        {
            List<Vector3> spots;
            if (!craftSpots.TryGetValue(cellId, out spots))
            {
                var def = WorldLookup.GetCellDef(map, cellId);
                var occupied = new HashSet<int>();
                var belted = new HashSet<int>();
                foreach (var n in w.nodes) if (n.cellId == cellId) occupied.Add(n.y * 8 + n.x);
                foreach (var b in w.buildings) if (b.cellId == cellId) occupied.Add(b.y * 8 + b.x);
                foreach (var t in def.blockedTiles) occupied.Add(t.y * 8 + t.x);
                foreach (var s in w.belts)
                {
                    if (s.fromCellId == cellId) belted.Add(s.fromY * 8 + s.fromX);
                    if (s.toCellId == cellId) belted.Add(s.toY * 8 + s.toX);
                }
                var candidates = new List<KeyValuePair<float, Vector3>>();
                float cx = def.gridWidth * 0.5f - 0.5f, cy = def.gridHeight * 0.5f - 0.5f;
                for (int y = 0; y < def.gridHeight; y++)
                    for (int x = 0; x < def.gridWidth; x++)
                    {
                        int key = y * 8 + x;
                        if (occupied.Contains(key)) continue;
                        float score = Mathf.Abs(x - cx) + Mathf.Abs(y - cy) + (belted.Contains(key) ? 20f : 0f) + x * 0.001f + y * 0.0001f;
                        candidates.Add(new KeyValuePair<float, Vector3>(score, TileCenter(map, cellId, x, y)));
                    }
                candidates.Sort((a, b) => a.Key.CompareTo(b.Key));
                spots = new List<Vector3>();
                foreach (var kv in candidates) spots.Add(kv.Value);
                if (spots.Count == 0) spots.Add(CellCenter(map, cellId));
                craftSpots[cellId] = spots;
            }
            return spots[slot % spots.Count];
        }

        void RefreshHolds(WorldState w)
        {
            var seen = new HashSet<int>();
            foreach (var h in w.captureHolds)
            {
                seen.Add(h.cellId * 4 + (int)h.faction);
                SpriteRenderer sr;
                int key = h.cellId * 4 + (int)h.faction;
                if (!holds.TryGetValue(key, out sr))
                {
                    sr = Make("Hold " + h.cellId, sprites.GetRingSprite(), -55, true);
                    holds[key] = sr;
                }
                sr.transform.position = CellCenter(map, h.cellId) + Vector3.up * 0.03f;
                float pulse = 1.4f + Mathf.PingPong(Time.time, 0.5f);
                sr.transform.localScale = Vector3.one * pulse;
                sr.color = SpaceSpriteLibrary.FactionColor(h.faction);
            }
            RemoveMissing(holds, seen, Destroy);
        }

        void RefreshSelection(WorldState w, UiState ui)
        {
            if (ui == null) return;
            var craft = WorldLookup.GetCraft(w, ui.selectedCraftId);
            var building = WorldLookup.GetBuilding(w, ui.selectedBuildingId);
            if (craft != null && crafts.ContainsKey(craft.id))
            {
                selectionRing.enabled = true;
                var v = crafts[craft.id];
                selectionRing.transform.position = v.Shadow.transform.position + Vector3.up * 0.01f;
                selectionRing.transform.localScale = Vector3.one * 1.4f;
                selectionRing.color = Color.white;
            }
            else if (building != null)
            {
                selectionRing.enabled = true;
                selectionRing.transform.position = TileCenter(map, building.cellId, building.x, building.y) + Vector3.up * 0.03f;
                selectionRing.transform.localScale = Vector3.one * 1.2f;
                selectionRing.color = Color.white;
            }
            else selectionRing.enabled = false;
        }

        void FaceCamera()
        {
            if (camera == null) return;
            var rot = camera.transform.rotation;
            foreach (var v in buildings.Values) { v.Body.transform.rotation = rot; v.Alert.transform.rotation = rot; }
            foreach (var t in tokens.Values) t.transform.rotation = rot;
            foreach (var s in hqStars.Values) s.transform.rotation = rot;
            foreach (var v in crafts.Values)
            {
                v.Body.transform.rotation = rot;
                v.HpBack.transform.rotation = rot;
                v.HpFill.transform.rotation = rot;
            }
        }

        static void RemoveMissing<T>(Dictionary<int, T> table, HashSet<int> seen, System.Action<T> destroy)
        {
            List<int> stale = null;
            foreach (var id in table.Keys) if (!seen.Contains(id)) (stale ?? (stale = new List<int>())).Add(id);
            if (stale == null) return;
            foreach (var id in stale) { destroy(table[id]); table.Remove(id); }
        }

        // ---------------------------------------------------------------- overlays and picking

        /// <summary>Highlights battle cells and legal targets for the selected craft.</summary>
        public void SetTacticalFocus(EncounterState e, ICollection<int> moveTargets, ICollection<int> attackTargets)
        {
            foreach (var kv in overlays)
            {
                var sr = kv.Value;
                int id = kv.Key;
                if (!e.active) { sr.enabled = false; continue; }
                if (attackTargets != null && attackTargets.Contains(id)) { sr.enabled = true; sr.color = new Color(1f, 0.3f, 0.25f, 0.28f); }
                else if (moveTargets != null && moveTargets.Contains(id)) { sr.enabled = true; sr.color = new Color(0.3f, 0.9f, 1f, 0.22f); }
                else if (id == e.targetCellId) { sr.enabled = true; sr.color = new Color(1f, 0.85f, 0.2f, 0.16f); }
                else if (!e.battleCellIds.Contains(id)) { sr.enabled = true; sr.color = new Color(0f, 0f, 0f, 0.55f); }
                else sr.enabled = false;
            }
        }

        public void SetHover(bool visible, int cellId, int x, int y, Color color)
        {
            if (hoverTile == null) return;
            hoverTile.enabled = visible && map != null && WorldLookup.GetCellDef(map, cellId) != null;
            if (!hoverTile.enabled) return;
            hoverTile.transform.position = TileCenter(map, cellId, x, y) + Vector3.up * 0.04f;
            hoverTile.color = color;
        }

        /// <summary>Returns the craft whose sprite is nearest the screen point within <paramref name="maxPixels"/>, or -1.</summary>
        public int PickCraft(Vector2 screenPoint, float maxPixels, WorldState w)
        {
            if (camera == null) return -1;
            int best = -1;
            float bestDist = maxPixels;
            foreach (var kv in crafts)
            {
                var craft = WorldLookup.GetCraft(w, kv.Key);
                if (craft == null || craft.withdrawn) continue;
                Vector3 sp = camera.WorldToScreenPoint(kv.Value.Body.transform.position);
                float d = Vector2.Distance(screenPoint, new Vector2(sp.x, sp.y));
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }
    }
}
