using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// The deck art alone, for map_b's cell 7 (the one with a wall of blocked tiles), under the board's lighting: the
    /// whole asteroid, the rubble ridge up close, and the pad's kerb from low down, to Logs/deck-*.png.
    /// -executeMethod OrbitalFoundry.EditorTools.DeckPreview.RenderBatch
    /// </summary>
    public static class DeckPreview
    {
        const int Width = 1600, Height = 900;

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

        [MenuItem("Orbital Foundry/Preview deck")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var catalog = CatalogRepository.LoadContent();
            var map = CatalogRepository.LoadMap("map_b", catalog);
            var def = WorldLookup.GetCellDef(map, 7);
            var deck = BoardArt.Build(new GameObject("World").transform, Vector3.zero, def.gridWidth, def.gridHeight, def.id, def.blockedTiles);
            // Grid lines as the game draws them, so the mounds can be judged against the tiles.
            var grid = new GameObject("Grid").AddComponent<SpriteRenderer>();
            grid.sprite = new SpaceSpriteLibrary().GetGridSprite(def.gridWidth, def.gridHeight);
            grid.color = new Color(0.62f, 0.63f, 0.6f, 0.45f);
            grid.transform.SetPositionAndRotation(Vector3.down * 0.01f, Quaternion.Euler(90f, 0f, 0f));

            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            RenderRig.Build(cam);
            // Local tile (x, y) to deck space; the deck is centred on the origin.
            System.Func<float, float, Vector3> tile = (x, y) => new Vector3(x + 0.5f - def.gridWidth * 0.5f, 0f, y + 0.5f - def.gridHeight * 0.5f);
            var shots = new[]
            {
                new { Name = "deck-overview", Focus = Vector3.zero, Yaw = 30f, Pitch = 45f, Distance = 38f },
                new { Name = "deck-mid", Focus = Vector3.zero, Yaw = 30f, Pitch = 45f, Distance = 20f },
                new { Name = "deck-ridge", Focus = tile(9, 6), Yaw = 35f, Pitch = 38f, Distance = 9f },
                new { Name = "deck-kerb", Focus = tile(4, 0) + Vector3.back * 0.6f, Yaw = 20f, Pitch = 18f, Distance = 5f },
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
            Debug.Log("Deck preview: " + def.blockedTiles.Count + " blocked tiles -> Logs/deck-*.png");
        }
    }
}
