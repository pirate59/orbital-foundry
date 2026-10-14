using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Renders every procedural model with the game's lighting into PNGs for review:
    /// -executeMethod OrbitalFoundry.EditorTools.ModelGallery.ExportBatch -galleryOut &lt;dir&gt;
    /// </summary>
    public static class ModelGallery
    {
        const int Size = 512;

        public static void ExportBatch()
        {
            string outDir = "Logs/model-gallery";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-galleryOut") outDir = args[i + 1];
            }
            try
            {
                Export(outDir);
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Tools/Orbital Foundry/Export Model Gallery")]
        public static void ExportMenu() { Export("Logs/model-gallery"); }

        static void Export(string outDir)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("Gallery Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            RenderRig.Build(cam);
            var stage = new GameObject("Stage").transform;
            var catalog = Resources.Load<TextAsset>("catalog");
            var items = new Dictionary<string, Color>();
            if (catalog != null)
            {
                var c = JsonUtility.FromJson<ContentCatalog>(catalog.text);
                foreach (var item in c.items)
                {
                    Color col;
                    if (ColorUtility.TryParseHtmlString(item.colorHex, out col)) items[item.id] = col;
                }
            }

            var shots = new List<KeyValuePair<string, System.Func<Model>>>();
            foreach (var id in new[] { "headquarters", "extractor", "refinery", "purifier", "assembler", "sale_depot", "shipyard", "fort", "fort_site", "storage", "solar_array" })
            {
                string b = id;
                shots.Add(new KeyValuePair<string, System.Func<Model>>("building_" + b, () => ModelLibrary.Building(b, FactionId.Player, stage)));
            }
            shots.Add(new KeyValuePair<string, System.Func<Model>>("building_headquarters_ai", () => ModelLibrary.Building("headquarters", FactionId.Ai, stage)));
            foreach (var id in new[] { "guard", "tank", "artillery", "strike" })
            {
                string cr = id;
                shots.Add(new KeyValuePair<string, System.Func<Model>>("craft_" + cr, () => ModelLibrary.Craft(cr, FactionId.Player, stage)));
                shots.Add(new KeyValuePair<string, System.Func<Model>>("craft_" + cr + "_ai", () => ModelLibrary.Craft(cr, FactionId.Ai, stage)));
            }
            int seed = 1;
            foreach (var kv in items)
            {
                if (!(kv.Key.EndsWith("_ore") || kv.Key == "ore" || kv.Key == "ice" || kv.Key == "sulphur" || kv.Key == "crystal" || kv.Key == "complex_carbon" || kv.Key == "quantum_core")) continue;
                string it = kv.Key;
                var col = kv.Value;
                int s = seed++;
                shots.Add(new KeyValuePair<string, System.Func<Model>>("node_" + it, () => ModelLibrary.Asteroid(it, col, s, s % 3 == 0, stage)));
            }

            // A deck tile under each model for scale.
            var deck = new MeshKit();
            deck.Box(new Vector3(0, -0.04f, 0), new Vector3(2.6f, 0.08f, 2.6f), Palette.Get(Palette.Hex(0x1A4A52), Palette.Finish.Matte));
            var deckGo = deck.Instantiate("Deck", stage, false);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var written = new List<string>();
            const int Thumb = 256, Cols = 4;
            var small = new RenderTexture(Thumb, Thumb, 0, RenderTextureFormat.ARGB32);
            var groups = new[] { "building_", "craft_", "node_" };
            var sheets = new Texture2D[groups.Length];
            var counts = new int[groups.Length];
            for (int g = 0; g < groups.Length; g++)
            {
                int n = 0;
                foreach (var sh in shots) if (sh.Key.StartsWith(groups[g])) n++;
                int rows = (n + Cols - 1) / Cols;
                sheets[g] = new Texture2D(Cols * Thumb, Mathf.Max(1, rows) * Thumb, TextureFormat.RGB24, false);
            }
            cam.transform.position = new Vector3(0, 2, -4);
            cam.transform.LookAt(Vector3.zero);
            cam.Render(); // the first render after setup has no main light yet
            foreach (var shot in shots)
            {
                var model = shot.Value();
                if (model == null) continue;
                bool isCraft = shot.Key.StartsWith("craft_");
                if (isCraft) model.Root.transform.position = new Vector3(0, 0.35f, 0);
                if (shot.Key.StartsWith("node_")) model.Root.transform.position = new Vector3(0, 0.4f, 0);
                var bounds = model.Bounds;
                bounds.Encapsulate(Vector3.zero);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.45f);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                var rot = Quaternion.Euler(30f, isCraft ? 20f : 45f, 0f);
                cam.transform.rotation = rot;
                cam.transform.position = bounds.center - rot * Vector3.forward * dist;
                deckGo.transform.localScale = Vector3.one * Mathf.Max(1f, radius);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                string path = Path.Combine(outDir, shot.Key + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                written.Add(shot.Key);
                for (int g = 0; g < groups.Length; g++)
                {
                    if (!shot.Key.StartsWith(groups[g])) continue;
                    Graphics.Blit(rt, small);
                    RenderTexture.active = small;
                    int i = counts[g]++;
                    int rows = sheets[g].height / Thumb;
                    sheets[g].ReadPixels(new Rect(0, 0, Thumb, Thumb), (i % Cols) * Thumb, (rows - 1 - i / Cols) * Thumb);
                    RenderTexture.active = null;
                }
                Object.DestroyImmediate(model.Root);
            }
            File.WriteAllText(Path.Combine(outDir, "index.txt"), string.Join("\n", written));
            // Comic battle portraits, as the battle scene shows them.
            foreach (var id in new[] { "guard", "tank", "artillery", "strike" })
                foreach (var owner in new[] { FactionId.Player, FactionId.Ai })
                {
                    var portrait = ComicCraft.Portrait(id, owner);
                    if (portrait != null)
                    {
                        var r = portrait.textureRect;
                        var crop = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                        crop.SetPixels(portrait.texture.GetPixels((int)r.x, (int)r.y, (int)r.width, (int)r.height));
                        crop.Apply();
                        File.WriteAllBytes(Path.Combine(outDir, "comic_" + id + "_" + owner.ToString().ToLowerInvariant() + ".png"), crop.EncodeToPNG());
                    }
                    else Debug.LogWarning("No comic portrait for " + id);
                }
            for (int g = 0; g < groups.Length; g++)
            {
                sheets[g].Apply();
                File.WriteAllBytes(Path.Combine(outDir, "sheet_" + groups[g].TrimEnd('_') + ".png"), sheets[g].EncodeToPNG());
            }
            Debug.Log("Model gallery: " + written.Count + " shots in " + outDir);
        }
    }
}
