using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Generates small alpha-backed sprites at runtime with distinct silhouettes. Shapes are drawn in light greys so a
    /// SpriteRenderer tint supplies faction or item colour. Each sprite is created once and cached.
    /// </summary>
    public sealed class SpaceSpriteLibrary
    {
        const int Size = 64;
        readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        readonly HashSet<string> reportedMissing = new HashSet<string>();

        public static readonly Color PlayerColor = new Color(0.30f, 0.62f, 1f);
        public static readonly Color AiColor = new Color(1f, 0.36f, 0.36f);
        public static readonly Color NeutralColor = new Color(0.55f, 0.58f, 0.66f);

        public static Color FactionColor(FactionId f)
        {
            return f == FactionId.Player ? PlayerColor : f == FactionId.Ai ? AiColor : NeutralColor;
        }

        public static Color Hex(string hex, Color fallback)
        {
            Color c;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c) ? c : fallback;
        }

        public Sprite GetCellSprite(FactionId owner) { return Get("cell", PaintCell); }
        const int GridPixelsPerTile = 16;

        /// <summary>
        /// Grid overlay for a whole cell with exactly 16 pixels per tile, so every tile is drawn the same size.
        /// One world unit per tile.
        /// </summary>
        public Sprite GetGridSprite(int tilesX, int tilesY)
        {
            string key = "grid_" + tilesX + "x" + tilesY;
            Sprite s;
            if (cache.TryGetValue(key, out s)) return s;
            int w = tilesX * GridPixelsPerTile, h = tilesY * GridPixelsPerTile;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            var pixels = new Color[w * h];
            var line = new Color(1, 1, 1, 0.2f);
            var border = new Color(1, 1, 1, 0.9f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool edge = x < 2 || y < 2 || x >= w - 2 || y >= h - 2;
                    bool tileLine = x % GridPixelsPerTile == 0 || y % GridPixelsPerTile == 0;
                    pixels[y * w + x] = edge ? border : tileLine ? line : Color.clear;
                }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), GridPixelsPerTile);
            s.name = key;
            cache[key] = s;
            return s;
        }
        public Sprite GetBlockedSprite() { return Get("blocked", PaintRock); }
        public Sprite GetNodeSprite(string itemId) { return Get("node", PaintNode); }
        public Sprite GetBeltSprite() { return Get("belt", PaintArrow); }
        public Sprite GetBeltTrackSprite() { return Get("belt_track", PaintBeltTrack); }
        public Sprite GetBeltChevronSprite() { return Get("belt_chevron", PaintChevron); }
        public Sprite GetBeltPadSprite() { return Get("belt_pad", PaintBeltPad); }
        public Sprite GetTokenSprite(string itemId) { return Get("token", PaintToken); }
        public Sprite GetControlPointSprite() { return Get("control", PaintRing); }
        public Sprite GetRingSprite() { return Get("ring", PaintThinRing); }
        public Sprite GetSquareSprite() { return Get("square", PaintSquare); }
        public Sprite GetShadowSprite() { return Get("shadow", PaintShadow); }
        public Sprite GetAlertSprite() { return Get("alert", PaintAlert); }
        public Sprite GetStarSprite() { return Get("star", PaintStar); }

        public Sprite GetBuildingSprite(string typeId)
        {
            switch (typeId)
            {
                case "extractor": return Get("b_extractor", PaintExtractor);
                case "refinery": return Get("b_refinery", PaintRefinery);
                case "assembler": return Get("b_assembler", PaintAssembler);
                case "sale_depot": return Get("b_depot", PaintDepot);
                case "shipyard": return Get("b_shipyard", PaintShipyard);
                case "headquarters": return Get("b_hq", PaintHeadquarters);
                case "fort": return Get("b_fort", PaintFort);
                default: return Missing("building " + typeId);
            }
        }

        public Sprite GetCraftSprite(string typeId)
        {
            switch (typeId)
            {
                case "tank": return Get("c_tank", PaintTank);
                case "artillery": return Get("c_artillery", PaintArtillery);
                case "guard": return Get("c_guard", PaintGuard);
                case "strike": return Get("c_strike", PaintStrike);
                default: return Missing("craft " + typeId);
            }
        }

        Sprite Missing(string what)
        {
            if (reportedMissing.Add(what)) Debug.LogError("No sprite for " + what + "; showing magenta fallback.");
            return Get("missing", PaintMissing);
        }

        Sprite Get(string key, Func<float, float, Color> painter)
        {
            Sprite s;
            if (cache.TryGetValue(key, out s)) return s;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = key };
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    pixels[y * Size + x] = painter((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), Size);
            s.name = key;
            cache[key] = s;
            return s;
        }

        // ---------------------------------------------------------------- painting helpers (u, v in [-1, 1])

        static Color Fill(float d, float shade = 0.92f)
        {
            // d < 0 inside. Soft edge and a darker rim for readability on any tint.
            float a = Mathf.Clamp01(0.5f - d * Size * 0.5f);
            float rim = Mathf.Clamp01(1f - Mathf.Abs(d + 0.06f) * Size * 0.25f);
            float g = Mathf.Lerp(shade, 0.35f, rim * 0.85f);
            return new Color(g, g, g, a);
        }

        static float Box(float u, float v, float cx, float cy, float hw, float hh)
        {
            float dx = Mathf.Abs(u - cx) - hw, dy = Mathf.Abs(v - cy) - hh;
            return Mathf.Max(dx, dy);
        }

        static float Circle(float u, float v, float cx, float cy, float r)
        {
            return Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r;
        }

        static float Union(float a, float b) { return Mathf.Min(a, b); }
        static float Subtract(float a, float b) { return Mathf.Max(a, -b); }

        static float Triangle(float u, float v, Vector2 a, Vector2 b, Vector2 c)
        {
            // Edge() measures outward for counter-clockwise triangles; flip clockwise ones so either winding works.
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (area < 0) { var t = b; b = c; c = t; }
            var p = new Vector2(u, v);
            float d1 = Edge(p, a, b), d2 = Edge(p, b, c), d3 = Edge(p, c, a);
            return Mathf.Max(d1, Mathf.Max(d2, d3));
        }

        static float Edge(Vector2 p, Vector2 a, Vector2 b)
        {
            var n = new Vector2(b.y - a.y, a.x - b.x).normalized;
            return Vector2.Dot(p - a, n);
        }

        // ---------------------------------------------------------------- terrain

        static Color PaintCell(float u, float v)
        {
            float d = Box(u, v, 0, 0, 0.985f, 0.985f);
            return d < 0 ? new Color(1, 1, 1, 1) : Color.clear;
        }

        static Color PaintRock(float u, float v)
        {
            float d = Box(u, v, 0, 0, 0.82f, 0.82f) - 0.1f;
            float n = Mathf.PerlinNoise(u * 4 + 7, v * 4 + 3);
            var c = Fill(d + (n - 0.5f) * 0.15f, 0.5f);
            return c;
        }

        static Color PaintNode(float u, float v)
        {
            float d = Circle(u, v, 0, 0, 0.62f);
            float facets = Mathf.Abs(Mathf.Sin(Mathf.Atan2(v, u) * 3f)) * 0.1f;
            return Fill(d + facets);
        }

        static Color PaintArrow(float u, float v)
        {
            // Points toward +u.
            float shaft = Box(u, v, -0.2f, 0, 0.55f, 0.16f);
            float head = Triangle(u, v, new Vector2(0.25f, 0.5f), new Vector2(0.85f, 0f), new Vector2(0.25f, -0.5f));
            return Fill(Union(shaft, head), 0.95f);
        }

        /// <summary>Conveyor running along u: dark bed with cross slats between two bright rails.</summary>
        static Color PaintBeltTrack(float u, float v)
        {
            float av = Mathf.Abs(v);
            if (av > 0.86f) return Color.clear;
            if (av > 0.62f) return new Color(0.92f, 0.92f, 0.92f, 1f);
            float slat = Mathf.Abs(Mathf.Repeat(u * 4f, 1f) - 0.5f) < 0.07f ? 0.48f : 0.34f;
            return new Color(slat, slat, slat, 0.96f);
        }

        /// <summary>A bold filled arrowhead with a notched back, pointing toward +u. Reads clearly even when foreshortened.</summary>
        static Color PaintChevron(float u, float v)
        {
            float head = Triangle(u, v, new Vector2(-0.75f, 0.85f), new Vector2(0.85f, 0f), new Vector2(-0.75f, -0.85f));
            float notch = Triangle(u, v, new Vector2(-0.8f, 0.5f), new Vector2(-0.15f, 0f), new Vector2(-0.8f, -0.5f));
            return Fill(Subtract(head, notch), 1f);
        }

        /// <summary>Rounded junction plate that joins belt corners, splits and merges.</summary>
        static Color PaintBeltPad(float u, float v)
        {
            float d = Box(u, v, 0, 0, 0.62f, 0.62f) - 0.24f;
            if (d > 0) return Color.clear;
            float g = d > -0.12f ? 0.92f : 0.34f;
            return new Color(g, g, g, 0.96f);
        }

        static Color PaintToken(float u, float v)
        {
            return Fill(Circle(u, v, 0, 0, 0.7f), 1f);
        }

        static Color PaintRing(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float d = Mathf.Abs(r - 0.7f) - 0.12f;
            float cross = Mathf.Min(Box(u, v, 0, 0, 0.06f, 0.35f), Box(u, v, 0, 0, 0.35f, 0.06f));
            return Fill(Mathf.Min(d, cross), 1f);
        }

        static Color PaintThinRing(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            return Fill(Mathf.Abs(r - 0.85f) - 0.08f, 1f);
        }

        static Color PaintSquare(float u, float v)
        {
            return new Color(1, 1, 1, 1);
        }

        static Color PaintShadow(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v * 4f);
            return new Color(0, 0, 0, Mathf.Clamp01(0.55f * (1f - r)));
        }

        static Color PaintAlert(float u, float v)
        {
            float tri = Triangle(u, v, new Vector2(0, 0.9f), new Vector2(0.9f, -0.75f), new Vector2(-0.9f, -0.75f));
            float bar = Box(u, v, 0, 0.1f, 0.09f, 0.35f);
            float dot = Circle(u, v, 0, -0.5f, 0.1f);
            var c = Fill(tri, 1f);
            if (Union(bar, dot) < 0) c = new Color(0.1f, 0.1f, 0.1f, c.a);
            return c;
        }

        static Color PaintStar(float u, float v)
        {
            float a = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
            float k = Mathf.Cos(a * 5f) * 0.5f + 0.5f;
            return Fill(r - Mathf.Lerp(0.38f, 0.85f, Mathf.Pow(k, 3f)), 1f);
        }

        static Color PaintMissing(float u, float v)
        {
            return Box(u, v, 0, 0, 0.8f, 0.8f) < 0 ? new Color(1, 0, 1, 1) : Color.clear;
        }

        // ---------------------------------------------------------------- buildings

        static Color PaintExtractor(float u, float v)
        {
            float body = Box(u, v, 0, -0.35f, 0.6f, 0.3f) - 0.05f;
            float drill = Triangle(u, v, new Vector2(-0.3f, -0.05f), new Vector2(0.3f, -0.05f), new Vector2(0, 0.85f));
            float tower = Box(u, v, 0, 0.15f, 0.12f, 0.5f);
            return Fill(Union(body, Union(Subtract(drill, Box(u, v, 0, 0.2f, 0.06f, 0.4f)), tower)));
        }

        static Color PaintRefinery(float u, float v)
        {
            float tank = Box(u, v, -0.2f, -0.2f, 0.45f, 0.55f) - 0.12f;
            float stack = Box(u, v, 0.5f, 0.15f, 0.12f, 0.75f);
            float window = Circle(u, v, -0.2f, -0.1f, 0.18f);
            return Fill(Subtract(Union(tank, stack), window));
        }

        static Color PaintAssembler(float u, float v)
        {
            float a = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
            float teeth = (Mathf.Cos(a * 8f) > 0.3f ? 0.82f : 0.65f);
            float gear = r - teeth;
            float hole = Circle(u, v, 0, 0, 0.25f);
            return Fill(Subtract(gear, hole));
        }

        static Color PaintDepot(float u, float v)
        {
            float diamond = (Mathf.Abs(u) + Mathf.Abs(v)) - 0.85f;
            float slot = Box(u, v, 0, 0, 0.3f, 0.06f);
            return Fill(Subtract(diamond * 0.8f, slot));
        }

        /// <summary>Command spire: wide base, tower, dome and antenna.</summary>
        static Color PaintHeadquarters(float u, float v)
        {
            float baseD = Box(u, v, 0, -0.72f, 0.92f, 0.2f) - 0.04f;
            float tower = Box(u, v, 0, -0.2f, 0.42f, 0.42f) - 0.04f;
            float dome = Circle(u, v, 0, 0.28f, 0.36f);
            float antenna = Box(u, v, 0, 0.75f, 0.05f, 0.22f);
            float window = Box(u, v, 0, -0.2f, 0.22f, 0.07f);
            return Fill(Subtract(Union(Union(baseD, tower), Union(dome, antenna)), window));
        }

        /// <summary>Bastion with crenellations and a gate.</summary>
        static Color PaintFort(float u, float v)
        {
            float wall = Box(u, v, 0, -0.3f, 0.78f, 0.42f) - 0.03f;
            float merlons = 1f;
            for (int i = -2; i <= 2; i++) merlons = Mathf.Min(merlons, Box(u, v, i * 0.36f, 0.22f, 0.11f, 0.12f));
            float gate = Box(u, v, 0, -0.55f, 0.18f, 0.2f);
            float flag = Union(Box(u, v, 0.02f, 0.62f, 0.03f, 0.3f), Triangle(u, v, new Vector2(0.05f, 0.9f), new Vector2(0.45f, 0.78f), new Vector2(0.05f, 0.66f)));
            return Fill(Union(Subtract(Union(wall, merlons), gate), flag));
        }

        static Color PaintShipyard(float u, float v)
        {
            // Hangar: wide arch with an open bay and a gantry crane.
            float hall = Box(u, v, 0, -0.25f, 0.85f, 0.45f) - 0.05f;
            float roof = Circle(u, v, 0, 0.1f, 0.85f);
            float body = Union(hall, Mathf.Max(roof, -(v - 0.1f) - 0.6f));
            float bay = Box(u, v, 0, -0.4f, 0.4f, 0.32f);
            float crane = Box(u, v, 0.55f, 0.55f, 0.08f, 0.35f);
            return Fill(Union(Subtract(body, bay), crane));
        }

        // ---------------------------------------------------------------- craft

        static Color PaintTank(float u, float v)
        {
            float hull = Box(u, v, 0, -0.35f, 0.8f, 0.25f) - 0.08f;
            float turret = Box(u, v, -0.05f, 0.05f, 0.35f, 0.2f) - 0.08f;
            float barrel = Box(u, v, 0.45f, 0.08f, 0.4f, 0.06f);
            return Fill(Union(hull, Union(turret, barrel)));
        }

        static Color PaintArtillery(float u, float v)
        {
            float baseD = Box(u, v, -0.15f, -0.5f, 0.6f, 0.18f) - 0.06f;
            float wheel1 = Circle(u, v, -0.5f, -0.6f, 0.2f);
            float wheel2 = Circle(u, v, 0.2f, -0.6f, 0.2f);
            // Long barrel raised at 40 degrees.
            float c = Mathf.Cos(0.7f), s = Mathf.Sin(0.7f);
            float bu = (u + 0.1f) * c + (v + 0.3f) * s, bv = -(u + 0.1f) * s + (v + 0.3f) * c;
            float barrel = Box(bu, bv, 0.45f, 0, 0.55f, 0.08f);
            return Fill(Union(Union(baseD, barrel), Union(wheel1, wheel2)));
        }

        static Color PaintGuard(float u, float v)
        {
            // Shield: rounded top, pointed bottom.
            float top = Box(u, v, 0, 0.25f, 0.65f, 0.55f) - 0.1f;
            float bottom = Triangle(u, v, new Vector2(-0.75f, -0.2f), new Vector2(0.75f, -0.2f), new Vector2(0, -0.95f));
            float emblem = Box(u, v, 0, 0.1f, 0.08f, 0.35f);
            float emblem2 = Box(u, v, 0, 0.25f, 0.3f, 0.08f);
            return Fill(Subtract(Union(top, bottom), Union(emblem, emblem2)));
        }

        static Color PaintStrike(float u, float v)
        {
            float body = Triangle(u, v, new Vector2(0, 0.95f), new Vector2(0.85f, -0.6f), new Vector2(-0.85f, -0.6f));
            float notch = Triangle(u, v, new Vector2(0, -0.2f), new Vector2(0.4f, -0.7f), new Vector2(-0.4f, -0.7f));
            float canopy = Circle(u, v, 0, 0.2f, 0.12f);
            return Fill(Subtract(Subtract(body, notch), canopy));
        }
    }
}
