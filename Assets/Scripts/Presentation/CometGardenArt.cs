using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Comet Garden: a separate procedural art direction built from seed pods, orbital trellises and petal sails.
    /// The shapes are generated at runtime like the rest of the game's sprites, with no imported sprite sheet.
    /// </summary>
    public static class CometGardenArt
    {
        const int Size = 96;
        const float PixelsPerUnit = 96f;

        static readonly Color Ink = new Color32(27, 20, 50, 255);
        static readonly Color Plum = new Color32(68, 51, 101, 255);
        static readonly Color Moon = new Color32(155, 140, 194, 255);
        static readonly Color Pearl = new Color32(246, 232, 232, 255);
        static readonly Color Glass = new Color32(108, 211, 208, 255);
        static readonly Color Mint = new Color32(151, 247, 200, 255);
        static readonly Color Peach = new Color32(255, 177, 153, 255);
        static readonly Color Gold = new Color32(255, 222, 126, 255);
        static readonly Color Leaf = new Color32(92, 193, 135, 255);
        static readonly Color DeepGlass = new Color32(35, 91, 118, 255);
        static readonly Color Gilt = new Color32(223, 162, 99, 255);

        static Color Accent(FactionId owner)
        {
            return owner == FactionId.Player ? Mint : owner == FactionId.Ai ? Peach : Moon;
        }

        static Sprite Finish(PixelCanvas c, string name, float ppu = PixelsPerUnit, float pivotY = 0.32f)
        {
            c.Outline(Ink);
            return c.ToSprite(name, ppu, new Vector2(0.5f, pivotY));
        }

        static void Flare(PixelCanvas c, int x, int y, Color color)
        {
            c.Line(x - 3, y, x + 3, y, color);
            c.Line(x, y - 3, x, y + 3, color);
            c.Set(x, y, Pearl);
        }

        static void Footing(PixelCanvas c, Color accent)
        {
            c.Ellipse(48, 15, 25, 6, new Color(accent.r, accent.g, accent.b, 0.22f));
            c.Ellipse(48, 23, 36, 11, Plum);
            c.Ring(48, 23, 36, 11, 3, Moon);
            c.Ellipse(48, 25, 27, 6, Ink);
            c.Ring(48, 25, 29, 7, 2, Gold);
            c.Line(24, 25, 72, 25, accent, 2);
            for (int x = 31; x <= 65; x += 8) c.Set(x, 19, Gold);
            c.Line(39, 13, 39, 19, Gilt);
            c.Line(57, 13, 57, 19, Gilt);
            c.Ellipse(39, 12, 2, 2, Gold);
            c.Ellipse(57, 12, 2, 2, Gold);
            Flare(c, 17, 27, Gold);
            Flare(c, 79, 27, Gold);
        }

        static void Flower(PixelCanvas c, int x, int y, int scale)
        {
            c.Line(x, y - 5 * scale, x, y + scale, Leaf, Mathf.Max(1, scale));
            c.Ellipse(x - 3 * scale, y - 3 * scale, 3 * scale, scale, Leaf);
            c.Ellipse(x + 3 * scale, y - 2 * scale, 3 * scale, scale, Leaf);
            foreach (var p in new[] { new Vector2(-2, 0), new Vector2(2, 0), new Vector2(0, 2), new Vector2(0, -2) })
                c.Ellipse(x + p.x * scale, y + p.y * scale, 2 * scale, 2 * scale, Peach);
            c.Ellipse(x, y, scale, scale, Gold);
        }

        static void Lantern(PixelCanvas c, int x, int y, int length)
        {
            c.Line(x, y, x, y - length, Gilt);
            c.Ellipse(x, y - length - 1, 3, 3, Gold);
            c.Set(x, y - length, Pearl);
        }

        static void Pod(PixelCanvas c, int x, int y, int radius, Color accent, bool garden = true)
        {
            c.Ellipse(x, y - radius * 0.55f, radius + 3, radius * 0.38f, Plum);
            c.Ellipse(x, y, radius, radius * 0.88f, DeepGlass);
            c.Ellipse(x, y - radius * 0.42f, radius * 0.73f, radius * 0.23f, Plum);
            c.Ellipse(x, y - radius * 0.28f, radius * 0.62f, radius * 0.18f, Leaf);
            if (garden)
            {
                int scale = radius >= 30 ? 2 : 1;
                Flower(c, x, y + radius / 7, scale);
                if (radius >= 14)
                {
                    Flower(c, x - radius / 3, y - radius / 6, 1);
                    Flower(c, x + radius / 3, y - radius / 8, 1);
                    c.Ellipse(x - radius / 2, y - radius / 5, 4, 3, Leaf);
                    c.Ellipse(x + radius / 2, y - radius / 5, 4, 3, Leaf);
                }
            }
            else
            {
                c.Ellipse(x, y + 1, radius / 2f, radius / 3f, new Color(accent.r, accent.g, accent.b, 0.42f));
                c.Polygon(new[] { new Vector2(x, y + radius / 2), new Vector2(x + radius / 3, y),
                    new Vector2(x, y - radius / 3), new Vector2(x - radius / 3, y) }, Gold);
                c.Line(x, y + radius / 2, x, y - radius / 3, Pearl);
            }
            c.Ellipse(x, y + radius * 0.26f, radius * 0.85f, radius * 0.45f,
                new Color(Glass.r, Glass.g, Glass.b, 0.15f));
            c.Ring(x, y, radius, radius * 0.88f, 2, Pearl);
            if (radius >= 14)
            {
                c.Line(x - radius / 2, y - radius * 0.55f, x - radius / 3, y + radius * 0.7f, Gilt);
                c.Line(x + radius / 2, y - radius * 0.55f, x + radius / 3, y + radius * 0.7f, Gilt);
                c.Ring(x, y - radius * 0.46f, radius * 0.79f, radius * 0.25f, 2, Gold);
            }
            c.Line(x - radius / 2, y + radius / 2, x - radius / 4, y + radius * 0.68f, Pearl, 2);
            c.Ellipse(x, y + radius * 0.88f, 3, 2, Gold);
        }

        static void Petal(PixelCanvas c, Vector2 root, Vector2 tip, Color color)
        {
            Vector2 delta = tip - root;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized * 9;
            c.Polygon(new[] { root - side * 0.35f, root + side * 0.35f, tip + side * 0.65f,
                tip + delta.normalized * 3, tip - side * 0.65f }, Gilt);
            c.Polygon(new[] { root + side * 0.25f, tip + side * 0.54f,
                tip + delta.normalized * 2, root - side * 0.12f }, color);
            c.Line(root.x, root.y, tip.x, tip.y, Pearl);
            c.Line(root.x + side.x * 0.15f, root.y + side.y * 0.15f,
                tip.x + side.x * 0.35f, tip.y + side.y * 0.35f, Gold);
            c.Ellipse(tip.x, tip.y, 2, 2, Gold);
        }

        static void Moonstone(PixelCanvas c, int x, int y, int r, Color accent)
        {
            c.Polygon(new[]
            {
                new Vector2(x-r, y-r/3), new Vector2(x-r/2, y+r), new Vector2(x+r/3, y+r),
                new Vector2(x+r, y), new Vector2(x+r/2, y-r), new Vector2(x-r/2, y-r)
            }, Plum);
            c.Polygon(new[] { new Vector2(x-r, y-r/3), new Vector2(x-r/2, y+r), new Vector2(x+r/3, y+r), new Vector2(x, y) }, Moon);
            c.Line(x-r/3, y-r/2, x+r/3, y+r/2, accent, 2);
            c.Line(x-r/2, y+r/2, x-r/4, y+r/2, Pearl);
            c.Set(x+r/3, y-r/3, Gold);
        }

        public static Sprite Station(string typeId, FactionId owner)
        {
            var accent = Accent(owner);
            if (typeId == "headquarters") return Headquarters(accent, owner);
            var c = new PixelCanvas(Size, Size);
            Footing(c, accent);
            switch (typeId)
            {
                case "extractor":
                    c.Ring(48, 42, 31, 15, 2, Gold);
                    Moonstone(c, 48, 36, 24, Glass);
                    foreach (int px in new[] { 28, 64 })
                    {
                        c.Polygon(new[] { new Vector2(px - 3, 54), new Vector2(px, 65), new Vector2(px + 4, 54) }, Glass);
                        c.Line(px, 56, px, 62, Pearl);
                    }
                    c.Line(28, 47, 35, 68, Moon, 4);
                    c.Line(68, 47, 61, 68, Moon, 4);
                    Petal(c, new Vector2(35, 68), new Vector2(20, 79), Peach);
                    Petal(c, new Vector2(61, 68), new Vector2(76, 79), Peach);
                    Pod(c, 48, 60, 13, accent, false);
                    c.Line(37, 70, 59, 70, Gold, 2);
                    Lantern(c, 25, 70, 9);
                    Lantern(c, 71, 70, 9);
                    Flare(c, 48, 86, Gold);
                    break;
                case "refinery":
                    c.Ring(48, 53, 37, 23, 2, Gilt);
                    Pod(c, 27, 45, 13, accent, false);
                    Pod(c, 69, 45, 13, accent, false);
                    c.Cylinder(40, 56, 30, 66, Plum, Moon);
                    for (int y = 34; y <= 62; y += 6) c.Rect(44, y, 52, y + 1, Gilt);
                    c.Ellipse(48, 68, 20, 7, Pearl);
                    c.Ellipse(48, 68, 15, 4, Peach);
                    Petal(c, new Vector2(45, 66), new Vector2(22, 77), Peach);
                    Petal(c, new Vector2(51, 66), new Vector2(74, 77), Peach);
                    c.Ellipse(21, 77, 12, 4, Plum);
                    c.Ring(21, 77, 12, 4, 1, Gold);
                    c.Ellipse(75, 77, 12, 4, Plum);
                    c.Ring(75, 77, 12, 4, 1, Gold);
                    Lantern(c, 19, 73, 8);
                    Lantern(c, 77, 73, 8);
                    Flare(c, 48, 83, Gold);
                    break;
                case "assembler":
                    c.Ring(48, 48, 34, 19, 3, Moon);
                    c.Ring(48, 52, 40, 24, 2, Gilt);
                    foreach (var tip in new[] { new Vector2(48, 86), new Vector2(13, 59), new Vector2(83, 59) })
                        Petal(c, new Vector2(48, 48), tip, Peach);
                    Pod(c, 23, 61, 8, accent);
                    Pod(c, 73, 61, 8, accent);
                    Pod(c, 48, 49, 18, accent);
                    c.Ellipse(48, 38, 9, 4, Gold);
                    Lantern(c, 15, 63, 8);
                    Lantern(c, 81, 63, 8);
                    break;
                case "sale_depot":
                    c.Ring(48, 56, 38, 32, 4, Moon);
                    c.Ring(48, 56, 34, 29, 2, Gold);
                    c.Ring(48, 30, 29, 8, 2, Gold);
                    c.Cylinder(42, 54, 31, 67, Plum, Moon);
                    for (int y = 38; y <= 60; y += 7) c.Rect(44, y, 52, y + 1, Gold);
                    Pod(c, 48, 69, 12, accent, false);
                    Pod(c, 22, 45, 8, accent);
                    Pod(c, 74, 45, 8, accent);
                    Lantern(c, 18, 65, 12);
                    Lantern(c, 78, 65, 12);
                    Flare(c, 48, 87, Gold);
                    break;
                case "shipyard":
                    c.Ring(48, 48, 39, 27, 4, Moon);
                    c.Ring(48, 48, 35, 23, 2, Gilt);
                    c.Rect(14, 44, 82, 47, Gold);
                    c.Rect(16, 72, 80, 74, Moon);
                    for (int x = 19; x <= 77; x += 9) c.Line(x, 72, x + 7, 66, Gilt);
                    c.Polygon(new[] { new Vector2(25, 39), new Vector2(63, 39), new Vector2(77, 48), new Vector2(63, 57), new Vector2(25, 57) }, Pearl);
                    c.Rect(28, 47, 66, 49, accent);
                    for (int x = 28; x <= 58; x += 7) c.Line(x, 40, x + 4, 56, Moon);
                    Petal(c, new Vector2(37, 55), new Vector2(25, 71), Peach);
                    Petal(c, new Vector2(37, 41), new Vector2(25, 27), Peach);
                    Pod(c, 65, 48, 8, accent, false);
                    Lantern(c, 53, 72, 11);
                    Flare(c, 48, 82, Gold);
                    break;
                case "storage":
                    c.Ring(48, 59, 41, 22, 2, Gold);
                    c.Panel(17, 31, 79, 41, Plum, 2);
                    Pod(c, 27, 52, 14, accent);
                    Pod(c, 69, 52, 14, accent);
                    Pod(c, 48, 65, 16, accent);
                    c.Line(28, 39, 48, 49, Gold, 2);
                    c.Line(68, 39, 48, 49, Gold, 2);
                    for (int x = 23; x <= 73; x += 10) c.Set(x, 35, Gold);
                    Lantern(c, 15, 69, 11);
                    Lantern(c, 81, 69, 11);
                    Flare(c, 48, 86, Gold);
                    break;
                case "fort":
                    c.Ring(48, 49, 39, 26, 5, new Color(accent.r, accent.g, accent.b, 0.75f));
                    c.Ring(48, 49, 35, 23, 2, Gold);
                    c.Polygon(new[] { new Vector2(14, 48), new Vector2(27, 73), new Vector2(32, 66), new Vector2(25, 44) }, Moon);
                    c.Polygon(new[] { new Vector2(82, 48), new Vector2(69, 73), new Vector2(64, 66), new Vector2(71, 44) }, Moon);
                    Pod(c, 23, 49, 7, accent, false);
                    Pod(c, 73, 49, 7, accent, false);
                    Pod(c, 48, 52, 19, accent, false);
                    c.Ellipse(48, 52, 8, 6, Gold);
                    Lantern(c, 33, 72, 9);
                    Lantern(c, 63, 72, 9);
                    Flare(c, 48, 81, Gold);
                    break;
                default: return null;
            }
            return Finish(c, "garden_" + typeId + "_" + owner);
        }

        static Sprite Headquarters(Color accent, FactionId owner)
        {
            var c = new PixelCanvas(192, 192);
            c.Ring(96, 83, 81, 30, 5, Moon);
            c.Ring(96, 83, 76, 26, 2, Gold);
            foreach (int x in new[] { 25, 167 })
            {
                c.Line(x, 82, x, 129, Gilt, 3);
                Pod(c, x, 84, 14, accent);
                Pod(c, x, 132, 10, accent);
                Lantern(c, x, 69, 12);
            }
            c.Ellipse(96, 55, 65, 18, Plum);
            c.Ring(96, 55, 65, 18, 5, Gold);
            Petal(c, new Vector2(89, 97), new Vector2(41, 153), Peach);
            Petal(c, new Vector2(103, 97), new Vector2(151, 153), Peach);
            Petal(c, new Vector2(96, 100), new Vector2(96, 178), Peach);
            Pod(c, 96, 101, 48, accent);
            Flower(c, 69, 104, 2);
            Flower(c, 121, 106, 2);
            c.Ellipse(96, 74, 36, 7, new Color(accent.r, accent.g, accent.b, 0.35f));
            c.Ring(96, 101, 52, 44, 4, Pearl);
            for (int x = 55; x <= 137; x += 11) c.Ellipse(x, 70, 2, 2, Gold);
            for (int x = 42; x <= 150; x += 18) Lantern(c, x, 65, 10);
            Flare(c, 96, 184, Gold);
            return Finish(c, "garden_headquarters_" + owner);
        }

        public static Sprite Craft(string typeId, FactionId owner)
        {
            var c = new PixelCanvas(56, 56);
            Color accent = Accent(owner);
            switch (typeId)
            {
                case "tank":
                    c.Ellipse(28, 12, 19, 4, new Color(accent.r, accent.g, accent.b, 0.35f));
                    c.Ellipse(28, 23, 22, 10, Plum);
                    c.Ellipse(28, 27, 18, 9, Pearl);
                    c.Ring(28, 27, 19, 9, 2, Gold);
                    c.Rect(12, 26, 44, 27, accent);
                    Petal(c, new Vector2(15, 27), new Vector2(5, 36), Peach);
                    Petal(c, new Vector2(41, 27), new Vector2(51, 36), Peach);
                    Pod(c, 28, 32, 9, accent, false);
                    c.Ellipse(12, 16, 5, 3, Mint); c.Ellipse(44, 16, 5, 3, Mint);
                    c.Set(10, 24, Gold); c.Set(46, 24, Gold);
                    c.Line(17, 20, 22, 20, Gilt);
                    c.Line(34, 20, 39, 20, Gilt);
                    break;
                case "artillery":
                    c.Panel(12, 18, 44, 27, Plum, 2);
                    c.Rect(12, 22, 44, 23, accent);
                    c.Line(13, 19, 6, 11, Moon, 2); c.Line(43, 19, 50, 11, Moon, 2);
                    c.Line(28, 27, 44, 49, Pearl, 5);
                    c.Line(31, 31, 43, 47, Gilt, 2);
                    c.Ellipse(45, 49, 5, 3, Gold);
                    c.Ring(45, 49, 6, 4, 1, Plum);
                    Petal(c, new Vector2(20, 26), new Vector2(8, 38), Peach);
                    Pod(c, 28, 29, 9, accent, false);
                    c.Set(17, 19, Gold); c.Set(39, 19, Gold);
                    break;
                case "guard":
                    c.Ring(28, 27, 24, 20, 2, accent);
                    c.Ring(28, 27, 19, 17, 2, Gold);
                    Petal(c, new Vector2(28, 27), new Vector2(28, 52), Peach);
                    Pod(c, 28, 27, 13, accent);
                    c.Ellipse(7, 27, 3, 3, Mint); c.Ellipse(49, 27, 3, 3, Mint);
                    Flare(c, 8, 28, Gold);
                    Flare(c, 48, 28, Gold);
                    c.Ellipse(28, 8, 4, 2, Gold);
                    break;
                case "strike":
                    c.Ellipse(9, 28, 7, 3, Mint);
                    c.Ellipse(4, 28, 4, 2, new Color(Mint.r, Mint.g, Mint.b, 0.4f));
                    Petal(c, new Vector2(26, 28), new Vector2(13, 50), Peach);
                    Petal(c, new Vector2(26, 28), new Vector2(13, 6), Peach);
                    c.Polygon(new[] { new Vector2(8, 22), new Vector2(49, 28), new Vector2(8, 34) }, Pearl);
                    c.Polygon(new[] { new Vector2(13, 28), new Vector2(42, 28), new Vector2(14, 33) }, Moon);
                    c.Line(13, 28, 46, 28, accent, 2);
                    Pod(c, 35, 28, 8, accent, false);
                    c.Set(47, 28, Gold);
                    break;
                default: return null;
            }
            return Finish(c, "garden_craft_" + typeId + "_" + owner, 56f, 0.4f);
        }

        public static Sprite Asteroid(string itemId, int variant, bool rich)
        {
            var c = new PixelCanvas(72, 72);
            int wobble = variant % 4;
            Moonstone(c, 36, 34, rich ? 25 : 21, itemId == "crystal" ? Glass : Peach);
            if (itemId == "crystal")
            {
                for (int i = 0; i < (rich ? 5 : 3); i++)
                {
                    int x = 22 + i * 7, y = 42 + (i % 2) * 7 + wobble;
                    c.Polygon(new[] { new Vector2(x-3, y-4), new Vector2(x, y+7), new Vector2(x+4, y-4) }, Glass);
                    c.Line(x, y-1, x, y+5, Pearl);
                }
            }
            else
            {
                c.Ellipse(27, 39, 5, 3, Gold);
                c.Ellipse(46, 27, 4, 3, Gold);
                c.Line(21, 27, 51, 46, Peach, 2);
            }
            if (rich)
            {
                c.Ring(36, 34, 32, 27, 2, Gold);
                Flare(c, 60, 57, Gold);
            }
            return Finish(c, "garden_asteroid_" + itemId + "_" + variant + "_" + rich, 64f, 0.5f);
        }
    }
}
