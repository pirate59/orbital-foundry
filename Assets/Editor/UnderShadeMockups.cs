using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Mock-ups of ways to shade the space under a raised building, rendered side by side to Logs/under-shade.png:
    /// as now, a soft ground shadow, layered shade that deepens toward the middle, and the layers plus a dark skirt.
    /// Each shows the Meshy assembler over a belt carrying ore.
    /// </summary>
    public static class UnderShadeMockups
    {
        const float Under = 0.36f;          // underside of the assembler's deck above the ground
        const float HalfX = 0.42f, HalfZ = 0.32f;

        [MenuItem("Orbital Foundry/Under-building shade mock-ups")]
        public static void Run()
        {
            const int size = 460;
            var sheet = new Texture2D(size * 4, size, TextureFormat.RGB24, false);
            var dust = Resources.Load<Material>("Materials/Dust");
            for (int option = 0; option < 4; option++)
            {
                var root = new GameObject("Scene");
                MeshyLibrary.Enabled = true;
                var model = MeshyLibrary.Building("assembler", FactionId.Player, Color.white, root.transform, 1);
                // Ground deck and a belt running under the building with ore on it.
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(root.transform);
                ground.transform.localScale = Vector3.one * 0.4f;
                ground.GetComponent<MeshRenderer>().sharedMaterial = Palette.Get(new Color(0.42f, 0.4f, 0.38f), Palette.Finish.Matte);
                var beltMats = ModelLibrary.ConveyorMaterials(FactionId.Player, 0, true, false);
                for (int i = -2; i <= 2; i++)
                {
                    var belt = new GameObject("Belt");
                    belt.transform.SetParent(root.transform);
                    belt.transform.SetPositionAndRotation(new Vector3(i, 0f, 0f), Quaternion.LookRotation(Vector3.right));
                    belt.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ConveyorMesh();
                    belt.AddComponent<MeshRenderer>().sharedMaterials = beltMats;
                }
                for (int i = 0; i < 9; i++)
                {
                    var ore = ModelLibrary.Cargo("ore", new Color(0.62f, 0.55f, 0.5f), root.transform, 1);
                    ore.transform.position = new Vector3(-1.2f + i * 0.3f, ModelLibrary.BeltSurface, 0f);
                }
                if (option >= 1) GroundShadow(root.transform, dust);
                if (option >= 2) ShadeLayers(root.transform, dust, option == 3);

                var light = new GameObject("Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(55, -30, 0);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.18f, 0.24f);
                cam.fieldOfView = 40f;
                cam.transform.position = new Vector3(-1.05f, 1.05f, -1.55f);
                cam.transform.LookAt(new Vector3(0f, 0.22f, 0f));
                var rt = new RenderTexture(size, size, 24);
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                sheet.SetPixels(option * size, 0, size, size, shot.GetPixels());
                RenderTexture.active = null;
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(rt);
            }
            MeshyLibrary.Enabled = false;
            File.WriteAllBytes("Logs/under-shade.png", sheet.EncodeToPNG());
            Debug.Log("UNDER SHADE Logs/under-shade.png");
        }

        /// <summary>
        /// The game's own shading and belt trimming: the refinery and the assembler (both elevated) with belts running
        /// in from three sides to their centres and ore on them, to Logs/under-shade-game.png.
        /// </summary>
        [MenuItem("Orbital Foundry/Under-building shade in game")]
        public static void Game()
        {
            const int size = 512;
            var sheet = new Texture2D(size * 2, size, TextureFormat.RGB24, false);
            var ids = new[] { "refinery", "assembler" }; // left angled, right low and front-on
            for (int panel = 0; panel < 2; panel++)
            {
                var root = new GameObject("Scene");
                MeshyLibrary.Enabled = true;
                var model = MeshyLibrary.Building(ids[panel], FactionId.Player, new Color(0.77f, 0.56f, 0.39f), root.transform, 1);
                model.SetState(Model.Normal);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(root.transform);
                ground.transform.localScale = Vector3.one * 0.5f;
                ground.GetComponent<MeshRenderer>().sharedMaterial = Palette.Get(new Color(0.42f, 0.4f, 0.38f), Palette.Finish.Matte);
                var beltMats = ModelLibrary.ConveyorMaterials(FactionId.Player, 0, true, false);
                // Three lines into the building's tile (at the origin), each ending there with its rails trimmed.
                foreach (var dir in new[] { Vector3.left, Vector3.right, Vector3.back })
                {
                    for (int i = 2; i >= 1; i--)
                    {
                        var from = dir * i;
                        var to = dir * (i - 1);
                        var belt = new GameObject("Belt");
                        belt.transform.SetParent(root.transform);
                        belt.transform.SetPositionAndRotation((from + to) * 0.5f, Quaternion.LookRotation(to - from));
                        belt.AddComponent<MeshFilter>().sharedMesh = ModelLibrary.ConveyorMesh(false, i == 1);
                        belt.AddComponent<MeshRenderer>().sharedMaterials = beltMats;
                    }
                    for (int k = 0; k < 5; k++)
                    {
                        var ore = ModelLibrary.Cargo("ore", new Color(0.62f, 0.55f, 0.5f), root.transform, 1);
                        ore.transform.position = dir * (0.15f + k * 0.35f) + Vector3.up * ModelLibrary.BeltSurface;
                    }
                }
                var light = new GameObject("Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(55, -30, 0);
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.18f, 0.24f);
                cam.fieldOfView = 40f;
                cam.transform.position = panel == 0 ? new Vector3(-1.1f, 1.0f, -1.6f) : new Vector3(0.1f, 0.55f, -1.7f);
                cam.transform.LookAt(new Vector3(0f, 0.2f, 0f));
                var rt = new RenderTexture(size, size, 24);
                cam.targetTexture = rt;
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                sheet.SetPixels(panel * size, 0, size, size, shot.GetPixels());
                RenderTexture.active = null;
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(rt);
            }
            MeshyLibrary.Enabled = false;
            File.WriteAllBytes("Logs/under-shade-game.png", sheet.EncodeToPNG());
        }

        /// <summary>A rounded-rectangle gradient: clear at the edge, <paramref name="core"/> opaque inside, fading over <paramref name="soft"/> of the half-size.</summary>
        static Texture2D Gradient(float core, float soft)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / n * 2f - 1f), v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    float edge = Mathf.Max(u, v);
                    float a = Mathf.SmoothStep(0f, 1f, (1f - edge) / soft) * core;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        static Material Shade(Material dust, Texture tex, float alpha)
        {
            var m = new Material(dust);
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.04f, alpha));
            return m;
        }

        static void Quad(Transform parent, Vector3 centre, Vector3 size, Quaternion rotation, Material m)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent);
            q.transform.SetPositionAndRotation(centre, rotation);
            q.transform.localScale = size;
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            q.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>B: a soft dark patch on the ground under the footprint (an ambient-occlusion shadow).</summary>
        static void GroundShadow(Transform parent, Material dust)
        {
            var m = Shade(dust, Gradient(0.92f, 0.5f), 1f);
            // Just above the owner plinth (0.05 up), which would otherwise hide it.
            Quad(parent, new Vector3(0, 0.056f, 0), new Vector3(HalfX * 2.4f, HalfZ * 2.4f, 1), Quaternion.Euler(90, 0, 0), m);
            // Also on top of the belt bed, so goods and belt darken as they pass under.
            Quad(parent, new Vector3(0, ModelLibrary.BeltSurface + 0.002f, 0), new Vector3(HalfX * 2.3f, 0.36f, 1), Quaternion.Euler(90, 0, 0), m);
        }

        /// <summary>
        /// C: stacked see-through shade boxes filling the space under the building, each smaller than the last, so a
        /// look further in passes through more of them and gets darker: things fade out going in and back on coming out.
        /// D (<paramref name="topDark"/>): the same, with each layer also darkening toward the underside.
        /// </summary>
        static void ShadeLayers(Transform parent, Material dust, bool topDark)
        {
            Texture tex = Gradient(1f, 0.3f);
            if (topDark)
            {
                // Soft at the sides, and rising from faint at the ground to full under the deck.
                var t = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        float u = Mathf.Abs((x + 0.5f) / 64f * 2f - 1f);
                        float side = Mathf.SmoothStep(0f, 1f, (1f - u) / 0.3f);
                        float rise = Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0f, 1f, y / 63f));
                        t.SetPixel(x, y, new Color(1, 1, 1, side * rise));
                    }
                t.Apply();
                tex = t;
            }
            var m = Shade(dust, tex, 0.3f);
            for (int layer = 0; layer < 5; layer++)
            {
                float shrink = 1f - layer * 0.16f, hx = HalfX * shrink, hz = HalfZ * shrink, h = Under - 0.01f;
                // Four walls of the layer: soft-edged dark panels from the ground to the underside.
                Quad(parent, new Vector3(0, h / 2, -hz), new Vector3(hx * 2, h, 1), Quaternion.identity, m);
                Quad(parent, new Vector3(0, h / 2, hz), new Vector3(hx * 2, h, 1), Quaternion.Euler(0, 180, 0), m);
                Quad(parent, new Vector3(-hx, h / 2, 0), new Vector3(hz * 2, h, 1), Quaternion.Euler(0, 90, 0), m);
                Quad(parent, new Vector3(hx, h / 2, 0), new Vector3(hz * 2, h, 1), Quaternion.Euler(0, -90, 0), m);
            }
        }
    }
}
