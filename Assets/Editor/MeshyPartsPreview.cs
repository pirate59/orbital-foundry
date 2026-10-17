using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// A Meshy building (MESHY_MODEL, default shipyard) from the top, front and side, orthographic, with a grid every
    /// tenth of the model's size (yellow every half), to Logs/&lt;model&gt;-parts-*.png. Used to find the boxes for animated parts in
    /// models.json, which are given as fractions of the model's size. Boxes listed in Logs/meshy-boxes.txt
    /// (one per line: "r g b  minX minY minZ  maxX maxY maxZ", fractions) are painted in their colour, and
    /// SHIPYARD_ZOOM="x0 x1 y0 y1 z0 z1" frames the views on one region with a grid every 0.02.
    /// -executeMethod OrbitalFoundry.EditorTools.MeshyPartsPreview.RenderBatch
    /// </summary>
    public static class MeshyPartsPreview
    {
        const int Size = 1200;
        static string prefix = "Logs/shipyard-parts-";

        public static void RenderBatch()
        {
            try { Render(); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        [MenuItem("Orbital Foundry/Preview Meshy parts")]
        public static void Render()
        {
            bool previous = MeshyLibrary.Enabled;
            MeshyLibrary.Enabled = true;
            string id = System.Environment.GetEnvironmentVariable("MESHY_MODEL");
            if (string.IsNullOrEmpty(id)) id = "shipyard";
            var def = CatalogRepository.GetBuilding(CatalogRepository.LoadContent(), id);
            int tiles = def != null ? Mathf.Max(def.width, def.footprint) : 1;
            prefix = "Logs/" + id + "-parts-";
            var model = MeshyLibrary.Building(id, FactionId.Player, Color.white, null, tiles);
            if (model == null) throw new System.InvalidOperationException("No Meshy model for " + id);
            try
            {
                model.SetState(Model.Normal);
                // The body mesh still holds its cut-away vertices, so its bounds are the model's whole frame.
                var frame = model.Renderers[0].bounds;
                Paint(model, frame);
                // Several zoom regions may be given, separated by ';': each is rendered as its own angled close-up.
                var zooms = (System.Environment.GetEnvironmentVariable("SHIPYARD_ZOOM") ?? "").Split(';');
                var view = frame;
                float step = 0.1f;
                if (zooms.Length > 1)
                {
                    for (int k = 0; k < zooms.Length; k++)
                    {
                        var r = Region(frame, zooms[k]);
                        Shot(new GameObject("Empty"), "zoom" + k, r, new Vector3(0.5f, -0.6f, 0.8f).normalized, Vector3.up, Mathf.Max(r.size.x, r.size.y, r.size.z) * 0.7f);
                        Shot(new GameObject("Empty"), "zoom" + k + "b", r, new Vector3(-0.6f, -0.5f, -0.7f).normalized, Vector3.up, Mathf.Max(r.size.x, r.size.y, r.size.z) * 0.7f);
                    }
                    return;
                }
                if (!string.IsNullOrEmpty(zooms[0]))
                {
                    view = Region(frame, zooms[0]);
                    step = 0.02f;
                }
                var grid = new GameObject("Grid");
                var minor = Palette.Flat(new Color(0.3f, 0.9f, 1f));
                var major = Palette.Flat(new Color(1f, 0.85f, 0.2f));
                int lines = Mathf.RoundToInt(1f / step);
                for (int i = 0; i <= lines; i++)
                {
                    float f = i * step;
                    var m = Mathf.Abs(f * 10f - Mathf.Round(f * 10f)) < 0.001f ? major : minor;
                    float x = frame.min.x + f * frame.size.x, y = frame.min.y + f * frame.size.y, z = frame.min.z + f * frame.size.z;
                    // Top view lines (x and z), drawn above the model; front lines (x and y) in front; side lines (z and y) beside.
                    Line(grid, new Vector3(x, frame.max.y + 0.05f, frame.center.z), new Vector3(0.004f, 0.004f, frame.size.z), m, "top");
                    Line(grid, new Vector3(frame.center.x, frame.max.y + 0.05f, z), new Vector3(frame.size.x, 0.004f, 0.004f), m, "top");
                    Line(grid, new Vector3(x, frame.center.y, frame.min.z - 0.05f), new Vector3(0.004f, frame.size.y, 0.004f), m, "front");
                    Line(grid, new Vector3(frame.center.x, y, frame.min.z - 0.05f), new Vector3(frame.size.x, 0.004f, 0.004f), m, "front");
                    Line(grid, new Vector3(frame.max.x + 0.05f, frame.center.y, z), new Vector3(0.004f, frame.size.y, 0.004f), m, "side");
                    Line(grid, new Vector3(frame.max.x + 0.05f, y, frame.center.z), new Vector3(0.004f, 0.004f, frame.size.z), m, "side");
                }
                Shot(grid, "top", view, Vector3.down, Vector3.forward, Mathf.Max(view.size.x, view.size.z) * 0.55f);
                Shot(grid, "front", view, Vector3.forward, Vector3.up, Mathf.Max(view.size.x, view.size.y) * 0.55f);
                Shot(grid, "side", view, Vector3.left, Vector3.up, Mathf.Max(view.size.z, view.size.y) * 0.6f);
                Shot(grid, "angle", view, new Vector3(0.5f, -0.6f, 0.8f).normalized, Vector3.up, Mathf.Max(view.size.x, view.size.y, view.size.z) * 0.6f);
                Object.DestroyImmediate(grid);
            }
            finally
            {
                Object.DestroyImmediate(model.Root);
                MeshyLibrary.Enabled = previous;
            }
        }

        static Bounds Region(Bounds frame, string text)
        {
            var z = System.Array.ConvertAll(text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries), float.Parse);
            var r = new Bounds();
            r.SetMinMax(Point(frame, new Vector3(z[0], z[2], z[4])), Point(frame, new Vector3(z[1], z[3], z[5])));
            return r;
        }

        static Vector3 Point(Bounds frame, Vector3 f)
        {
            return frame.min + Vector3.Scale(f, frame.size);
        }

        /// <summary>Paints the triangles of each listed box in its colour, as extra overlay meshes.</summary>
        static void Paint(Model model, Bounds frame)
        {
            const string path = "Logs/meshy-boxes.txt";
            if (!File.Exists(path)) return;
            var body = model.Renderers[0];
            var mesh = body.GetComponent<MeshFilter>().sharedMesh;
            var v = mesh.vertices;
            var tris = new System.Collections.Generic.List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) tris.AddRange(mesh.GetTriangles(s));
            int[] island = null;
            foreach (var raw in File.ReadAllLines(path))
            {
                // "I r g b box... size": paints every connected piece (sharing vertex positions) that has a triangle in
                // the box and is no wider than size (a share of the model's width), so a whole arm is picked at once.
                bool islands = raw.StartsWith("I ");
                var line = islands ? raw.Substring(2) : raw;
                if (islands && island == null) island = Islands(mesh, tris);
                var n = System.Array.ConvertAll(line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries), float.Parse);
                if (n.Length < 9) continue;
                var min = Point(frame, new Vector3(n[3], n[4], n[5])) - body.transform.position;
                var max = Point(frame, new Vector3(n[6], n[7], n[8])) - body.transform.position;
                var picked = new System.Collections.Generic.List<int>();
                if (islands)
                {
                    var seeds = new System.Collections.Generic.HashSet<int>();
                    var lo = new System.Collections.Generic.Dictionary<int, Vector3>();
                    var hi = new System.Collections.Generic.Dictionary<int, Vector3>();
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                    {
                        int id = island[i / 3];
                        for (int k = 0; k < 3; k++)
                        {
                            var p = v[tris[i + k]];
                            lo[id] = lo.ContainsKey(id) ? Vector3.Min(lo[id], p) : p;
                            hi[id] = hi.ContainsKey(id) ? Vector3.Max(hi[id], p) : p;
                        }
                        var c = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                        if (c.x >= min.x && c.y >= min.y && c.z >= min.z && c.x <= max.x && c.y <= max.y && c.z <= max.z) seeds.Add(id);
                    }
                    // Pieces lying wholly inside the box (with a hair of slack).
                    var chosen = new System.Collections.Generic.HashSet<int>();
                    var slack = frame.size * 0.004f;
                    foreach (int id in seeds)
                        if (lo[id].x >= min.x - slack.x && lo[id].y >= min.y - slack.y && lo[id].z >= min.z - slack.z
                            && hi[id].x <= max.x + slack.x && hi[id].y <= max.y + slack.y && hi[id].z <= max.z + slack.z) chosen.Add(id);
                    for (int i = 0; i + 2 < tris.Count; i += 3)
                        if (chosen.Contains(island[i / 3])) { picked.Add(tris[i]); picked.Add(tris[i + 1]); picked.Add(tris[i + 2]); }
                    var sb = new System.Text.StringBuilder("ISLANDS " + line + ": " + seeds.Count + " touched, " + chosen.Count + " chosen\n");
                    foreach (int id in chosen)
                    {
                        var f0 = new Vector3((lo[id].x - frame.min.x + body.transform.position.x) / frame.size.x, (lo[id].y - frame.min.y + body.transform.position.y) / frame.size.y, (lo[id].z - frame.min.z + body.transform.position.z) / frame.size.z);
                        var f1 = new Vector3((hi[id].x - frame.min.x + body.transform.position.x) / frame.size.x, (hi[id].y - frame.min.y + body.transform.position.y) / frame.size.y, (hi[id].z - frame.min.z + body.transform.position.z) / frame.size.z);
                        sb.Append("   piece " + id + " " + f0.ToString("F3") + " - " + f1.ToString("F3") + "\n");
                    }
                    Debug.Log(sb.ToString());
                }
                else for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    var c = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                    if (c.x < min.x || c.y < min.y || c.z < min.z || c.x > max.x || c.y > max.y || c.z > max.z) continue;
                    // An optional tenth number leaves long triangles (longer than that share of the model's width).
                    if (n.Length > 9)
                    {
                        float lo = Mathf.Min(v[tris[i]].x, Mathf.Min(v[tris[i + 1]].x, v[tris[i + 2]].x));
                        float hi = Mathf.Max(v[tris[i]].x, Mathf.Max(v[tris[i + 1]].x, v[tris[i + 2]].x));
                        if (hi - lo > n[9] * frame.size.x) continue;
                    }
                    picked.Add(tris[i]); picked.Add(tris[i + 1]); picked.Add(tris[i + 2]);
                }
                var overlay = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, vertices = v };
                overlay.SetTriangles(picked, 0);
                overlay.RecalculateNormals();
                var go = new GameObject("Painted");
                go.transform.SetParent(body.transform, false);
                go.transform.localScale = Vector3.one * 1.004f;
                go.AddComponent<MeshFilter>().sharedMesh = overlay;
                go.AddComponent<MeshRenderer>().sharedMaterial = Palette.Flat(new Color(n[0], n[1], n[2]));
                Debug.Log("PAINTED " + line + " -> " + picked.Count / 3 + " triangles");
            }
        }

        /// <summary>For each triangle, the connected piece it belongs to (triangles sharing a vertex position).</summary>
        static int[] Islands(Mesh mesh, System.Collections.Generic.List<int> tris)
        {
            var v = mesh.vertices;
            var parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            var byPosition = new System.Collections.Generic.Dictionary<Vector3Int, int>();
            for (int i = 0; i < v.Length; i++)
            {
                var key = Vector3Int.RoundToInt(v[i] * 10000f);
                int other;
                if (byPosition.TryGetValue(key, out other)) parent[find(i)] = find(other); else byPosition[key] = i;
            }
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                parent[find(tris[i + 1])] = find(tris[i]);
                parent[find(tris[i + 2])] = find(tris[i]);
            }
            var result = new int[tris.Count / 3];
            for (int i = 0; i < result.Length; i++) result[i] = find(tris[i * 3]);
            return result;
        }

        static void Line(GameObject grid, Vector3 at, Vector3 size, Material m, string view)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.name = view;
            bar.transform.SetParent(grid.transform);
            bar.transform.position = at;
            bar.transform.localScale = size;
            bar.GetComponent<MeshRenderer>().sharedMaterial = m;
        }

        static void Shot(GameObject grid, string view, Bounds frame, Vector3 look, Vector3 up, float half)
        {
            foreach (Transform bar in grid.transform) bar.gameObject.SetActive(bar.name == view);
            var light = new GameObject("Light").AddComponent<Light>();
            var cam = new GameObject("Camera").AddComponent<Camera>();
            var target = new RenderTexture(Size, Size, 24) { antiAliasing = 4 };
            try
            {
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.LookRotation(look + Vector3.down * 0.5f + Vector3.right * 0.3f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.13f, 0.18f);
                cam.orthographic = true;
                cam.orthographicSize = half;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;
                cam.transform.position = frame.center - look * 6f;
                cam.transform.rotation = Quaternion.LookRotation(look, up);
                cam.targetTexture = target;
                cam.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                File.WriteAllBytes(prefix + view + ".png", picture.EncodeToPNG());
                Object.DestroyImmediate(picture);
            }
            finally
            {
                RenderTexture.active = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(light.gameObject);
            }
        }
    }
}
