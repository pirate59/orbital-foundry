using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Tileable ground textures for the asteroid decks, painted in code once and shared by every deck: dusty regolith
    /// pocked with craters and pebbles for the open rock, and a finer graded surface scuffed by tracks for the
    /// buildable pad. Both are near-white so each asteroid's mineral cast tints them, with relief shading baked in
    /// from the sun's direction so the craters read without a normal map.
    /// </summary>
    public static class GroundTextures
    {
        const int Size = 512;
        static Texture2D regolith, graded;

        /// <summary>Open rock round the grid and down the cliffs.</summary>
        public static Texture2D Regolith => regolith ?? (regolith = Paint("Regolith", 11, 18, 700, false));

        /// <summary>The levelled build pad: no craters, few pebbles, faint broad tracks along x and less baked relief.</summary>
        public static Texture2D Graded => graded ?? (graded = Paint("Graded", 23, 0, 140, true));

        static Texture2D Paint(string name, int seed, int craters, int pebbles, bool scrapes)
        {
            var h = new float[Size * Size];
            var tone = new float[Size * Size];
            var rng = new System.Random(seed);

            // Rolling dust and mottled tone from layered periodic noise, so the texture tiles without seams.
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    int i = y * Size + x;
                    h[i] = Fbm(u, v, 8, 5, seed) * 7f;
                    tone[i] = Fbm(u, v, 4, 3, seed + 101);
                    if (scrapes) h[i] = h[i] * 0.5f + (Noise(u, v, 2, 36, seed + 7) - 0.5f) * 0.9f;
                }

            // Craters: a bowl with a raised lip, mostly small, a few large.
            for (int c = 0; c < craters; c++)
            {
                float r = 3f + 32f * Mathf.Pow((float)rng.NextDouble(), 2.6f);
                int cx = rng.Next(Size), cy = rng.Next(Size);
                float depth = r * 0.45f, lip = r * 0.16f;
                Splat(h, cx, cy, r * 1.6f, s =>
                {
                    float q = s * 1.6f; // distance in crater radii
                    return q < 1f ? -depth * (1f - q * q) + lip : lip * Mathf.Exp(-(q - 1f) * (q - 1f) / 0.09f);
                });
                // Crater floors collect darker fines.
                Splat(tone, cx, cy, r, q => -0.12f * (1f - q));
            }

            // Pebbles: small sharp bumps, a few with a pale fleck.
            for (int p = 0; p < pebbles; p++)
            {
                float r = 0.8f + 2.6f * (float)rng.NextDouble() * (float)rng.NextDouble();
                int px = rng.Next(Size), py = rng.Next(Size);
                float lift = r * 1.1f, fleck = rng.NextDouble() < 0.3 ? 0.18f : -0.05f;
                Splat(h, px, py, r, q => lift * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)));
                Splat(tone, px, py, r, q => fleck * (1f - q));
            }

            // Bake relief light from the board's sun: the texture's u runs along world x and v along world z.
            var toSun = -(Quaternion.Euler(RenderRig.SunEuler) * Vector3.forward);
            var light = new Vector3(toSun.x, toSun.z, toSun.y).normalized;
            var grain = new System.Random(seed + 1);
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    float gx = h[y * Size + (x + 1) % Size] - h[y * Size + (x + Size - 1) % Size];
                    float gy = h[((y + 1) % Size) * Size + x] - h[((y + Size - 1) % Size) * Size + x];
                    var n = new Vector3(-gx * 0.5f, -gy * 0.5f, 1f).normalized;
                    float shade = 1f + (Vector3.Dot(n, light) - light.z) * (scrapes ? 0.7f : 1.1f);
                    float t = 0.84f + (tone[i] - 0.5f) * (scrapes ? 0.2f : 0.32f) + ((float)grain.NextDouble() - 0.5f) * 0.07f;
                    float k = Mathf.Clamp01(t * shade);
                    // A faint warm / cool drift so the dust isn't a pure grey wash.
                    float warm = (tone[i] - 0.5f) * 0.06f;
                    pixels[i] = new Color(Mathf.Clamp01(k * (1f + warm)), k, Mathf.Clamp01(k * (1f - warm)), 1f);
                }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Adds <paramref name="profile"/>(distance / radius) to every pixel within the wrapped radius.</summary>
        static void Splat(float[] field, int cx, int cy, float radius, System.Func<float, float> profile)
        {
            int reach = Mathf.CeilToInt(radius);
            for (int dy = -reach; dy <= reach; dy++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float q = Mathf.Sqrt(dx * dx + dy * dy) / radius;
                    if (q >= 1f) continue;
                    int x = ((cx + dx) % Size + Size) % Size, y = ((cy + dy) % Size + Size) % Size;
                    field[y * Size + x] += profile(q);
                }
        }

        /// <summary>Fractal sum of periodic noise, 0-1, starting at <paramref name="period"/> cells across the tile.</summary>
        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u, v, period << o, period << o, seed + o * 31) * amp;
                total += amp;
                amp *= 0.5f;
            }
            return sum / total;
        }

        /// <summary>Smooth value noise over a lattice that wraps every <paramref name="px"/> by <paramref name="py"/> cells.</summary>
        static float Noise(float u, float v, int px, int py, int seed)
        {
            float fx = u * px, fy = v * py;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float tx = fx - ix, ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Lattice(ix, iy, px, py, seed), b = Lattice(ix + 1, iy, px, py, seed);
            float c = Lattice(ix, iy + 1, px, py, seed), d = Lattice(ix + 1, iy + 1, px, py, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Lattice(int x, int y, int px, int py, int seed)
        {
            uint ux = (uint)(((x % px) + px) % px), uy = (uint)(((y % py) + py) % py);
            uint h = ux * 0x8DA6B343u ^ uy * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }
}
