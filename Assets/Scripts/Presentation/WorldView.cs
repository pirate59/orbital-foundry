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

        readonly SpaceSpriteLibrary sprites;
        readonly Transform root;
        Camera camera;
        MapDefinition map;

        readonly Dictionary<int, SpriteRenderer> cellFloors = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, BoardArt.Deck> decks = new Dictionary<int, BoardArt.Deck>();
        readonly Dictionary<int, FactionId> deckOwners = new Dictionary<int, FactionId>();
        readonly Dictionary<int, SpriteRenderer> cellGrids = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> controlPoints = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> hqStars = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> overlays = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, SpriteRenderer> nodes = new Dictionary<int, SpriteRenderer>();
        readonly Dictionary<int, float> nodeScales = new Dictionary<int, float>();
        readonly Dictionary<int, Model> nodeModels = new Dictionary<int, Model>();
        readonly Dictionary<int, BuildingVisual> buildings = new Dictionary<int, BuildingVisual>();
        readonly Dictionary<int, BeltVisual> belts = new Dictionary<int, BeltVisual>();
        readonly Dictionary<int, MeshRenderer> beltHubs = new Dictionary<int, MeshRenderer>();
        readonly Dictionary<int, TokenVisual> tokens = new Dictionary<int, TokenVisual>();

        sealed class TokenVisual
        {
            /// <summary>The 3D good riding the belt, and a small icon above it naming the item.</summary>
            public GameObject Crate;
            public SpriteRenderer Cargo;
            public Material[] Normal;
            public bool Frozen;
        }
        readonly Dictionary<int, CraftVisual> crafts = new Dictionary<int, CraftVisual>();
        readonly Dictionary<int, SpriteRenderer> holds = new Dictionary<int, SpriteRenderer>();
        readonly List<SpriteRenderer> pool = new List<SpriteRenderer>();
        SpriteRenderer hoverTile;
        SelectionRing selectionRing;

        sealed class BuildingVisual
        {
            public SpriteRenderer Body, Shadow;
            public Marker Alert;
            /// <summary>The 3D model, when the type has one; the Body sprite then stays hidden as an anchor for icons.</summary>
            public Model Model;
            public FactionId Owner;
            public Vector3 Ground;
            public float Size, Phase;
            float modelHeight = -1f;
            public float ModelHeight => modelHeight >= 0f ? modelHeight : (modelHeight = Model != null ? Model.Height : 0f);
            public string NeedSignature = "";
            public readonly List<NeedIcon> Needs = new List<NeedIcon>();
        }

        /// <summary>One needed item floating above a machine: item-coloured icon, status ring, and have/need pips.</summary>
        sealed class NeedIcon
        {
            /// <summary>A status ring around a spinning model of the needed good, with pips underneath.</summary>
            public Marker Ring;
            public GameObject Icon;
            public Marker[] Pips;
        }

        /// <summary>Player setting: draw ingredient icons above machines.</summary>
        public bool ShowNeedIcons = true;

        const float NeedRowHeight = 1.55f;
        const float NeedSpacing = 0.42f;

        sealed class BeltVisual
        {
            /// <summary>The conveyor tile, and the arrows riding it.</summary>
            public MeshRenderer Conveyor;
            public MeshRenderer[] Arrows;
            /// <summary>A belt between two asteroids is a drone link instead of a lane.</summary>
            public Model Link, Drone;
            public Vector3 From, To;
            public bool Moving;
        }

        const int ChevronsPerBelt = 2;

        sealed class CraftVisual
        {
            public SpriteRenderer Body, Shadow, DefenderRing;
            public Marker HpBack, HpFill;
            public Model Model;
            public Vector3 LastPos;
            public float Heading;
            public Vector3 Target;
        }

        public WorldView(SpaceSpriteLibrary sprites, Transform root)
        {
            this.sprites = sprites;
            this.root = root;
        }

        public static Vector3 TileCenter(MapDefinition map, int cellId, float x, float y)
        {
            return MapLayout.TileCenter(map, cellId, x, y);
        }

        public static Vector3 CellCenter(MapDefinition map, int cellId)
        {
            var def = WorldLookup.GetCellDef(map, cellId);
            return BoardLayout.Origin(map, def) + new Vector3(def.gridWidth * 0.5f, 0f, def.gridHeight * 0.5f);
        }

        public static Bounds MapBounds(MapDefinition map)
        {
            var b = new Bounds(CellCenter(map, map.cells[0].id), Vector3.zero);
            foreach (var c in map.cells)
            {
                var o = BoardLayout.Origin(map, c);
                b.Encapsulate(o);
                b.Encapsulate(o + new Vector3(c.gridWidth, 0, c.gridHeight));
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
                decks[def.id] = BoardArt.Build(root, center, def.gridWidth, def.gridHeight, def.id);
                var floor = Make("Cell " + def.id, sprites.GetCellSprite(FactionId.Neutral), -100, true);
                floor.transform.position = center + Vector3.down * 0.02f;
                floor.transform.localScale = new Vector3(def.gridWidth, def.gridHeight, 1f);
                cellFloors[def.id] = floor;

                var grid = Make("Grid " + def.id, sprites.GetGridSprite(def.gridWidth, def.gridHeight), -95, true);
                grid.transform.position = center + Vector3.down * 0.01f;
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

                if (def.headquartersFor != FactionId.Neutral && !Palette.Ready)
                {
                    var star = Make("HQ " + def.id, sprites.GetStarSprite(), 5, false);
                    star.transform.position = center + Vector3.up * 2.1f;
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
            selectionRing = new SelectionRing(root);
        }

        public void Clear()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Object.Destroy(root.GetChild(i).gameObject);
            cellFloors.Clear(); decks.Clear(); deckOwners.Clear(); cellGrids.Clear(); controlPoints.Clear(); hqStars.Clear(); overlays.Clear();
            nodes.Clear(); nodeScales.Clear(); nodeModels.Clear(); buildings.Clear(); belts.Clear(); beltHubs.Clear(); tokens.Clear(); crafts.Clear(); holds.Clear(); pool.Clear();
        }

        // ---------------------------------------------------------------- refresh

        /// <summary>How visible buildings (and the asteroids under extractors) stay in Belt View.</summary>
        const float BeltViewAlpha = 0.18f;
        bool beltView;

        public void Refresh(WorldState w, ContentCatalog c, int revision, PullRouter router, UiState ui, float tickFraction)
        {
            if (map == null) return;
            beltView = ui != null && ui.beltView;
            RefreshCells(w);
            RefreshNodes(w, c);
            AnimateAsteroids(w);
            RefreshBuildings(w, c, router);
            RefreshBelts(w, ui);
            AnimateBelts(c);
            RefreshTokens(w, c, tickFraction);
            AnimateDrones(w, c, tickFraction);
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
                // A faint wash of the owner's colour over the deck plate.
                floor.color = new Color(col.r, col.g, col.b, cell.owner == FactionId.Neutral ? 0f : 0.1f);
                FactionId shownOwner;
                BoardArt.Deck deck;
                if (decks.TryGetValue(cell.id, out deck) && (!deckOwners.TryGetValue(cell.id, out shownOwner) || shownOwner != cell.owner))
                {
                    deckOwners[cell.id] = cell.owner;
                    deck.Trim.sharedMaterial = cell.owner == FactionId.Neutral
                        ? Palette.Get(Palette.Cream)
                        : Palette.Get(col, Palette.Finish.Glow);
                }
                if (cell.unheldSinceTick != -1 && cell.owner != FactionId.Neutral)
                {
                    // Unguarded territory pulses orange until a Fort or unit holds it.
                    float p = Mathf.PingPong(Time.time * 1.5f, 1f);
                    cellGrids[cell.id].color = Color.Lerp(new Color(1f, 0.55f, 0.1f, 0.9f), new Color(col.r, col.g, col.b, 0.55f), p);
                }
                else cellGrids[cell.id].color = new Color(col.r, col.g, col.b, 0.55f);
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

        /// <summary>Resource nodes are floating asteroids above their tile, with a shadow below.</summary>
        void RefreshNodes(WorldState w, ContentCatalog c)
        {
            foreach (var n in w.nodes)
            {
                if (nodes.ContainsKey(n.id)) continue;
                bool rich = WorldLookup.IsRichNode(w, n);
                var ground = TileCenter(map, n.cellId, n.x, n.y);
                var sr = Make("Asteroid " + n.id, sprites.GetAsteroidSprite(n.itemId, n.id, rich), -2, false);
                sr.transform.position = ground + Vector3.up * 0.45f;
                sr.transform.localScale = Vector3.one * (rich ? 1.1f : 0.95f);
                nodes[n.id] = sr;
                nodeScales[n.id] = rich ? 1.1f : 0.95f;
                var item = CatalogRepository.GetItem(c, n.itemId);
                Color itemColor;
                if (item == null || !ColorUtility.TryParseHtmlString(item.colorHex, out itemColor)) itemColor = Color.grey;
                var model = ModelLibrary.Asteroid(n.itemId, itemColor, n.id, rich, root);
                if (model != null)
                {
                    nodeModels[n.id] = model;
                    model.Root.transform.position = ground + Vector3.up * 0.45f;
                    model.Root.transform.rotation = Quaternion.Euler(0, n.id * 47f, 0);
                    sr.enabled = false;
                    continue; // real shadows replace the blob and rich ring
                }
                var shadow = Make("AsteroidShadow " + n.id, sprites.GetShadowSprite(), -66, true);
                shadow.transform.position = ground + Vector3.up * 0.012f;
                shadow.transform.localScale = new Vector3(rich ? 1.1f : 0.9f, rich ? 0.8f : 0.65f, 1f);
                if (rich)
                {
                    var ring = Make("Rich " + n.id, sprites.GetRingSprite(), -65, true);
                    ring.transform.position = ground + Vector3.up * 0.014f;
                    ring.transform.localScale = Vector3.one * 1.05f;
                    ring.color = new Color(1f, 0.82f, 0.3f, 0.6f);
                }
            }
        }

        /// <summary>
        /// Asteroids bob gently (rotation would smear the pixel art). A mined one shrinks into the front of its rig's
        /// cradle and draws over the rig, so you can still tell what each extractor is mining.
        /// </summary>
        void AnimateAsteroids(WorldState w)
        {
            foreach (var n in w.nodes)
            {
                SpriteRenderer sr;
                if (!nodes.TryGetValue(n.id, out sr)) continue;
                bool mined = n.extractorBuildingId != -1;
                float bob = Mathf.Sin(Time.time * 0.8f + n.id) * (mined ? 0.02f : 0.04f);
                float baseScale;
                if (!nodeScales.TryGetValue(n.id, out baseScale)) baseScale = 1f;
                Model model;
                if (nodeModels.TryGetValue(n.id, out model))
                {
                    var t = model.Root.transform;
                    // Outcrops are part of the ground: they never move, and a rig simply stands over one.
                    t.position = TileCenter(map, n.cellId, n.x, n.y) + Vector3.up * 0.06f;
                    t.localScale = Vector3.one * (mined ? 0.85f : 1.1f);
                    model.SetState(mined && beltView ? Model.Faded : Model.Normal);
                    continue;
                }
                sr.sortingOrder = mined ? 1 : -2;
                sr.color = mined && beltView ? new Color(1f, 1f, 1f, BeltViewAlpha) : Color.white;
                sr.transform.localScale = Vector3.one * (mined ? baseScale * 0.62f : baseScale);
                sr.transform.position = TileCenter(map, n.cellId, n.x, n.y) + Vector3.up * ((mined ? 0.1f : 0.45f) + bob);
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
                    float size = b.typeId == "headquarters" ? 1.3f : b.typeId == "fort" ? 1.25f : 1.2f;
                    var ground = TileCenter(map, b.cellId, b.x, b.y);
                    v = new BuildingVisual
                    {
                        Body = Make("Station " + b.id, sprites.GetStationSprite(b.typeId, b.owner), 0, false),
                        Alert = new Marker("Alert " + b.id, Marker.Shape.Warning, root),
                        Shadow = Make("StationShadow " + b.id, sprites.GetShadowSprite(), -64, true),
                        Owner = b.owner,
                        Ground = ground,
                        Size = size,
                        Phase = b.id * 0.7f
                    };
                    v.Body.transform.localScale = Vector3.one * size;
                    v.Shadow.transform.position = ground + Vector3.up * 0.011f;
                    float shadowSize = b.typeId == "headquarters" ? 2.6f : 1.05f;
                    v.Shadow.transform.localScale = new Vector3(shadowSize, shadowSize * 0.7f, 1f);
                    v.Alert.Transform.localScale = Vector3.one * 0.34f;
                    v.Model = MakeBuildingModel(b, ground);
                    buildings[b.id] = v;
                }
                if (v.Owner != b.owner)
                {
                    // HQ buildings change hands with their cell; repaint in the new owner's colours.
                    v.Owner = b.owner;
                    v.Body.sprite = sprites.GetStationSprite(b.typeId, b.owner);
                    if (v.Model != null) { v.Model.Destroy(); v.Model = MakeBuildingModel(b, v.Ground); }
                }
                bool active = WorldLookup.IsBuildingActive(w, b);
                if (v.Model != null)
                {
                    // Models stand on the deck; the hidden sprite rides above them as the anchor for alerts and needs.
                    v.Model.SetState(beltView || b.underConstruction ? Model.Faded : active ? Model.Normal : Model.Dimmed);
                    if (active) v.Model.Animate(Time.deltaTime);
                    v.Body.enabled = false;
                    v.Shadow.enabled = false;
                    v.Body.transform.position = v.Ground + Vector3.up * Mathf.Max(0.32f, v.ModelHeight - 0.75f);
                    v.Alert.Transform.position = v.Body.transform.position + Vector3.up * (v.Size * 0.8f + Mathf.Sin(Time.time * 3f + v.Phase) * 0.04f);
                    bool modelBlocked = active && b.owner == FactionId.Player && router.BlockedReasonByBuilding.ContainsKey(b.id);
                    RefreshNeeds(w, c, b, v, active || (b.underConstruction && WorldLookup.CanReceive(w, b)));
                    v.Alert.Enabled = !beltView && ((modelBlocked && v.Needs.Count == 0) || (!active && !b.underConstruction && b.owner == FactionId.Player));
                    v.Alert.Color = active ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f);
                    continue;
                }
                // Stations float with a gentle bob; stopped ones sit still and go dark.
                float bob = active ? Mathf.Sin(Time.time * 1.3f + v.Phase) * 0.03f : 0f;
                v.Body.transform.position = v.Ground + Vector3.up * (0.32f + bob);
                v.Alert.Transform.position = v.Body.transform.position + Vector3.up * (v.Size * 0.8f + Mathf.Sin(Time.time * 3f + v.Phase) * 0.04f);
                // Construction sites are ghosted; stopped buildings go dark; Belt View fades everything to an outline's worth.
                v.Body.color = b.underConstruction ? new Color(1f, 1f, 1f, 0.6f) : active ? Color.white : new Color(0.42f, 0.42f, 0.48f, 0.85f);
                if (beltView) v.Body.color = new Color(v.Body.color.r, v.Body.color.g, v.Body.color.b, BeltViewAlpha);
                v.Shadow.enabled = !beltView;
                bool blocked = active && b.owner == FactionId.Player && router.BlockedReasonByBuilding.ContainsKey(b.id);
                RefreshNeeds(w, c, b, v, active || (b.underConstruction && WorldLookup.CanReceive(w, b)));
                // Machines with a needs row explain themselves there; the alert icon covers depots and stopped buildings.
                v.Alert.Enabled = !beltView && ((blocked && v.Needs.Count == 0) || (!active && !b.underConstruction && b.owner == FactionId.Player));
                v.Alert.Color = active ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f);
            }
            RemoveMissing(buildings, seen, v => { Destroy(v.Body); v.Alert.Destroy(); Destroy(v.Shadow); ClearNeeds(v); if (v.Model != null) v.Model.Destroy(); });
        }

        Model MakeBuildingModel(BuildingState b, Vector3 ground)
        {
            var model = ModelLibrary.Building(b.typeId, b.owner, root);
            if (model == null) return null;
            model.Root.transform.position = ground;
            // The HQ's tower is sized for its plaza; keep it within the tiles around it.
            if (b.typeId == "headquarters") model.Root.transform.localScale = Vector3.one * 0.6f;
            return model;
        }

        void ClearNeeds(BuildingVisual v)
        {
            foreach (var n in v.Needs)
            {
                Object.Destroy(n.Icon);
                n.Ring.Destroy();
                foreach (var p in n.Pips) p.Destroy();
            }
            v.Needs.Clear();
        }

        /// <summary>
        /// Shows what a player machine is waiting for. Ring colours: green = in stock, amber = on its way, pulsing
        /// red = missing with nothing coming. Pips under each icon fill as units arrive.
        /// </summary>
        void RefreshNeeds(WorldState w, ContentCatalog c, BuildingState b, BuildingVisual v, bool active)
        {
            var needs = b.owner == FactionId.Player ? FactorySimulation.CurrentNeeds(w, c, b) : null;
            string sig = needs == null || needs.Count == 0 ? "" : string.Join(",", needs.ConvertAll(n => n.itemId + "x" + n.quantity));
            if (sig != v.NeedSignature)
            {
                ClearNeeds(v);
                v.NeedSignature = sig;
                if (sig.Length > 0)
                {
                    foreach (var need in needs)
                    {
                        var item = CatalogRepository.GetItem(c, need.itemId);
                        Color itemColor;
                        if (item == null || !ColorUtility.TryParseHtmlString(item.colorHex, out itemColor)) itemColor = Color.grey;
                        var icon = new NeedIcon
                        {
                            Ring = new Marker("NeedRing", Marker.Shape.Ring, root),
                            Icon = ModelLibrary.Cargo(need.itemId, itemColor, root),
                            Pips = new Marker[Mathf.Min(need.quantity, 4)]
                        };
                        icon.Icon.transform.localScale = Vector3.one * 1.05f;
                        for (int i = 0; i < icon.Pips.Length; i++)
                        {
                            icon.Pips[i] = new Marker("Pip", Marker.Shape.Pip, root);
                            icon.Pips[i].Transform.localScale = Vector3.one * 0.075f;
                        }
                        v.Needs.Add(icon);
                    }
                }
            }
            if (v.Needs.Count == 0) return;
            foreach (var n in v.Needs)
            {
                n.Icon.SetActive(ShowNeedIcons && !beltView);
                n.Ring.Enabled = ShowNeedIcons && !beltView;
                foreach (var p in n.Pips) p.Enabled = ShowNeedIcons;
            }
            if (!ShowNeedIcons) return;

            var camRight = camera != null ? camera.transform.right : Vector3.right;
            var camUp = camera != null ? camera.transform.up : Vector3.up;
            var anchor = v.Body.transform.position + Vector3.up * (NeedRowHeight - BillboardHeight);
            float start = -(v.Needs.Count - 1) * NeedSpacing * 0.5f;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
            bool full = FactorySimulation.IsStorageFull(w, c, b);
            for (int i = 0; i < v.Needs.Count; i++)
            {
                var n = v.Needs[i];
                var need = needs[i];
                int have = WorldLookup.CountItem(b.inventory, need.itemId);
                int coming = FactorySimulation.Inbound(w, b.id, need.itemId);
                var pos = anchor + camRight * (start + i * NeedSpacing);
                // The good turns slowly inside its ring.
                n.Icon.transform.position = pos - camUp * 0.11f;
                n.Icon.transform.rotation = Quaternion.Euler(-15f, Time.time * 50f + i * 40f, 0f);
                n.Ring.Transform.position = pos;
                Color ring;
                float ringScale = 0.36f;
                if (!active) ring = new Color(0.5f, 0.5f, 0.5f, 0.7f);
                else if (have >= need.quantity) ring = new Color(0.35f, 1f, 0.5f, 0.95f);
                else if (coming > 0) ring = new Color(1f, 0.8f, 0.25f, 0.95f);
                else if (full) ring = new Color(0.45f, 0.65f, 1f, 0.9f); // backed up on output, not starved
                else { ring = new Color(1f, 0.25f, 0.2f, pulse); ringScale = 0.36f + 0.06f * pulse; }
                n.Ring.Color = ring;
                n.Ring.Transform.localScale = Vector3.one * ringScale;
                for (int p = 0; p < n.Pips.Length; p++)
                {
                    float px = (p - (n.Pips.Length - 1) * 0.5f) * 0.1f;
                    n.Pips[p].Transform.position = pos - camUp * 0.25f + camRight * px;
                    n.Pips[p].Color = p < have ? Color.white : p < have + coming ? new Color(1f, 0.8f, 0.25f, 0.9f) : new Color(0.15f, 0.15f, 0.2f, 0.9f);
                }
            }
        }

        /// <summary>Set by the bootstrap: a belt's whole line, for highlighting the selected one.</summary>
        public System.Func<WorldState, int, List<BeltSegmentState>> LineOf;

        static readonly Color[] TierTint = { Color.white, new Color(0.47f, 0.88f, 1f), new Color(1f, 0.87f, 0.49f) };

        /// <summary>
        /// A belt that crosses between cells spans open space between asteroids, so it is drawn as a drone link.
        /// </summary>
        static bool IsDroneLink(BeltSegmentState s)
        {
            return s.fromCellId != s.toCellId;
        }

        void RefreshBelts(WorldState w, UiState ui)
        {
            var highlighted = new HashSet<int>();
            if (ui != null && ui.selectedBeltId >= 0 && LineOf != null)
                foreach (var s in LineOf(w, ui.selectedBeltId)) highlighted.Add(s.id);
            var seen = new HashSet<int>();
            var hubKeys = new HashSet<int>();
            var hubOwner = new Dictionary<int, BeltSegmentState>();
            var touching = new Dictionary<int, List<BeltSegmentState>>();
            var buildingTiles = new HashSet<int>();
            foreach (var b in w.buildings) buildingTiles.Add(BeltNetwork.VertexKey(b.cellId, b.x, b.y));
            bool flowing = w.mode == MatchMode.Factory;
            foreach (var s in w.belts)
            {
                seen.Add(s.id);
                BeltVisual v;
                if (!belts.TryGetValue(s.id, out v))
                {
                    v = new BeltVisual
                    {
                        From = TileCenter(map, s.fromCellId, s.fromX, s.fromY),
                        To = TileCenter(map, s.toCellId, s.toX, s.toY)
                    };
                    if (IsDroneLink(s))
                    {
                        v.Link = ModelLibrary.DroneLink(v.From, v.To, s.owner, root);
                        v.Drone = ModelLibrary.Drone(s.owner, root);
                    }
                    else
                    {
                        // A conveyor tile from tile centre to tile centre, with arrows riding it.
                        var rot = Quaternion.LookRotation(v.To - v.From, Vector3.up);
                        var go = new GameObject("Belt " + s.id);
                        go.transform.SetParent(root, false);
                        go.transform.SetPositionAndRotation((v.From + v.To) * 0.5f, rot);
                        go.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ConveyorMesh();
                        v.Conveyor = go.AddComponent<MeshRenderer>();
                        v.Arrows = new MeshRenderer[ChevronsPerBelt];
                        for (int i = 0; i < ChevronsPerBelt; i++)
                        {
                            var arrow = new GameObject("Arrow");
                            arrow.transform.SetParent(root, false);
                            arrow.transform.rotation = rot;
                            arrow.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ArrowMesh();
                            v.Arrows[i] = arrow.AddComponent<MeshRenderer>();
                            v.Arrows[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        }
                    }
                    belts[s.id] = v;
                }
                bool active = WorldLookup.IsBeltActive(w, s);
                v.Moving = active && flowing;
                if (v.Link != null) v.Link.SetState(active ? Model.Normal : Model.Dimmed);
                else
                {
                    // Rails show the speed tier, the side strip lights in the owner's colour, the selected line
                    // brightens, and stopped belts go dark.
                    v.Conveyor.sharedMaterials = ModelLibrary.ConveyorMaterials(s.owner, s.tier, active, highlighted.Contains(s.id));
                    var arrowMat = ModelLibrary.ArrowMaterial(s.owner, s.tier, active);
                    foreach (var arrow in v.Arrows) arrow.sharedMaterial = arrowMat;
                }

                if (IsDroneLink(s)) continue;
                foreach (int key in new[] { BeltNetwork.VertexKey(s.fromCellId, s.fromX, s.fromY), BeltNetwork.VertexKey(s.toCellId, s.toX, s.toY) })
                {
                    if (buildingTiles.Contains(key)) continue;
                    List<BeltSegmentState> list;
                    if (!touching.TryGetValue(key, out list)) touching[key] = list = new List<BeltSegmentState>();
                    list.Add(s);
                    if (!hubOwner.ContainsKey(key) || active) hubOwner[key] = s;
                }
            }
            // Hubs mark corners, splits, merges and line ends; a straight pass-through needs none.
            foreach (var kv in touching)
            {
                var list = kv.Value;
                if (list.Count == 2)
                {
                    var d0 = belts[list[0].id].To - belts[list[0].id].From;
                    var d1 = belts[list[1].id].To - belts[list[1].id].From;
                    if (Mathf.Abs(Vector3.Dot(d0.normalized, d1.normalized)) > 0.99f) continue;
                }
                hubKeys.Add(kv.Key);
            }
            RemoveMissing(belts, seen, v =>
            {
                if (v.Conveyor != null) Destroy(v.Conveyor);
                if (v.Arrows != null) foreach (var arrow in v.Arrows) Destroy(arrow);
                if (v.Link != null) v.Link.Destroy();
                if (v.Drone != null) v.Drone.Destroy();
            });

            foreach (int key in hubKeys)
            {
                MeshRenderer hub;
                var s = hubOwner[key];
                if (!beltHubs.TryGetValue(key, out hub))
                {
                    int cellId = BeltNetwork.VertexCell(key), y = BeltNetwork.VertexY(key), x = BeltNetwork.VertexX(key);
                    var go = ModelLibrary.Hub(root);
                    go.transform.position = TileCenter(map, cellId, x, y);
                    hub = go.GetComponent<MeshRenderer>();
                    beltHubs[key] = hub;
                }
                hub.sharedMaterials = ModelLibrary.HubMaterials(s.owner, WorldLookup.IsBeltActive(w, s));
            }
            RemoveMissing(beltHubs, hubKeys, Destroy);
        }

        /// <summary>Slides arrows along their belts in the direction of travel at the item speed, and flies the drones.</summary>
        void AnimateBelts(ContentCatalog c)
        {
            // Arrows run at base belt speed; faster tiers are told apart by colour.
            float secondsPerBelt = Mathf.Max(0.05f, c.rules.beltTiers[0].ticksPerSegment * c.rules.tickMilliseconds / 1000f);
            if (beltClockLastFrame != Time.frameCount)
            {
                beltClockLastFrame = Time.frameCount;
                beltClock += Time.deltaTime / secondsPerBelt;
            }
            foreach (var v in belts.Values)
            {
                if (v.Drone != null) continue; // drones fly in AnimateDrones, from the simulation

                if (v.Arrows == null) continue;
                for (int i = 0; i < v.Arrows.Length; i++)
                {
                    float t = v.Moving ? Mathf.Repeat(beltClock + (float)i / v.Arrows.Length, 1f) : (i + 0.5f) / v.Arrows.Length;
                    var arrow = v.Arrows[i];
                    arrow.transform.position = Vector3.Lerp(v.From, v.To, t);
                    // Shrink at the ends so arrows hand over smoothly between consecutive belts.
                    arrow.transform.localScale = Vector3.one * (v.Moving ? Mathf.Clamp01(Mathf.Sin(t * Mathf.PI) * 1.8f) : 0.8f);
                }
            }
        }

        float beltClock;
        int beltClockLastFrame = -1;

        /// <summary>
        /// Each link's drone follows the simulation: it carries the item in flight (riding just above it), flies home
        /// empty after a delivery, and otherwise waits on its home pad.
        /// </summary>
        void AnimateDrones(WorldState w, ContentCatalog c, float tickFraction)
        {
            var carrying = new Dictionary<int, float>();
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                var seg = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                if (seg == null || !IsDroneLink(seg)) continue;
                carrying[seg.id] = TokenProgress(w, c, t, seg, tickFraction);
            }
            foreach (var s in w.belts)
            {
                BeltVisual v;
                if (!belts.TryGetValue(s.id, out v) || v.Drone == null) continue;
                float along, lift = 0.1f;
                if (carrying.TryGetValue(s.id, out along)) lift = 0.24f;
                else if (BeltNetwork.DroneAway(w, s))
                {
                    float remaining = s.droneBackTick - w.tick - (w.mode == MatchMode.Factory ? tickFraction : 0f);
                    along = Mathf.Clamp01(remaining / Mathf.Max(1, BeltNetwork.TravelTicks(w, c, s)));
                }
                else along = 0f;
                v.Drone.Root.transform.position = ModelLibrary.LinkArc(v.From, v.To, along) + Vector3.up * lift;
                v.Drone.Animate(Time.deltaTime);
                v.Drone.SetState(beltView ? Model.Faded : v.Moving ? Model.Normal : Model.Dimmed);
            }
        }

        /// <summary>How far along its current belt an item is drawn (0-1, stopping short of each end).</summary>
        float TokenProgress(WorldState w, ContentCatalog c, ItemTokenState t, BeltSegmentState seg, float tickFraction)
        {
            int travel = BeltNetwork.TravelTicks(w, c, seg);
            float remaining = t.ticksUntilNextSegment;
            if (t.status == TokenStatus.Moving && remaining > 0) remaining = Mathf.Max(0f, remaining - tickFraction);
            float p = t.status == TokenStatus.Moving ? 1f - remaining / travel : (t.ticksUntilNextSegment <= 0 ? 1f : 1f - (float)t.ticksUntilNextSegment / travel);
            return Mathf.Clamp01(p) * 0.85f + 0.075f;
        }

        /// <summary>Camera distance up to which cargo is drawn at its true size.</summary>
        const float CargoFullSizeDistance = 16f;

        void RefreshTokens(WorldState w, ContentCatalog c, float tickFraction)
        {
            var seen = new HashSet<int>();
            foreach (var t in w.tokens)
            {
                if (t.segmentIndex < 0 || t.segmentIndex >= t.routeSegmentIds.Count) continue;
                var seg = WorldLookup.GetBelt(w, t.routeSegmentIds[t.segmentIndex]);
                if (seg == null) continue;
                seen.Add(t.id);
                TokenVisual tv;
                if (!tokens.TryGetValue(t.id, out tv))
                {
                    // A 3D good rides the conveyor, with a small icon above it naming the item.
                    var item = CatalogRepository.GetItem(c, t.itemId);
                    Color itemColor;
                    if (item == null || !ColorUtility.TryParseHtmlString(item.colorHex, out itemColor)) itemColor = Color.grey;
                    tv = new TokenVisual
                    {
                        Crate = ModelLibrary.Cargo(t.itemId, itemColor, root),
                        Cargo = Make("Cargo " + t.id, sprites.GetItemSprite(t.itemId), 2, false)
                    };
                    tokens[t.id] = tv;
                }
                var a = TileCenter(map, seg.fromCellId, seg.fromX, seg.fromY);
                var b = TileCenter(map, seg.toCellId, seg.toX, seg.toY);
                // Stop short of the vertex so waiting items do not overlap the next machine; items crossing between
                // asteroids fly the drone link's path.
                float along = TokenProgress(w, c, t, seg, tickFraction);
                var pos = IsDroneLink(seg) ? ModelLibrary.LinkArc(a, b, along) : Vector3.Lerp(a, b, along) + Vector3.up * ModelLibrary.BeltSurface;
                // Cargo grows as the camera pulls back so goods stay readable when zoomed out.
                float zoom = camera != null ? Mathf.Clamp(Vector3.Distance(camera.transform.position, pos) / CargoFullSizeDistance, 1f, 2.2f) : 1f;
                tv.Crate.transform.position = pos;
                tv.Crate.transform.rotation = Quaternion.LookRotation(b - a, Vector3.up);
                tv.Crate.transform.localScale = Vector3.one * zoom;
                tv.Cargo.transform.position = pos + Vector3.up * (0.48f * zoom);
                tv.Cargo.transform.localScale = Vector3.one * (0.36f * zoom);
                bool frozen = t.status == TokenStatus.Frozen;
                if (frozen != tv.Frozen)
                {
                    tv.Frozen = frozen;
                    // Items stuck on a stopped belt go grey.
                    var r = tv.Crate.GetComponent<MeshRenderer>();
                    if (tv.Normal == null) tv.Normal = r.sharedMaterials;
                    var mats = new Material[tv.Normal.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = frozen ? Palette.Dimmed(tv.Normal[i]) : tv.Normal[i];
                    r.sharedMaterials = mats;
                }
                tv.Cargo.color = frozen ? new Color(0.55f, 0.55f, 0.6f, 0.6f) : Color.white;
                tv.Cargo.enabled = !beltView;
            }
            RemoveMissing(tokens, seen, tv => { if (tv.Crate != null) Object.Destroy(tv.Crate); Destroy(tv.Cargo); });
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
                        Body = Make("Craft " + craft.id, sprites.GetCraftSprite(craft.typeId, craft.owner), 3, false),
                        Shadow = Make("Shadow " + craft.id, sprites.GetShadowSprite(), -30, true),
                        DefenderRing = Make("DefRing " + craft.id, sprites.GetRingSprite(), -31, true),
                        HpBack = new Marker("HpBack " + craft.id, Marker.Shape.Bar, root, false),
                        HpFill = new Marker("HpFill " + craft.id, Marker.Shape.Bar, root)
                    };
                    v.Body.transform.localScale = Vector3.one * 0.9f;
                    v.HpBack.Color = new Color(0.08f, 0.07f, 0.12f);
                    v.Model = ModelLibrary.Craft(craft.typeId, craft.owner, root);
                    if (v.Model != null)
                    {
                        v.Body.enabled = false;
                        v.Shadow.enabled = false;
                        v.Model.Root.transform.localScale = Vector3.one * 1.05f;
                        v.Heading = craft.owner == FactionId.Player ? 45f : 225f;
                    }
                    crafts[craft.id] = v;
                }
                var factionCol = SpaceSpriteLibrary.FactionColor(craft.owner);
                v.Body.color = Color.white; // ship art carries its own faction colours
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
                if (v.Model != null)
                {
                    // Turn to face the way the craft is moving, easing round; hover a little above the deck.
                    var moved = pos - v.LastPos;
                    if (v.LastPos != Vector3.zero && moved.sqrMagnitude > 1e-5f)
                        v.Heading = Mathf.MoveTowardsAngle(v.Heading, Mathf.Atan2(moved.x, moved.z) * Mathf.Rad2Deg, 240f * Time.deltaTime);
                    v.LastPos = pos;
                    float hover = def != null && def.airborne ? AirborneHeight * 0.75f : 0.32f;
                    hover += Mathf.Sin(Time.time * 2f + craft.id) * 0.04f;
                    v.Model.Root.transform.position = pos + Vector3.up * hover;
                    v.Model.Root.transform.rotation = Quaternion.Euler(0, v.Heading, 0);
                    v.Model.Animate(Time.deltaTime);
                    v.Model.SetState(Model.Normal);
                    v.Body.transform.position = pos + Vector3.up * Mathf.Max(BillboardHeight, hover);
                }
                v.Shadow.transform.localScale = def != null && def.airborne ? Vector3.one * 0.7f : Vector3.one * 0.9f;
                v.DefenderRing.transform.position = pos + Vector3.up * 0.018f;
                v.DefenderRing.transform.localScale = Vector3.one * 1.1f;

                float hp = def != null ? Mathf.Clamp01((float)craft.currentHealth / def.maxHealth) : 1f;
                var barCenter = v.Body.transform.position + Vector3.up * 0.6f;
                // A dark backing bar with the fill drawn just in front of it, shrinking toward its left end.
                var right = camera != null ? camera.transform.right : Vector3.right;
                var toward = camera != null ? -camera.transform.forward : Vector3.back;
                v.HpBack.Transform.position = barCenter;
                v.HpBack.Transform.localScale = new Vector3(0.8f, 0.1f, 0.1f);
                v.HpFill.Transform.localScale = new Vector3(0.76f * hp, 0.065f, 0.1f);
                v.HpFill.Color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 1f, 0.45f), hp);
                v.Target = barCenter;
                v.HpFill.Transform.position = barCenter - right * (0.38f * (1f - hp)) + toward * 0.02f;
            }
            RemoveMissing(crafts, seen, v => { Destroy(v.Body); Destroy(v.Shadow); Destroy(v.DefenderRing); v.HpBack.Destroy(); v.HpFill.Destroy(); if (v.Model != null) v.Model.Destroy(); });
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
                foreach (var n in w.nodes) if (n.cellId == cellId) occupied.Add(n.y * 32 + n.x);
                foreach (var b in w.buildings) if (b.cellId == cellId) occupied.Add(b.y * 32 + b.x);
                foreach (var t in def.blockedTiles) occupied.Add(t.y * 32 + t.x);
                foreach (var s in w.belts)
                {
                    if (s.fromCellId == cellId) belted.Add(s.fromY * 32 + s.fromX);
                    if (s.toCellId == cellId) belted.Add(s.toY * 32 + s.toX);
                }
                var candidates = new List<KeyValuePair<float, Vector3>>();
                float cx = def.gridWidth * 0.5f - 0.5f, cy = def.gridHeight * 0.5f - 0.5f;
                for (int y = 0; y < def.gridHeight; y++)
                    for (int x = 0; x < def.gridWidth; x++)
                    {
                        int key = y * 32 + x;
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
            var node = WorldLookup.GetNode(w, ui.selectedNodeId);
            // The ring lies on the deck just outside the selection's footprint, so a building's base never hides it.
            var selected = new Color(0.45f, 1f, 0.8f);
            if (craft != null && crafts.ContainsKey(craft.id))
            {
                var v = crafts[craft.id];
                selectionRing.Show(v.Shadow.transform.position, v.Model != null ? Footprint(v.Model.Bounds) + 0.08f : 0.6f, selected);
            }
            else if (building != null && buildings.ContainsKey(building.id))
            {
                var v = buildings[building.id];
                selectionRing.Show(v.Ground, v.Model != null ? Footprint(v.Model.Bounds) + 0.1f : 0.6f, selected);
            }
            else if (node != null)
            {
                Model model;
                float r = nodeModels.TryGetValue(node.id, out model) ? Footprint(model.Bounds) + 0.1f : 0.6f;
                selectionRing.Show(TileCenter(map, node.cellId, node.x, node.y), r, new Color(0.65f, 0.96f, 0.85f));
            }
            else selectionRing.Hide();
            selectionRing.Animate(Time.time);
        }

        /// <summary>Radius of the circle covering a model's footprint on the deck.</summary>
        static float Footprint(Bounds b)
        {
            return Mathf.Max(0.45f, new Vector2(b.extents.x, b.extents.z).magnitude * 0.85f);
        }

        void FaceCamera()
        {
            if (camera == null) return;
            var rot = camera.transform.rotation;
            foreach (var v in buildings.Values)
            {
                v.Body.transform.rotation = rot;
                v.Alert.Transform.rotation = rot;
                foreach (var n in v.Needs)
                {
                    n.Ring.Transform.rotation = rot;
                }
            }
            foreach (var t in tokens.Values) t.Cargo.transform.rotation = rot;
            foreach (var kv in nodes) kv.Value.transform.rotation = rot;
            foreach (var s in hqStars.Values) s.transform.rotation = rot;
            foreach (var v in crafts.Values)
            {
                v.Body.transform.rotation = rot;
                v.HpBack.Transform.rotation = rot;
                v.HpFill.Transform.rotation = rot;
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

        /// <summary>Pick the visible floating asteroid, including the part above its ground tile.</summary>
        public int PickNode(Vector2 screenPoint, float paddingPixels)
        {
            if (camera == null) return -1;
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var kv in nodes)
            {
                float d;
                Model model;
                bool hit = nodeModels.TryGetValue(kv.Key, out model)
                    ? BoundsHit(model.Bounds, screenPoint, paddingPixels, out d)
                    : SpriteHit(kv.Value, screenPoint, paddingPixels, out d);
                if (!hit) continue;
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Pick a building by its visible body, which sits above the ground tile in screen space.</summary>
        public int PickBuilding(Vector2 screenPoint, float paddingPixels)
        {
            if (camera == null) return -1;
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var kv in buildings)
            {
                float d;
                bool hit = kv.Value.Model != null
                    ? BoundsHit(kv.Value.Model.Bounds, screenPoint, paddingPixels, out d)
                    : SpriteHit(kv.Value.Body, screenPoint, paddingPixels, out d);
                if (!hit) continue;
                if (d < bestDist) { bestDist = d; best = kv.Key; }
            }
            return best;
        }

        /// <summary>Whether the screen point falls in the screen rectangle covering a model's world bounds.</summary>
        bool BoundsHit(Bounds bounds, Vector2 screenPoint, float paddingPixels, out float distance)
        {
            distance = float.MaxValue;
            Vector3 center = camera.WorldToScreenPoint(bounds.center);
            if (center.z <= 0f) return false;
            distance = Vector2.Distance(screenPoint, new Vector2(center.x, center.y));
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 sp = camera.WorldToScreenPoint(corner);
                minX = Mathf.Min(minX, sp.x);
                minY = Mathf.Min(minY, sp.y);
                maxX = Mathf.Max(maxX, sp.x);
                maxY = Mathf.Max(maxY, sp.y);
            }
            return screenPoint.x >= minX - paddingPixels && screenPoint.x <= maxX + paddingPixels
                && screenPoint.y >= minY - paddingPixels && screenPoint.y <= maxY + paddingPixels;
        }

        bool SpriteHit(SpriteRenderer renderer, Vector2 screenPoint, float paddingPixels, out float distance)
        {
            distance = float.MaxValue;
            if (renderer == null || renderer.sprite == null) return false;
            var bounds = renderer.sprite.bounds;
            var transform = renderer.transform;
            Vector3 center = camera.WorldToScreenPoint(transform.TransformPoint(bounds.center));
            if (center.z <= 0f) return false;
            distance = Vector2.Distance(screenPoint, new Vector2(center.x, center.y));
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (float x in new[] { bounds.min.x, bounds.max.x })
                foreach (float y in new[] { bounds.min.y, bounds.max.y })
                {
                    Vector3 corner = camera.WorldToScreenPoint(transform.TransformPoint(new Vector3(x, y, bounds.center.z)));
                    minX = Mathf.Min(minX, corner.x);
                    minY = Mathf.Min(minY, corner.y);
                    maxX = Mathf.Max(maxX, corner.x);
                    maxY = Mathf.Max(maxY, corner.y);
                }
            return screenPoint.x >= minX - paddingPixels && screenPoint.x <= maxX + paddingPixels
                && screenPoint.y >= minY - paddingPixels && screenPoint.y <= maxY + paddingPixels;
        }
    }
}
