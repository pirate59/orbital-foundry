using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// The three ship types as fleets through the real WorldView: a Mobile Guard (ten ships), a Heavy Bomber (four),
    /// a Flying Fortress (one) and a Heavy Bomber down to 4 HP (two ships), on the home platform, to
    /// Logs/fleets-*.png. -executeMethod OrbitalFoundry.EditorTools.FleetPreview.RenderBatch
    /// </summary>
    public static class FleetPreview
    {
        const int Width = 1400, Height = 900;

        public static void RenderBatch()
        {
            try { Render(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Orbital Foundry/Preview fleets")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = System.Environment.GetEnvironmentVariable("FLEET_STYLE") != "light";
            try
            {
                var catalog = CatalogRepository.LoadContent();
                var map = CatalogRepository.LoadMap("map_a", catalog);
                var loop = new GameLoop();
                loop.Initialize(catalog, map, GameFactory.CreateNew(catalog, map, 1234), new SaveService(Path.Combine(Path.GetTempPath(), "OrbitalFoundryFleets")));
                var w = loop.World;
                w.crafts.RemoveAll(k => k.cellId == 0);
                foreach (var type in new[] { "guard", "bomber", "fortress", "lancer", "bomber" })
                    EconomyService.SpawnCraft(w, CatalogRepository.GetCraft(catalog, type), FactionId.Player, 0);
                w.crafts[w.crafts.Count - 1].currentHealth = 4; // a damaged bomber: two ships left

                var cam = new GameObject("Camera").AddComponent<Camera>();
                cam.fieldOfView = 38f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 800f;
                RenderRig.Build(cam);
                var view = new WorldView(new SpaceSpriteLibrary(), new GameObject("World").transform);
                view.LineOf = (ws, id) => loop.Services.Belts.LineOf(ws, id);
                view.BuildMap(map, w, cam);
                var ui = new UiState();
                var centre = WorldView.CellCenter(map, 0);
                var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                var shots = new[]
                {
                    new { Name = "game", Yaw = 30f, Pitch = 48f, Distance = 16f },
                    new { Name = "low", Yaw = 20f, Pitch = 20f, Distance = 12f },
                };
                foreach (var shot in shots)
                {
                    for (int f = 0; f < 3; f++) view.Refresh(w, catalog, loop.Revision, loop.Services.Pull, ui, 0.5f);
                    var rot = Quaternion.Euler(shot.Pitch, shot.Yaw, 0f);
                    cam.transform.rotation = rot;
                    cam.transform.position = centre - rot * Vector3.forward * shot.Distance;
                    cam.Render();
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    File.WriteAllBytes("Logs/fleets-" + shot.Name + ".png", tex.EncodeToPNG());
                }
                Debug.Log("FLEETS " + w.crafts.Count + " craft");
            }
            finally
            {
                MeshyLibrary.Enabled = previous;
            }
        }
    }
}
