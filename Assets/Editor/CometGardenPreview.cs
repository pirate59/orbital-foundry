using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    public static class CometGardenPreview
    {
        [MenuItem("Tools/Orbital Foundry/Export Comet Garden Sprites")]
        public static void Export()
        {
            string output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "docs/comet-garden-sprites");
            Directory.CreateDirectory(output);
            foreach (string id in new[] { "headquarters", "extractor", "refinery", "assembler", "sale_depot", "shipyard", "storage", "fort" })
                Save(CometGardenArt.Station(id, FactionId.Player), Path.Combine(output, id + ".png"));
            foreach (string id in new[] { "tank", "artillery", "guard", "strike" })
                Save(CometGardenArt.Craft(id, FactionId.Player), Path.Combine(output, id + ".png"));
            Save(CometGardenArt.Asteroid("ore", 2, true), Path.Combine(output, "ore.png"));
            Save(CometGardenArt.Asteroid("crystal", 3, true), Path.Combine(output, "crystal.png"));
            Debug.Log("Comet Garden sprites exported to " + output);
        }

        public static void ExportBatch()
        {
            Export();
            EditorApplication.Exit(0);
        }

        static void Save(Sprite sprite, string path)
        {
            var source = sprite.texture;
            var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                try
                {
                    readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    readable.Apply();
                    File.WriteAllBytes(path, readable.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(readable); }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(sprite);
            }
        }
    }
}
