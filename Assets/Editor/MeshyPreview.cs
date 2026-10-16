using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Renders each Meshy model as the game loads it (its file's root rotation included), turned each of the six ways it could
    /// stand, side by side in Logs/meshy-preview-&lt;id&gt;.png, so the right orientation.json turn can be picked.
    /// Panels, left to right: none, X+90, X-90, X 180, Z+90, Z-90.
    /// </summary>
    public static class MeshyPreview
    {
        static readonly Vector3[] Turns =
        {
            Vector3.zero, new Vector3(90, 0, 0), new Vector3(-90, 0, 0), new Vector3(180, 0, 0), new Vector3(0, 0, 90), new Vector3(0, 0, -90)
        };

        /// <summary>
        /// Renders each Meshy building as the game shows it, from two sides, with any moving part tinted red, to
        /// Logs/meshy-building-&lt;id&gt;.png, for tuning the moving part in models.json.
        /// </summary>
        [MenuItem("Orbital Foundry/Preview Meshy buildings")]
        public static void Buildings()
        {
            const int size = 512;
            bool was = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Meshy/Buildings" }))
                {
                    var id = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                    Color typeColour;
                    var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), id);
                    if (def == null || !ColorUtility.TryParseHtmlString(def.colorHex, out typeColour)) typeColour = Color.white;
                    var model = MeshyLibrary.Building(id, FactionId.Player, typeColour, null, def != null ? Mathf.Max(def.width, def.footprint) : 1);
                    if (model == null) continue;
                    // Swinging parts are tinted red too, to check what their boxes caught.
                    foreach (Transform child in model.Root.transform.Find("Body"))
                    {
                        if (child.name != "Swing") continue;
                        var sr = child.GetComponent<MeshRenderer>();
                        var tint = new Material[sr.sharedMaterials.Length];
                        for (int i = 0; i < tint.Length; i++) tint[i] = Palette.Get(Color.red);
                        sr.sharedMaterials = tint;
                    }
                    // Loader pieces: upper arm red, forearm green, claw blue, fingers yellow (only when MESHY_TINT is set).
                    foreach (var t in System.Environment.GetEnvironmentVariable("MESHY_TINT") != null ? model.Root.GetComponentsInChildren<Transform>() : new Transform[0])
                    {
                        var tint = t.name == "Shoulder" || t.name == "WeldShoulder" ? Color.red : t.name == "Elbow" || t.name == "WeldElbow" ? Color.green
                            : t.name == "Claw" || t.name == "Press" || t.name == "Spinner" ? Color.blue
                            : t.name == "RadarTilt" ? Color.magenta : t.name == "Finger" ? Color.yellow : Color.clear;
                        var lr = t.GetComponent<MeshRenderer>();
                        if (tint == Color.clear || lr == null) continue;
                        var paints = new Material[lr.sharedMaterials.Length];
                        for (int i = 0; i < paints.Length; i++) paints[i] = Palette.Get(tint);
                        lr.sharedMaterials = paints;
                    }
                    var part = model.Root.transform.Find("Body/Part");
                    if (part != null)
                    {
                        var pm = part.GetComponent<MeshFilter>().sharedMesh;
                        long tris = 0;
                        for (int s = 0; s < pm.subMeshCount; s++) tris += pm.GetIndexCount(s) / 3;
                        Debug.Log("MESHY PART " + id + ": " + tris + " triangles, bounds " + pm.bounds + ", at " + part.localPosition);
                        var r = part.GetComponent<MeshRenderer>();
                        var red = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < red.Length; i++) red[i] = Palette.Get(Color.red);
                        r.sharedMaterials = red;
                    }
                    var sheet = new Texture2D(size * 2, size * 2, TextureFormat.RGB24, false);
                    for (int view = 0; view < 4; view++)
                    {
                        // The bottom row is the model at rest; the top row is partway through its working cycle.
                        if (view == 2)
                        {
                            // MESHY_TIME="seconds" picks the moment in the working cycle the top row shows (default 3.8).
                            float at = 3.8f;
                            var time = System.Environment.GetEnvironmentVariable("MESHY_TIME");
                            if (!string.IsNullOrEmpty(time)) float.TryParse(time, out at);
                            foreach (var motion in model.Motions) motion(at);
                        }
                        var light = new GameObject("Light").AddComponent<Light>();
                        light.type = LightType.Directional;
                        light.transform.rotation = Quaternion.Euler(50, -30, 0);
                        var cam = new GameObject("Cam").AddComponent<Camera>();
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0.25f, 0.22f, 0.3f);
                        var target = new Vector3(0, 0.5f, 0);
                        float distance = 1.5f;
                        // MESHY_FOCUS="x,y,z,distance" (model space) frames a close-up instead.
                        var focus = System.Environment.GetEnvironmentVariable("MESHY_FOCUS");
                        if (!string.IsNullOrEmpty(focus))
                        {
                            var f = System.Array.ConvertAll(focus.Split(','), float.Parse);
                            target = new Vector3(f[0], f[1], f[2]);
                            distance = f[3];
                        }
                        cam.transform.position = target + Quaternion.Euler(view % 2 == 0 ? 35 : 5, view % 2 == 0 ? 200 : 180, 0) * new Vector3(0, 0, -distance);
                        cam.transform.LookAt(target);
                        var rt = new RenderTexture(size, size, 24);
                        cam.targetTexture = rt;
                        cam.Render(); // the first render after building meshes can be incomplete; the second is kept
                        cam.Render();
                        RenderTexture.active = rt;
                        var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                        shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        sheet.SetPixels((view % 2) * size, (view / 2) * size, size, size, shot.GetPixels());
                        RenderTexture.active = null;
                        Object.DestroyImmediate(cam.gameObject);
                        Object.DestroyImmediate(light.gameObject);
                        Object.DestroyImmediate(rt);
                    }
                    Object.DestroyImmediate(model.Root);
                    File.WriteAllBytes("Logs/meshy-building-" + id + ".png", sheet.EncodeToPNG());
                    Debug.Log("MESHY BUILDING Logs/meshy-building-" + id + ".png");
                }
            }
            finally { MeshyLibrary.Enabled = was; }
        }

        /// <summary>
        /// Orthographic plan (from above), front (from -Z) and side (from +X) views of each Meshy building as the game
        /// fits it, each framed to the model's bounds with grid lines every tenth, to Logs/meshy-blueprint-&lt;id&gt;.png.
        /// Read a point as fractions of the bounds (0 at the left/bottom of each view) for models.json.
        /// </summary>
        [MenuItem("Orbital Foundry/Meshy blueprints")]
        public static void Blueprints()
        {
            const int size = 512;
            bool was = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Meshy/Buildings" }))
                {
                    var id = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                    Color typeColour;
                    var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), id);
                    if (def == null || !ColorUtility.TryParseHtmlString(def.colorHex, out typeColour)) typeColour = Color.white;
                    var model = MeshyLibrary.Building(id, FactionId.Player, typeColour, null, def != null ? Mathf.Max(def.width, def.footprint) : 1);
                    if (model == null) continue;
                    var body = model.Root.transform.Find("Body");
                    var b = body.GetComponent<MeshRenderer>().bounds;
                    var sheet = new Texture2D(size * 3, size, TextureFormat.RGB24, false);
                    // plan: look down -Y, x right, z up; front: look +Z, x right, y up; side: look -X, z right, y up
                    var dirs = new[] { Vector3.down, Vector3.forward, Vector3.left };
                    var ups = new[] { Vector3.forward, Vector3.up, Vector3.up };
                    for (int view = 0; view < 3; view++)
                    {
                        var light = new GameObject("Light").AddComponent<Light>();
                        light.type = LightType.Directional;
                        light.transform.rotation = Quaternion.Euler(50, -30, 0);
                        var cam = new GameObject("Cam").AddComponent<Camera>();
                        cam.orthographic = true;
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0.2f, 0.18f, 0.25f);
                        float extent = b.extents.magnitude * 2f;
                        cam.transform.position = b.center - dirs[view] * extent;
                        cam.transform.rotation = Quaternion.LookRotation(dirs[view], ups[view]);
                        var right = Vector3.Cross(ups[view], dirs[view]);
                        float w = Mathf.Abs(Vector3.Dot(b.size, right)), h = Mathf.Abs(Vector3.Dot(b.size, ups[view]));
                        cam.orthographicSize = Mathf.Max(w, h) * 0.5f;
                        cam.nearClipPlane = 0.01f;
                        cam.farClipPlane = extent * 3f;
                        var rt = new RenderTexture(size, size, 24);
                        cam.targetTexture = rt;
                        cam.Render();
                        cam.Render();
                        RenderTexture.active = rt;
                        var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                        shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                        // Grid every tenth of the bounds; the view is square, so the bounds sit centred in it.
                        float span = Mathf.Max(w, h);
                        float x0 = size * 0.5f * (1f - w / span), y0 = size * 0.5f * (1f - h / span);
                        for (int k = 0; k <= 10; k++)
                        {
                            int gx = Mathf.Clamp((int)(x0 + k / 10f * size * w / span), 0, size - 1);
                            int gy = Mathf.Clamp((int)(y0 + k / 10f * size * h / span), 0, size - 1);
                            var c = k == 0 || k == 10 ? Color.yellow : k == 5 ? new Color(1f, 0.5f, 0.2f) : new Color(1f, 1f, 1f, 0.5f);
                            for (int i = 0; i < size; i++) { shot.SetPixel(gx, i, c); shot.SetPixel(i, gy, c); }
                        }
                        // In the side view, looking along -X, +Z points left; flip so z runs left to right like the others.
                        var pixels = shot.GetPixels();
                        if (view == 2)
                            for (int y = 0; y < size; y++)
                                for (int x = 0; x < size / 2; x++)
                                {
                                    var t = pixels[y * size + x];
                                    pixels[y * size + x] = pixels[y * size + size - 1 - x];
                                    pixels[y * size + size - 1 - x] = t;
                                }
                        sheet.SetPixels(view * size, 0, size, size, pixels);
                        RenderTexture.active = null;
                        Object.DestroyImmediate(cam.gameObject);
                        Object.DestroyImmediate(light.gameObject);
                        Object.DestroyImmediate(rt);
                    }
                    Debug.Log("MESHY BLUEPRINT " + id + " body bounds " + b);
                    Object.DestroyImmediate(model.Root);
                    File.WriteAllBytes("Logs/meshy-blueprint-" + id + ".png", sheet.EncodeToPNG());
                }
            }
            finally { MeshyLibrary.Enabled = was; }
        }

        [MenuItem("Orbital Foundry/Preview Meshy orientations")]
        public static void Run()
        {
            const int size = 384;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Meshy" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var sheet = new Texture2D(size * Turns.Length, size, TextureFormat.RGB24, false);
                for (int i = 0; i < Turns.Length; i++)
                {
                    var holder = new GameObject("Holder");
                    var model = Object.Instantiate(prefab, holder.transform);
                    model.transform.localPosition = Vector3.zero;
                    holder.transform.rotation = Quaternion.Euler(Turns[i]);
                    var bounds = new Bounds(holder.transform.position, Vector3.zero);
                    bool first = true;
                    foreach (var r in holder.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.8f, 0.85f, 0.9f) };
                        if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                    }
                    // Put the model on the ground at the origin, then look at it from the game's usual angle.
                    holder.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                    float extent = bounds.size.magnitude;
                    var light = new GameObject("Light").AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.transform.rotation = Quaternion.Euler(50, -30, 0);
                    var cam = new GameObject("Cam").AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.25f, 0.22f, 0.3f);
                    cam.transform.position = new Vector3(0, bounds.size.y * 0.5f, 0) + Quaternion.Euler(30, 200, 0) * new Vector3(0, 0, -extent * 1.15f);
                    cam.transform.LookAt(new Vector3(0, bounds.size.y * 0.5f, 0));
                    cam.nearClipPlane = extent * 0.01f;
                    cam.farClipPlane = extent * 10f;
                    var rt = new RenderTexture(size, size, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var shot = new Texture2D(size, size, TextureFormat.RGB24, false);
                    shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    sheet.SetPixels(i * size, 0, size, size, shot.GetPixels());
                    RenderTexture.active = null;
                    Object.DestroyImmediate(cam.gameObject);
                    Object.DestroyImmediate(light.gameObject);
                    Object.DestroyImmediate(holder);
                    Object.DestroyImmediate(rt);
                }
                File.WriteAllBytes("Logs/meshy-preview-" + Path.GetFileNameWithoutExtension(path) + ".png", sheet.EncodeToPNG());
                Debug.Log("MESHY PREVIEW Logs/meshy-preview-" + Path.GetFileNameWithoutExtension(path) + ".png");
            }
        }
    }
}
