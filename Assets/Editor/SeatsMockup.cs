using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Four AI seats on map_d (Four Corners), played for fifteen minutes and rendered through the real WorldView, for
    /// checking how four seats look: their colours on platform lights, craft and buildings. Writes Logs/seats-*.png.
    /// -executeMethod OrbitalFoundry.EditorTools.SeatsMockup.ExportBatch
    /// </summary>
    public static class SeatsMockup
    {
        const int Width = 1600, Height = 900;

        public static void ExportBatch()
        {
            try
            {
                Export();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Orbital Foundry/Preview four seats")]
        public static void Export()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = CatalogRepository.LoadContent();
            catalog.rules.aiExpands = catalog.rules.aiRaids = catalog.rules.aiDefends = true;
            var map = CatalogRepository.LoadMap("map_d", catalog);
            var seats = GameFactory.DefaultSeats(map);
            foreach (var s in seats) s.ai = true;
            var loop = new GameLoop();
            loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 4321, seats), null);
            var w = loop.World;
            for (int i = 0; i < 9000 && w.mode != MatchMode.Ended; i++) loop.TickNormal();

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 800f;
            RenderRig.Build(cam);
            var view = new WorldView(new SpaceSpriteLibrary(), new GameObject("World").transform);
            view.LineOf = (ws, id) => loop.Services.Belts.LineOf(ws, id);
            view.BuildMap(map, w, cam);
            var ui = new UiState();
            for (int f = 0; f < 3; f++) view.Refresh(w, catalog, loop.Revision, loop.Services.Pull, ui, 0.5f);

            var bounds = WorldView.MapBounds(map);
            var shots = new[]
            {
                new { Name = "seats-overview", Focus = bounds.center, Yaw = 30f, Pitch = 55f, Distance = 140f },
                new { Name = "seats-seat3", Focus = WorldView.CellCenter(map, 3), Yaw = 30f, Pitch = 45f, Distance = 40f },
                new { Name = "seats-seat4", Focus = WorldView.CellCenter(map, 12), Yaw = 30f, Pitch = 45f, Distance = 40f },
            };
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            Directory.CreateDirectory("Logs");
            cam.Render(); // warm-up
            foreach (var shot in shots)
            {
                var rot = Quaternion.Euler(shot.Pitch, shot.Yaw, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = shot.Focus - rot * Vector3.forward * shot.Distance;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes("Logs/" + shot.Name + ".png", tex.EncodeToPNG());
            }
            var sb = new System.Text.StringBuilder("Four seats at tick " + w.tick + ":");
            foreach (var f in w.factions)
                sb.Append(' ').Append(f.id).Append(" cells ").Append(w.cells.FindAll(c => c.owner == f.id).Count)
                  .Append(" ships ").Append(w.crafts.FindAll(k => k.owner == f.id && WorldLookup.IsCraftLive(k)).Count).Append(f.suppressed ? " (out)" : "").Append(';');
            Debug.Log(sb.ToString());
        }
    }
}
