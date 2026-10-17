using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// One Meshy ship (SHIP="guard", "bomber" or "fortress") from above, the side and behind, beside an arrow along
    /// +Z, the way ships fly, to Logs/ship-&lt;type&gt;-*.png, so its "rotate" in models.json can be checked.
    /// -executeMethod OrbitalFoundry.EditorTools.ShipPreview.RenderBatch
    /// </summary>
    public static class ShipPreview
    {
        const int Size = 700;

        public static void RenderBatch()
        {
            try { Render(System.Environment.GetEnvironmentVariable("SHIP") ?? "guard"); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        public static void Render(string type)
        {
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            var ship = MeshyLibrary.Craft(type, FactionId.Player, Color.white, null, 1f);
            if (ship == null) throw new System.InvalidOperationException("No Meshy model for " + type);
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arrow.transform.position = new Vector3(0.8f, 0.1f, 0f);
            arrow.transform.localScale = new Vector3(0.04f, 0.04f, 0.9f);
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.transform.position = new Vector3(0.8f, 0.1f, 0.5f);
            tip.transform.localScale = Vector3.one * 0.14f;
            var light = new GameObject("Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            var target = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            try
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.15f, 0.16f, 0.22f);
                cam.targetTexture = target;
                var b = ship.Renderers[0].bounds;
                Debug.Log("SHIP " + type + " size " + b.size.ToString("F3"));
                var views = new[] { new Vector3(90f, 0f, 0f), new Vector3(15f, 90f, 0f), new Vector3(20f, 0f, 0f) };
                string[] names = { "top", "side", "behind" };
                for (int i = 0; i < views.Length; i++)
                {
                    cam.transform.rotation = Quaternion.Euler(views[i]);
                    cam.transform.position = new Vector3(0.3f, 0.25f, 0f) - cam.transform.forward * 3f;
                    cam.Render();
                    RenderTexture.active = target;
                    var picture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                    File.WriteAllBytes("Logs/ship-" + type + "-" + names[i] + ".png", picture.EncodeToPNG());
                    Object.DestroyImmediate(picture);
                    RenderTexture.active = null;
                }
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(arrow);
                Object.DestroyImmediate(tip);
                Object.DestroyImmediate(ship.Root);
                MeshyLibrary.Enabled = previous;
            }
        }
    }
}
