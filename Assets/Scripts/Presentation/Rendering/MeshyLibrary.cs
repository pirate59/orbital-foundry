using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The "Meshy" visual style: models made with Meshy (meshy.ai), loaded by id from Resources/Meshy/Items/&lt;itemId&gt;
    /// and Resources/Meshy/Buildings/&lt;buildingTypeId&gt; (an imported FBX or OBJ, or a prefab). Each model is merged
    /// into one mesh, centred, stood on y = 0 and scaled to fit (a cargo slot for items, the building's footprint for
    /// buildings), and drawn with the game's lit material carrying its own texture. A model with no texture is painted
    /// in the game's palette instead: a gunmetal base, cream on upward faces and the item's or building's own colour
    /// elsewhere; a PNG beside the model with the same name is used as its colour map. Resources/Meshy/models.json can
    /// turn a model that does not face the right way and name a moving part (a drill bit that spins, plunges and
    /// throws dust while the building works). Anything without a model keeps the built-in one.
    /// </summary>
    public static class MeshyLibrary
    {
        public const string ItemFolder = "Meshy/Items/";
        public const string BuildingFolder = "Meshy/Buildings/";
        const float CargoSize = 0.26f;
        /// <summary>A building model spans this much of its footprint, and stands at most this many tiles tall.</summary>
        const float BuildingSpan = 0.9f, BuildingMaxHeight = 2.4f;

        /// <summary>True while the Meshy visual style is chosen.</summary>
        public static bool Enabled;

        sealed class Shape
        {
            public Mesh Mesh;
            public Material[] Materials;
            /// <summary>True when no material carries a texture, so the model is painted from the palette.</summary>
            public bool Untextured;
            /// <summary>The moving part cut out of the model, centred on its own upright axis, or null.</summary>
            public Mesh Part;
            /// <summary>Where the moving part's axis stands in the model, and the tip it works from.</summary>
            public Vector3 PartOffset;
            public float PartTip;
            /// <summary>Height of the part's top in the model, and its width across, for the rod that drives it.</summary>
            public float PartTop, PartWidth;
            /// <summary>The fitted model's bounds before any part was cut out: the frame for fractions in models.json.</summary>
            public Bounds Frame;
            /// <summary>Glass parts cut out of the model (in place), each with its glass material.</summary>
            public readonly List<(Mesh mesh, Material material)> Glass = new List<(Mesh, Material)>();
            /// <summary>Swinging parts cut out of the model, each centred on its pivot.</summary>
            public readonly List<(Mesh mesh, Vector3 pivot, SwingPart settings)> Swings = new List<(Mesh, Vector3, SwingPart)>();

            public Vector3 Point(Vector3 fraction) { return Frame.min + Vector3.Scale(fraction, Frame.size); }
            public float Height;
        }

        /// <summary>
        /// Per-model settings from Resources/Meshy/models.json, keyed "Buildings/&lt;id&gt;" or "Items/&lt;id&gt;": a turn
        /// for a model that does not stand or face the right way, and a moving part.
        /// </summary>
        [System.Serializable]
        sealed class ModelList
        {
            public List<ModelSettings> models = new List<ModelSettings>();
        }

        [System.Serializable]
        public sealed class ModelSettings
        {
            public string id = "";
            /// <summary>Degrees about X, Y and Z.</summary>
            public Vector3 rotate;
            public MovingPart part = new MovingPart();
            public List<SwingPart> swings = new List<SwingPart>();
            public List<Beacon> beacons = new List<Beacon>();
            public List<Vent> steam = new List<Vent>();
            public List<Glass> glass = new List<Glass>();
            /// <summary>Colours for parts of an untextured model; the first region a triangle falls in wins.</summary>
            public List<PaintRegion> paint = new List<PaintRegion>();
            /// <summary>Puts a landing pad under each foot, so a model on legs has a reason to stand off the ground.</summary>
            public bool landingPads;
        }

        /// <summary>
        /// A colour for part of an untextured model: a box (<see cref="min"/>/<see cref="max"/> as fractions of the
        /// model's size), or, when <see cref="radius"/> is set, an upright cylinder at <see cref="centre"/> (X and Z
        /// fractions) <see cref="radius"/> (share of width) across, optionally hollow inside <see cref="inner"/>,
        /// between <see cref="bottom"/> and <see cref="top"/> (height fractions). <see cref="faces"/> limits it to
        /// faces looking "up", "down" or to the "side" ("all" by default). Colours are palette names (cream, ivory,
        /// cherry, mint, seafoam, teal, mustard, atomic, chrome, gunmetal, bakelite), "body" for the building's own
        /// colour, or #RRGGBB.
        /// </summary>
        [System.Serializable]
        public sealed class PaintRegion
        {
            public string colour = "cream";
            public Vector3 min;
            public Vector3 max;
            public Vector3 centre;
            public float radius;
            public float inner;
            public float bottom;
            public float top = 1f;
            public string faces = "all";

            public bool Contains(Vector3 f, float widthFraction, Vector2 centreOffset, string facing)
            {
                if (faces != "all" && faces != facing) return false;
                if (radius > 0f)
                {
                    if (f.y < bottom || f.y > top) return false;
                    float d = centreOffset.magnitude / widthFraction;
                    return d <= radius && d >= inner;
                }
                return f.x >= min.x && f.x <= max.x && f.y >= min.y && f.y <= max.y && f.z >= min.z && f.z <= max.z;
            }
        }

        /// <summary>
        /// Part of the model shown as tinted, glowing glass (a dome), so a light placed inside shows through: the
        /// triangles within <see cref="radius"/> (share of width) of an upright line at <see cref="centre"/> (X and Z
        /// fractions) and between <see cref="bottom"/> and <see cref="top"/> (height fractions).
        /// </summary>
        [System.Serializable]
        public sealed class Glass
        {
            public Vector3 centre;
            public float radius;
            public float bottom;
            public float top;
            public string tint = "#FFB238";
        }

        /// <summary>
        /// A box within the model, as fractions of its size along X, Y and Z (0 at the left, bottom and front, 1 at the
        /// right, top and back), as read off the grid in Logs/meshy-blueprint-&lt;id&gt;.png.
        /// </summary>
        [System.Serializable]
        public sealed class Box
        {
            public Vector3 min;
            public Vector3 max;
            public bool Contains(Vector3 f) { return f.x >= min.x && f.x <= max.x && f.y >= min.y && f.y <= max.y && f.z >= min.z && f.z <= max.z; }
        }

        /// <summary>
        /// A part (such as a crane boom) cut out by one or more boxes that swings about an upright pivot while the
        /// building works: out by <see cref="angle"/> degrees, a pause, back the other way, a pause.
        /// </summary>
        [System.Serializable]
        public sealed class SwingPart
        {
            public List<Box> boxes = new List<Box>();
            /// <summary>Where the upright pivot stands, as fractions of the model's size (Y is not used).</summary>
            public Vector3 pivot;
            public float angle = 25f;
            public float period = 9f;
        }

        /// <summary>A lighthouse lamp at <see cref="at"/> with two sweeping beams, and a soft pulsing glow around it.</summary>
        [System.Serializable]
        public sealed class Beacon
        {
            public Vector3 at;
            public string colour = "#FFB238";
            /// <summary>Degrees per second the beams turn.</summary>
            public float spin = 140f;
            /// <summary>Beam length and glow radius, as shares of the model's width.</summary>
            public float beam = 0.45f;
            public float glow = 0.22f;
        }

        /// <summary>
        /// Steam or smoke puffing from a vent (a cooling tower's or chimney's mouth) at <see cref="at"/>,
        /// <see cref="radius"/> (share of width) across. Smoke is grey and drifts up more slowly than white steam.
        /// </summary>
        [System.Serializable]
        public sealed class Vent
        {
            public Vector3 at;
            public float radius = 0.1f;
            public bool smoke;
        }

        /// <summary>
        /// A part of a one-piece model that moves while the building works, such as a drill bit: the triangles within
        /// <see cref="radius"/> (a share of the model's width) of the model's upright centre line and between
        /// <see cref="bottom"/> and <see cref="top"/> (shares of its height). It spins about that line and, with a
        /// plunge, rises and sinks; with dust, it throws dust and rock chips from its tip.
        /// </summary>
        [System.Serializable]
        public sealed class MovingPart
        {
            public float radius;
            public float bottom;
            public float top;
            /// <summary>Degrees per second about the upright axis.</summary>
            public float spin;
            /// <summary>How far it sinks at the bottom of each stroke, as a share of its tip's height above the ground (1 = the tip touches the ground).</summary>
            public float plunge;
            /// <summary>Seconds for one whole stroke: down, hold at the bottom, back up, hold at the top.</summary>
            public float period = 6f;
            public bool dust;
            public bool Used => radius > 0f && top > bottom;
        }

        static Dictionary<string, ModelSettings> settings;

        static ModelSettings SettingsFor(string path)
        {
            if (settings == null)
            {
                settings = new Dictionary<string, ModelSettings>();
                var asset = Resources.Load<TextAsset>("Meshy/models");
                if (asset != null)
                {
                    try
                    {
                        var parsed = JsonUtility.FromJson<ModelList>(asset.text);
                        if (parsed != null) foreach (var m in parsed.models) settings[m.id] = m;
                    }
                    catch (System.Exception e) { Debug.LogWarning("Meshy/models.json could not be read: " + e.Message); }
                }
            }
            ModelSettings found;
            return settings.TryGetValue(path.Substring("Meshy/".Length), out found) ? found : new ModelSettings();
        }

        static readonly Dictionary<string, Shape> shapes = new Dictionary<string, Shape>();
        static readonly HashSet<string> missing = new HashSet<string>();

        public static bool HasItem(string itemId) { return Raw(ItemFolder + itemId) != null; }
        public static bool HasBuilding(string typeId) { return Raw(BuildingFolder + typeId) != null; }

        /// <summary>How many of the catalog's items have a Meshy model.</summary>
        public static int CountItems(ContentCatalog c)
        {
            int n = 0;
            if (c != null) foreach (var item in c.items) if (HasItem(item.id)) n++;
            return n;
        }

        /// <summary>How many of the catalog's building types have a Meshy model.</summary>
        public static int CountBuildings(ContentCatalog c)
        {
            int n = 0;
            if (c != null) foreach (var b in c.buildings) if (HasBuilding(b.id)) n++;
            return n;
        }

        /// <summary>The item's Meshy cargo model (a pile of <paramref name="count"/> when stacked), or null to use the built-in one.</summary>
        public static GameObject Cargo(string itemId, Color colour, Transform parent, int count, Vector3[] pileSpots, float pileScale)
        {
            if (!Enabled) return null;
            var single = Fitted(ItemFolder + itemId, "", Fit(CargoSize, CargoSize), colour);
            if (single == null) return null;
            var shape = single;
            if (count > 1)
            {
                // A pile: the single model repeated at the pile spots.
                string key = ItemFolder + itemId + "x" + count;
                if (!shapes.TryGetValue(key, out shape))
                {
                    var parts = new List<CombineInstance>();
                    var mats = new List<Material>();
                    for (int i = 0; i < count; i++)
                    {
                        for (int s = 0; s < single.Mesh.subMeshCount; s++)
                            parts.Add(new CombineInstance { mesh = single.Mesh, subMeshIndex = s, transform = Matrix4x4.TRS(pileSpots[i], Quaternion.Euler(0, i * 47f, 0), Vector3.one * pileScale) });
                        mats.AddRange(single.Materials);
                    }
                    shape = new Shape { Mesh = Combine("Meshy " + key, parts), Materials = mats.ToArray() };
                    shapes[key] = shape;
                }
            }
            var go = Instance("Cargo " + itemId, shape, parent);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// The building type's Meshy model on a thin plinth in the owner's colour (so whose it is still reads at a
        /// glance), sized to a footprint <paramref name="tiles"/> wide; null to use the built-in model.
        /// </summary>
        public static Model Building(string typeId, FactionId owner, Color colour, Transform parent, int tiles)
        {
            if (!Enabled) return null;
            tiles = Mathf.Max(1, tiles);
            var shape = Fitted(BuildingFolder + typeId, "@" + tiles, Fit(tiles * BuildingSpan, BuildingMaxHeight), colour);
            if (shape == null) return null;
            var root = new GameObject("Meshy " + typeId);
            root.transform.SetParent(parent, false);
            var model = new Model { Root = root };

            var plinth = new MeshKit();
            plinth.Cylinder(Vector3.zero, tiles * 0.47f, 0.05f, 28, Palette.Get(ModelLibrary.FactionPaint(owner)));
            model.Renderers.Add(plinth.Instantiate("Plinth", root.transform, true).GetComponent<MeshRenderer>());

            var body = Instance("Body", shape, root.transform);
            body.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            model.Renderers.Add(body.GetComponent<MeshRenderer>());

            var settingsFor = SettingsFor(BuildingFolder + typeId);
            if (shape.Part != null)
            {
                var part = new GameObject("Part");
                part.transform.SetParent(body.transform, false);
                part.transform.localPosition = shape.PartOffset;
                part.AddComponent<MeshFilter>().sharedMesh = shape.Part;
                part.AddComponent<MeshRenderer>().sharedMaterials = shape.Materials;
                model.Renderers.Add(part.GetComponent<MeshRenderer>());
                model.Moving.Add(part.GetComponent<MeshRenderer>());
                var move = settingsFor.part;
                // The body sits 0.05 up on its plinth, so the ground under the tip is that far below the model's base.
                float depth = Mathf.Clamp01(move.plunge) * (shape.PartTip + 0.05f - 0.02f), period = Mathf.Max(0.2f, move.period), clock = 0f;
                var home = shape.PartOffset;
                var t = part.transform;

                // A chrome rod, one unit tall from its base, stretched each frame between the machine and the bit.
                var rodKit = new MeshKit();
                rodKit.Cylinder(Vector3.zero, Mathf.Max(0.008f, shape.PartWidth * RodShare), 1f, 12, Palette.Get(Palette.Chrome, Palette.Finish.Chrome));
                var rodObject = rodKit.Instantiate("Rod", body.transform, true);
                var rod = rodObject.transform;
                rod.localScale = new Vector3(1f, RodOverlap, 1f);
                rod.localPosition = new Vector3(home.x, shape.PartTop - RodOverlap * 0.5f, home.z);
                model.Renderers.Add(rodObject.GetComponent<MeshRenderer>());
                model.Moving.Add(rodObject.GetComponent<MeshRenderer>());
                ParticleSystem[] dust = null; // filled in below; only throws dust while the bit is in the ground
                model.Motions.Add(dt =>
                {
                    clock += dt;
                    t.localRotation = Quaternion.Euler(0f, clock * move.spin, 0f);
                    float sink = Stroke(Mathf.Repeat(clock / period, 1f));
                    t.localPosition = home + Vector3.down * (depth * sink);
                    // The rod reaches from the machine to the bit's top, so the bit always looks driven from above.
                    float length = depth * sink + RodOverlap;
                    rod.localPosition = new Vector3(home.x, shape.PartTop - length + RodOverlap * 0.5f, home.z);
                    rod.localScale = new Vector3(1f, length, 1f);
                    if (dust != null)
                        foreach (var d in dust)
                        {
                            var emission = d.emission;
                            emission.enabled = sink > 0.85f;
                        }
                });
                if (move.dust)
                {
                    dust = WorkDust.Make(body.transform, new Vector3(shape.PartOffset.x, Mathf.Max(0.02f, shape.PartTip - depth), shape.PartOffset.z), tiles);
                    if (dust != null) model.Effects.AddRange(dust);
                }
            }
            if (settingsFor.landingPads) AddLandingPads(model, body.transform, shape.Mesh);
            foreach (var swing in shape.Swings) AddSwing(model, body.transform, swing.mesh, swing.pivot, swing.settings, shape.Materials);
            foreach (var glass in shape.Glass)
            {
                var pane = new GameObject("Glass");
                pane.transform.SetParent(body.transform, false);
                pane.AddComponent<MeshFilter>().sharedMesh = glass.mesh;
                var materials = new Material[glass.mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = glass.material;
                var r = pane.AddComponent<MeshRenderer>();
                r.sharedMaterials = materials;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                model.Renderers.Add(r);
            }
            foreach (var beacon in settingsFor.beacons) AddBeacon(model, body.transform, shape, beacon);
            foreach (var vent in settingsFor.steam)
            {
                var steam = WorkDust.Steam(body.transform, shape.Point(vent.at), vent.radius * Mathf.Max(shape.Frame.size.x, shape.Frame.size.z), vent.smoke);
                if (steam != null) model.Effects.Add(steam);
            }
            return model;
        }

        static void AddSwing(Model model, Transform body, Mesh mesh, Vector3 pivot, SwingPart settings, Material[] materials)
        {
            var go = new GameObject("Swing");
            go.transform.SetParent(body, false);
            go.transform.localPosition = pivot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            model.Renderers.Add(r);
            model.Moving.Add(r);
            float clock = 0f, period = Mathf.Max(1f, settings.period), angle = settings.angle;
            var t = go.transform;
            // Out to one side, pause, across to the other, pause: Stroke's ease and holds, mapped onto -angle..+angle.
            model.Motions.Add(dt =>
            {
                clock += dt;
                t.localRotation = Quaternion.Euler(0f, Mathf.Lerp(-angle, angle, Stroke(Mathf.Repeat(clock / period, 1f))), 0f);
            });
        }

        /// <summary>A lamp on its post, two beams sweeping round, and a pulsing halo; lit only while the building works.</summary>
        static void AddBeacon(Model model, Transform body, Shape shape, Beacon settings)
        {
            Color colour;
            if (!ColorUtility.TryParseHtmlString(settings.colour, out colour)) colour = new Color(1f, 0.7f, 0.22f);
            float width = Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
            var at = shape.Point(settings.at);

            var lampKit = new MeshKit();
            lampKit.Cylinder(Vector3.zero, width * 0.025f, width * 0.03f, 10, Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome));
            lampKit.Sphere(new Vector3(0f, width * 0.04f, 0f), width * 0.022f, 10, Palette.Get(colour, Palette.Finish.Glow));
            var lamp = lampKit.Instantiate("Beacon", body, false);
            lamp.transform.localPosition = at;
            model.Renderers.Add(lamp.GetComponent<MeshRenderer>());

            // Two beams: tapered sheets, crossed so they read from any angle, turning about the lamp.
            float length = settings.beam * width, spread = length * 0.11f;
            var beamKit = new MeshKit();
            var beamPaint = Palette.Flat(new Color(colour.r * 0.32f, colour.g * 0.32f, colour.b * 0.32f), true);
            var wedge = new[] { new Vector2(0f, -0.004f), new Vector2(length, -spread), new Vector2(length, spread), new Vector2(0f, 0.004f) };
            foreach (float side in new[] { 0f, 180f })
                foreach (float roll in new[] { 0f, 90f })
                    beamKit.Push(Vector3.zero, Quaternion.Euler(0f, side, 0f) * Quaternion.Euler(roll, 0f, 0f)).Fin(wedge, 0.002f, beamPaint).Pop();
            var beams = beamKit.Instantiate("Beams", lamp.transform, false);
            beams.transform.localPosition = new Vector3(0f, width * 0.04f, 0f);
            model.Lights.Add(beams.GetComponent<MeshRenderer>());

            var haloKit = new MeshKit();
            haloKit.Sphere(Vector3.zero, settings.glow * width, 16, Palette.Flat(new Color(colour.r * 0.22f, colour.g * 0.22f, colour.b * 0.22f), true));
            var halo = haloKit.Instantiate("Glow", lamp.transform, false);
            halo.transform.localPosition = new Vector3(0f, -settings.glow * width * 0.35f, 0f);
            model.Lights.Add(halo.GetComponent<MeshRenderer>());

            float clock = 0f;
            var beamTransform = beams.transform;
            var haloTransform = halo.transform;
            model.Motions.Add(dt =>
            {
                clock += dt;
                beamTransform.localRotation = Quaternion.Euler(0f, clock * settings.spin, 0f);
                haloTransform.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(clock * 2.6f)); // a slow breathing glow
            });
        }

        /// <summary>The drive rod's radius as a share of the moving part's width, and how far it reaches into the bit and the machine.</summary>
        const float RodShare = 0.14f, RodOverlap = 0.06f;

        /// <summary>
        /// How far down a moving part is (0 top, 1 bottom) at a point in its stroke: ease down, hold at the bottom
        /// while it grinds, ease back up, then hold at the top before the next stroke.
        /// </summary>
        public static float Stroke(float phase)
        {
            const float down = 0.35f, hold = 0.15f, up = 0.35f;
            if (phase < down) return Mathf.SmoothStep(0f, 1f, phase / down);
            if (phase < down + hold) return 1f;
            if (phase < down + hold + up) return Mathf.SmoothStep(1f, 0f, (phase - down - hold) / up);
            return 0f;
        }

        static GameObject Instance(string name, Shape shape, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = shape.Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = shape.Materials;
            return go;
        }

        /// <summary>Scales a model's bounds so its widest side is <paramref name="span"/> and it is no taller than <paramref name="maxHeight"/>.</summary>
        static System.Func<Bounds, float> Fit(float span, float maxHeight)
        {
            return b => Mathf.Min(span / Mathf.Max(0.0001f, Mathf.Max(b.size.x, b.size.z)), maxHeight / Mathf.Max(0.0001f, b.size.y));
        }

        /// <summary>
        /// The model at <paramref name="path"/>, centred, stood on y = 0 and scaled by <paramref name="scaleFor"/>;
        /// painted with <paramref name="colour"/> as its main colour when it has no texture.
        /// </summary>
        static Shape Fitted(string path, string variant, System.Func<Bounds, float> scaleFor, Color colour)
        {
            Shape shape;
            string key = path + "|" + variant + "|" + ColorUtility.ToHtmlStringRGB(colour);
            if (shapes.TryGetValue(key, out shape)) return shape;
            var raw = Raw(path);
            if (raw == null) return null;
            var b = raw.Mesh.bounds;
            var fit = Matrix4x4.Scale(Vector3.one * scaleFor(b)) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            var parts = new List<CombineInstance>();
            for (int s = 0; s < raw.Mesh.subMeshCount; s++) parts.Add(new CombineInstance { mesh = raw.Mesh, subMeshIndex = s, transform = fit });
            var mesh = Combine("Meshy " + path + variant, parts);
            var modelSettingsForPaint = SettingsFor(path);
            shape = raw.Untextured ? Paint(mesh, colour, modelSettingsForPaint.paint) : new Shape { Mesh = mesh, Materials = raw.Materials };
            shape.Height = shape.Mesh.bounds.size.y;
            shape.Frame = shape.Mesh.bounds;
            var modelSettings = SettingsFor(path);
            var move = modelSettings.part;
            if (move.Used) SplitPart(shape, move);
            foreach (var swing in modelSettings.swings) SplitSwing(shape, swing);
            foreach (var glass in modelSettings.glass) SplitGlass(shape, glass);
            shapes[key] = shape;
            return shape;
        }

        /// <summary>
        /// Cuts the moving part out of a fitted model: triangles whose centre is near the upright centre line and in
        /// the part's height band move to their own mesh, re-centred on the part's own axis so it spins in place.
        /// </summary>
        static void SplitPart(Shape shape, MovingPart move)
        {
            var mesh = shape.Mesh;
            var vertices = mesh.vertices;
            var size = mesh.bounds.size;
            float radius = move.radius * Mathf.Max(size.x, size.z), low = move.bottom * size.y, high = move.top * size.y;
            var triangles = new int[mesh.subMeshCount][];
            for (int s = 0; s < triangles.Length; s++) triangles[s] = mesh.GetTriangles(s);

            // The part's own upright axis: start at the model's centre line, then re-centre on what was caught, a few
            // times, so a part that stands off-centre is caught whole rather than cut in half.
            var axis = Vector2.zero;
            for (int pass = 0; pass < 4; pass++)
            {
                var sum = Vector2.zero;
                int n = 0;
                foreach (var t in triangles)
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        var c = (vertices[t[i]] + vertices[t[i + 1]] + vertices[t[i + 2]]) / 3f;
                        if (c.y < low || c.y > high || (new Vector2(c.x, c.z) - axis).magnitude > radius) continue;
                        sum += new Vector2(c.x, c.z);
                        n++;
                    }
                if (n == 0) break;
                axis = sum / n;
            }

            var bodySubs = new List<int>[triangles.Length];
            var partSubs = new List<int>[triangles.Length];
            var remap = new Dictionary<int, int>();
            var partVertices = new List<Vector3>();
            var uv = mesh.uv;
            bool hasUv = uv != null && uv.Length == vertices.Length;
            var partUv = new List<Vector2>();
            var normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            var partNormals = new List<Vector3>();
            var centre = new Vector3(axis.x, 0f, axis.y);
            float tip = float.MaxValue;
            for (int s = 0; s < triangles.Length; s++)
            {
                bodySubs[s] = new List<int>();
                partSubs[s] = new List<int>();
                var t = triangles[s];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (vertices[t[i]] + vertices[t[i + 1]] + vertices[t[i + 2]]) / 3f;
                    bool inPart = c.y >= low && c.y <= high && (new Vector2(c.x, c.z) - axis).magnitude <= radius;
                    if (!inPart) { bodySubs[s].Add(t[i]); bodySubs[s].Add(t[i + 1]); bodySubs[s].Add(t[i + 2]); continue; }
                    for (int k = 0; k < 3; k++)
                    {
                        int v = t[i + k], mapped;
                        if (!remap.TryGetValue(v, out mapped))
                        {
                            mapped = partVertices.Count;
                            remap[v] = mapped;
                            partVertices.Add(vertices[v] - centre); // centred on its own axis, so it spins in place
                            if (hasUv) partUv.Add(uv[v]);
                            if (hasNormals) partNormals.Add(normals[v]);
                            tip = Mathf.Min(tip, vertices[v].y);
                        }
                        partSubs[s].Add(mapped);
                    }
                }
            }
            if (partVertices.Count == 0)
            {
                Debug.LogWarning("Meshy moving part found no triangles; check its radius, bottom and top in models.json.");
                return;
            }
            // Only the part's own vertices, so its bounds are its own (the selection ring and culling rely on them).
            var part = new Mesh { name = mesh.name + " part", indexFormat = partVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            part.SetVertices(partVertices);
            if (hasUv) part.SetUVs(0, partUv);
            part.subMeshCount = triangles.Length;
            for (int s = 0; s < triangles.Length; s++)
            {
                part.SetTriangles(partSubs[s], s);
                mesh.SetTriangles(bodySubs[s], s);
            }
            if (hasNormals) part.SetNormals(partNormals); else part.RecalculateNormals();
            part.RecalculateBounds();
            mesh.RecalculateBounds();
            shape.Part = part;
            shape.PartOffset = centre;
            shape.PartTip = tip;
            shape.PartTop = part.bounds.max.y;
            shape.PartWidth = Mathf.Max(part.bounds.size.x, part.bounds.size.z);
        }

        /// <summary>Cuts a swinging part out of a fitted model by its boxes, centred on its pivot so it turns in place.</summary>
        static void SplitSwing(Shape shape, SwingPart swing)
        {
            if (swing.boxes.Count == 0) return;
            var mesh = shape.Mesh;
            var vertices = mesh.vertices;
            var uv = mesh.uv;
            bool hasUv = uv != null && uv.Length == vertices.Length;
            var normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            var frame = shape.Frame;
            var pivot = shape.Point(new Vector3(swing.pivot.x, 0f, swing.pivot.z));
            pivot.y = 0f;
            var bodySubs = new List<int>[mesh.subMeshCount];
            var partSubs = new List<int>[mesh.subMeshCount];
            var remap = new Dictionary<int, int>();
            var partVertices = new List<Vector3>();
            var partUv = new List<Vector2>();
            var partNormals = new List<Vector3>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                bodySubs[s] = new List<int>();
                partSubs[s] = new List<int>();
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (vertices[t[i]] + vertices[t[i + 1]] + vertices[t[i + 2]]) / 3f;
                    var f = new Vector3((c.x - frame.min.x) / frame.size.x, (c.y - frame.min.y) / frame.size.y, (c.z - frame.min.z) / frame.size.z);
                    bool inPart = swing.boxes.Exists(b => b.Contains(f));
                    if (!inPart) { bodySubs[s].Add(t[i]); bodySubs[s].Add(t[i + 1]); bodySubs[s].Add(t[i + 2]); continue; }
                    for (int k = 0; k < 3; k++)
                    {
                        int v = t[i + k], mapped;
                        if (!remap.TryGetValue(v, out mapped))
                        {
                            mapped = partVertices.Count;
                            remap[v] = mapped;
                            partVertices.Add(vertices[v] - pivot);
                            if (hasUv) partUv.Add(uv[v]);
                            if (hasNormals) partNormals.Add(normals[v]);
                        }
                        partSubs[s].Add(mapped);
                    }
                }
            }
            if (partVertices.Count == 0)
            {
                Debug.LogWarning("Meshy swinging part found no triangles; check its boxes in models.json.");
                return;
            }
            var part = new Mesh { name = mesh.name + " swing", indexFormat = partVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            part.SetVertices(partVertices);
            if (hasUv) part.SetUVs(0, partUv);
            part.subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                part.SetTriangles(partSubs[s], s);
                mesh.SetTriangles(bodySubs[s], s);
            }
            if (hasNormals) part.SetNormals(partNormals); else part.RecalculateNormals();
            part.RecalculateBounds();
            mesh.RecalculateBounds();
            shape.Swings.Add((part, pivot, swing));
        }

        /// <summary>
        /// Cuts a glass part out of a fitted model (it stays where it is) and gives it a see-through glowing tint.
        /// </summary>
        static void SplitGlass(Shape shape, Glass glass)
        {
            if (glass.radius <= 0f || glass.top <= glass.bottom) return;
            var mesh = shape.Mesh;
            var vertices = mesh.vertices;
            var frame = shape.Frame;
            var centre = shape.Point(new Vector3(glass.centre.x, 0f, glass.centre.z));
            float radius = glass.radius * Mathf.Max(frame.size.x, frame.size.z);
            float low = frame.min.y + glass.bottom * frame.size.y, high = frame.min.y + glass.top * frame.size.y;
            var bodySubs = new List<int>[mesh.subMeshCount];
            var paneSubs = new List<int>[mesh.subMeshCount];
            int caught = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                bodySubs[s] = new List<int>();
                paneSubs[s] = new List<int>();
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (vertices[t[i]] + vertices[t[i + 1]] + vertices[t[i + 2]]) / 3f;
                    bool inPane = c.y >= low && c.y <= high && new Vector2(c.x - centre.x, c.z - centre.z).magnitude <= radius;
                    var into = inPane ? paneSubs[s] : bodySubs[s];
                    into.Add(t[i]); into.Add(t[i + 1]); into.Add(t[i + 2]);
                    if (inPane) caught++;
                }
            }
            if (caught == 0)
            {
                Debug.LogWarning("Meshy glass found no triangles; check its centre, radius, bottom and top in models.json.");
                return;
            }
            // The glass shares the model's vertices; only its triangles differ.
            var pane = Object.Instantiate(mesh);
            pane.name = mesh.name + " glass";
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                pane.SetTriangles(paneSubs[s], s);
                mesh.SetTriangles(bodySubs[s], s);
            }
            pane.RecalculateBounds();
            mesh.RecalculateBounds();
            Color tint;
            if (!ColorUtility.TryParseHtmlString(glass.tint, out tint)) tint = new Color(1f, 0.7f, 0.22f);
            // The Belt View twin of a glowing paint: see-through and lit from within.
            shape.Glass.Add((pane, Palette.Faded(Palette.Get(tint, Palette.Finish.Glow))));
        }

        /// <summary>Base band below this share of the height is painted gunmetal.</summary>
        const float BaseBand = 0.08f;
        /// <summary>The height band (shares of the height) where the building's own colour runs as a trim stripe by default.</summary>
        const float TrimLow = 0.55f, TrimHigh = 0.66f;

        /// <summary>
        /// Paints an untextured model in the same palette as built-in models (so Belt View and stopped buildings fade
        /// and darken it the same way). Each triangle takes the first paint region from models.json it falls in;
        /// otherwise the atomic-age default: gunmetal base and undersides, cream on faces that look up, ivory sides,
        /// and the building's own colour as a trim stripe around the upper body.
        /// </summary>
        static Shape Paint(Mesh mesh, Color colour, List<PaintRegion> regions)
        {
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            var size = new Vector3(Mathf.Max(0.0001f, bounds.size.x), Mathf.Max(0.0001f, bounds.size.y), Mathf.Max(0.0001f, bounds.size.z));
            float width = Mathf.Max(size.x, size.z);
            var groups = new Dictionary<string, List<int>>();
            var order = new List<string>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    Vector3 a = vertices[t[i]], b = vertices[t[i + 1]], c = vertices[t[i + 2]];
                    var centre = (a + b + c) / 3f;
                    var f = new Vector3((centre.x - bounds.min.x) / size.x, (centre.y - bounds.min.y) / size.y, (centre.z - bounds.min.z) / size.z);
                    var cross = Vector3.Cross(b - a, c - a);
                    float ny = cross.sqrMagnitude > 0f ? cross.normalized.y : 0f;
                    string facing = ny > 0.65f ? "up" : ny < -0.5f ? "down" : "side";

                    string key = null;
                    if (regions != null)
                        foreach (var r in regions)
                        {
                            var offset = new Vector2(centre.x - (bounds.min.x + r.centre.x * size.x), centre.z - (bounds.min.z + r.centre.z * size.z));
                            if (r.Contains(f, width, offset, facing)) { key = r.colour; break; }
                        }
                    if (key == null)
                        key = f.y < BaseBand || facing == "down" ? "gunmetal"
                            : facing == "up" ? "cream"
                            : f.y >= TrimLow && f.y <= TrimHigh ? "body"
                            : "ivory";
                    List<int> list;
                    if (!groups.TryGetValue(key, out list)) { groups[key] = list = new List<int>(); order.Add(key); }
                    list.Add(t[i]); list.Add(t[i + 1]); list.Add(t[i + 2]);
                }
            }
            mesh.subMeshCount = order.Count;
            var materials = new Material[order.Count];
            for (int k = 0; k < order.Count; k++)
            {
                mesh.SetTriangles(groups[order[k]], k);
                materials[k] = PaintFor(order[k], colour);
            }
            mesh.RecalculateBounds();
            return new Shape { Mesh = mesh, Materials = materials };
        }

        /// <summary>The palette paint for a colour name, "body" (the building's colour) or #RRGGBB.</summary>
        static Material PaintFor(string name, Color body)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "body": return Palette.Get(new Color(body.r, body.g, body.b));
                case "cream": return Palette.Get(Palette.Cream);
                case "ivory": return Palette.Get(Palette.Ivory);
                case "cherry": return Palette.Get(Palette.Cherry);
                case "mint": return Palette.Get(Palette.Mint);
                case "seafoam": return Palette.Get(Palette.Seafoam);
                case "teal": return Palette.Get(Palette.Teal);
                case "mustard": return Palette.Get(Palette.Mustard);
                case "atomic": return Palette.Get(Palette.Atomic);
                case "chrome": return Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
                case "gunmetal": return Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
                case "bakelite": return Palette.Get(Palette.Bakelite, Palette.Finish.Matte);
            }
            Color c;
            return ColorUtility.TryParseHtmlString(name, out c) ? Palette.Get(new Color(c.r, c.g, c.b)) : Palette.Get(Palette.Ivory);
        }

        /// <summary>
        /// A chrome landing pad under each of the model's feet: the lowest points of the model, grouped into separate
        /// feet. Each pad is a low dished disc a little wider than its foot.
        /// </summary>
        static void AddLandingPads(Model model, Transform body, Mesh mesh)
        {
            var vertices = mesh.vertices;
            var bounds = mesh.bounds;
            float floor = bounds.min.y + bounds.size.y * 0.03f;
            float cell = Mathf.Max(bounds.size.x, bounds.size.z) * 0.06f;
            // Group the lowest vertices into feet by flood-filling neighbouring grid cells.
            var cells = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (var v in vertices)
            {
                if (v.y > floor) continue;
                var key = new Vector2Int(Mathf.FloorToInt(v.x / cell), Mathf.FloorToInt(v.z / cell));
                List<Vector3> list;
                if (!cells.TryGetValue(key, out list)) cells[key] = list = new List<Vector3>();
                list.Add(v);
            }
            var seen = new HashSet<Vector2Int>();
            var kit = new MeshKit();
            var chrome = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var dark = Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
            int feet = 0;
            foreach (var start in cells.Keys)
            {
                if (!seen.Add(start)) continue;
                var queue = new Queue<Vector2Int>();
                queue.Enqueue(start);
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    foreach (var v in cells[c])
                    {
                        min = Vector2.Min(min, new Vector2(v.x, v.z));
                        max = Vector2.Max(max, new Vector2(v.x, v.z));
                    }
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            var n = new Vector2Int(c.x + dx, c.y + dz);
                            if (cells.ContainsKey(n) && seen.Add(n)) queue.Enqueue(n);
                        }
                }
                var footCentre = (min + max) * 0.5f;
                float footRadius = Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f;
                if (footRadius <= 0f) continue;
                float pad = footRadius * 1.7f + 0.01f;
                var at = new Vector3(footCentre.x, bounds.min.y, footCentre.y);
                // A dished chrome pad with a dark rim underneath, like a lander's foot.
                kit.Push(at).Lathe(new[]
                {
                    new Vector2(0f, 0f), new Vector2(pad, 0f), new Vector2(pad, pad * 0.12f),
                    new Vector2(pad * 0.55f, pad * 0.32f), new Vector2(0f, pad * 0.32f)
                }, 16, chrome).Pop();
                kit.Push(at).Cylinder(Vector3.zero, pad * 1.04f, pad * 0.05f, 16, dark).Pop();
                feet++;
            }
            if (feet == 0) return;
            var pads = kit.Instantiate("LandingPads", body, true);
            model.Renderers.Add(pads.GetComponent<MeshRenderer>());
        }

        /// <summary>Every mesh in the model merged as it was authored; null (remembered) when there is no usable model.</summary>
        static Shape Raw(string path)
        {
            Shape shape;
            if (shapes.TryGetValue(path, out shape)) return shape;
            if (missing.Contains(path)) return null;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) { missing.Add(path); return null; }

            var parts = new List<CombineInstance>();
            var mats = new List<Material>();
            bool textured = false;
            // The model as it stands in its file, including the rotation on its root: Meshy's FBX exports are Z-up and
            // carry the turn that stands them up there. Then any turn from models.json. Position is dropped later
            // when the model is centred.
            var turn = Matrix4x4.Rotate(Quaternion.Euler(SettingsFor(path).rotate));
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (source == null || renderer == null) continue;
                var shared = renderer.sharedMaterials;
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    parts.Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = turn * filter.transform.localToWorldMatrix });
                    var material = s < shared.Length ? shared[s] : null;
                    if (HasTexture(material)) textured = true;
                    mats.Add(Convert(material));
                }
            }
            if (!textured)
            {
                // A colour map saved beside the model with the same name (e.g. extractor.png) dresses every part.
                var colourMap = Resources.Load<Texture2D>(path);
                if (colourMap != null)
                {
                    for (int i = 0; i < mats.Count; i++) mats[i] = Palette.Textured(colourMap, Color.white);
                    textured = true;
                }
            }
            if (parts.Count == 0)
            {
                Debug.LogWarning("Meshy model " + path + " has no meshes; using the built-in model.");
                missing.Add(path);
                return null;
            }
            try { shape = new Shape { Mesh = Combine("Meshy " + path, parts), Materials = mats.ToArray(), Untextured = !textured }; }
            catch (System.Exception e)
            {
                // Meshes must be readable; the importer in Assets/Editor/MeshyImport.cs turns that on for these folders.
                Debug.LogWarning("Meshy model " + path + " could not be read (" + e.Message + "); using the built-in model.");
                missing.Add(path);
                return null;
            }
            shapes[path] = shape;
            return shape;
        }

        static Mesh Combine(string name, List<CombineInstance> parts)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            // One submesh per source submesh, so each keeps its material.
            mesh.CombineMeshes(parts.ToArray(), false, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool HasTexture(Material m)
        {
            return m != null && ((m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null));
        }

        /// <summary>The game's lit material carrying the imported material's texture and tint.</summary>
        static Material Convert(Material source)
        {
            if (source == null) return Palette.Get(Color.white, Palette.Finish.Matte);
            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (tex == null && source.HasProperty("_MainTex")) tex = source.GetTexture("_MainTex");
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.color : Color.white;
            return tex == null ? Palette.Get(new Color(tint.r, tint.g, tint.b), Palette.Finish.Matte) : Palette.Textured(tex, tint);
        }
    }
}
