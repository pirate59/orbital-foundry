using System;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Small software canvas for procedural pixel art. Coordinates are pixels with (0,0) at the bottom-left.
    /// Shapes are drawn opaque; <see cref="Outline"/> adds the dark rim that gives sprites their pixel-art read.
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width, Height;
        readonly Color32[] px;

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            px = new Color32[width * height];
        }

        public bool Inside(int x, int y) { return x >= 0 && y >= 0 && x < Width && y < Height; }

        public Color32 Get(int x, int y) { return Inside(x, y) ? px[y * Width + x] : new Color32(0, 0, 0, 0); }

        public void Set(int x, int y, Color c)
        {
            if (!Inside(x, y) || c.a <= 0f) return;
            if (c.a >= 0.999f) { px[y * Width + x] = c; return; }
            // Alpha-blend over what is there.
            Color under = px[y * Width + x];
            float a = c.a + under.a * (1f - c.a);
            if (a <= 0f) return;
            var o = (c * c.a + under * under.a * (1f - c.a)) / a;
            o.a = a;
            px[y * Width + x] = o;
        }

        public bool Opaque(int x, int y) { return Inside(x, y) && px[y * Width + x].a > 0; }

        public void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++) Set(x, y, c);
        }

        public void Ellipse(float cx, float cy, float rx, float ry, Color c)
        {
            for (int y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (int x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    if (dx * dx + dy * dy <= 1f) Set(x, y, c);
                }
        }

        /// <summary>Ellipse shaded as a sphere lit from the upper left, between dark and light.</summary>
        public void Sphere(float cx, float cy, float rx, float ry, Color dark, Color light)
        {
            for (int y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (int x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float d = dx * dx + dy * dy;
                    if (d > 1f) continue;
                    float nz = Mathf.Sqrt(1f - d);
                    float lit = Mathf.Clamp01(-0.55f * dx + 0.55f * dy + 0.65f * nz);
                    Set(x, y, Quantise(Color.Lerp(dark, light, lit), 5));
                }
        }

        /// <summary>Ring (annulus) between inner and outer radii.</summary>
        public void Ring(float cx, float cy, float rx, float ry, float thickness, Color c)
        {
            for (int y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
                for (int x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float inner = 1f - thickness / Mathf.Min(rx, ry);
                    if (d <= 1f && d >= inner) Set(x, y, c);
                }
        }

        /// <summary>Vertical cylinder: horizontally shaded body with a lighter elliptical top.</summary>
        public void Cylinder(int x0, int x1, int y0, int y1, Color dark, Color light)
        {
            float w = x1 - x0;
            for (int x = x0; x <= x1; x++)
            {
                float t = (x - x0) / Mathf.Max(1f, w);
                float lit = Mathf.Clamp01(1f - Mathf.Abs(t - 0.32f) * 1.6f);
                Rect(x, y0, x, y1, Quantise(Color.Lerp(dark, light, lit), 5));
            }
            Ellipse((x0 + x1) * 0.5f + 0.5f, y1, w * 0.5f + 0.5f, Mathf.Max(1.5f, w * 0.18f), light);
        }

        /// <summary>Box shaded with a lit top-left bevel and a dark bottom-right bevel.</summary>
        public void Panel(int x0, int y0, int x1, int y1, Color body, int bevel = 1)
        {
            Rect(x0, y0, x1, y1, body);
            var hi = Color.Lerp(body, Color.white, 0.35f);
            var lo = Color.Lerp(body, Color.black, 0.4f);
            for (int b = 0; b < bevel; b++)
            {
                Rect(x0 + b, y1 - b, x1 - b, y1 - b, hi);
                Rect(x0 + b, y0 + b, x0 + b, y1 - b, hi);
                Rect(x0 + b, y0 + b, x1 - b, y0 + b, lo);
                Rect(x1 - b, y0 + b, x1 - b, y1 - b, lo);
            }
        }

        public void Line(float x0, float y0, float x1, float y1, Color c, float width = 1f)
        {
            int steps = (int)Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)) * 2 + 1;
            float r = width * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = Mathf.Lerp(x0, x1, t), y = Mathf.Lerp(y0, y1, t);
                if (width <= 1f) Set((int)x, (int)y, c);
                else Ellipse(x, y, r, r, c);
            }
        }

        public void Polygon(Vector2[] pts, Color c)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            for (int y = (int)minY; y <= (int)maxY; y++)
            {
                float sy = y + 0.5f;
                for (int x = 0; x < Width; x++)
                {
                    float sx = x + 0.5f;
                    bool inside = false;
                    for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                    {
                        if ((pts[i].y > sy) != (pts[j].y > sy) &&
                            sx < (pts[j].x - pts[i].x) * (sy - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x) inside = !inside;
                    }
                    if (inside) Set(x, y, c);
                }
            }
        }

        /// <summary>A small light with a soft halo.</summary>
        public void Light(float x, float y, Color c, float halo = 2.5f)
        {
            Ellipse(x, y, halo, halo, new Color(c.r, c.g, c.b, 0.25f));
            Set((int)x, (int)y, Color.Lerp(c, Color.white, 0.5f));
        }

        /// <summary>Darkens transparent pixels touching opaque ones into a one-pixel outline.</summary>
        public void Outline(Color c)
        {
            var mark = new bool[px.Length];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    if (px[y * Width + x].a > 128) continue;
                    if (Opaque(x - 1, y) && Get(x - 1, y).a > 128 || Opaque(x + 1, y) && Get(x + 1, y).a > 128
                        || Opaque(x, y - 1) && Get(x, y - 1).a > 128 || Opaque(x, y + 1) && Get(x, y + 1).a > 128) mark[y * Width + x] = true;
                }
            for (int i = 0; i < px.Length; i++) if (mark[i]) px[i] = c;
        }

        public static Color Quantise(Color c, int levels)
        {
            float q = levels - 1;
            return new Color(Mathf.Round(c.r * q) / q, Mathf.Round(c.g * q) / q, Mathf.Round(c.b * q) / q, c.a);
        }

        public Sprite ToSprite(string name, float pixelsPerUnit, Vector2 pivot)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, Width, Height), pivot, pixelsPerUnit);
            s.name = name;
            return s;
        }
    }
}
