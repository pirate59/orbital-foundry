using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// The shipyard on its catalog footprint through the real WorldView, with belts arriving at both open sides of
    /// its anchor tile and the footprint highlighted, to Logs/shipyard-footprint-*.png:
    /// -executeMethod OrbitalFoundry.EditorTools.ShipyardFootprintPreview.RenderBatch
    /// </summary>
    public static class ShipyardFootprintPreview
    {
        const int Width = 1400, Height = 900;

        public static void RenderBatch()
        {
            try
            {
                Render();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Orbital Foundry/Preview shipyard footprint")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = System.Environment.GetEnvironmentVariable("SHIPYARD_STYLE") != "light";
            try
            {
                var catalog = CatalogRepository.LoadContent();
                var map = CatalogRepository.LoadMap("map_a", catalog);
                var loop = new GameLoop();
                loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 1234), new SaveService(Path.Combine(Path.GetTempPath(), "OrbitalFoundryShipyard")));
                var w = loop.World;
                WorldLookup.GetFaction(w, FactionId.Player).currency = 100000;

                // Anchor (12,6) facing east: the yard covers x 12-14, y 6-7. Belts arrive from the west and the south.
                const int ax = 12, ay = 6;
                var placed = loop.Submit(new GameCommand { kind = CommandKind.PlaceBuilding, actor = FactionId.Player, contentId = "shipyard", fromCellId = 0, fromX = ax, fromY = ay, rotation = 0 });
                if (!placed.success) throw new System.InvalidOperationException("Shipyard: " + placed.code + " " + placed.message);
                Line(loop, ax - 5, ay, ax, ay);
                Line(loop, ax, ay - 4, ax, ay);
                Line(loop, ax + 2, ay - 4, ax + 2, ay); // into the far end: any tile of the yard takes deliveries
                for (int i = 0; i < 200; i++) loop.TickNormal();

                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.fieldOfView = 38f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 800f;
                RenderRig.Build(cam);
                var view = new WorldView(new SpaceSpriteLibrary(), new GameObject("World").transform);
                view.LineOf = (ws, id) => loop.Services.Belts.LineOf(ws, id);
                view.BuildMap(map, w, cam);
                var ui = new UiState();

                var def = CatalogRepository.GetBuilding(catalog, "shipyard");
                var centre = WorldView.TileCenter(map, 0, ax, ay);
                var extras = GridRules.ShipyardExtraTiles(def, ax, ay, 0);
                foreach (var t in extras) centre += WorldView.TileCenter(map, 0, t[0], t[1]);
                centre /= extras.Count + 1;

                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                var shots = new[]
                {
                    new { Name = "game", Yaw = 30f, Pitch = 50f, Distance = 9f, Outline = false, Shift = Vector3.zero },
                    new { Name = "low", Yaw = 215f, Pitch = 22f, Distance = 8f, Outline = false, Shift = Vector3.zero },
                    new { Name = "belts", Yaw = 40f, Pitch = 14f, Distance = 6.5f, Outline = false, Shift = Vector3.zero },
                    new { Name = "corner", Yaw = 300f, Pitch = 32f, Distance = 3.2f, Outline = false, Shift = new Vector3(1.3f, 0.3f, -0.8f) },
                    new { Name = "top", Yaw = 0f, Pitch = 89f, Distance = 9f, Outline = true, Shift = Vector3.zero },
                };
                foreach (var shot in shots)
                {
                    // The footprint outline only on the top-down shot, so the model reads clearly in the others.
                    if (shot.Outline) view.SetHover(true, 0, ax, ay, new Color(0.3f, 1f, 0.5f, 0.3f), extras);
                    else view.SetHover(false, 0, ax, ay, Color.clear);
                    for (int f = 0; f < 3; f++) view.Refresh(w, catalog, loop.Revision, loop.Services.Pull, ui, 0.5f);
                    var rot = Quaternion.Euler(shot.Pitch, shot.Yaw, 0f);
                    cam.transform.rotation = rot;
                    cam.transform.position = centre + shot.Shift - rot * Vector3.forward * shot.Distance;
                    cam.Render();
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes("Logs/shipyard-footprint-" + shot.Name + ".png", tex.EncodeToPNG());
                }
                Debug.Log("SHIPYARD FOOTPRINT " + def.width + "x" + def.height + ", " + w.belts.Count + " belts");
            }
            finally
            {
                MeshyLibrary.Enabled = previous;
            }
        }

        /// <summary>A straight belt line between two tiles of the HQ cell, ending in the second.</summary>
        static void Line(GameLoop loop, int x1, int y1, int x2, int y2)
        {
            int x = x1, y = y1;
            while (x != x2 || y != y2)
            {
                int nx = x + System.Math.Sign(x2 - x), ny = nx != x ? y : y + System.Math.Sign(y2 - y);
                var r = loop.Submit(new GameCommand { kind = CommandKind.PlaceBelt, actor = FactionId.Player, fromCellId = 0, fromX = x, fromY = y, toCellId = 0, toX = nx, toY = ny });
                if (!r.success) Debug.Log("Shipyard preview: belt " + x + "," + y + " -> " + nx + "," + ny + " skipped (" + r.code + ")");
                x = nx;
                y = ny;
            }
        }
    }
}
