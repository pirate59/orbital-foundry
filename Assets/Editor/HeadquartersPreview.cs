using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// The headquarters in both visual styles on a deck with its 3x3 square outlined, from the game's usual angle and
    /// from low down, to Logs/headquarters.png: Meshy on the left, Light weight on the right.
    /// </summary>
    public static class HeadquartersPreview
    {
        [MenuItem("Orbital Foundry/Preview headquarters")]
        public static void Run()
        {
            const int size = 560;
            var sheet = new Texture2D(size * 2, size * 2, TextureFormat.RGB24, false);
            for (int style = 0; style < 2; style++)
            {
                var root = new GameObject("Scene");
                var deck = GameObject.CreatePrimitive(PrimitiveType.Plane);
                deck.transform.SetParent(root.transform);
                deck.transform.localScale = Vector3.one * 0.7f;
                deck.GetComponent<MeshRenderer>().sharedMaterial = Palette.Get(new Color(0.45f, 0.43f, 0.41f), Palette.Finish.Matte);
                // Tile grid lines, with the 3x3 square in yellow.
                var line = Palette.Flat(new Color(0.6f, 0.62f, 0.6f));
                var edge = Palette.Flat(new Color(1f, 0.85f, 0.2f));
                for (int i = -3; i <= 3; i++)
                {
                    float p = i + 0.5f;
                    bool square = Mathf.Abs(p) == 1.5f;
                    Bar(root, new Vector3(p, 0.004f, 0f), new Vector3(0.02f, 0.002f, 7f), square ? edge : line);
                    Bar(root, new Vector3(0f, 0.004f, p), new Vector3(7f, 0.002f, 0.02f), square ? edge : line);
                }
                Model model;
                if (style == 0)
                {
                    MeshyLibrary.Enabled = true;
                    model = MeshyLibrary.Building("headquarters", FactionId.Player, Palette.Cream, root.transform, 3);
                }
                else
                {
                    MeshyLibrary.Enabled = false;
                    model = ModelLibrary.Building("headquarters", FactionId.Player, root.transform);
                    var s = model.Bounds.size;
                    model.Root.transform.localScale = Vector3.one * (3 * 0.97f / Mathf.Max(s.x, s.z));
                    MeshyLibrary.AddGroundShadow(model, model.Root.transform, new Bounds(Vector3.zero, new Vector3(3f, 0f, 3f) / model.Root.transform.localScale.x), 0.55f);
                }
                model.SetState(Model.Normal);
                for (int view = 0; view < 2; view++)
                {
                    var light = new GameObject("Light").AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.shadows = LightShadows.Soft;
                    light.transform.rotation = Quaternion.Euler(55, -30, 0);
                    var cam = new GameObject("Cam").AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.16f, 0.14f, 0.2f);
                    cam.fieldOfView = 38f;
                    var target = new Vector3(0f, 1.1f, 0f);
                    cam.transform.position = target + Quaternion.Euler(view == 0 ? 42 : 12, 205, 0) * new Vector3(0, 0, -7.2f);
                    cam.transform.LookAt(target);
                    var rt = new RenderTexture(size, size, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.Render();
                    RenderTexture.active = rt;
                    var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    sheet.SetPixels(style * size, (1 - view) * size, size, size, shot.GetPixels());
                    RenderTexture.active = null;
                    Object.DestroyImmediate(cam.gameObject);
                    Object.DestroyImmediate(light.gameObject);
                    Object.DestroyImmediate(rt);
                }
                Object.DestroyImmediate(root);
            }
            MeshyLibrary.Enabled = false;
            File.WriteAllBytes("Logs/headquarters.png", sheet.EncodeToPNG());
            Debug.Log("HQ PREVIEW Logs/headquarters.png");
        }

        static void Bar(GameObject root, Vector3 at, Vector3 size, Material m)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.transform.SetParent(root.transform);
            bar.transform.position = at;
            bar.transform.localScale = size;
            bar.GetComponent<MeshRenderer>().sharedMaterial = m;
        }
    }
}
