using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The board under each cell: a small asteroid levelled on top, a graded build pad raised on a bevelled kerb
    /// above a margin of cratered regolith that rolls up into a ragged rim, rubble mounds on blocked tiles, a jagged
    /// rocky underside, loose boulders round the rim and safety lights round the edge that show who holds it. The
    /// height is visual only: the buildable grid stays flat at the deck surface, y = 0 relative to the cell's layout
    /// origin.
    /// </summary>
    public static class BoardArt
    {
        public const float DeckTop = -0.04f;

        public sealed class Deck
        {
            public GameObject Root;
            /// <summary>The safety lights' lamps: off when unclaimed, blue for the player, red for the opponent.</summary>
            public MeshRenderer Trim;
            /// <summary>A soft glow round each lit lamp, so the lights read from across the map (hidden when off).</summary>
            public MeshRenderer Halo;
        }

        /// <summary>The deck under a cell; <paramref name="blocked"/> are its unbuildable tiles, raised into mounds.</summary>
        public static Deck Build(Transform parent, Vector3 center, int width, int height, int cellId, IList<TilePoint> blocked = null)
        {
            var root = new GameObject("Deck");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            var deck = new Deck { Root = root };
            BuildIsland(deck, width, height, cellId, blocked);
            return deck;
        }

        static void BuildIsland(Deck deck, int width, int height, int cellId, IList<TilePoint> blocked)
        {
            // Each asteroid gets its own mineral cast: rusty, slate, sandy or violet.
            var casts = new[] { Palette.Hex(0x7A5E52), Palette.Hex(0x5E6470), Palette.Hex(0x7C7058), Palette.Hex(0x6A5A70) };
            var cast = casts[Mathf.Abs(cellId * 7 + 3) % casts.Length];
            // Ground textures are near-white, so the tints run a little brighter than plain paint would.
            var regolith = GroundTextures.Regolith;
            var top = Palette.Textured(regolith, Color.Lerp(Palette.Hex(0x605854), cast, 0.45f) * 1.15f, 0.15f);
            var cliff = Palette.Textured(regolith, Color.Lerp(Palette.Hex(0x4D4548), cast, 0.35f) * 1.15f, 0.15f);
            var under = Palette.Get(Color.Lerp(Palette.Hex(0x4A4048), cast, 0.3f), Palette.Finish.Matte);
            var padTint = Color.Lerp(Palette.Hex(0x524A48), cast, 0.3f) * 1.15f;
            var pad = Palette.Textured(GroundTextures.Graded, padTint, 0.2f);
            // The kerb's cut face catches the light a little paler than the pad's top.
            var kerb = Palette.Textured(GroundTextures.Graded, padTint * 1.35f, 0.2f);
            var relief = new Relief(width, height, cellId);
            var k = new MeshKit();
            k.Island(width, height, cellId, DeckTop, relief.At, 0.15f, top, cliff, under);
            // A slightly darker graded pad marks the buildable grid, raised on a bevelled kerb above the sunk skirt.
            var padShift = new Vector2(cellId * 0.29f, cellId * 0.53f);
            k.Plane(new Vector3(0, PadTop, 0), new Vector2(width, height), pad, 0.125f, padShift);
            BuildKerb(k, width, height, kerb, padShift);
            BuildMounds(k, width, height, cellId, blocked, top, cliff, padShift);
            // Loose boulders on the rim, outside the grid.
            var rng = new System.Random(cellId * 31 + 7);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Max(width, height) * 0.5f + 0.6f + (float)rng.NextDouble() * 1.2f;
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                p.x = Mathf.Clamp(p.x, -width * 0.5f - 2f, width * 0.5f + 2f);
                p.z = Mathf.Clamp(p.z, -height * 0.5f - 2f, height * 0.5f + 2f);
                if (Mathf.Abs(p.x) < width * 0.5f + 0.3f && Mathf.Abs(p.z) < height * 0.5f + 0.3f) continue;
                p.y = DeckTop + relief.At(p.x, p.z) + 0.06f;
                k.Rock(p, 0.25f + (float)rng.NextDouble() * 0.35f, cellId * 100 + i, 0.3f, cliff, 0.7f);
            }
            // A few big outcrops half sunk into the margin's far side, breaking the skyline.
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                float reach = Mathf.Min((width * 0.5f + 1.6f) / Mathf.Max(Mathf.Abs(c), 1e-3f), (height * 0.5f + 1.6f) / Mathf.Max(Mathf.Abs(s), 1e-3f));
                var p = new Vector3(c * reach, 0, s * reach);
                float size = 0.55f + (float)rng.NextDouble() * 0.45f;
                p.y = DeckTop + relief.At(p.x, p.z) - size * 0.2f;
                k.Rock(p, size, cellId * 100 + 50 + i, 0.4f, cliff, 0.65f);
            }
            var go = k.Instantiate("Asteroid", deck.Root.transform, true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // Safety lights round the plateau's edge, one every few tiles: a post, a dark housing and a lamp dome that
            // shows who holds the platform (off when unclaimed, blue for the player, red for the opponent).
            var posts = new MeshKit();
            var chrome = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var housing = Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
            var beacons = new MeshKit();
            var beaconMat = Palette.Get(Palette.Cream);
            var halos = new MeshKit();
            var haloMat = Palette.Flat(Color.black, true);
            float hw = width * 0.5f + 0.35f, hh = height * 0.5f + 0.35f;
            var spots = new System.Collections.Generic.List<Vector2>();
            const int perSide = 4; // lights between corners on each side
            for (int i = 0; i <= perSide; i++)
            {
                float t = (float)i / perSide;
                spots.Add(new Vector2(Mathf.Lerp(-hw, hw, t), -hh));
                spots.Add(new Vector2(Mathf.Lerp(hw, -hw, t), hh));
                if (i > 0 && i < perSide)
                {
                    spots.Add(new Vector2(-hw, Mathf.Lerp(hh, -hh, t)));
                    spots.Add(new Vector2(hw, Mathf.Lerp(-hh, hh, t)));
                }
            }
            foreach (var c in spots)
            {
                float y = DeckTop + relief.At(c.x, c.y);
                posts.Cylinder(new Vector3(c.x, y, c.y), 0.06f, 0.65f, 8, chrome);
                posts.Cylinder(new Vector3(c.x, y + 0.65f, c.y), 0.19f, 0.08f, 12, housing);
                beacons.Sphere(new Vector3(c.x, y + 0.73f, c.y), 0.16f, 12, beaconMat, true);
                halos.Sphere(new Vector3(c.x, y + 0.78f, c.y), 0.42f, 12, haloMat);
            }
            posts.Instantiate("Beacon Posts", deck.Root.transform, true);
            deck.Trim = beacons.Instantiate("Beacons", deck.Root.transform, false).GetComponent<MeshRenderer>();
            deck.Halo = halos.Instantiate("Beacon Halos", deck.Root.transform, false).GetComponent<MeshRenderer>();
            deck.Halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Height of the build pad's top: a hair under the grid lines and the buildings at the deck surface.</summary>
        const float PadTop = DeckTop + 0.015f;

        /// <summary>How far below the pad the flat skirt round it lies, and how wide the kerb's slope is.</summary>
        const float Sunk = 0.14f, KerbWidth = 0.18f;

        /// <summary>
        /// The pad's edge: a chamfered strip on each side, mitred at the corners, from the pad's top down to the sunk
        /// skirt, so the grid reads as ground levelled into the rock rather than a sheet laid on it.
        /// </summary>
        static void BuildKerb(MeshKit k, int width, int height, Material mat, Vector2 uvShift)
        {
            float gw = width * 0.5f, gh = height * 0.5f, ow = gw + KerbWidth, oh = gh + KerbWidth, foot = DeckTop - Sunk;
            var inner = new[] { new Vector3(-gw, PadTop, -gh), new Vector3(gw, PadTop, -gh), new Vector3(gw, PadTop, gh), new Vector3(-gw, PadTop, gh) };
            var outer = new[] { new Vector3(-ow, foot, -oh), new Vector3(ow, foot, -oh), new Vector3(ow, foot, oh), new Vector3(-ow, foot, oh) };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                var mid = (inner[i] + inner[j]) * 0.5f;
                var outward = new Vector3(mid.x, 0f, mid.z).normalized + Vector3.up;
                k.Facet(inner[i], inner[j], outer[j], outward, mat, 0.125f, uvShift);
                k.Facet(inner[i], outer[j], outer[i], outward, mat, 0.125f, uvShift);
            }
        }

        /// <summary>
        /// Blocked tiles rise into low rubble mounds that stay inside their own tiles: flush with the pad where they meet
        /// a buildable tile and humped in the middle, so a run of blocked tiles reads as one ridge, strewn with rubble.
        /// </summary>
        static void BuildMounds(MeshKit k, int width, int height, int cellId, IList<TilePoint> blocked, Material mat, Material rockMat, Vector2 uvShift)
        {
            if (blocked == null || blocked.Count == 0) return;
            var set = new HashSet<int>();
            foreach (var t in blocked)
                if (t.x >= 0 && t.y >= 0 && t.x < width && t.y < height) set.Add(t.y * width + t.x);
            System.Func<int, int, bool> isBlocked = (x, y) => x >= 0 && y >= 0 && x < width && y < height && set.Contains(y * width + x);

            // Distance, in tiles, from grid point (u, v) to the nearest buildable tile (capped at one tile).
            System.Func<float, float, float> clearance = (u, v) =>
            {
                int tx = Mathf.FloorToInt(u), ty = Mathf.FloorToInt(v);
                if (!isBlocked(tx, ty)) return 0f;
                float best = 1f;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = tx + dx, ny = ty + dy;
                        if (isBlocked(nx, ny)) continue;
                        float ex = Mathf.Max(0f, Mathf.Max(nx - u, u - (nx + 1))), ey = Mathf.Max(0f, Mathf.Max(ny - v, v - (ny + 1)));
                        best = Mathf.Min(best, Mathf.Sqrt(ex * ex + ey * ey));
                    }
                return best;
            };
            // Every vertex is a function of its grid position alone, so neighbouring tiles' mounds meet without seams.
            System.Func<float, float, Vector3> point = (u, v) =>
            {
                float c = clearance(u, v);
                var p = new Vector3(u - width * 0.5f, 0f, v - height * 0.5f);
                if (c > 0.15f)
                {
                    p.x += (MeshKit.Noise(new Vector3(u * 5f, 0.5f, v * 5f), cellId + 31) - 0.5f) * 0.18f;
                    p.z += (MeshKit.Noise(new Vector3(u * 5f, 0.5f, v * 5f), cellId + 37) - 0.5f) * 0.18f;
                }
                // Rounded shoulders, a crest that wanders in height, and knobbly lumps over it all.
                float hump = Mathf.Pow(Mathf.SmoothStep(0f, 1f, c / 0.5f), 0.7f);
                float peak = 0.2f + 0.4f * MeshKit.Noise(new Vector3(u * 0.55f, 0.5f, v * 0.55f), cellId + 41);
                float lumps = (MeshKit.Noise(new Vector3(u * 2.3f, 0.5f, v * 2.3f), cellId + 43) - 0.5f) * 0.32f;
                p.y = PadTop + hump * (peak + lumps);
                return p;
            };

            const int steps = 4; // lattice cells per tile side
            var rng = new System.Random(cellId * 977 + 3);
            foreach (var t in blocked)
            {
                if (!isBlocked(t.x, t.y)) continue;
                for (int sy = 0; sy < steps; sy++)
                    for (int sx = 0; sx < steps; sx++)
                    {
                        float u0 = t.x + (float)sx / steps, u1 = t.x + (sx + 1f) / steps;
                        float v0 = t.y + (float)sy / steps, v1 = t.y + (sy + 1f) / steps;
                        Vector3 a = point(u0, v0), b = point(u1, v0), c = point(u1, v1), d = point(u0, v1);
                        // Alternate the diagonal so the facets don't line up in rows.
                        if (((sx + sy) & 1) == 0)
                        {
                            k.Facet(a, b, c, Vector3.up, mat, 0.15f, uvShift);
                            k.Facet(a, c, d, Vector3.up, mat, 0.15f, uvShift);
                        }
                        else
                        {
                            k.Facet(a, b, d, Vector3.up, mat, 0.15f, uvShift);
                            k.Facet(b, c, d, Vector3.up, mat, 0.15f, uvShift);
                        }
                    }
                // Rubble: one to three rocks of mixed size scattered over the tile.
                int rocks = 1 + rng.Next(3);
                for (int r = 0; r < rocks; r++)
                {
                    float ru = t.x + 0.15f + (float)rng.NextDouble() * 0.7f, rv = t.y + 0.15f + (float)rng.NextDouble() * 0.7f;
                    float size = 0.07f + (float)rng.NextDouble() * (float)rng.NextDouble() * 0.2f;
                    k.Rock(point(ru, rv) + Vector3.up * size * 0.2f, size, cellId * 1000 + (t.y * width + t.x) * 4 + r, 0.35f, rockMat, 0.7f);
                }
            }
        }

        /// <summary>
        /// Visual-only rise of the rocky margin round the grid: a flat skirt sunk just below the pad's kerb, where the
        /// safety lights stand, then rolling up into berms and lumps with a few craters towards the rim, so the asteroid's edge is a
        /// ragged skyline rather than a table's. The grid itself stays at the deck height every building uses.
        /// </summary>
        sealed class Relief
        {
            readonly float gw, gh;
            readonly int seed;
            readonly Vector3[] craters; // x, z and radius

            public Relief(int width, int height, int seed)
            {
                gw = width * 0.5f;
                gh = height * 0.5f;
                this.seed = seed;
                var rng = new System.Random(seed * 53 + 19);
                craters = new Vector3[3 + rng.Next(3)];
                for (int i = 0; i < craters.Length; i++)
                {
                    // A spot along one side, out in the margin.
                    int side = rng.Next(4);
                    float along = ((float)rng.NextDouble() * 2f - 1f) * 0.85f, outward = 1.1f + (float)rng.NextDouble() * 0.6f;
                    var p = side == 0 ? new Vector2(along * gw, -gh - outward) : side == 1 ? new Vector2(along * gw, gh + outward)
                        : side == 2 ? new Vector2(-gw - outward, along * gh) : new Vector2(gw + outward, along * gh);
                    craters[i] = new Vector3(p.x, p.y, 0.45f + (float)rng.NextDouble() * 0.4f);
                }
            }

            /// <summary>Height above the deck at local (x, z); the skirt (and the ground under the pad) is <see cref="Sunk"/> below it.</summary>
            public float At(float x, float z)
            {
                float dx = Mathf.Max(0f, Mathf.Abs(x) - gw), dz = Mathf.Max(0f, Mathf.Abs(z) - gh);
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 2f, Mathf.Sqrt(dx * dx + dz * dz)));
                if (rise <= 0f) return -Sunk;
                float swell = MeshKit.Noise(new Vector3(x * 0.22f, 0.5f, z * 0.22f), seed);
                float lumps = MeshKit.Noise(new Vector3(x * 1.1f, 0.5f, z * 1.1f), seed + 5);
                float h = Mathf.Lerp(-Sunk, 0.22f + swell * swell * 1.1f + (lumps - 0.5f) * 0.3f, rise);
                foreach (var c in craters)
                {
                    float q = new Vector2(x - c.x, z - c.y).magnitude / c.z;
                    if (q > 1.8f) continue;
                    float bowl = q < 1f ? -0.3f * c.z * (1f - q * q) : 0f;
                    h += rise * (bowl + 0.12f * c.z * Mathf.Exp(-(q - 1f) * (q - 1f) / 0.08f));
                }
                return h;
            }
        }
    }
}
