using System;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Procedural pixel art for the space theme: asteroid resource nodes, mini space-station buildings, the HQ station,
    /// item cargo icons, hover platforms and lit lane pieces. Everything is drawn in full colour; faction colour is baked
    /// into each station's accent stripes and lights.
    /// </summary>
    public static class StationArt
    {
        // Station sprites are 96px per world unit; the HQ is drawn larger on the same scale.
        public const int StationSize = 96;
        public const float StationPixelsPerUnit = 96f;

        static readonly Color Outline = new Color32(14, 16, 28, 255);
        static readonly Color HullDark = new Color32(62, 70, 92, 255);
        static readonly Color HullMid = new Color32(118, 130, 156, 255);
        static readonly Color HullLight = new Color32(196, 206, 224, 255);
        static readonly Color Steel = new Color32(88, 98, 122, 255);
        static readonly Color Window = new Color32(255, 226, 140, 255);
        static readonly Color Cyan = new Color32(96, 226, 255, 255);
        static readonly Color Red = new Color32(255, 84, 72, 255);
        static readonly Color Green = new Color32(110, 255, 150, 255);
        static readonly Color Hazard = new Color32(255, 196, 60, 255);
        static readonly Color Furnace = new Color32(255, 140, 50, 255);
        static readonly Color Solar = new Color32(46, 86, 170, 255);

        static Color Accent(FactionId f)
        {
            return f == FactionId.Player ? new Color32(76, 160, 255, 255) : f == FactionId.Ai ? new Color32(255, 92, 92, 255) : new Color32(170, 176, 190, 255);
        }

        // ---------------------------------------------------------------- stations

        public static Sprite Station(string typeId, FactionId owner)
        {
            var a = Accent(owner);
            switch (typeId)
            {
                case "extractor": return Finish(MiningRig(a), "st_extractor");
                case "refinery": return Finish(Smelter(a), "st_refinery");
                case "assembler": return Finish(Fabricator(a), "st_assembler");
                case "sale_depot": return Finish(TradeDock(a), "st_depot");
                case "shipyard": return Finish(Drydock(a), "st_shipyard");
                case "fort": return Finish(Bastion(a), "st_fort");
                case "headquarters": return Finish(Headquarters(a), "st_hq");
                default: return null;
            }
        }

        static Sprite Finish(PixelCanvas c, string name)
        {
            c.Outline(Outline);
            return c.ToSprite(name, StationPixelsPerUnit, new Vector2(0.5f, 0.32f));
        }

        static void Antenna(PixelCanvas c, int x, int y0, int height, Color light)
        {
            c.Rect(x, y0, x, y0 + height, HullLight);
            c.Light(x, y0 + height + 1, light);
        }

        static void WindowRow(PixelCanvas c, int x0, int x1, int y, int step = 3)
        {
            for (int x = x0; x <= x1; x += step) c.Set(x, y, Window);
        }

        /// <summary>Extractor: mining rig with a hazard-striped drill reaching down into the asteroid.</summary>
        static PixelCanvas MiningRig(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            // Drill shaft and bit.
            c.Polygon(new[] { new Vector2(40, 40), new Vector2(56, 40), new Vector2(51, 10), new Vector2(45, 10) }, HullDark);
            for (int y = 12; y < 40; y += 6) c.Rect(42 + (40 - y) / 8, y, 54 - (40 - y) / 8, y + 2, Hazard);
            c.Polygon(new[] { new Vector2(44, 11), new Vector2(52, 11), new Vector2(48, 2) }, HullLight);
            // Side ore pods.
            c.Sphere(22, 44, 9, 8, HullDark, HullMid);
            c.Sphere(74, 44, 9, 8, HullDark, HullMid);
            c.Rect(28, 42, 68, 46, Steel);
            // Main hull.
            c.Panel(30, 38, 66, 64, HullMid, 2);
            c.Rect(30, 52, 66, 55, accent);
            WindowRow(c, 34, 62, 59);
            WindowRow(c, 35, 62, 44, 4);
            // Upper winch house and mast.
            c.Panel(38, 64, 58, 74, HullLight);
            c.Rect(44, 68, 52, 70, Cyan);
            Antenna(c, 54, 74, 12, Red);
            Antenna(c, 41, 74, 6, accent);
            c.Light(22, 47, Green, 1.5f);
            c.Light(74, 47, Green, 1.5f);
            return c;
        }

        /// <summary>Refinery: twin smelting tanks around a glowing furnace with a vent stack.</summary>
        static PixelCanvas Smelter(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            c.Panel(16, 22, 80, 34, HullDark, 2);           // base deck
            c.Cylinder(18, 34, 30, 70, HullDark, HullLight); // left tank
            c.Cylinder(62, 78, 30, 64, HullDark, HullLight); // right tank
            c.Rect(18, 46, 34, 49, accent);
            c.Rect(62, 44, 78, 47, accent);
            // Furnace block with molten window.
            c.Panel(34, 30, 62, 58, HullMid, 2);
            c.Rect(40, 36, 56, 48, new Color32(70, 26, 14, 255));
            c.Ellipse(48, 42, 7, 5, Furnace);
            c.Ellipse(48, 42, 4, 3, new Color32(255, 230, 150, 255));
            c.Ellipse(48, 42, 12, 9, new Color(1f, 0.55f, 0.2f, 0.18f));
            // Pipes.
            c.Line(34, 54, 26, 62, Steel, 3);
            c.Line(62, 54, 70, 58, Steel, 3);
            // Vent stack with heat glow.
            c.Cylinder(44, 52, 58, 82, HullDark, HullMid);
            c.Ellipse(48, 84, 5, 2, new Color(1f, 0.6f, 0.25f, 0.6f));
            c.Light(26, 72, Red, 1.5f);
            c.Light(70, 66, Green, 1.5f);
            WindowRow(c, 37, 59, 54, 4);
            return c;
        }

        /// <summary>Assembler: hexagonal fabrication core inside a ring, with two robotic arms.</summary>
        static PixelCanvas Fabricator(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            c.Ring(48, 46, 40, 16, 4, HullMid);              // back of the ring
            // Robotic arms.
            c.Line(26, 40, 14, 60, Steel, 3);
            c.Line(14, 60, 24, 70, Steel, 3);
            c.Light(24, 70, Hazard, 1.5f);
            c.Line(70, 40, 82, 58, Steel, 3);
            c.Line(82, 58, 74, 70, Steel, 3);
            c.Light(74, 70, Hazard, 1.5f);
            // Hexagonal core.
            var hex = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float ang = Mathf.PI / 6f + i * Mathf.PI / 3f;
                hex[i] = new Vector2(48 + Mathf.Cos(ang) * 22, 50 + Mathf.Sin(ang) * 20);
            }
            c.Polygon(hex, HullMid);
            c.Polygon(new[] { hex[1], hex[2], hex[3], new Vector2(48, 50) }, HullLight);
            c.Polygon(new[] { hex[4], hex[5], hex[0], new Vector2(48, 50) }, HullDark);
            c.Sphere(48, 50, 8, 8, new Color32(30, 90, 140, 255), Cyan);
            c.Ellipse(48, 50, 13, 13, new Color(0.4f, 0.9f, 1f, 0.16f));
            // Front of the ring with accent band and running lights.
            for (int x = 8; x <= 88; x++)
            {
                float t = (x - 48) / 40f;
                int y = (int)(46 - 16 * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t)));
                c.Rect(x, y - 1, x, y + 2, HullLight);
                c.Set(x, y, accent);
                if (x % 8 == 0) c.Light(x, y + 1, Cyan, 1.2f);
            }
            Antenna(c, 48, 70, 10, Red);
            return c;
        }

        /// <summary>Sale depot: trade dock with a landing ring, stacked cargo containers and a green beacon.</summary>
        static PixelCanvas TradeDock(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            c.Panel(14, 22, 82, 34, HullDark, 2);            // dock deck
            // Landing pad ring.
            c.Ellipse(48, 30, 30, 9, new Color32(40, 46, 64, 255));
            c.Ring(48, 30, 30, 9, 2, Hazard);
            for (int i = 0; i < 10; i++)
            {
                float ang = i * Mathf.PI * 2f / 10f;
                c.Light(48 + Mathf.Cos(ang) * 28, 30 + Mathf.Sin(ang) * 8, i % 2 == 0 ? Green : Cyan, 1.2f);
            }
            // Cargo containers.
            Color[] crates = { new Color32(214, 120, 62, 255), new Color32(80, 150, 200, 255), new Color32(200, 180, 80, 255), accent };
            c.Panel(18, 34, 32, 44, crates[0]);
            c.Panel(18, 44, 32, 54, crates[1]);
            c.Panel(64, 34, 78, 44, crates[2]);
            c.Panel(64, 44, 78, 54, crates[3]);
            // Control tower.
            c.Panel(40, 34, 56, 66, HullMid, 2);
            c.Rect(40, 58, 56, 61, accent);
            c.Panel(36, 66, 60, 74, HullLight);
            c.Rect(39, 69, 57, 71, Cyan);
            Antenna(c, 48, 74, 10, Green);
            c.Ellipse(48, 85, 5, 5, new Color(0.4f, 1f, 0.55f, 0.25f));
            WindowRow(c, 43, 54, 50, 3);
            WindowRow(c, 43, 54, 44, 3);
            return c;
        }

        /// <summary>Shipyard: lattice drydock frame cradling a half-built hull, with a gantry crane.</summary>
        static PixelCanvas Drydock(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            c.Panel(10, 20, 86, 28, HullDark, 2);
            // Half-built hull: plated nose, open ribs at the back.
            c.Polygon(new[] { new Vector2(30, 36), new Vector2(66, 36), new Vector2(80, 46), new Vector2(66, 56), new Vector2(30, 56) }, HullMid);
            c.Polygon(new[] { new Vector2(30, 46), new Vector2(66, 46), new Vector2(80, 46), new Vector2(66, 56), new Vector2(30, 56) }, HullLight);
            for (int x = 16; x < 30; x += 4) c.Rect(x, 38, x + 1, 54, Steel);
            c.Rect(16, 45, 30, 47, Steel);
            c.Rect(34, 44, 64, 46, accent);
            c.Light(72, 46, Cyan, 1.5f);
            // Frame towers with cross bracing.
            foreach (int fx in new[] { 12, 82 })
            {
                c.Rect(fx - 2, 28, fx + 2, 72, Steel);
                for (int y = 30; y < 70; y += 8) c.Line(fx - 2, y, fx + 2, y + 6, HullLight);
            }
            // Gantry and crane hook.
            c.Rect(10, 70, 86, 74, HullMid);
            for (int x = 14; x < 84; x += 6) c.Set(x, 72, Hazard);
            c.Rect(56, 58, 57, 70, HullLight);
            c.Rect(52, 56, 61, 58, Hazard);
            c.Light(12, 76, Red, 1.5f);
            c.Light(84, 76, Red, 1.5f);
            return c;
        }

        /// <summary>Fort: armoured octagonal bastion with a turret and a shield glow.</summary>
        static PixelCanvas Bastion(Color accent)
        {
            var c = new PixelCanvas(StationSize, StationSize);
            c.Ellipse(48, 46, 40, 26, new Color(accent.r, accent.g, accent.b, 0.12f));   // shield bubble
            var oct = new Vector2[8];
            for (int i = 0; i < 8; i++)
            {
                float ang = Mathf.PI / 8f + i * Mathf.PI / 4f;
                oct[i] = new Vector2(48 + Mathf.Cos(ang) * 32, 36 + Mathf.Sin(ang) * 14);
            }
            c.Polygon(oct, HullDark);
            c.Panel(20, 34, 76, 48, HullMid, 2);
            c.Rect(20, 40, 76, 42, accent);
            for (int x = 24; x < 74; x += 10) c.Panel(x, 48, x + 5, 53, HullLight);   // battlements
            // Turret dome and twin barrels.
            c.Sphere(48, 56, 13, 10, HullDark, HullLight);
            c.Line(54, 60, 76, 70, Steel, 3);
            c.Line(54, 56, 76, 66, Steel, 3);
            c.Light(77, 70, Red, 1.2f);
            c.Light(77, 66, Red, 1.2f);
            c.Rect(42, 58, 46, 59, Cyan);
            c.Light(22, 46, accent, 1.5f);
            c.Light(74, 46, accent, 1.5f);
            return c;
        }

        /// <summary>Headquarters: a large station hub with solar wings, docking spine and many lit windows.</summary>
        static PixelCanvas Headquarters(Color accent)
        {
            int s = StationSize * 2;
            var c = new PixelCanvas(s, s);
            float cx = s / 2f, cy = s * 0.46f;
            // Solar wings.
            foreach (int dir in new[] { -1, 1 })
            {
                float x0 = cx + dir * 34, x1 = cx + dir * 92;
                c.Rect((int)Mathf.Min(x0, x1), (int)cy - 2, (int)Mathf.Max(x0, x1), (int)cy + 2, Steel);
                for (int p = 0; p < 3; p++)
                {
                    float px0 = cx + dir * (42 + p * 17);
                    int left = (int)Mathf.Min(px0, px0 + dir * 14), right = (int)Mathf.Max(px0, px0 + dir * 14);
                    c.Rect(left, (int)cy - 26, right, (int)cy + 26, Solar);
                    for (int gy = (int)cy - 26; gy <= cy + 26; gy += 6) c.Rect(left, gy, right, gy, new Color32(90, 140, 220, 255));
                    c.Rect(left, (int)cy - 26, left, (int)cy + 26, new Color32(150, 190, 255, 255));
                }
            }
            // Docking spine.
            c.Panel((int)cx - 7, (int)cy - 74, (int)cx + 7, (int)cy + 74, HullMid, 2);
            for (int y = (int)cy - 70; y < cy + 70; y += 9) c.Light(cx, y, y % 18 == 0 ? Green : Cyan, 1.2f);
            // Habitat ring.
            c.Ring(cx, cy, 52, 20, 5, HullLight);
            for (int i = 0; i < 24; i++)
            {
                float ang = i * Mathf.PI * 2f / 24f;
                c.Set((int)(cx + Mathf.Cos(ang) * 49.5f), (int)(cy + Mathf.Sin(ang) * 17.5f), Window);
            }
            // Central hub.
            c.Sphere(cx, cy, 30, 28, HullDark, HullLight);
            c.Rect((int)cx - 30, (int)cy - 3, (int)cx + 30, (int)cy + 2, accent);
            for (int ring = -1; ring <= 1; ring += 2)
                for (int x = (int)cx - 22; x <= cx + 22; x += 4) c.Set(x, (int)cy + ring * 10, Window);
            c.Ellipse(cx, cy + 14, 8, 4, new Color32(120, 220, 255, 255));
            // Command tower and antennas.
            c.Panel((int)cx - 10, (int)cy + 26, (int)cx + 10, (int)cy + 40, HullLight, 2);
            c.Rect((int)cx - 7, (int)cy + 33, (int)cx + 7, (int)cy + 35, Cyan);
            Antenna(c, (int)cx - 4, (int)cy + 40, 26, Red);
            Antenna(c, (int)cx + 5, (int)cy + 40, 16, accent);
            // Docking ports at the spine ends.
            c.Sphere(cx, cy - 76, 9, 6, HullDark, HullMid);
            c.Sphere(cx, cy + 76, 9, 6, HullDark, HullMid);
            c.Light(cx, cy - 82, accent, 2f);
            return c;
        }

        // ---------------------------------------------------------------- asteroids

        /// <summary>
        /// Irregular cratered asteroid. Ore asteroids are rusty grey with metallic seams; crystal asteroids are dark slate
        /// with glowing cyan shards. Rich asteroids carry more seams/shards and a glow. Variant seeds the shape.
        /// </summary>
        public static Sprite Asteroid(string itemId, int variant, bool rich)
        {
            const int size = 72;
            var c = new PixelCanvas(size, size);
            var rng = new System.Random(variant * 7919 + (itemId == "crystal" ? 31 : 17) + (rich ? 1000 : 0));
            bool crystal = itemId == "crystal";
            Color dark = crystal ? (Color)new Color32(38, 42, 62, 255) : (Color)new Color32(70, 58, 50, 255);
            Color light = crystal ? (Color)new Color32(112, 120, 150, 255) : (Color)new Color32(176, 160, 140, 255);
            float cx = size / 2f, cy = size / 2f, radius = rich ? 27f : 22f;

            // Lumpy outline from a few low-frequency harmonics.
            var amp = new float[5];
            var phase = new float[5];
            for (int k = 0; k < 5; k++) { amp[k] = (float)rng.NextDouble() * 0.12f / (k * 0.6f + 1); phase[k] = (float)rng.NextDouble() * 6.28f; }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - cx, dy = (y + 0.5f - cy) * 1.15f;
                    float ang = Mathf.Atan2(dy, dx), r = Mathf.Sqrt(dx * dx + dy * dy);
                    float edge = radius;
                    for (int k = 0; k < 5; k++) edge *= 1f + amp[k] * Mathf.Sin((k + 2) * ang + phase[k]);
                    if (r > edge) continue;
                    float nx = dx / edge, ny = dy / edge;
                    float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx - ny * ny));
                    float lit = Mathf.Clamp01(-0.5f * nx + 0.5f * ny + 0.7f * nz);
                    float grain = (float)((x * 73856093 ^ y * 19349663 ^ variant * 83492791) & 7) / 7f * 0.12f;
                    c.Set(x, y, PixelCanvas.Quantise(Color.Lerp(dark, light, lit - grain), 6));
                }
            // Craters: dark bowl with a lit lower-right rim.
            int craters = 3 + rng.Next(3);
            for (int i = 0; i < craters; i++)
            {
                float a = (float)rng.NextDouble() * 6.28f, d = (float)rng.NextDouble() * radius * 0.6f;
                float px = cx + Mathf.Cos(a) * d, py = cy + Mathf.Sin(a) * d, cr = 1.5f + (float)rng.NextDouble() * 2.2f;
                if (!c.Opaque((int)px, (int)py)) continue;
                // Shallow crater: a lit lower rim and a slightly darker bowl, never a black hole.
                var floor = c.Get((int)px, (int)py);
                c.Ellipse(px + 0.7f, py - 0.7f, cr, cr * 0.75f, Color.Lerp(floor, Color.white, 0.18f));
                c.Ellipse(px, py, cr, cr * 0.75f, Color.Lerp(floor, Color.black, 0.22f));
            }
            if (crystal)
            {
                int shards = rich ? 7 : 4;
                var glow = new Color32(120, 240, 255, 255);
                for (int i = 0; i < shards; i++)
                {
                    float a = (float)rng.NextDouble() * 6.28f, d = (float)rng.NextDouble() * radius * 0.55f;
                    float bx = cx + Mathf.Cos(a) * d, by = cy + Mathf.Sin(a) * d * 0.8f;
                    float h = 7 + (float)rng.NextDouble() * (rich ? 12 : 8), w = 2.5f + (float)rng.NextDouble() * 2f;
                    float tilt = ((float)rng.NextDouble() - 0.5f) * 6f;
                    c.Ellipse(bx, by + h * 0.4f, w * 2.2f, h * 0.7f, new Color(0.4f, 0.9f, 1f, 0.15f));
                    c.Polygon(new[] { new Vector2(bx - w, by), new Vector2(bx + w, by), new Vector2(bx + tilt, by + h) }, new Color32(40, 170, 220, 255));
                    c.Polygon(new[] { new Vector2(bx - w, by), new Vector2(bx, by), new Vector2(bx + tilt, by + h) }, glow);
                    c.Set((int)(bx + tilt), (int)(by + h - 1), Color.white);
                }
            }
            else
            {
                int seams = rich ? 6 : 3;
                for (int i = 0; i < seams; i++)
                {
                    float a = (float)rng.NextDouble() * 6.28f, d = (float)rng.NextDouble() * radius * 0.6f;
                    float sx = cx + Mathf.Cos(a) * d, sy = cy + Mathf.Sin(a) * d;
                    float ex = sx + ((float)rng.NextDouble() - 0.5f) * 16, ey = sy + ((float)rng.NextDouble() - 0.5f) * 10;
                    Color metal = i % 2 == 0 ? (Color)new Color32(214, 222, 236, 255) : (Color)new Color32(196, 120, 70, 255);
                    for (float t = 0; t <= 1f; t += 0.08f)
                    {
                        int qx = (int)Mathf.Lerp(sx, ex, t), qy = (int)Mathf.Lerp(sy, ey, t);
                        if (c.Opaque(qx, qy)) c.Set(qx, qy, metal);
                    }
                }
            }
            if (rich)
            {
                var halo = crystal ? new Color(0.4f, 0.9f, 1f, 0.12f) : new Color(1f, 0.82f, 0.4f, 0.12f);
                var fresh = new PixelCanvas(size, size);
                fresh.Ellipse(cx, cy, radius + 7, (radius + 7) / 1.15f, halo);
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        if (c.Opaque(x, y)) fresh.Set(x, y, c.Get(x, y));
                c = fresh;
            }
            c.Outline(Outline);
            return c.ToSprite("asteroid_" + itemId + "_" + variant + (rich ? "_rich" : ""), 64f, new Vector2(0.5f, 0.5f));
        }

        // ---------------------------------------------------------------- craft

        /// <summary>Combat craft in the owner's colours: hover tank, artillery platform, shield drone or strike fighter.</summary>
        public static Sprite Craft(string typeId, FactionId owner)
        {
            const int size = 56;
            var c = new PixelCanvas(size, size);
            var a = Accent(owner);
            var glow = new Color(0.45f, 0.85f, 1f, 0.3f);
            switch (typeId)
            {
                case "tank":
                    c.Ellipse(28, 12, 20, 4, glow);                                   // hover wash
                    c.Polygon(new[] { new Vector2(6, 16), new Vector2(50, 16), new Vector2(46, 28), new Vector2(10, 28) }, HullMid);
                    c.Polygon(new[] { new Vector2(10, 22), new Vector2(48, 22), new Vector2(46, 28), new Vector2(10, 28) }, HullLight);
                    c.Rect(8, 18, 48, 19, a);
                    c.Ellipse(14, 15, 4, 2, Cyan); c.Ellipse(42, 15, 4, 2, Cyan);      // hover pads
                    c.Sphere(26, 32, 9, 6, HullDark, HullMid);                         // turret
                    c.Line(32, 33, 52, 35, Steel, 3);
                    c.Rect(22, 33, 26, 34, Cyan);
                    break;
                case "artillery":
                    c.Line(10, 10, 18, 20, Steel, 2); c.Line(46, 10, 38, 20, Steel, 2); // stabiliser legs
                    c.Panel(14, 18, 42, 28, HullMid, 2);
                    c.Rect(14, 22, 42, 23, a);
                    c.Sphere(26, 30, 7, 5, HullDark, HullLight);
                    c.Line(28, 31, 50, 48, Steel, 4);                                 // long gun
                    c.Rect(48, 46, 52, 50, HullDark);
                    c.Light(16, 27, Hazard, 1.2f);
                    break;
                case "guard":
                    c.Ellipse(28, 26, 22, 20, new Color(a.r, a.g, a.b, 0.16f));     // shield bubble
                    c.Ring(28, 26, 22, 20, 1.5f, new Color(a.r, a.g, a.b, 0.6f));
                    c.Sphere(28, 24, 12, 11, HullDark, HullLight);                    // drone core
                    c.Rect(16, 23, 40, 25, a);
                    c.Ellipse(28, 27, 4, 3, Cyan);
                    c.Line(14, 20, 6, 14, Steel, 2); c.Line(42, 20, 50, 14, Steel, 2);
                    c.Light(6, 14, a, 1.5f); c.Light(50, 14, a, 1.5f);
                    break;
                case "strike":
                    c.Ellipse(8, 28, 6, 3, new Color(1f, 0.6f, 0.25f, 0.5f));        // engine plume
                    c.Polygon(new[] { new Vector2(8, 26), new Vector2(50, 30), new Vector2(8, 34) }, HullMid);   // fuselage
                    c.Polygon(new[] { new Vector2(16, 30), new Vector2(34, 30), new Vector2(14, 48) }, HullLight); // upper wing
                    c.Polygon(new[] { new Vector2(16, 30), new Vector2(34, 30), new Vector2(14, 12) }, HullDark);  // lower wing
                    c.Line(16, 46, 22, 38, a, 2); c.Line(16, 14, 22, 22, a, 2);
                    c.Ellipse(38, 31, 5, 2, Cyan);                                    // canopy
                    c.Light(8, 30, Furnace, 2f);
                    break;
                default:
                    c.Rect(8, 8, 47, 47, Color.magenta);
                    break;
            }
            c.Outline(Outline);
            return c.ToSprite("craft_" + typeId + "_" + owner, 56f, new Vector2(0.5f, 0.4f));
        }

        // ---------------------------------------------------------------- cargo

        /// <summary>Item cargo icon: ore chunk, crystal shard, alloy plate, signal relay or quantum core.</summary>
        public static Sprite Item(string itemId)
        {
            const int size = 24;
            var c = new PixelCanvas(size, size);
            switch (itemId)
            {
                case "ore":
                    c.Polygon(new[] { new Vector2(4, 6), new Vector2(10, 3), new Vector2(19, 6), new Vector2(21, 14), new Vector2(14, 20), new Vector2(5, 16) }, new Color32(120, 104, 92, 255));
                    c.Polygon(new[] { new Vector2(5, 16), new Vector2(14, 20), new Vector2(15, 12), new Vector2(7, 11) }, new Color32(176, 160, 140, 255));
                    c.Line(9, 8, 16, 13, new Color32(214, 222, 236, 255));
                    c.Set(12, 15, new Color32(196, 120, 70, 255));
                    break;
                case "crystal":
                    c.Polygon(new[] { new Vector2(8, 3), new Vector2(16, 3), new Vector2(19, 12), new Vector2(12, 22), new Vector2(5, 12) }, new Color32(40, 170, 220, 255));
                    c.Polygon(new[] { new Vector2(8, 3), new Vector2(12, 3), new Vector2(12, 22), new Vector2(5, 12) }, new Color32(130, 240, 255, 255));
                    c.Set(11, 18, Color.white);
                    break;
                case "alloy":
                    c.Panel(3, 6, 20, 13, new Color32(216, 180, 112, 255), 2);
                    c.Panel(5, 11, 18, 17, new Color32(236, 204, 140, 255), 1);
                    c.Line(6, 9, 17, 9, new Color32(150, 116, 60, 255));
                    break;
                case "relay":
                    c.Panel(4, 3, 19, 13, new Color32(90, 96, 118, 255), 1);
                    c.Rect(6, 6, 17, 9, new Color32(245, 166, 91, 255));
                    c.Rect(15, 13, 16, 21, new Color32(196, 206, 224, 255));
                    c.Light(15, 21, new Color32(255, 200, 120, 255), 1.5f);
                    c.Set(8, 11, Green);
                    break;
                case "quantum_core":
                    c.Ellipse(12, 12, 10, 10, new Color(0.83f, 0.57f, 1f, 0.2f));
                    c.Sphere(12, 12, 7, 7, new Color32(110, 40, 160, 255), new Color32(236, 190, 255, 255));
                    c.Ring(12, 12, 11, 4, 1.2f, new Color32(255, 230, 140, 255));
                    break;
                default:
                    c.Rect(4, 4, 19, 19, Color.magenta);
                    break;
            }
            c.Outline(Outline);
            return c.ToSprite("item_" + itemId, 48f, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Hover platform that carries cargo along lanes, with a cyan underglow.</summary>
        public static Sprite Platform()
        {
            const int size = 32;
            var c = new PixelCanvas(size, size);
            c.Ellipse(16, 10, 13, 6, new Color(0.35f, 0.85f, 1f, 0.25f));
            c.Ellipse(16, 13, 13, 5, HullDark);
            c.Ellipse(16, 15, 13, 5, HullMid);
            c.Ellipse(16, 16, 10, 3.5f, HullLight);
            c.Rect(4, 13, 28, 13, Cyan);
            c.Light(5, 14, Cyan, 1.2f);
            c.Light(27, 14, Cyan, 1.2f);
            c.Outline(Outline);
            return c.ToSprite("platform", 48f, new Vector2(0.5f, 0.42f));
        }

        // ---------------------------------------------------------------- lanes

        /// <summary>Lane bed for a belt running along +x: dark translucent deck with lit edge rails and runway studs.</summary>
        public static Sprite LaneBed()
        {
            var c = new PixelCanvas(32, 16);
            // Drawn in white/grey; the lane's faction colour comes from the renderer tint.
            c.Rect(0, 3, 31, 12, new Color(0.1f, 0.12f, 0.16f, 0.8f));
            c.Rect(0, 2, 31, 2, new Color(1f, 1f, 1f, 0.95f));
            c.Rect(0, 13, 31, 13, new Color(1f, 1f, 1f, 0.95f));
            c.Rect(0, 1, 31, 1, new Color(1f, 1f, 1f, 0.3f));
            c.Rect(0, 14, 31, 14, new Color(1f, 1f, 1f, 0.3f));
            for (int x = 3; x < 32; x += 8) { c.Set(x, 5, new Color(1f, 1f, 1f, 0.55f)); c.Set(x, 10, new Color(1f, 1f, 1f, 0.55f)); }
            return c.ToSprite("lane_bed", 32f, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Light pulse chevron pointing along +x.</summary>
        public static Sprite LanePulse()
        {
            var c = new PixelCanvas(16, 16);
            c.Ellipse(8, 8, 7, 6, new Color(1f, 1f, 1f, 0.18f));
            for (int i = 0; i < 6; i++)
            {
                c.Rect(4 + i, 8 + i, 6 + i, 8 + i, Color.white);
                c.Rect(4 + i, 7 - i, 6 + i, 7 - i, Color.white);
            }
            return c.ToSprite("lane_pulse", 32f, new Vector2(0.5f, 0.5f));
        }

        /// <summary>Junction beacon joining lanes at corners, splits and merges.</summary>
        public static Sprite LaneJunction()
        {
            var c = new PixelCanvas(16, 16);
            c.Ellipse(8, 8, 7.5f, 7.5f, new Color(0.1f, 0.12f, 0.16f, 0.85f));
            c.Ring(8, 8, 7.5f, 7.5f, 1.2f, new Color(1f, 1f, 1f, 0.95f));
            c.Ellipse(8, 8, 2, 2, Color.white);
            return c.ToSprite("lane_junction", 32f, new Vector2(0.5f, 0.5f));
        }
    }
}
