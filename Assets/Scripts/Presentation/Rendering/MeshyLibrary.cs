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
        public const string CraftFolder = "Meshy/Crafts/";
        const float CargoSize = 0.26f;
        /// <summary>
        /// A building model spans this much of its footprint and stands at most <see cref="MaxHeight"/> tiles tall:
        /// 2.4 for a one-tile building, rising with its footprint so a big one (the 3x3 headquarters) is not squashed.
        /// </summary>
        const float BuildingSpan = 0.9f, BuildingMaxHeight = 2.4f, ExtraHeightPerTile = 1.4f;
        /// <summary>A big building fills nearly all of its square, so a round model reaches into the corners.</summary>
        const float BigBuildingSpan = 0.97f;

        static float MaxHeight(int tiles) { return BuildingMaxHeight + (Mathf.Max(1, tiles) - 1) * ExtraHeightPerTile; }

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
            /// <summary>The leg copied onto every tile corner, centred on its foot, or null.</summary>
            public Mesh Leg;
            /// <summary>A leg's width in tiles.</summary>
            public float LegPad;
            /// <summary>The platform the legs hold up: the body's extent just above the legs.</summary>
            public Bounds Platform;
            /// <summary>The hoist's parts, each centred on its own origin, and where those origins sit.</summary>
            public Mesh HoistCarriage, HoistCables, HoistLoad;
            public Vector3 HoistCarriageAt, HoistCablesTop, HoistLoadTop;
            public float HoistCableLength;
            /// <summary>The tracker's turning mount and tilting dish, each centred on its pivot.</summary>
            public Mesh TrackerTurn, TrackerTilt;
            public Vector3 TrackerTurnAt, TrackerTiltAt;
            /// <summary>Welding arms: each centred on its pivot, with its tip and the axis it leans about.</summary>
            public readonly List<(Mesh mesh, Vector3 pivot, Vector3 tip, Vector3 axis, WeldingArm settings)> WeldArms =
                new List<(Mesh, Vector3, Vector3, Vector3, WeldingArm)>();
            /// <summary>Where the moving part's axis stands in the model, and the tip it works from.</summary>
            public Vector3 PartOffset;
            public float PartTip;
            /// <summary>Height of the part's top in the model, and its width across, for the rod that drives it.</summary>
            public float PartTop, PartWidth;
            /// <summary>The fitted model's bounds before any part was cut out: the frame for fractions in models.json.</summary>
            public Bounds Frame;
            /// <summary>Glass parts cut out of the model (in place), each with its glass material.</summary>
            public readonly List<(Mesh mesh, Material material)> Glass = new List<(Mesh, Material)>();
            /// <summary>The loading arm's parts, each centred on its own pivot, or null.</summary>
            public Mesh Upper, Fore, Claw;
            public Vector3 UpperPivot, ForePivot, Hang;
            public readonly List<(Mesh mesh, Vector3 hinge, Vector3 axis)> Fingers = new List<(Mesh, Vector3, Vector3)>();
            /// <summary>Spinning parts, each centred on its axis.</summary>
            public readonly List<(Mesh mesh, Vector3 axis, Spinner settings, Mesh radar)> Spinners = new List<(Mesh, Vector3, Spinner, Mesh)>();
            /// <summary>The teleporter's two tubes (base centre on the tube floor, radius and height in model units).</summary>
            public Vector3 TubeFrom, TubeTo;
            public float TubeRadius, TubeHeight;
            /// <summary>Press rams, cut out in place.</summary>
            public readonly List<(Mesh mesh, Press settings)> Presses = new List<(Mesh, Press)>();
            /// <summary>The welding arm's parts, centred on their pivots, and its torch tip; null when there is none.</summary>
            public Mesh WeldUpper, WeldFore;
            public Vector3 WeldShoulder, WeldElbow, WeldTip;
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
            /// <summary>Legs replaced by copies of one of them standing on every tile corner of the footprint.</summary>
            public Legs legs = new Legs();
            /// <summary>A crane carriage hovering over the work, hoisting a part on cables and carrying it away and back.</summary>
            public Hoist hoist = new Hoist();
            /// <summary>Robot arms that lean in to the hull, weld with sparks, and lean back out.</summary>
            public Welding welding = new Welding();
            /// <summary>A dish or panel that slowly turns on its mount and tilts up and down (the solar array's).</summary>
            public Tracker tracker = new Tracker();
            public List<Beacon> beacons = new List<Beacon>();
            public List<Vent> steam = new List<Vent>();
            public List<Glass> glass = new List<Glass>();
            /// <summary>Colours for parts of an untextured model; the first region a triangle falls in wins.</summary>
            public List<PaintRegion> paint = new List<PaintRegion>();
            /// <summary>Puts a landing pad under each foot, so a model on legs has a reason to stand off the ground.</summary>
            public bool landingPads;
            /// <summary>
            /// The building stands on a raised platform: the space under it is shaded, darkening the further in you
            /// look and toward the platform's underside, over a soft ground shadow, so belts and goods passing under
            /// fade into the building and back out.
            /// </summary>
            public bool elevated;
            /// <summary>The platform's underside as a share of the model's height; 0 finds it from the model.</summary>
            public float underside;
            /// <summary>
            /// A soft contact shadow on the ground under the building, this dark (0 to 1, 0 for none): a gentle
            /// darkening that settles it onto the deck, without the layered shade of an elevated building.
            /// </summary>
            public float groundShadow;
            /// <summary>A jointed loading arm with a claw on a cable (the drone pad's).</summary>
            public Loader loader = new Loader();
            /// <summary>Parts that lift slowly and slam down, like a stamping press's ram.</summary>
            public List<Press> presses = new List<Press>();
            /// <summary>A jointed welding arm that dips its torch onto the work and throws sparks.</summary>
            public Welder welder = new Welder();
            /// <summary>Upright cylinders of the model that turn steadily (a carousel, a rooftop widget).</summary>
            public List<Spinner> spinners = new List<Spinner>();
            /// <summary>Soft lights inside or on the model, lit while it works.</summary>
            public List<Glow> glows = new List<Glow>();
            /// <summary>Lit water columns with rising bubbles.</summary>
            public List<WaterTank> waterTanks = new List<WaterTank>();
            /// <summary>Small lamps travelling around the centre tower's bands.</summary>
            public List<OrbitRing> orbitRings = new List<OrbitRing>();
            /// <summary>A pair of tubes: cargo teleports out of one and gold coins teleport into the other (the sale depot).</summary>
            public Teleporter teleporter = new Teleporter();
        }

        /// <summary>An upright tube inside the model: <see cref="centre"/> (X and Z fractions), <see cref="radius"/> (share of width), height band.</summary>
        [System.Serializable]
        public sealed class Tube
        {
            public Vector3 centre;
            public float radius;
            public float bottom;
            public float top;
            public bool Used => radius > 0f && top > bottom;
        }

        /// <summary>
        /// Two tubes whose baked-in contents are replaced: crates in <see cref="from"/>, gold coins in
        /// <see cref="to"/>. Each cycle of <see cref="period"/> seconds the crates teleport out (fading, with a beam,
        /// a scanning ring and sparkles), the coins teleport in the same way, sit a moment, and both tubes restock.
        /// </summary>
        [System.Serializable]
        public sealed class Teleporter
        {
            public Tube from = new Tube();
            public Tube to = new Tube();
            public float period = 10f;
            public string beam = "#7FE8FF";
            public bool Used => from.Used && to.Used;
        }

        /// <summary>
        /// Part of the model that turns steadily about an upright axis at <see cref="centre"/> (X and Z fractions): the
        /// triangles within <see cref="radius"/> (share of width) of it and between <see cref="bottom"/> and
        /// <see cref="top"/> (height fractions). <see cref="inner"/> leaves a core standing still.
        /// </summary>
        [System.Serializable]
        public sealed class Spinner
        {
            public Vector3 centre;
            public float radius;
            public float inner;
            public float bottom;
            public float top;
            /// <summary>Degrees per second.</summary>
            public float spin = 30f;
            /// <summary>Occasional quick target checks, in degrees; zero keeps a steady turn.</summary>
            public float quickTurn;
            public float quickEvery = 13f;
            /// <summary>A radar assembly cut from this spinner and animated as its child.</summary>
            public Radar radar = new Radar();
            /// <summary>Re-centre on the middle of what is caught (for a part whose centre is hard to read off the blueprint).</summary>
            public bool recentre;
            /// <summary>
            /// A carousel: the caught geometry is removed and replaced by round shelves at these heights (fractions of
            /// the model's height), each ringed with goods, turning together about a central column.
            /// </summary>
            public List<float> shelves = new List<float>();
            /// <summary>Goods per shelf, and the item ids to fill them with (in turn).</summary>
            public int perShelf = 7;
            public List<string> goods = new List<string> { "ore", "iron_bar", "copper_ore", "copper_bar", "water", "copper_wire", "iron_plate" };
        }

        [System.Serializable]
        public sealed class Radar
        {
            public Vector3 centre;
            public float radius;
            public float bottom;
            public float top;
            public Vector3 pivot;
            public float spin = 30f;
            public float tiltAngle = 12f;
            public float tiltPeriod = 4f;
            public bool Used => radius > 0f && top > bottom;
        }

        /// <summary>A soft light at <see cref="at"/>, <see cref="radius"/> (share of width) across, gently pulsing.</summary>
        [System.Serializable]
        public sealed class Glow
        {
            public Vector3 at;
            public float radius = 0.2f;
            public string colour = "#FFC860";
            /// <summary>How strong it is (0 to 1).</summary>
            public float strength = 0.5f;
            /// <summary>Seconds between pairs of short flashes; zero keeps the gentle pulse.</summary>
            public float flashPeriod;
        }

        [System.Serializable]
        public sealed class WaterTank
        {
            public Vector3 centre;
            public float radius;
            public float bottom;
            public float top;
            public string colour = "#57D9FF";
            public float pulsePeriod = 3.2f;
        }

        [System.Serializable]
        public sealed class OrbitRing
        {
            public float height;
            public float radius;
            public float speed = 35f;
            public int lamps = 4;
            public string colour = "#57D9FF";
        }

        /// <summary>A stamping press's ram, cut out by its boxes: it lifts by <see cref="travel"/> (share of the model's height), pauses, then slams back down.</summary>
        [System.Serializable]
        public sealed class Press
        {
            public List<Box> boxes = new List<Box>();
            public float travel = 0.035f;
            public float period = 2.2f;
        }

        /// <summary>
        /// A welding arm: an upper arm turning at the shoulder and a forearm at the elbow (each cut out by its boxes).
        /// The model is posed welding; each joint's <c>reach</c> is how far it lifts the torch away (negative lifts for
        /// an arm reaching out and down). It lifts away, comes back down onto the work at <see cref="tip"/>, holds while
        /// sparks fly, and lifts again.
        /// </summary>
        [System.Serializable]
        public sealed class Welder
        {
            public Joint upper = new Joint();
            public Joint fore = new Joint();
            /// <summary>The torch tip, as fractions of the model's size; sparks fly from here.</summary>
            public Vector3 tip;
            public float period = 6f;
            public bool Used => upper.boxes.Count > 0 && fore.boxes.Count > 0;
        }

        /// <summary>One joint of a loading arm: the part cut out by its boxes, turning about <see cref="pivot"/>.</summary>
        [System.Serializable]
        public sealed class Joint
        {
            public List<Box> boxes = new List<Box>();
            /// <summary>The joint's centre, as fractions of the model's size.</summary>
            public Vector3 pivot;
            /// <summary>The hinge's axis (default Z: the arm bends in the X-Y plane).</summary>
            public Vector3 axis = new Vector3(0f, 0f, 1f);
            /// <summary>Degrees it turns from rest to full reach.</summary>
            public float reach;
        }

        /// <summary>
        /// A loading arm: an upper arm turning at the shoulder, a forearm turning at the elbow, and a claw hanging on a
        /// cable from the wrist that stays upright, lowers, and opens and closes its fingers. One cycle of
        /// <see cref="period"/> seconds: reach out, lower, grip, raise, swing back, lower, release, raise.
        /// </summary>
        [System.Serializable]
        public sealed class Loader
        {
            public Joint upper = new Joint();
            public Joint fore = new Joint();
            /// <summary>The claw's boxes (its head and fingers).</summary>
            public List<Box> claw = new List<Box>();
            /// <summary>Where the cable leaves the wrist, as fractions of the model's size.</summary>
            public Vector3 hang;
            /// <summary>How far the claw lowers, as a share of the model's height.</summary>
            public float drop = 0.2f;
            /// <summary>The fingers' height band (fractions of the model's height), how many there are, and how far they open (degrees).</summary>
            public float fingerBottom;
            public float fingerTop;
            public int fingers = 4;
            public float open = 28f;
            public float period = 10f;
            public bool Used => upper.boxes.Count > 0 && fore.boxes.Count > 0 && claw.Count > 0;
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

        /// <summary>
        /// A model's legs, found by their boxes and removed; the box at <see cref="template"/> is kept as the leg
        /// that is copied onto every tile corner of the footprint (so legs never stand in a belt lane, which runs
        /// along the middle of a tile). <see cref="pad"/> is a leg's width in tiles.
        /// </summary>
        /// <summary>
        /// A hovering crane: the <see cref="carriage"/> floats <see cref="raise"/> (a share of the model's height)
        /// above where it was modelled; <see cref="cables"/> hang from it to the <see cref="load"/> (the claw and the
        /// part it holds). Each cycle the load is hoisted by <see cref="lift"/>, carried <see cref="travel"/> along
        /// the model's length (a share of its width; negative toward its start), held, carried back and set down.
        /// Boxes pick whole pieces of the model that lie inside them.
        /// </summary>
        [System.Serializable]
        public sealed class Hoist
        {
            public List<Box> carriage = new List<Box>();
            public List<Box> cables = new List<Box>();
            public List<Box> load = new List<Box>();
            public float raise = 0.15f;
            public float lift = 0.24f;
            public float travel = -0.26f;
            public float period = 18f;
            public bool Used { get { return carriage.Count > 0 && load.Count > 0; } }
        }

        /// <summary>
        /// Welding arms around a hull lying along the model's length at <see cref="hull"/> (y and z as shares of
        /// the model's size; x unused). Each arm leans in toward it, welds, and leans back.
        /// </summary>
        [System.Serializable]
        public sealed class Welding
        {
            public Vector3 hull;
            public List<WeldingArm> arms = new List<WeldingArm>();
        }

        /// <summary>
        /// One welding arm: the whole pieces inside its boxes, turning about the foot of its lowest piece. It is
        /// modelled at work, its tip (the point nearest the hull) on the hull; it rests leaned <see cref="angle"/>
        /// degrees back from there. Each cycle of <see cref="period"/> seconds, started <see cref="delay"/> seconds
        /// late, it leans in to the hull, welds, and leans back.
        /// </summary>
        [System.Serializable]
        public sealed class WeldingArm
        {
            public List<Box> boxes = new List<Box>();
            public float angle = 12f;
            public float period = 6f;
            public float delay;
        }

        /// <summary>
        /// A slow sun-tracker: the whole pieces in <see cref="turn"/> (a dish and its yoke) turn about an upright
        /// axis through <see cref="turnPivot"/>, one full turn every <see cref="turnPeriod"/> seconds; the pieces in
        /// <see cref="tilt"/> (the dish) ride along and also tilt up and down by <see cref="tiltAngle"/> degrees about
        /// a side-to-side axis through <see cref="tiltPivot"/>, once every <see cref="tiltPeriod"/> seconds.
        /// Pivots are fractions of the model's size.
        /// </summary>
        [System.Serializable]
        public sealed class Tracker
        {
            public List<Box> turn = new List<Box>();
            public List<Box> tilt = new List<Box>();
            public Vector3 turnPivot;
            public Vector3 tiltPivot;
            public float turnPeriod = 90f;
            public float tiltAngle = 10f;
            public float tiltPeriod = 30f;
            public bool Used { get { return turn.Count > 0; } }
        }

        [System.Serializable]
        public sealed class Legs
        {
            public List<Box> boxes = new List<Box>();
            public int template;
            public float pad = 0.22f;
            /// <summary>Where the leg's solid body ends, as a share of the model's height; slivers above are dropped (0 keeps all).</summary>
            public float top;
            public bool Used { get { return boxes.Count > 0 && template >= 0 && template < boxes.Count; } }
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
        public static bool HasCraft(string typeId) { return Raw(CraftFolder + typeId) != null; }

        /// <summary>
        /// One ship of a craft type's fleet from Meshy/Crafts/&lt;type&gt;, its longest side <paramref name="size"/>,
        /// facing +Z (turn it with "rotate" in models.json); null in Light weight or without a model.
        /// </summary>
        public static Model Craft(string typeId, FactionId owner, Color colour, Transform parent, float size)
        {
            if (!Enabled) return null;
            var shape = Fitted(CraftFolder + typeId, "@" + size.ToString("0.###"), Fit(size, size), colour);
            if (shape == null) return null;
            var root = new GameObject("Meshy " + typeId);
            root.transform.SetParent(parent, false);
            var model = new Model { Root = root };
            var body = Instance("Body", shape, root.transform);
            model.Renderers.Add(body.GetComponent<MeshRenderer>());
            return model;
        }

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

        /// <summary>How many of the catalog's ships have a Meshy model.</summary>
        public static int CountCrafts(ContentCatalog c)
        {
            int n = 0;
            if (c != null) foreach (var k in c.crafts) if (HasCraft(k.id)) n++;
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
        /// The building type's Meshy model standing on the deck (the platform's own colour shows whose it is), sized to a footprint <paramref name="tiles"/> wide; null to use the built-in model.
        /// </summary>
        public static Model Building(string typeId, FactionId owner, Color colour, Transform parent, int tiles)
        {
            if (!Enabled) return null;
            tiles = Mathf.Max(1, tiles);
            var shape = Fitted(BuildingFolder + typeId, "@" + tiles, Fit(tiles * (tiles > 1 ? BigBuildingSpan : BuildingSpan), MaxHeight(tiles)), colour);
            if (shape == null) return null;
            var root = new GameObject("Meshy " + typeId);
            root.transform.SetParent(parent, false);
            var model = new Model { Root = root };


            var body = Instance("Body", shape, root.transform);
            body.transform.localPosition = Vector3.zero; // stands straight on the deck; the platform shows the owner
            model.Renderers.Add(body.GetComponent<MeshRenderer>());
            model.Platform = shape.Platform;
            if (shape.Leg != null)
            {
                // A hidden leg to copy; until the footprint is known, legs stand on a grid matching the model's shape.
                var template = new GameObject(LegTemplateName);
                template.transform.SetParent(root.transform, false);
                template.AddComponent<MeshFilter>().sharedMesh = shape.Leg;
                template.AddComponent<MeshRenderer>().sharedMaterials = shape.Materials;
                template.transform.localScale = Vector3.one * shape.LegPad; // the hidden template carries the leg width
                template.SetActive(false);
                int deep = Mathf.Max(1, Mathf.RoundToInt(tiles * shape.Frame.size.z / Mathf.Max(0.0001f, shape.Frame.size.x)));
                PlaceLegs(model, tiles, deep);
            }

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
                // The body stands on the deck, so the ground under the tip is the model's base.
                float depth = Mathf.Clamp01(move.plunge) * (shape.PartTip - 0.02f), period = Mathf.Max(0.2f, move.period), clock = 0f;
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
            if (settingsFor.elevated) AddUnderShade(model, body.transform, shape, settingsFor.underside);
            else if (settingsFor.groundShadow > 0f) AddGroundShadow(model, body.transform, shape.Frame, settingsFor.groundShadow);
            if (shape.Upper != null) AddLoader(model, body.transform, shape, settingsFor.loader);
            foreach (var spinner in shape.Spinners)
            {
                var go = new GameObject("Spinner");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = spinner.axis;
                if (spinner.settings.shelves.Count > 0) BuildCarousel(model, go.transform, shape, spinner.mesh, spinner.settings);
                else
                {
                    go.AddComponent<MeshFilter>().sharedMesh = spinner.mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = shape.Materials;
                    model.Renderers.Add(r);
                    model.Moving.Add(r);
                }
                var t = go.transform;
                float speed = spinner.settings.spin, clock = 0f;
                float quickTurn = spinner.settings.quickTurn, quickEvery = Mathf.Max(2f, spinner.settings.quickEvery);
                model.Motions.Add(dt =>
                {
                    clock += dt;
                    float quick = quickTurn > 0f ? QuickCheck(clock, quickEvery) * quickTurn : 0f;
                    t.localRotation = Quaternion.Euler(0f, clock * speed + quick, 0f);
                });
                if (spinner.radar != null)
                {
                    var settings = spinner.settings.radar;
                    var yaw = new GameObject("RadarYaw").transform;
                    yaw.SetParent(t, false);
                    yaw.localPosition = shape.Point(settings.pivot) - spinner.axis;
                    var tilt = new GameObject("RadarTilt").transform;
                    tilt.SetParent(yaw, false);
                    tilt.gameObject.AddComponent<MeshFilter>().sharedMesh = spinner.radar;
                    var radarRenderer = tilt.gameObject.AddComponent<MeshRenderer>();
                    radarRenderer.sharedMaterials = shape.Materials;
                    model.Renderers.Add(radarRenderer);
                    model.Moving.Add(radarRenderer);
                    float radarClock = 0f;
                    model.Motions.Add(dt =>
                    {
                        radarClock += dt;
                        yaw.localRotation = Quaternion.Euler(0f, radarClock * settings.spin, 0f);
                        tilt.localRotation = Quaternion.Euler(0f, 0f,
                            settings.tiltAngle * Mathf.Sin(radarClock * Mathf.PI * 2f / Mathf.Max(0.5f, settings.tiltPeriod)));
                    });
                }
            }
            foreach (var glow in settingsFor.glows) AddGlow(model, body.transform, shape, glow);
            foreach (var tank in settingsFor.waterTanks) AddWaterTank(model, body.transform, shape, tank);
            foreach (var ring in settingsFor.orbitRings) AddOrbitRing(model, body.transform, shape, ring);
            if (shape.TubeRadius > 0f) AddTeleporter(model, body.transform, shape, settingsFor.teleporter);
            foreach (var press in shape.Presses) AddPress(model, body.transform, shape, press.mesh, press.settings);
            if (shape.WeldUpper != null) AddWelder(model, body.transform, shape, settingsFor.welder);
            foreach (var swing in shape.Swings) AddSwing(model, body.transform, swing.mesh, swing.pivot, swing.settings, shape.Materials);
            if (shape.HoistCarriage != null) AddHoist(model, body.transform, shape, settingsFor.hoist);
            foreach (var arm in shape.WeldArms) AddWeldingArm(model, body.transform, shape, arm.mesh, arm.pivot, arm.tip, arm.axis, arm.settings);
            if (shape.TrackerTurn != null) AddTracker(model, body.transform, shape, settingsFor.tracker);
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

        static Transform AddMoving(Model model, Transform parent, string name, Mesh mesh, Vector3 at, Material[] materials)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            model.Renderers.Add(r);
            model.Moving.Add(r);
            return go.transform;
        }

        /// <summary>
        /// The hoist's cycle: hold, hoist the load up to the raised carriage, carry both along, hold, carry back,
        /// set the load down. The cables stretch from the carriage to the top of the load all the way.
        /// </summary>
        static void AddHoist(Model model, Transform body, Shape shape, Hoist settings)
        {
            float height = shape.Frame.size.y;
            var raise = Vector3.up * (settings.raise * height);
            var carriageAt = shape.HoistCarriageAt + raise;
            var carriage = AddMoving(model, body, "Hoist carriage", shape.HoistCarriage, carriageAt, shape.Materials);
            var load = AddMoving(model, body, "Hoist load", shape.HoistLoad, shape.HoistLoadTop, shape.Materials);
            Transform cables = null;
            if (shape.HoistCables != null)
                cables = AddMoving(model, body, "Hoist cables", shape.HoistCables, shape.HoistCablesTop + raise, shape.Materials);
            float lift = settings.lift * height, travel = settings.travel * shape.Frame.size.x;
            float period = Mathf.Max(4f, settings.period), clock = 0f;
            System.Action<float> pose = phase =>
            {
                float up = Window(phase, 0.06f, 0.2f, 0.8f, 0.94f);
                float along = Window(phase, 0.22f, 0.42f, 0.58f, 0.78f) * travel;
                carriage.localPosition = carriageAt + Vector3.right * along;
                load.localPosition = shape.HoistLoadTop + new Vector3(along, up * lift, 0f);
                if (cables != null)
                {
                    var top = shape.HoistCablesTop + raise + Vector3.right * along;
                    cables.localPosition = top;
                    float length = top.y - load.localPosition.y;
                    cables.localScale = new Vector3(1f, Mathf.Max(0.02f, length / shape.HoistCableLength), 1f);
                }
            };
            pose(0f);
            model.Motions.Add(dt =>
            {
                clock += dt;
                pose(Mathf.Repeat(clock / period, 1f));
            });
        }

        /// <summary>
        /// The tracker's slow motion: the mount turns steadily about its upright axis while the dish, riding on it,
        /// tilts up and down about its side-to-side axis.
        /// </summary>
        static void AddTracker(Model model, Transform body, Shape shape, Tracker settings)
        {
            var turn = shape.TrackerTurn != null
                ? AddMoving(model, body, "Tracker turn", shape.TrackerTurn, shape.TrackerTurnAt, shape.Materials)
                : new GameObject("Tracker turn").transform;
            if (shape.TrackerTurn == null) { turn.SetParent(body, false); turn.localPosition = shape.TrackerTurnAt; }
            Transform tilt = null;
            if (shape.TrackerTilt != null)
                tilt = AddMoving(model, turn, "Tracker tilt", shape.TrackerTilt, shape.TrackerTiltAt - shape.TrackerTurnAt, shape.Materials);
            float turnPeriod = Mathf.Max(5f, settings.turnPeriod), tiltPeriod = Mathf.Max(2f, settings.tiltPeriod);
            float clock = 0f;
            model.Motions.Add(dt =>
            {
                clock += dt;
                turn.localRotation = Quaternion.Euler(0f, 360f * Mathf.Repeat(clock / turnPeriod, 1f), 0f);
                if (tilt != null) tilt.localRotation = Quaternion.Euler(settings.tiltAngle * Mathf.Sin(clock / tiltPeriod * Mathf.PI * 2f), 0f, 0f);
            });
        }

        /// <summary>A welding arm leaning in to the hull, welding with sparks and a flickering arc, and leaning back.</summary>
        static void AddWeldingArm(Model model, Transform body, Shape shape, Mesh mesh, Vector3 pivot, Vector3 tip, Vector3 axis, WeldingArm settings)
        {
            var arm = AddMoving(model, body, "Welding arm", mesh, pivot, shape.Materials);
            var sparks = WorkDust.Sparks(arm, tip);
            if (sparks != null) model.Effects.Add(sparks);
            var kit = new MeshKit();
            kit.Sphere(Vector3.zero, Mathf.Max(shape.Frame.size.x, shape.Frame.size.z) * 0.006f, 8, Palette.Flat(new Color(0.6f, 0.85f, 1f), true));
            var arc = kit.Instantiate("Weld arc", arm, false);
            arc.transform.localPosition = tip;
            var arcRenderer = arc.GetComponent<MeshRenderer>();
            arcRenderer.enabled = false;
            model.Lights.Add(arcRenderer);
            float period = Mathf.Max(1.5f, settings.period), clock = -settings.delay;
            // Resting: leaned back from the hull, out of the modelled working pose.
            arm.localRotation = Quaternion.AngleAxis(-settings.angle, axis);
            model.Motions.Add(dt =>
            {
                clock += dt;
                float phase = Mathf.Repeat(clock / period, 1f);
                float lean = Window(phase, 0.05f, 0.25f, 0.7f, 0.9f);
                arm.localRotation = Quaternion.AngleAxis(-settings.angle * (1f - lean), axis);
                bool welding = lean > 0.98f;
                if (sparks != null)
                {
                    var emission = sparks.emission;
                    emission.enabled = welding;
                }
                arcRenderer.enabled = welding && Mathf.PerlinNoise(clock * 22f, settings.delay) > 0.3f;
            });
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

        /// <summary>A short look aside while a turret continues its slow scan.</summary>
        static float QuickCheck(float clock, float period)
        {
            float phase = Mathf.Repeat(clock, period) - period * 0.65f;
            if (phase < 0f || phase >= 1.16f) return 0f;
            float look = phase < 0.26f ? Mathf.SmoothStep(0f, 1f, phase / 0.26f)
                : phase < 0.64f ? 1f
                : Mathf.SmoothStep(1f, 0f, (phase - 0.64f) / 0.52f);
            return (Mathf.FloorToInt(clock / period) & 1) == 0 ? look : -look;
        }

        const string LegTemplateName = "Leg template", LegName = "Leg";

        /// <summary>
        /// Stands a copy of the model's leg on every tile corner of an <paramref name="across"/> by
        /// <paramref name="deep"/> footprint (centred on the model, long side along local x), replacing any legs
        /// placed before. Call it after the model's root has its final scale: each leg keeps a square foot of its
        /// set width however the root is stretched. Outer legs stand at the footprint's edge, under the platform's rim. Does nothing for a
        /// model without legs.
        /// </summary>
        public static void PlaceLegs(Model model, int across, int deep)
        {
            var root = model.Root.transform;
            var template = root.Find(LegTemplateName);
            if (template == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var old = root.GetChild(i);
                if (old.name != LegName) continue;
                model.Renderers.Remove(old.GetComponent<MeshRenderer>());
                if (Application.isPlaying) Object.Destroy(old.gameObject); else Object.DestroyImmediate(old.gameObject);
            }
            var mesh = template.GetComponent<MeshFilter>().sharedMesh;
            float pad = template.localScale.x;
            var scale = root.localScale;
            float size = Mathf.Max(0.0001f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.z));
            // Outer legs stand just inside the footprint's edge, under the platform's rim. A belt's rails reach 0.23
            // of a tile either side of its centre line (0.268 from a tile edge), so a leg 0.22 wide set 0.025 in
            // clears a lane on any tile edge, and at a corner by far more.
            float inset = pad * 0.5f + 0.025f;
            // Each leg reaches up to the underside of whatever is above it, so it meets the platform.
            var bodyFilter = root.Find("Body") != null ? root.Find("Body").GetComponent<MeshFilter>() : null;
            float legTop = Mathf.Max(0.0001f, mesh.bounds.max.y);
            for (int cz = 0; cz <= deep; cz++)
                for (int cx = 0; cx <= across; cx++)
                {
                    float x = Mathf.Clamp(cx, inset, across - inset) - across * 0.5f;
                    float z = Mathf.Clamp(cz, inset, deep - inset) - deep * 0.5f;
                    var leg = Object.Instantiate(template.gameObject, root, false);
                    leg.name = LegName;
                    leg.transform.localPosition = new Vector3(x / scale.x, 0f, z / scale.z);
                    float under = bodyFilter != null ? Underside(bodyFilter.sharedMesh, x / scale.x, z / scale.z,
                        pad * 0.5f / scale.x, pad * 0.5f / scale.z, legTop * 0.6f) : legTop;
                    float stretch = Mathf.Clamp((under + 0.03f) / legTop, 1f, 3f); // a little into the platform
                    leg.transform.localScale = new Vector3(pad / (size * scale.x), stretch, pad / (size * scale.z));
                    leg.SetActive(true);
                    model.Renderers.Add(leg.GetComponent<MeshRenderer>());
                }
        }

        /// <summary>
        /// The lowest point, above <paramref name="above"/>, of the triangles over a rectangle (centre and half sizes
        /// in the mesh's own space), or <paramref name="above"/> when nothing is there.
        /// </summary>
        static float Underside(Mesh mesh, float x, float z, float halfX, float halfZ, float above)
        {
            var v = mesh.vertices;
            float lowest = float.MaxValue;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    if (Mathf.Abs(c.x - x) > halfX || Mathf.Abs(c.z - z) > halfZ) continue;
                    float low = Mathf.Min(v[t[i]].y, Mathf.Min(v[t[i + 1]].y, v[t[i + 2]].y));
                    if (low > above) lowest = Mathf.Min(lowest, low);
                }
            }
            return lowest == float.MaxValue ? above : lowest;
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
            if (modelSettings.legs.Used) SplitLegs(shape, modelSettings.legs);
            if (modelSettings.hoist.Used) SplitHoist(shape, modelSettings.hoist);
            foreach (var arm in modelSettings.welding.arms) SplitWeldingArm(shape, arm, modelSettings.welding.hull);
            if (modelSettings.tracker.Used) SplitTracker(shape, modelSettings.tracker);
            var move = modelSettings.part;
            if (move.Used) SplitPart(shape, move);
            if (modelSettings.loader.Used) SplitLoader(shape, modelSettings.loader);
            foreach (var spinner in modelSettings.spinners)
            {
                var axis = shape.Point(new Vector3(spinner.centre.x, 0f, spinner.centre.z));
                axis.y = 0f;
                float width = Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
                float r = spinner.radius * width, rIn = spinner.inner * width;
                float low = shape.Frame.min.y + spinner.bottom * shape.Frame.size.y, high = shape.Frame.min.y + spinner.top * shape.Frame.size.y;
                // A spinning part is round about its true axis: re-centre on the middle of what is caught (its
                // footprint's bounds), a few times, so it turns in place rather than swinging out of its housing.
                var all = shape.Mesh.vertices;
                for (int pass = 0; pass < (spinner.recentre ? 3 : 0); pass++)
                {
                    var min = new Vector2(float.MaxValue, float.MaxValue);
                    var max = new Vector2(float.MinValue, float.MinValue);
                    for (int sub = 0; sub < shape.Mesh.subMeshCount; sub++)
                    {
                        var tris = shape.Mesh.GetTriangles(sub);
                        for (int i = 0; i + 2 < tris.Length; i += 3)
                        {
                            var c = (all[tris[i]] + all[tris[i + 1]] + all[tris[i + 2]]) / 3f;
                            float d = new Vector2(c.x - axis.x, c.z - axis.z).magnitude;
                            if (c.y < low || c.y > high || d > r || d < rIn) continue;
                            for (int k = 0; k < 3; k++)
                            {
                                var v = all[tris[i + k]];
                                min = Vector2.Min(min, new Vector2(v.x, v.z));
                                max = Vector2.Max(max, new Vector2(v.x, v.z));
                            }
                        }
                    }
                    if (min.x > max.x) break;
                    var middle = (min + max) * 0.5f;
                    axis = new Vector3(middle.x, 0f, middle.y);
                }
                var turning = Cut(shape.Mesh, c =>
                {
                    float d = new Vector2(c.x - axis.x, c.z - axis.z).magnitude;
                    return c.y >= low && c.y <= high && d <= r && d >= rIn;
                }, axis, "spinner");
                if (turning != null)
                {
                    Mesh radarMesh = null;
                    if (spinner.radar != null && spinner.radar.Used)
                    {
                        var radar = spinner.radar;
                        var centre = shape.Point(new Vector3(radar.centre.x, 0f, radar.centre.z)) - axis;
                        var pivot = shape.Point(radar.pivot) - axis;
                        float radarRadius = radar.radius * width;
                        float radarLow = shape.Frame.min.y + radar.bottom * shape.Frame.size.y;
                        float radarHigh = shape.Frame.min.y + radar.top * shape.Frame.size.y;
                        radarMesh = Cut(turning, c => c.y >= radarLow && c.y <= radarHigh &&
                            new Vector2(c.x - centre.x, c.z - centre.z).magnitude <= radarRadius,
                            pivot, "radar");
                        if (radarMesh == null) Debug.LogWarning("Meshy radar found no triangles; check its region in models.json.");
                    }
                    shape.Spinners.Add((turning, axis, spinner, radarMesh));
                }
                else Debug.LogWarning("Meshy spinner found no triangles; check its centre, radius, bottom and top in models.json.");
            }
            if (modelSettings.teleporter.Used)
            {
                // The tubes' baked contents are dropped; the teleporter fills them with its own goods.
                foreach (var tube in new[] { modelSettings.teleporter.from, modelSettings.teleporter.to })
                {
                    var axis = shape.Point(new Vector3(tube.centre.x, tube.bottom, tube.centre.z));
                    float r = tube.radius * Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
                    float low = axis.y, high = shape.Frame.min.y + tube.top * shape.Frame.size.y;
                    Cut(shape.Mesh, c => c.y >= low && c.y <= high && new Vector2(c.x - axis.x, c.z - axis.z).magnitude <= r, Vector3.zero, "tube contents");
                }
                var t = modelSettings.teleporter;
                shape.TubeFrom = shape.Point(new Vector3(t.from.centre.x, t.from.bottom, t.from.centre.z));
                shape.TubeTo = shape.Point(new Vector3(t.to.centre.x, t.to.bottom, t.to.centre.z));
                shape.TubeRadius = Mathf.Min(t.from.radius, t.to.radius) * Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
                shape.TubeHeight = (Mathf.Min(t.from.top, t.to.top) - Mathf.Max(t.from.bottom, t.to.bottom)) * shape.Frame.size.y;
            }
            foreach (var press in modelSettings.presses)
            {
                var ram = Cut(shape.Mesh, c => press.boxes.Exists(b => b.Contains(Fraction(shape, c))), Vector3.zero, "press");
                if (ram != null) shape.Presses.Add((ram, press));
                else Debug.LogWarning("Meshy press found no triangles; check its boxes in models.json.");
            }
            if (modelSettings.welder.Used)
            {
                var welder = modelSettings.welder;
                shape.WeldShoulder = shape.Point(welder.upper.pivot);
                shape.WeldElbow = shape.Point(welder.fore.pivot);
                shape.WeldTip = shape.Point(welder.tip);
                shape.WeldFore = Cut(shape.Mesh, c => welder.fore.boxes.Exists(b => b.Contains(Fraction(shape, c))), shape.WeldElbow, "welder forearm");
                shape.WeldUpper = Cut(shape.Mesh, c => welder.upper.boxes.Exists(b => b.Contains(Fraction(shape, c))), shape.WeldShoulder, "welder upper arm");
                if (shape.WeldFore == null || shape.WeldUpper == null)
                {
                    Debug.LogWarning("Meshy welder: a part found no triangles; check its boxes in models.json.");
                    shape.WeldUpper = null;
                }
            }
            foreach (var swing in modelSettings.swings) SplitSwing(shape, swing);
            foreach (var glass in modelSettings.glass) SplitGlass(shape, glass);
            foreach (var tank in modelSettings.waterTanks)
                SplitGlass(shape, new Glass { centre = tank.centre, radius = tank.radius, bottom = tank.bottom, top = tank.top, tint = tank.colour });
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

        /// <summary>
        /// Moves the triangles of <paramref name="source"/> whose centres pass <paramref name="take"/> into a new mesh,
        /// with its vertices measured from <paramref name="origin"/> (so it turns about it); null when none pass.
        /// </summary>
        static Mesh Cut(Mesh source, System.Func<Vector3, bool> take, Vector3 origin, string name)
        {
            return CutWhere(source, (index, centre) => take(centre), origin, name);
        }

        /// <summary>
        /// <see cref="Cut"/>, choosing triangles by their running index (across submeshes, in order) and centre.
        /// </summary>
        static Mesh CutWhere(Mesh source, System.Func<int, Vector3, bool> take, Vector3 origin, string name)
        {
            int triangle = 0;
            var vertices = source.vertices;
            var uv = source.uv;
            bool hasUv = uv != null && uv.Length == vertices.Length;
            var normals = source.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;
            var keep = new List<int>[source.subMeshCount];
            var moved = new List<int>[source.subMeshCount];
            var remap = new Dictionary<int, int>();
            var newVertices = new List<Vector3>();
            var newUv = new List<Vector2>();
            var newNormals = new List<Vector3>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                keep[s] = new List<int>();
                moved[s] = new List<int>();
                var t = source.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var c = (vertices[t[i]] + vertices[t[i + 1]] + vertices[t[i + 2]]) / 3f;
                    if (!take(triangle++, c)) { keep[s].Add(t[i]); keep[s].Add(t[i + 1]); keep[s].Add(t[i + 2]); continue; }
                    for (int k = 0; k < 3; k++)
                    {
                        int v = t[i + k], mapped;
                        if (!remap.TryGetValue(v, out mapped))
                        {
                            mapped = newVertices.Count;
                            remap[v] = mapped;
                            newVertices.Add(vertices[v] - origin);
                            if (hasUv) newUv.Add(uv[v]);
                            if (hasNormals) newNormals.Add(normals[v]);
                        }
                        moved[s].Add(mapped);
                    }
                }
            }
            if (newVertices.Count == 0) return null;
            var mesh = new Mesh { name = name, indexFormat = newVertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(newVertices);
            if (hasUv) mesh.SetUVs(0, newUv);
            mesh.subMeshCount = source.subMeshCount;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                mesh.SetTriangles(moved[s], s);
                source.SetTriangles(keep[s], s);
            }
            if (hasNormals) mesh.SetNormals(newNormals); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            source.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Cuts the template leg out of a fitted model, centred on its foot, and drops the other legs; the copies
        /// are stood on the footprint's tile corners by <see cref="PlaceLegs"/>.
        /// </summary>
        static void SplitLegs(Shape shape, Legs legs)
        {
            var box = legs.boxes[legs.template];
            var leg = Cut(shape.Mesh, c => box.Contains(Fraction(shape, c)), Vector3.zero, "leg");
            if (leg == null)
            {
                Debug.LogWarning("Meshy legs: the template box found no triangles; check it in models.json.");
                return;
            }
            Cut(shape.Mesh, c => legs.boxes.Exists(b => b.Contains(Fraction(shape, c))), Vector3.zero, "old legs");
            // Thin bits above the leg's solid top would poke out of the platform once the leg is stretched to meet it.
            if (legs.top > 0f)
            {
                float top = shape.Frame.min.y + legs.top * shape.Frame.size.y;
                Cut(leg, c => c.y > top, Vector3.zero, "leg slivers");
            }
            leg = Cut(leg, c => true, Vector3.zero, "leg"); // only the vertices still in use, so its bounds are the leg's
            // The platform: what the body has in a band just above the legs' tops.
            float from = leg.bounds.max.y, to = from + shape.Frame.size.y * 0.12f;
            var bodyVertices = shape.Mesh.vertices;
            bool found = false;
            for (int s = 0; s < shape.Mesh.subMeshCount; s++)
                foreach (int i in shape.Mesh.GetTriangles(s))
                {
                    var p = bodyVertices[i];
                    if (p.y < from || p.y > to) continue;
                    if (found) shape.Platform.Encapsulate(p); else { shape.Platform = new Bounds(p, Vector3.zero); found = true; }
                }
            var centre = leg.bounds.center;
            var vertices = leg.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] -= new Vector3(centre.x, 0f, centre.z);
            leg.vertices = vertices;
            leg.RecalculateBounds();
            shape.Leg = leg;
            shape.LegPad = legs.pad;
        }

        /// <summary>
        /// Cuts out the whole pieces of the model (triangles joined by shared vertex positions) that lie inside any
        /// of the boxes, re-centred on <paramref name="origin"/>; <paramref name="bounds"/> is their extent in the
        /// model's space. Null when no piece fits.
        /// </summary>
        static Mesh CutPieces(Shape shape, List<Box> boxes, Vector3 origin, string name, out Bounds bounds)
        {
            bounds = new Bounds();
            var picked = PickPieces(shape, boxes, out bounds);
            if (picked == null) return null;
            return CutWhere(shape.Mesh, (index, centre) => picked[index], origin, name);
        }

        /// <summary>For each triangle of the model, whether its piece lies wholly inside one of the boxes (null for none).</summary>
        static bool[] PickPieces(Shape shape, List<Box> boxes, out Bounds bounds)
        {
            bounds = new Bounds();
            var mesh = shape.Mesh;
            var v = mesh.vertices;
            var tris = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) tris.AddRange(mesh.GetTriangles(s));
            // Union-find over vertices: welded by position, then joined through each triangle.
            var parent = new int[v.Length];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            System.Func<int, int> find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            var byPosition = new Dictionary<Vector3Int, int>();
            foreach (int i in tris)
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
            var lo = new Dictionary<int, Vector3>();
            var hi = new Dictionary<int, Vector3>();
            foreach (int i in tris)
            {
                int id = find(i);
                var f = Fraction(shape, v[i]);
                lo[id] = lo.ContainsKey(id) ? Vector3.Min(lo[id], f) : f;
                hi[id] = hi.ContainsKey(id) ? Vector3.Max(hi[id], f) : f;
            }
            const float slack = 0.004f;
            var chosen = new HashSet<int>();
            foreach (var id in lo.Keys)
                foreach (var b in boxes)
                    if (lo[id].x >= b.min.x - slack && lo[id].y >= b.min.y - slack && lo[id].z >= b.min.z - slack
                        && hi[id].x <= b.max.x + slack && hi[id].y <= b.max.y + slack && hi[id].z <= b.max.z + slack) { chosen.Add(id); break; }
            if (chosen.Count == 0) return null;
            var picked = new bool[tris.Count / 3];
            bool any = false;
            for (int t = 0; t < picked.Length; t++)
            {
                if (!chosen.Contains(find(tris[t * 3]))) continue;
                picked[t] = true;
                for (int k = 0; k < 3; k++)
                {
                    var p = v[tris[t * 3 + k]];
                    if (any) bounds.Encapsulate(p); else { bounds = new Bounds(p, Vector3.zero); any = true; }
                }
            }
            return picked;
        }

        /// <summary>Cuts the hoist's carriage, cables and load out of the model, each about the point it moves from.</summary>
        static void SplitHoist(Shape shape, Hoist hoist)
        {
            Bounds carriage, cables, load;
            // Origins are found from each part's own extent, so pick first, then cut about the right point.
            if (PickPieces(shape, hoist.carriage, out carriage) == null || PickPieces(shape, hoist.load, out load) == null)
            {
                Debug.LogWarning("Meshy hoist: the carriage or load boxes hold no whole piece; check them in models.json.");
                return;
            }
            shape.HoistCarriageAt = carriage.center;
            shape.HoistLoadTop = new Vector3(load.center.x, load.max.y, load.center.z);
            shape.HoistCarriage = CutPieces(shape, hoist.carriage, shape.HoistCarriageAt, "hoist carriage", out carriage);
            shape.HoistLoad = CutPieces(shape, hoist.load, shape.HoistLoadTop, "hoist load", out load);
            if (hoist.cables.Count > 0 && PickPieces(shape, hoist.cables, out cables) != null)
            {
                shape.HoistCablesTop = new Vector3(cables.center.x, cables.max.y, cables.center.z);
                shape.HoistCableLength = Mathf.Max(0.0001f, cables.size.y);
                shape.HoistCables = CutPieces(shape, hoist.cables, shape.HoistCablesTop, "hoist cables", out cables);
            }
        }

        /// <summary>
        /// Cuts a welding arm out about the foot of its lowest piece, and finds its tip (the point nearest the hull)
        /// and the level axis it leans about to bring that tip in.
        /// </summary>
        static void SplitWeldingArm(Shape shape, WeldingArm arm, Vector3 hull)
        {
            Bounds extent;
            var picked = PickPieces(shape, arm.boxes, out extent);
            if (picked == null)
            {
                Debug.LogWarning("Meshy welding arm: its boxes hold no whole piece; check them in models.json.");
                return;
            }
            var mesh = shape.Mesh;
            var v = mesh.vertices;
            var tris = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) tris.AddRange(mesh.GetTriangles(s));
            float hullY = shape.Frame.min.y + hull.y * shape.Frame.size.y, hullZ = shape.Frame.min.z + hull.z * shape.Frame.size.z;
            Vector3 foot = Vector3.zero, tip = Vector3.zero;
            float lowest = float.MaxValue, nearest = float.MaxValue;
            for (int t = 0; t < picked.Length; t++)
            {
                if (!picked[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = v[tris[t * 3 + k]];
                    if (p.y < lowest) { lowest = p.y; foot = p; }
                    float d = new Vector2(p.y - hullY, p.z - hullZ).sqrMagnitude;
                    if (d < nearest) { nearest = d; tip = p; }
                }
            }
            // The arm turns about the middle of its foot: the points within a sliver of its lowest point.
            var sum = Vector3.zero;
            int count = 0;
            float band = shape.Frame.size.y * 0.02f;
            for (int t = 0; t < picked.Length; t++)
            {
                if (!picked[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = v[tris[t * 3 + k]];
                    if (p.y <= lowest + band) { sum += p; count++; }
                }
            }
            var pivot = count > 0 ? sum / count : foot;
            pivot.y = lowest;
            var reach = tip - pivot;
            reach.y = 0f;
            if (reach.sqrMagnitude < 1e-8f) reach = new Vector3(0f, 0f, hullZ - pivot.z);
            var axis = Vector3.Cross(Vector3.up, reach.normalized);
            // A positive turn about the axis brings the tip toward the hull; the arm rests turned the other way.
            var toHull = new Vector3(tip.x, hullY, hullZ);
            var leaned = pivot + Quaternion.AngleAxis(arm.angle, axis) * (tip - pivot);
            if ((leaned - toHull).sqrMagnitude > (tip - toHull).sqrMagnitude) axis = -axis;
            var part = CutWhere(mesh, (index, centre) => picked[index], pivot, "welding arm");
            shape.WeldArms.Add((part, pivot, tip - pivot, axis, arm));
        }

        /// <summary>Cuts the tracker's dish (its tilting pieces) and then the rest of its mount, each about its pivot.</summary>
        static void SplitTracker(Shape shape, Tracker tracker)
        {
            Bounds cut;
            shape.TrackerTurnAt = shape.Point(tracker.turnPivot);
            shape.TrackerTiltAt = shape.Point(tracker.tiltPivot);
            if (tracker.tilt.Count > 0) shape.TrackerTilt = CutPieces(shape, tracker.tilt, shape.TrackerTiltAt, "tracker tilt", out cut);
            shape.TrackerTurn = CutPieces(shape, tracker.turn, shape.TrackerTurnAt, "tracker turn", out cut);
            if (shape.TrackerTurn == null && shape.TrackerTilt == null)
                Debug.LogWarning("Meshy tracker: its boxes hold no whole piece; check them in models.json.");
        }

        static Vector3 Fraction(Shape shape, Vector3 point)
        {
            var f = shape.Frame;
            return new Vector3((point.x - f.min.x) / f.size.x, (point.y - f.min.y) / f.size.y, (point.z - f.min.z) / f.size.z);
        }

        /// <summary>
        /// Cuts the loading arm out of a fitted model: the claw first (so the arm's boxes cannot take it), then the
        /// forearm and upper arm, each centred on its pivot; then the claw's fingers, one per sector around the claw's
        /// upright axis, each centred on the hinge at its top.
        /// </summary>
        static void SplitLoader(Shape shape, Loader loader)
        {
            shape.Hang = shape.Point(loader.hang);
            shape.UpperPivot = shape.Point(loader.upper.pivot);
            shape.ForePivot = shape.Point(loader.fore.pivot);
            shape.Claw = Cut(shape.Mesh, c => loader.claw.Exists(b => b.Contains(Fraction(shape, c))), shape.Hang, "claw");
            shape.Fore = Cut(shape.Mesh, c => loader.fore.boxes.Exists(b => b.Contains(Fraction(shape, c))), shape.ForePivot, "forearm");
            shape.Upper = Cut(shape.Mesh, c => loader.upper.boxes.Exists(b => b.Contains(Fraction(shape, c))), shape.UpperPivot, "upper arm");
            if (shape.Claw == null || shape.Fore == null || shape.Upper == null)
            {
                Debug.LogWarning("Meshy loader: a part found no triangles; check its boxes in models.json.");
                shape.Upper = null;
                return;
            }
            // Fingers: the claw's triangles in the finger band, grouped into sectors around its upright axis.
            float low = shape.Frame.min.y + loader.fingerBottom * shape.Frame.size.y - shape.Hang.y;
            float high = shape.Frame.min.y + loader.fingerTop * shape.Frame.size.y - shape.Hang.y;
            var clawVertices = shape.Claw.vertices;
            var axis = Vector2.zero;
            foreach (var v in clawVertices) axis += new Vector2(v.x, v.z);
            axis /= Mathf.Max(1, clawVertices.Length);
            int sectors = Mathf.Max(1, loader.fingers);
            for (int k = 0; k < sectors; k++)
            {
                float from = k * 360f / sectors - 180f / sectors, to = from + 360f / sectors;
                System.Func<Vector3, bool> inSector = c =>
                {
                    if (c.y < low || c.y > high) return false;
                    float a = Mathf.Atan2(c.z - axis.y, c.x - axis.x) * Mathf.Rad2Deg;
                    a = Mathf.Repeat(a - from, 360f);
                    return a < to - from;
                };
                float mid = (from + to) * 0.5f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                // The hinge sits at the top of the finger, on its outer side's radius.
                float radius = 0f;
                int n = 0;
                foreach (var v in clawVertices)
                {
                    if (v.y < high - (high - low) * 0.25f || v.y > high) continue;
                    var d = new Vector2(v.x - axis.x, v.z - axis.y);
                    if (Vector2.Dot(d, new Vector2(dir.x, dir.z)) <= 0f) continue;
                    radius += d.magnitude;
                    n++;
                }
                radius = n > 0 ? radius / n : 0f;
                var hinge = new Vector3(axis.x, high, axis.y) + dir * radius;
                var finger = Cut(shape.Claw, inSector, hinge, "finger " + k);
                if (finger != null) shape.Fingers.Add((finger, hinge, Vector3.Cross(Vector3.up, dir)));
            }
        }

        /// <summary>
        /// Builds the loading arm under the body: shoulder, elbow, a cable from the wrist and the claw on it (kept
        /// upright), with its fingers on hinges, and drives one loading cycle on a loop.
        /// </summary>
        static void AddLoader(Model model, Transform body, Shape shape, Loader loader)
        {
            System.Func<string, Transform, Vector3, Mesh, Transform> piece = (name, parent, position, mesh) =>
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                if (mesh != null)
                {
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterials = shape.Materials;
                    model.Renderers.Add(r);
                    model.Moving.Add(r);
                }
                return go.transform;
            };
            var upper = piece("Shoulder", body, shape.UpperPivot, shape.Upper);
            var fore = piece("Elbow", upper, shape.ForePivot - shape.UpperPivot, shape.Fore);
            var hang = piece("Wrist", fore, shape.Hang - shape.ForePivot, null);
            var claw = piece("Claw", hang, Vector3.zero, shape.Claw);
            var fingers = new List<(Transform t, Vector3 axis)>();
            foreach (var f in shape.Fingers) fingers.Add((piece("Finger", claw, f.hinge, f.mesh), f.axis));

            // The cable: a thin dark cylinder from the wrist down to the claw, stretched as it lowers.
            var cableKit = new MeshKit();
            cableKit.Cylinder(Vector3.zero, 0.006f, 1f, 6, Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome));
            var cable = cableKit.Instantiate("Cable", hang, false);
            var cableRenderer = cable.GetComponent<MeshRenderer>();
            model.Renderers.Add(cableRenderer);
            model.Moving.Add(cableRenderer);
            cable.transform.localScale = new Vector3(1f, 0.0001f, 1f);

            var upperAxis = loader.upper.axis.sqrMagnitude > 0f ? loader.upper.axis.normalized : Vector3.forward;
            var foreAxis = loader.fore.axis.sqrMagnitude > 0f ? loader.fore.axis.normalized : Vector3.forward;
            float drop = loader.drop * shape.Frame.size.y, period = Mathf.Max(2f, loader.period), clock = 0f;
            model.Motions.Add(dt =>
            {
                clock += dt;
                float p = Mathf.Repeat(clock / period, 1f);
                // Keyframes: reach out, lower, grip, raise, swing back, lower, release, raise.
                float reach = p < 0.2f ? Ease(p / 0.2f) : p < 0.55f ? 1f : p < 0.75f ? 1f - Ease((p - 0.55f) / 0.2f) : 0f;
                float lower = Window(p, 0.2f, 0.32f, 0.42f, 0.55f) + Window(p, 0.75f, 0.83f, 0.9f, 1f);
                float grip = p < 0.35f ? 0f : p < 0.4f ? Ease((p - 0.35f) / 0.05f) : p < 0.85f ? 1f : p < 0.9f ? 1f - Ease((p - 0.85f) / 0.05f) : 0f;
                var upperTurn = Quaternion.AngleAxis(loader.upper.reach * reach, upperAxis);
                var foreTurn = Quaternion.AngleAxis(loader.fore.reach * reach, foreAxis);
                upper.localRotation = upperTurn;
                fore.localRotation = foreTurn;
                hang.localRotation = Quaternion.Inverse(upperTurn * foreTurn); // the claw hangs straight down
                float length = drop * lower;
                claw.localPosition = Vector3.down * length;
                cable.transform.localPosition = Vector3.down * length;
                cable.transform.localScale = new Vector3(1f, Mathf.Max(0.0001f, length), 1f);
                foreach (var f in fingers) f.t.localRotation = Quaternion.AngleAxis(loader.open * (1f - grip), f.axis);
            });
        }

        static float Ease(float t) { return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)); }

        const int ShadeLayers = 7;
        /// <summary>The first layer stands this far in from the footprint's edge (a share of it), so the shade begins just under the building, never outside it.</summary>
        const float ShadeInset = 0.82f;
        /// <summary>Each layer's darkness and how much smaller than the last it is: close together, so sight drops off quickly once under.</summary>
        const float ShadeLayerAlpha = 0.4f, ShadeLayerStep = 0.09f;
        static Material shadeWalls, shadeGround;

        /// <summary>
        /// Shade under an elevated building: a soft dark patch on the ground, and <see cref="ShadeLayers"/> nested
        /// see-through walls from the ground to the platform's underside, each smaller than the last and each darker
        /// toward the top. Looking further in passes through more of them, so what goes under fades into the building.
        /// One mesh, in the body's space; the deck is the body's y = 0.
        /// </summary>
        static void AddUnderShade(Model model, Transform body, Shape shape, float undersideShare)
        {
            if (!ShadeMaterials()) return;
            var frame = shape.Frame;
            float under = undersideShare > 0f ? frame.min.y + undersideShare * frame.size.y : FindUnderside(shape.Mesh, frame);
            if (under <= 0.04f) return;
            // The first wall stands a little in from the edge, so nothing outside the building is shaded.
            float hx = frame.extents.x * ShadeInset, hz = frame.extents.z * ShadeInset;
            var centre = new Vector3(frame.center.x, 0f, frame.center.z);

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var walls = new List<int>();
            var ground = new List<int>();
            System.Action<Vector3, Vector3, Vector3, List<int>> quad = (origin, across, up, into) =>
            {
                int i = vertices.Count;
                vertices.Add(origin); vertices.Add(origin + across); vertices.Add(origin + across + up); vertices.Add(origin + up);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                // Both faces, so the walls shade from inside and out.
                into.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2, i, i + 1, i + 2, i, i + 2, i + 3 });
            };
            // The ground patch: within the footprint, fading out before its edge, just above the deck.
            float gx = frame.extents.x * 0.95f, gz = frame.extents.z * 0.95f;
            quad(centre + new Vector3(-gx, 0.006f, -gz), new Vector3(gx * 2f, 0f, 0f), new Vector3(0f, 0f, gz * 2f), ground);
            var height = new Vector3(0f, under - 0.005f, 0f);
            for (int layer = 0; layer < ShadeLayers; layer++)
            {
                float shrink = Mathf.Max(0.2f, 1f - layer * ShadeLayerStep), lx = hx * shrink, lz = hz * shrink;
                var a = centre + new Vector3(-lx, 0f, -lz);
                var b = centre + new Vector3(lx, 0f, -lz);
                var c = centre + new Vector3(lx, 0f, lz);
                var d = centre + new Vector3(-lx, 0f, lz);
                quad(a, b - a, height, walls);
                quad(b, c - b, height, walls);
                quad(c, d - c, height, walls);
                quad(d, a - d, height, walls);
            }
            var mesh = new Mesh { name = "Under shade" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(ground, 0);
            mesh.SetTriangles(walls, 1);
            mesh.RecalculateBounds();
            var go = new GameObject("UnderShade");
            go.transform.SetParent(body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { shadeGround, shadeWalls };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            model.Shades.Add(r);
        }

        /// <summary>
        /// A soft dark patch on the ground under a building's footprint (just above the deck), fading out before the
        /// edge: a contact shadow, not the layered shade of an elevated building. Shown except in Belt View.
        /// </summary>
        public static void AddGroundShadow(Model model, Transform parent, Bounds frame, float strength)
        {
            if (!ShadeMaterials()) return;
            float gx = frame.extents.x * 0.92f, gz = frame.extents.z * 0.92f;
            var c = new Vector3(frame.center.x, 0.006f, frame.center.z);
            var mesh = new Mesh { name = "Ground shadow" };
            mesh.SetVertices(new List<Vector3> { c + new Vector3(-gx, 0, -gz), c + new Vector3(gx, 0, -gz), c + new Vector3(gx, 0, gz), c + new Vector3(-gx, 0, gz) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("GroundShadow");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material(shadeGround) { name = "Ground shadow" };
            m.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.04f, Mathf.Clamp01(strength)));
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            model.Shades.Add(r);
        }

        /// <summary>
        /// The platform's underside: the lowest point of the model over the middle of its footprint (where there are
        /// no legs), at least a little above the ground.
        /// </summary>
        static float FindUnderside(Mesh mesh, Bounds frame)
        {
            float low = float.MaxValue;
            float floor = frame.min.y + frame.size.y * 0.08f;
            foreach (var v in mesh.vertices)
            {
                if (Mathf.Abs(v.x - frame.center.x) > frame.extents.x * 0.5f || Mathf.Abs(v.z - frame.center.z) > frame.extents.z * 0.5f) continue;
                if (v.y > floor && v.y < low) low = v.y;
            }
            return low == float.MaxValue ? 0f : low;
        }

        /// <summary>The shade's two see-through dark materials, built once from the dust material with generated gradients.</summary>
        static bool ShadeMaterials()
        {
            if (shadeWalls != null) return true;
            var dust = Resources.Load<Material>("Materials/Dust");
            if (dust == null) return false;
            const int n = 64;
            // Walls: soft at the sides, rising from faint at the ground to full under the platform.
            var wallTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Shade walls" };
            // Ground: a rounded patch, clear at its edge and dark inside.
            var groundTexture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Shade ground" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / n * 2f - 1f), v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    float side = Mathf.SmoothStep(0f, 1f, (1f - u) / 0.15f);
                    float rise = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, y / (n - 1f)));
                    wallTexture.SetPixel(x, y, new Color(1f, 1f, 1f, side * rise));
                    groundTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, (1f - Mathf.Max(u, v)) / 0.4f) * 0.9f));
                }
            wallTexture.Apply();
            groundTexture.Apply();
            shadeWalls = new Material(dust) { name = "Under shade walls" };
            shadeWalls.SetTexture("_BaseMap", wallTexture);
            shadeWalls.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.04f, ShadeLayerAlpha));
            shadeGround = new Material(dust) { name = "Under shade ground" };
            shadeGround.SetTexture("_BaseMap", groundTexture);
            shadeGround.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.04f, 1f));
            return true;
        }

        /// <summary>Colours for carousel goods, so they read without the catalog.</summary>
        static readonly Dictionary<string, Color> GoodsColours = new Dictionary<string, Color>
        {
            { "ore", new Color(0.62f, 0.56f, 0.52f) }, { "iron_bar", new Color(0.72f, 0.74f, 0.78f) },
            { "copper_ore", new Color(0.78f, 0.5f, 0.32f) }, { "copper_bar", new Color(0.85f, 0.52f, 0.3f) },
            { "water", new Color(0.45f, 0.75f, 0.95f) }, { "copper_wire", new Color(0.9f, 0.55f, 0.3f) },
            { "iron_plate", new Color(0.7f, 0.72f, 0.76f) }
        };

        /// <summary>
        /// A carousel in place of the model's own shelf contents (cut out as <paramref name="removed"/> and dropped): a
        /// central column and round shelves, each ringed with goods, all under <paramref name="spinner"/> so they turn together.
        /// </summary>
        static void BuildCarousel(Model model, Transform spinner, Shape shape, Mesh removed, Spinner settings)
        {
            var b = removed.bounds; // in the spinner's own space, centred on its axis
            float radius = Mathf.Min(b.extents.x, b.extents.z) * 0.82f;
            if (radius <= 0f) return;
            var kit = new MeshKit();
            var shelf = Palette.Get(Palette.Seafoam, Palette.Finish.Chrome);
            var column = Palette.Get(Palette.Mustard);
            float bottom = b.min.y, top = b.max.y;
            kit.Cylinder(new Vector3(0f, bottom, 0f), radius * 0.16f, top - bottom, 12, column);
            foreach (float h in settings.shelves)
            {
                float y = shape.Frame.min.y + h * shape.Frame.size.y;
                kit.Cylinder(new Vector3(0f, y - 0.006f, 0f), radius, 0.012f, 24, shelf);
            }
            var frame = kit.Instantiate("Carousel", spinner, false);
            var frameRenderer = frame.GetComponent<MeshRenderer>();
            model.Renderers.Add(frameRenderer);
            model.Moving.Add(frameRenderer);

            int n = Mathf.Max(1, settings.perShelf), next = 0;
            float itemScale = Mathf.Clamp(radius * 1.5f, 0.25f, 0.9f);
            for (int level = 0; level < settings.shelves.Count; level++)
            {
                float y = shape.Frame.min.y + settings.shelves[level] * shape.Frame.size.y + 0.006f;
                for (int i = 0; i < n; i++)
                {
                    string id = settings.goods.Count > 0 ? settings.goods[next++ % settings.goods.Count] : "ore";
                    Color colour;
                    if (!GoodsColours.TryGetValue(id, out colour)) colour = Palette.Cream;
                    var good = ModelLibrary.Cargo(id, colour, spinner);
                    float a = (i + level * 0.5f) / n * Mathf.PI * 2f;
                    good.transform.localPosition = new Vector3(Mathf.Cos(a) * radius * 0.72f, y, Mathf.Sin(a) * radius * 0.72f);
                    good.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                    good.transform.localScale = Vector3.one * itemScale;
                    var r = good.GetComponent<MeshRenderer>();
                    if (r == null) continue;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    model.Renderers.Add(r);
                    model.Moving.Add(r);
                }
            }
        }

        /// <summary>A soft additive glow, gently pulsing, lit only while the building works.</summary>
        static void AddGlow(Model model, Transform body, Shape shape, Glow settings)
        {
            Color colour;
            if (!ColorUtility.TryParseHtmlString(settings.colour, out colour)) colour = new Color(1f, 0.78f, 0.38f);
            float k = Mathf.Clamp01(settings.strength) * 0.5f;
            var kit = new MeshKit();
            kit.Sphere(Vector3.zero, settings.radius * Mathf.Max(shape.Frame.size.x, shape.Frame.size.z), 16, Palette.Flat(new Color(colour.r * k, colour.g * k, colour.b * k), true));
            var glow = kit.Instantiate("Glow", body, false);
            glow.transform.localPosition = shape.Point(settings.at);
            model.Lights.Add(glow.GetComponent<MeshRenderer>());
            float clock = 0f;
            var t = glow.transform;
            model.Motions.Add(dt =>
            {
                clock += dt;
                if (settings.flashPeriod > 0f)
                {
                    // Two quick transmissions, then a long pause. Scaling the additive halo leaves the textured orb visible.
                    float phase = Mathf.Repeat(clock, settings.flashPeriod);
                    bool lit = phase < 0.12f || (phase >= 0.24f && phase < 0.36f);
                    t.localScale = Vector3.one * (lit ? 1f : 0f);
                }
                else t.localScale = Vector3.one * (0.92f + 0.1f * Mathf.Sin(clock * 1.7f));
            });
        }

        /// <summary>Bubble columns and a breathing light inside a tank's transparent window.</summary>
        static void AddWaterTank(Model model, Transform body, Shape shape, WaterTank settings)
        {
            if (settings.radius <= 0f || settings.top <= settings.bottom) return;
            Color colour;
            if (!ColorUtility.TryParseHtmlString(settings.colour, out colour)) colour = new Color(0.34f, 0.85f, 1f);
            float width = Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
            float radius = settings.radius * width;
            var floor = shape.Point(new Vector3(settings.centre.x, settings.bottom, settings.centre.z));
            float height = (settings.top - settings.bottom) * shape.Frame.size.y;

            var coreKit = new MeshKit();
            coreKit.Cylinder(Vector3.zero, radius * 0.58f, height * 0.85f, 16,
                Palette.Flat(new Color(colour.r * 0.18f, colour.g * 0.18f, colour.b * 0.18f), true));
            var core = coreKit.Instantiate("TankPulse", body, false);
            core.transform.localPosition = floor + Vector3.up * (height * 0.08f);
            model.Lights.Add(core.GetComponent<MeshRenderer>());
            float pulseClock = 0f;
            float period = Mathf.Max(0.5f, settings.pulsePeriod);
            model.Motions.Add(dt =>
            {
                pulseClock += dt;
                float beat = 0.5f + 0.5f * Mathf.Sin(pulseClock * Mathf.PI * 2f / period);
                core.transform.localScale = new Vector3(0.8f + 0.38f * beat, 1f, 0.8f + 0.38f * beat);
            });

            var bubblePaint = Palette.Flat(new Color(0.65f, 0.95f, 1f) * 0.85f, true);
            for (int i = 0; i < 11; i++)
            {
                int bubble = i;
                float phase = (bubble * 0.618034f) % 1f;
                float angle = bubble * 2.39996f;
                float offset = radius * (0.22f + 0.33f * ((bubble * 7) % 5) / 4f);
                var kit = new MeshKit();
                kit.Sphere(Vector3.zero, radius * (0.065f + 0.023f * (bubble % 3)), 8, bubblePaint);
                var go = kit.Instantiate("Bubble", body, false);
                var transform = go.transform;
                model.Lights.Add(go.GetComponent<MeshRenderer>());
                float clock = 0f;
                model.Motions.Add(dt =>
                {
                    clock += dt;
                    float rise = Mathf.Repeat(phase + clock * (0.22f + 0.024f * (bubble % 4)), 1f);
                    float sway = Mathf.Sin(clock * 1.3f + bubble) * radius * 0.1f;
                    transform.localPosition = floor + new Vector3(Mathf.Cos(angle) * offset + sway,
                        height * (0.04f + 0.91f * rise), Mathf.Sin(angle) * offset);
                    transform.localScale = Vector3.one * Mathf.Min(1f, (1f - rise) * 5f);
                });
                model.Motions[model.Motions.Count - 1](0f);
            }
        }

        /// <summary>Blue points of light circling one band on the purifier's middle tower.</summary>
        static void AddOrbitRing(Model model, Transform body, Shape shape, OrbitRing settings)
        {
            if (settings.radius <= 0f || settings.lamps <= 0) return;
            Color colour;
            if (!ColorUtility.TryParseHtmlString(settings.colour, out colour)) colour = new Color(0.34f, 0.85f, 1f);
            float width = Mathf.Max(shape.Frame.size.x, shape.Frame.size.z);
            float radius = settings.radius * width;
            var orbit = new GameObject("OrbitRing").transform;
            orbit.SetParent(body, false);
            orbit.localPosition = shape.Point(new Vector3(0.5f, settings.height, 0.5f));
            var lampPaint = Palette.Flat(new Color(colour.r * 0.85f, colour.g * 0.85f, colour.b * 0.85f), true);
            int count = Mathf.Clamp(settings.lamps, 1, 12);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var kit = new MeshKit();
                kit.Sphere(Vector3.zero, width * 0.018f, 10, lampPaint);
                var lamp = kit.Instantiate("OrbitLamp", orbit, false);
                lamp.transform.localPosition = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                model.Lights.Add(lamp.GetComponent<MeshRenderer>());
            }
            float clock = 0f;
            model.Motions.Add(dt =>
            {
                clock += dt;
                orbit.localRotation = Quaternion.Euler(0f, clock * settings.speed, 0f);
            });
        }

        /// <summary>One tube's goods, the copies of their materials it fades, and its effects.</summary>
        sealed class TubeShow
        {
            public Transform Goods;
            public readonly List<(Material material, Color colour)> Fades = new List<(Material, Color)>();
            public Transform Beam, Ring;
            public Material BeamPaint, RingPaint;
            public ParticleSystem Sparkles;
            public float Shown = -1f;
        }

        /// <summary>
        /// The sale depot's teleporter: crates in one tube, gold coins in the other, each faded through see-through
        /// copies of its materials, with a beam, a scanning ring and sparkles while it teleports. One cycle: crates
        /// sit, teleport out, coins teleport in, sit, then both tubes quietly restock.
        /// </summary>
        static void AddTeleporter(Model model, Transform body, Shape shape, Teleporter settings)
        {
            Color beamColour;
            if (!ColorUtility.TryParseHtmlString(settings.beam, out beamColour)) beamColour = new Color(0.5f, 0.9f, 1f);
            float r = shape.TubeRadius, h = shape.TubeHeight;
            var crates = MakeTube(body, shape.TubeFrom, r, h, beamColour);
            var coins = MakeTube(body, shape.TubeTo, r, h, beamColour);
            FillCrates(crates, r, h);
            FillCoins(coins, r, h);
            foreach (var show in new[] { crates, coins })
            {
                foreach (var rr in show.Goods.GetComponentsInChildren<MeshRenderer>()) { model.Renderers.Add(rr); model.Moving.Add(rr); }
                model.Lights.Add(show.Beam.GetComponent<MeshRenderer>());
                model.Lights.Add(show.Ring.GetComponent<MeshRenderer>());
                if (show.Sparkles != null) model.Effects.Add(show.Sparkles);
            }
            float period = Mathf.Max(2f, settings.period), clock = 0f;
            Show(crates, 1f, 0f, 0f, 0f, h); // start with crates waiting and the beams off
            Show(coins, 0f, 0f, 0f, 0f, h);
            model.Motions.Add(dt =>
            {
                clock += dt;
                float p = Mathf.Repeat(clock / period, 1f);
                // Crates: shown, teleport out (0.30-0.50), gone, restock (0.90-1.00). Coins: teleport in (0.50-0.70), shown, gone at restock.
                float crate = p < 0.30f ? 1f : p < 0.50f ? 1f - (p - 0.30f) / 0.20f : p < 0.90f ? 0f : (p - 0.90f) / 0.10f;
                float coin = p < 0.50f ? 0f : p < 0.70f ? (p - 0.50f) / 0.20f : p < 0.90f ? 1f : 1f - (p - 0.90f) / 0.10f;
                float crateBeam = p >= 0.28f && p < 0.52f ? Mathf.Sin((p - 0.28f) / 0.24f * Mathf.PI) : 0f;
                float coinBeam = p >= 0.48f && p < 0.72f ? Mathf.Sin((p - 0.48f) / 0.24f * Mathf.PI) : 0f;
                Show(crates, crate, crateBeam, (p - 0.28f) / 0.24f, clock, h);
                Show(coins, coin, coinBeam, (p - 0.48f) / 0.24f, clock, h);
            });
        }

        static TubeShow MakeTube(Transform body, Vector3 floor, float radius, float height, Color beamColour)
        {
            var show = new TubeShow();
            show.Goods = new GameObject("Goods").transform;
            show.Goods.SetParent(body, false);
            show.Goods.localPosition = floor;
            // A column of light filling the tube, and a ring that scans up and down it.
            show.BeamPaint = new Material(Palette.Flat(beamColour, true)) { name = "Teleport beam" };
            var beamKit = new MeshKit();
            beamKit.Cylinder(Vector3.zero, radius * 0.92f, height, 16, show.BeamPaint);
            var beam = beamKit.Instantiate("Beam", body, false);
            beam.transform.localPosition = floor;
            show.Beam = beam.transform;
            show.RingPaint = new Material(Palette.Flat(beamColour, true)) { name = "Teleport ring" };
            var ringKit = new MeshKit();
            ringKit.Torus(Vector3.zero, radius * 0.95f, radius * 0.06f, 24, 6, show.RingPaint);
            var ring = ringKit.Instantiate("Ring", body, false);
            ring.transform.localPosition = floor;
            show.Ring = ring.transform;
            show.Sparkles = WorkDust.Teleport(body, floor, radius, height, beamColour);
            return show;
        }

        /// <summary>A material copy that fades (see-through), remembered with its full colour.</summary>
        static Material Fading(TubeShow show, Material source)
        {
            var m = new Material(Palette.Faded(source)) { name = source.name + " teleport" };
            var c = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white;
            c.a = 1f;
            m.SetColor("_BaseColor", c);
            show.Fades.Add((m, c));
            return m;
        }

        static void Dress(TubeShow show, GameObject piece)
        {
            var r = piece.GetComponent<MeshRenderer>();
            if (r == null) return;
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = Fading(show, mats[i]);
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Crates stacked two by two, three high, in the tube's colours of trade.</summary>
        static void FillCrates(TubeShow show, float radius, float height)
        {
            var colours = new[] { Palette.Cherry, Palette.Seafoam, Palette.Mustard, Palette.Atomic };
            float crate = Mathf.Min(radius * 0.62f, height / 3.4f); // a crate's size, about
            float scale = crate / 0.22f, step = crate * 1.02f, k = 0;
            for (int layer = 0; layer < 3; layer++)
                for (int i = 0; i < 4; i++)
                {
                    if (layer == 2 && i % 2 == 1) continue; // a smaller top layer
                    var box = ModelLibrary.Cargo("crate", colours[(int)k++ % colours.Length], show.Goods);
                    float x = (i % 2 - 0.5f) * step, z = (i / 2 - 0.5f) * step;
                    box.transform.localPosition = new Vector3(x, layer * crate * 0.98f, z);
                    box.transform.localRotation = Quaternion.Euler(0f, (i * 23 + layer * 41) % 30 - 15, 0f);
                    box.transform.localScale = Vector3.one * scale;
                    Dress(show, box);
                }
        }

        /// <summary>Stacks of gold coins of different heights, with a few lying loose.</summary>
        static void FillCoins(TubeShow show, float radius, float height)
        {
            var gold = Palette.Get(Palette.Hex(0xF2C230), Palette.Finish.Chrome);
            var kit = new MeshKit();
            float coin = radius * 0.27f, thick = coin * 0.3f;
            var stacks = new[] { new Vector3(0f, 0f, 0f), new Vector3(0.45f, 0f, 0.1f), new Vector3(-0.4f, 0f, 0.25f), new Vector3(0.1f, 0f, -0.45f),
                                 new Vector3(-0.3f, 0f, -0.35f), new Vector3(0.35f, 0f, 0.48f), new Vector3(-0.55f, 0f, -0.05f) };
            int[] heights = { 22, 15, 18, 12, 16, 10, 13 };
            for (int s = 0; s < stacks.Length; s++)
                for (int i = 0; i < heights[s]; i++)
                {
                    var at = stacks[s] * radius * 0.85f + new Vector3(Mathf.Sin(i * 1.7f + s) * coin * 0.08f, i * thick, Mathf.Cos(i * 1.3f + s) * coin * 0.08f);
                    kit.Cylinder(at, coin, thick * 0.9f, 12, gold);
                }
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.3f;
                kit.Push(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * 0.65f + Vector3.up * thick, Quaternion.Euler(70f, a * 57f, 0f)).Cylinder(Vector3.zero, coin, thick * 0.9f, 12, gold).Pop();
            }
            var pile = kit.Instantiate("Coins", show.Goods, false);
            Dress(show, pile);
        }

        /// <summary>Sets a tube's goods to <paramref name="visible"/> (0-1), and its beam, ring and sparkles to <paramref name="teleport"/>.</summary>
        static void Show(TubeShow show, float visible, float teleport, float sweep, float clock, float height)
        {
            visible = Mathf.Clamp01(visible);
            // A shimmer while teleporting, so the goods flicker as they go or arrive.
            float shown = teleport > 0.01f ? visible * (0.75f + 0.25f * Mathf.Sin(clock * 40f)) : visible;
            if (Mathf.Abs(shown - show.Shown) > 0.003f)
            {
                show.Shown = shown;
                foreach (var f in show.Fades) f.material.SetColor("_BaseColor", new Color(f.colour.r, f.colour.g, f.colour.b, shown));
                show.Goods.gameObject.SetActive(shown > 0.01f);
            }
            show.BeamPaint.SetColor("_BaseColor", new Color(0.55f, 0.9f, 1f) * (0.45f * teleport));
            show.RingPaint.SetColor("_BaseColor", new Color(0.7f, 0.95f, 1f) * (1.2f * teleport));
            show.Beam.gameObject.SetActive(teleport > 0.01f);
            show.Ring.gameObject.SetActive(teleport > 0.01f);
            show.Ring.localPosition = new Vector3(show.Ring.localPosition.x, Mathf.PingPong(Mathf.Clamp01(sweep) * 2f, 1f) * height, show.Ring.localPosition.z);
            if (show.Sparkles != null)
            {
                var emission = show.Sparkles.emission;
                emission.enabled = teleport > 0.2f;
            }
        }

        /// <summary>A press ram: a slow lift, a short pause at the top, a fast slam back onto the die, then rest.</summary>
        static void AddPress(Model model, Transform body, Shape shape, Mesh mesh, Press settings)
        {
            var go = new GameObject("Press");
            go.transform.SetParent(body, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = shape.Materials;
            model.Renderers.Add(r);
            model.Moving.Add(r);
            float travel = settings.travel * shape.Frame.size.y, period = Mathf.Max(0.5f, settings.period), clock = 0f;
            var t = go.transform;
            model.Motions.Add(dt =>
            {
                clock += dt;
                float p = Mathf.Repeat(clock / period, 1f);
                float lift = p < 0.55f ? Ease(p / 0.55f) : p < 0.68f ? 1f : p < 0.72f ? 1f - (p - 0.68f) / 0.04f : 0f;
                t.localPosition = Vector3.up * (travel * lift);
            });
        }

        /// <summary>
        /// A welding arm: shoulder and elbow bend together to dip the torch onto the work, hold there throwing sparks
        /// (with a flickering glow at the tip), then lift away and pause.
        /// </summary>
        static void AddWelder(Model model, Transform body, Shape shape, Welder welder)
        {
            System.Func<string, Transform, Vector3, Mesh, Transform> piece = (name, parent, position, mesh) =>
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = shape.Materials;
                model.Renderers.Add(r);
                model.Moving.Add(r);
                return go.transform;
            };
            var upper = piece("WeldShoulder", body, shape.WeldShoulder, shape.WeldUpper);
            var fore = piece("WeldElbow", upper, shape.WeldElbow - shape.WeldShoulder, shape.WeldFore);
            var tipPoint = shape.WeldTip - shape.WeldElbow;

            var sparks = WorkDust.Sparks(fore, tipPoint);
            var glowKit = new MeshKit();
            glowKit.Sphere(Vector3.zero, 0.025f, 10, Palette.Flat(new Color(0.55f, 0.75f, 1f), true));
            var glow = glowKit.Instantiate("WeldGlow", fore, false);
            glow.transform.localPosition = tipPoint;
            var glowRenderer = glow.GetComponent<MeshRenderer>();
            glowRenderer.enabled = false;

            var upperAxis = welder.upper.axis.sqrMagnitude > 0f ? welder.upper.axis.normalized : Vector3.forward;
            var foreAxis = welder.fore.axis.sqrMagnitude > 0f ? welder.fore.axis.normalized : Vector3.forward;
            float period = Mathf.Max(1f, welder.period), clock = 0f;
            model.Motions.Add(dt =>
            {
                clock += dt;
                float p = Mathf.Repeat(clock / period, 1f);
                float dip = p < 0.22f ? Ease(p / 0.22f) : p < 0.62f ? 1f : p < 0.85f ? 1f - Ease((p - 0.62f) / 0.23f) : 0f;
                bool welding = p >= 0.24f && p < 0.6f;
                // A small tremble while welding, as the torch works along the seam.
                float jitter = welding ? Mathf.Sin(clock * 37f) * 0.6f : 0f;
                // The model is posed welding: lifted away when not, back on the work when welding.
                upper.localRotation = Quaternion.AngleAxis(welder.upper.reach * (1f - dip) + jitter, upperAxis);
                fore.localRotation = Quaternion.AngleAxis(welder.fore.reach * (1f - dip), foreAxis);
                if (sparks != null)
                {
                    var emission = sparks.emission;
                    emission.enabled = welding;
                    if (welding && !sparks.isPlaying) sparks.Play();
                }
                glowRenderer.enabled = welding && Mathf.PerlinNoise(clock * 18f, 0.3f) > 0.35f; // a flickering arc
                glow.transform.localScale = Vector3.one * (0.7f + Mathf.PerlinNoise(clock * 25f, 0.8f) * 0.8f);
            });
            if (sparks != null) model.Effects.Add(sparks);
        }

        /// <summary>0 before <paramref name="a"/>, easing up to 1 by <paramref name="b"/>, held to <paramref name="c"/>, easing back to 0 by <paramref name="d"/>.</summary>
        static float Window(float p, float a, float b, float c, float d)
        {
            if (p < a || p > d) return 0f;
            if (p < b) return Ease((p - a) / (b - a));
            if (p <= c) return 1f;
            return 1f - Ease((p - c) / (d - c));
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
