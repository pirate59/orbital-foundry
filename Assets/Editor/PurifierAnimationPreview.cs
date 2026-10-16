using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Repeatable stills and model-space markers for the purifier animation review.</summary>
    public static class PurifierAnimationPreview
    {
        const string Folder = "docs/meshy-purifier";

        [MenuItem("Orbital Foundry/Preview purifier animation")]
        public static void Render()
        {
            Directory.CreateDirectory(Folder);
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), "purifier");
            Color colour;
            if (def == null || !ColorUtility.TryParseHtmlString(def.colorHex, out colour)) colour = Color.white;
            var model = MeshyLibrary.Building("purifier", FactionId.Player, colour, null, 1);
            if (model == null) throw new System.InvalidOperationException("Meshy purifier did not load");
            try
            {
                model.SetState(Model.Normal);
                Shot(model, "purifier-rest.png", 0f, 210f);
                model.Animate(4f);
                Shot(model, "purifier-animated.png", 4f, 210f);
                Shot(model, "purifier-animated-reverse.png", 4f, 30f);

                var marks = new GameObject("Animation markers");
                try
                {
                    float width = model.Renderers[0].bounds.size.x;
                    float height = model.Height;
                    var b = model.Renderers[0].bounds;
                    // Red: fan sweep. Cyan: both bubble and glow columns. Gold: circling tower bands.
                    Mark(marks.transform, new Vector3(0f, height * 0.94f, 0f), width * 0.225f, Color.red);
                    var tanks = new[] { new Vector2(0.26f, 0.26f), new Vector2(0.74f, 0.74f) };
                    foreach (var tank in tanks)
                    {
                        float x = b.min.x + b.size.x * tank.x, z = b.min.z + b.size.z * tank.y;
                        Mark(marks.transform, new Vector3(x, height * 0.47f, z), width * 0.105f, Color.cyan);
                        Mark(marks.transform, new Vector3(x, height * 0.74f, z), width * 0.105f, Color.cyan);
                    }
                    foreach (float h in new[] { 0.47f, 0.53f, 0.59f })
                        Mark(marks.transform, new Vector3(0f, height * h, 0f), width * 0.22f, new Color(1f, 0.74f, 0.12f));
                    Shot(model, "purifier-marked.png", 4f, 210f);
                    Shot(model, "purifier-marked-reverse.png", 4f, 30f);
                    Shot(model, "purifier-marked-top.png", 4f, 180f, 68f);
                }
                finally { Object.DestroyImmediate(marks); }
            }
            finally
            {
                Object.DestroyImmediate(model.Root);
                MeshyLibrary.Enabled = previous;
            }
        }

        static void Mark(Transform parent, Vector3 at, float radius, Color colour)
        {
            var kit = new MeshKit();
            kit.Torus(Vector3.zero, radius, radius * 0.035f, 48, 6, Palette.Flat(colour));
            var marker = kit.Instantiate("Animation zone", parent, false);
            marker.transform.localPosition = at;
        }

        static void Shot(Model model, string name, float time, float yaw, float pitch = 25f)
        {
            var light = new GameObject("Preview light").AddComponent<Light>();
            var camera = new GameObject("Preview camera").AddComponent<Camera>();
            var target = new RenderTexture(1000, 1000, 24);
            try
            {
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.15f, 0.17f, 0.23f);
                camera.transform.position = new Vector3(0f, 0.5f, 0f) + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -1.47f);
                camera.transform.LookAt(new Vector3(0f, 0.5f, 0f));
                camera.targetTexture = target;
                camera.Render();
                camera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
                File.WriteAllBytes(Path.Combine(Folder, name), picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
                Debug.Log("PURIFIER PREVIEW " + name + " at " + time + "s");
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(light.gameObject);
            }
        }
    }
}
