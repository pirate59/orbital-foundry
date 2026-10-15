using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Stages a small running factory on map_a (an extractor chain in the HQ cell and a belt crossing into a fortified
    /// neighbour) and renders it through the real WorldView, for reviewing the art without playing:
    /// -executeMethod OrbitalFoundry.EditorTools.SceneMockup.ExportBatch -mockupOut &lt;dir&gt;
    /// </summary>
    public static class SceneMockup
    {
        const int Width = 1600, Height = 900;

        public static void ExportBatch()
        {
            string outDir = "Logs/mockup";
            bool markers = false;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-mockupOut") outDir = args[i + 1];
                if (args[i] == "-markers") markers = true;
            }
            try
            {
                Export(outDir, markers);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static void Export(string outDir, bool markers)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap("map_a", catalog);
            var loop = new GameLoop();
            var saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrbitalFoundryMockup");
            loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 1234), new SaveService(saveDir));
            var w = loop.World;
            WorldLookup.GetFaction(w, FactionId.Player).currency = 100000;
            Stage(loop, map);
            for (int i = 0; i < 900; i++) loop.TickNormal();

            var camGo = new GameObject("Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 800f;
            RenderRig.Build(cam);
            var view = new WorldView(new SpaceSpriteLibrary(), new GameObject("World").transform);
            view.LineOf = (ws, id) => loop.Services.Belts.LineOf(ws, id);
            view.BuildMap(map, w, cam);
            var ui = new UiState();

            var hq = WorldView.CellCenter(map, 0);
            var next = WorldView.CellCenter(map, 1);
            var focus = Vector3.Lerp(hq, next, 0.42f);
            var refinery = w.buildings.Find(b => b.cellId == 0 && b.typeId == "refinery");
            if (markers)
            {
                // Ground markers: a selected refinery, a hovered tile, the HQ defender's ring and control point,
                // and a capture hold on a neutral cell.
                ui.selectedBuildingId = refinery.id;
                view.SetHover(true, 0, refinery.x, refinery.y + 2, new Color(0.3f, 1f, 0.5f, 0.45f));
                var guard = w.crafts.Find(c => c.owner == FactionId.Player);
                w.captureHolds.Add(new CaptureHoldState { cellId = 2, craftId = guard != null ? guard.id : -1, faction = FactionId.Player });
            }
            var refineryAt = WorldView.TileCenter(map, 0, refinery.x, refinery.y);
            var sendPad = w.buildings.Find(b => b.typeId == "drone_pad" && b.cellId == 0);
            if (!markers && sendPad != null) ui.selectedBuildingId = sendPad.id; // shows the drone range ring
            var shots = markers ? new[]
            {
                new { Name = "markers-near-hq", Focus = Vector3.Lerp(hq, refineryAt, 0.5f), Yaw = 30f, Pitch = 52f, Distance = 11f },
                new { Name = "markers-selected", Focus = refineryAt, Yaw = 30f, Pitch = 50f, Distance = 5f },
                new { Name = "markers-capture", Focus = WorldView.CellCenter(map, 2), Yaw = 30f, Pitch = 55f, Distance = 12f },
            } : new[]
            {
                new { Name = "overview", Focus = Vector3.Lerp(hq, WorldView.CellCenter(map, 5), 0.5f), Yaw = 35f, Pitch = 40f, Distance = 70f },
                new { Name = "link", Focus = focus, Yaw = 20f, Pitch = 32f, Distance = 22f },
                new { Name = "factory", Focus = hq + new Vector3(2f, 0, 0), Yaw = 50f, Pitch = 38f, Distance = 16f },
                new { Name = "underside", Focus = focus + Vector3.down * 2f, Yaw = 200f, Pitch = 12f, Distance = 40f },
            };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            cam.Render(); // warm-up
            foreach (var shot in shots)
            {
                // Let animations and drones move a little between shots.
                for (int f = 0; f < 3; f++) view.Refresh(w, catalog, loop.Revision, loop.Services.Pull, ui, 0.5f);
                var rot = Quaternion.Euler(shot.Pitch, shot.Yaw, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = shot.Focus - rot * Vector3.forward * shot.Distance;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(System.IO.Path.Combine(outDir, shot.Name + ".png"), tex.EncodeToPNG());
            }
            Debug.Log("Scene mockup: " + w.buildings.Count + " buildings, " + w.belts.Count + " belts, " + w.tokens.Count + " items moving -> " + outDir);
        }

        /// <summary>An extractor-refinery chain in the HQ cell, feeding a storage that ships across into cell 1.</summary>
        static void Stage(GameLoop loop, MapDefinition map)
        {
            var w = loop.World;
            int cx, cy;
            var cell1 = WorldLookup.GetCellDef(map, 1);
            WorldLookup.GetCell(w, 1).owner = FactionId.Player;
            if (GameFactory.FindCentreTile(w, cell1, out cx, out cy))
                w.buildings.Add(new BuildingState { id = WorldLookup.AllocateId(w), owner = FactionId.Player, typeId = "fort", cellId = 1, x = cx, y = cy, anchor = true });
            OwnershipService.RefreshFactoryOnline(w);

            // Tap the HQ cell's ore nodes and put a refinery beside each.
            var ores = new List<ResourceNodeState>();
            foreach (var n in w.nodes) if (n.cellId == 0 && n.itemId != catalog(loop).rules.ionItemId) ores.Add(n);
            var c0 = WorldLookup.GetCellDef(map, 0);
            int row = 4;
            foreach (var n in ores)
            {
                if (!Place(loop, "extractor", 0, n.x, n.y)) continue;
                int rx = Mathf.Min(c0.gridWidth - 2, n.x + 2), ry = n.y;
                if (Place(loop, "refinery", 0, rx, ry)) LayBelts(loop, map, 0, n.x, n.y, 0, rx, ry);
                row++;
            }
            Place(loop, "solar_array", 0, 3, 3);
            Place(loop, "storage", 0, 17, 10);
            Place(loop, "assembler", 0, 14, 13);
            Place(loop, "shipyard", 0, 12, 6);
            foreach (var b in new List<BuildingState>(w.buildings))
                if (b.cellId == 0 && b.typeId == "refinery") LayBelts(loop, map, 0, b.x, b.y, 0, 17, 10);
            // Drone pads carry goods across the gap to a trading post in the fortified neighbour.
            var player = WorldLookup.GetFaction(w, FactionId.Player);
            player.research.Add(new ResearchProgress { nodeId = "drone_logistics", level = 3 });
            loop.Catalog.rules.droneBaseRange = Mathf.Max(loop.Catalog.rules.droneBaseRange, 40); // staging only: reach the neighbour whatever the layout
            Place(loop, "drone_pad", 0, c0.gridWidth - 1, 10);
            LayBelts(loop, map, 0, 17, 10, 0, c0.gridWidth - 1, 10);
            var pad = WorldLookup.GetBuildingAt(w, 0, c0.gridWidth - 1, 10);
            if (pad != null)
            {
                var r = loop.Submit(new GameCommand { kind = CommandKind.LinkDronePad, actor = FactionId.Player, buildingId = pad.id, toCellId = 1, toX = 0, toY = 10 });
                if (!r.success) Debug.Log("Mockup: link skipped (" + r.code + " " + r.message + ")");
            }
            Place(loop, "sale_depot", 1, 3, 10);
            Place(loop, "storage", 1, 8, 13);
            LayBelts(loop, map, 1, 0, 10, 1, 3, 10);
        }

        static ContentCatalog catalog(GameLoop loop) { return loop.Catalog; }

        static bool Place(GameLoop loop, string type, int cell, int x, int y)
        {
            var r = loop.Submit(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Player, contentId = type, fromCellId = cell, fromX = x, fromY = y });
            if (!r.success) { Debug.Log("Mockup: " + type + " at " + cell + ":" + x + "," + y + " skipped (" + r.code + ")"); return false; }
            // Machines start with no recipe: pick the first one the player can run.
            var b = WorldLookup.GetBuildingAt(loop.World, cell, x, y);
            foreach (var rid in CatalogRepository.GetBuilding(loop.Catalog, type).allowedRecipeIds)
                if (loop.Submit(new GameCommand { kind = CommandKind.SelectRecipe, actor = FactionId.Player, buildingId = b.id, contentId = rid }).success) break;
            return true;
        }

        /// <summary>Lays belts tile by tile along an L path between two tiles in global grid coordinates.</summary>
        static void LayBelts(GameLoop loop, MapDefinition map, int c1, int x1, int y1, int c2, int x2, int y2)
        {
            var a = WorldLookup.GetCellDef(map, c1);
            var b = WorldLookup.GetCellDef(map, c2);
            int gx = a.mapX * a.gridWidth + x1, gy = a.mapY * a.gridHeight + y1;
            int tx = b.mapX * b.gridWidth + x2, ty = b.mapY * b.gridHeight + y2;
            int guard = 0;
            while ((gx != tx || gy != ty) && guard++ < 80)
            {
                int nx = gx + (tx > gx ? 1 : tx < gx ? -1 : 0);
                int ny = nx != gx ? gy : gy + (ty > gy ? 1 : -1);
                int fc, fx, fy, tc, tx2, ty2;
                if (!GridRules.TryGlobalToLocal(map, gx, gy, out fc, out fx, out fy) || !GridRules.TryGlobalToLocal(map, nx, ny, out tc, out tx2, out ty2)) return;
                loop.Submit(new GameCommand { kind = CommandKind.PlaceBelt, actor = FactionId.Player, fromCellId = fc, fromX = fx, fromY = fy, toCellId = tc, toX = tx2, toY = ty2 });
                gx = nx;
                gy = ny;
            }
        }
    }
}
