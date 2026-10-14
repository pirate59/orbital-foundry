using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The board under each cell, in the current <see cref="ArtStyle"/>:
    /// Station - an open space-frame platform: deck plating on box-truss girders, corner nodes and a keel hub.
    /// Belt - a small asteroid sliced flat on top: a dusty plateau with a jagged rocky underside, loose boulders round
    /// the rim and beacon posts that light in the owner's colour.
    /// The deck surface is at y = 0 relative to the cell's layout origin.
    /// </summary>
    public static class BoardArt
    {
        public const float DeckTop = -0.04f;

        static Mesh stationMesh;
        static Material[] stationMaterials;

        public sealed class Deck
        {
            public GameObject Root;
            /// <summary>The part painted in the owner's colour (trim lights or beacons).</summary>
            public MeshRenderer Trim;
        }

        public static Deck Build(Transform parent, Vector3 center, int width, int height, int cellId)
        {
            var root = new GameObject("Deck");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            var deck = new Deck { Root = root };
            if (ArtDirection.Belt) BuildIsland(deck, width, height, cellId);
            else BuildPlatform(deck, width, height);
            return deck;
        }

        // ---------------------------------------------------------------- Station: truss platform

        static void BuildPlatform(Deck deck, int width, int height)
        {
            var hull = new GameObject("Platform");
            hull.transform.SetParent(deck.Root.transform, false);
            if (stationMesh == null)
            {
                var k = new MeshKit();
                PlatformMesh(k, width, height);
                stationMesh = k.Build("Platform " + width + "x" + height);
                stationMaterials = k.Materials;
            }
            hull.AddComponent<MeshFilter>().sharedMesh = stationMesh;
            hull.AddComponent<MeshRenderer>().sharedMaterials = stationMaterials;

            // Running lights along the outer girders, in the owner's colour.
            float w = width, h = height;
            var lights = new MeshKit();
            var mat = Palette.Get(Palette.Cream);
            for (int i = 0; i <= width; i += 2)
            {
                lights.Box(new Vector3(-w * 0.5f + i, DeckTop - 0.1f, h * 0.5f - 0.02f), new Vector3(0.18f, 0.05f, 0.05f), mat);
                lights.Box(new Vector3(-w * 0.5f + i, DeckTop - 0.1f, -h * 0.5f + 0.02f), new Vector3(0.18f, 0.05f, 0.05f), mat);
            }
            for (int i = 0; i <= height; i += 2)
            {
                lights.Box(new Vector3(w * 0.5f - 0.02f, DeckTop - 0.1f, -h * 0.5f + i), new Vector3(0.05f, 0.05f, 0.18f), mat);
                lights.Box(new Vector3(-w * 0.5f + 0.02f, DeckTop - 0.1f, -h * 0.5f + i), new Vector3(0.05f, 0.05f, 0.18f), mat);
            }
            deck.Trim = lights.Instantiate("Running Lights", deck.Root.transform, false).GetComponent<MeshRenderer>();
        }

        static void PlatformMesh(MeshKit k, int width, int height)
        {
            var plate = Palette.Get(Palette.Hex(0x2A3440), Palette.Finish.Matte);
            var seam = Palette.Get(Palette.Hex(0x1C232B), Palette.Finish.Matte);
            var girder = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var gold = Palette.Get(Palette.Hex(0xC9A24A), Palette.Finish.Chrome);
            var cream = Palette.Get(Palette.Cream);
            var glow = Palette.Get(Palette.Hex(0x7FF5E0), Palette.Finish.Glow);
            float w = width, h = height;
            float top = DeckTop;
            // Deck plates, one per 5x5 block with dark seams between, so the platform reads as panels.
            for (int bx = 0; bx < width; bx += 5)
                for (int bz = 0; bz < height; bz += 5)
                {
                    float cx = -w * 0.5f + bx + 2.5f, cz = -h * 0.5f + bz + 2.5f;
                    k.Box(new Vector3(cx, top - 0.025f, cz), new Vector3(4.9f, 0.05f, 4.9f), plate);
                }
            k.Box(new Vector3(0, top - 0.06f, 0), new Vector3(w, 0.03f, h), seam);
            // Box-truss girders under every 5th line: two chords and zig-zag diagonals.
            for (int i = 0; i <= width; i += 5) Truss(k, new Vector3(-w * 0.5f + i, top - 0.1f, -h * 0.5f), new Vector3(-w * 0.5f + i, top - 0.1f, h * 0.5f), 0.55f, girder);
            for (int i = 0; i <= height; i += 5) Truss(k, new Vector3(-w * 0.5f, top - 0.1f, -h * 0.5f + i), new Vector3(w * 0.5f, top - 0.1f, -h * 0.5f + i), 0.55f, girder);
            // Node balls at the outer corners and a keel hub with radiators and a docking ring below the centre.
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                k.Sphere(new Vector3(c.x * w * 0.5f, top - 0.38f, c.y * h * 0.5f), 0.32f, 14, cream);
            k.Push(new Vector3(0, top - 0.7f, 0), Quaternion.Euler(180, 0, 0));
            k.Lathe(new[] { new Vector2(0, -0.1f), new Vector2(1.6f, -0.1f), new Vector2(1.7f, 0.4f), new Vector2(1.2f, 1.6f), new Vector2(0.5f, 2.4f), new Vector2(0, 2.5f) }, 24, cream);
            k.Pop();
            k.Torus(new Vector3(0, top - 1.1f, 0), 1.72f, 0.1f, 32, 6, gold);
            k.Torus(new Vector3(0, top - 2.6f, 0), 0.75f, 0.08f, 24, 6, glow);
            k.Radial(4, 0, kk => kk.Box(new Vector3(2.8f, top - 1.6f, 0), new Vector3(2.4f, 0.05f, 0.9f), cream));
        }

        /// <summary>
        /// An open truss bridge across the gap between two platform edges: girders spanning the gap at intervals and
        /// a girder along each edge, so cross-platform rails have something to stand on.
        /// </summary>
        public static void Bridge(Transform parent, Vector3 from, Vector3 to, int edgeLength)
        {
            var k = new MeshKit();
            var girder = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var dir = (to - from).normalized;
            var along = Vector3.Cross(Vector3.up, dir);
            float half = edgeLength * 0.5f;
            var y = Vector3.up * (DeckTop - 0.1f);
            foreach (float s in new[] { -half, -half * 0.5f, 0f, half * 0.5f, half })
                Truss(k, from + along * s + y, to + along * s + y, 0.4f, girder);
            Truss(k, from + along * -half + y, from + along * half + y, 0.4f, girder);
            Truss(k, to + along * -half + y, to + along * half + y, 0.4f, girder);
            var go = k.Instantiate("Bridge", parent, true);
            go.transform.position = Vector3.zero;
        }

        static void Truss(MeshKit k, Vector3 a, Vector3 b, float depth, Material mat)
        {
            var down = Vector3.down * depth;
            k.Strut(a, b, 0.08f, mat);
            k.Strut(a + down, b + down, 0.08f, mat);
            float len = Vector3.Distance(a, b);
            int bays = Mathf.Max(1, Mathf.RoundToInt(len / 1.25f));
            for (int i = 0; i < bays; i++)
            {
                var p0 = Vector3.Lerp(a, b, (float)i / bays);
                var p1 = Vector3.Lerp(a, b, (float)(i + 1) / bays);
                k.Strut(i % 2 == 0 ? p0 : p0 + down, i % 2 == 0 ? p1 + down : p1, 0.045f, mat);
            }
        }

        // ---------------------------------------------------------------- Belt: asteroid island

        static void BuildIsland(Deck deck, int width, int height, int cellId)
        {
            // Each asteroid gets its own mineral cast: rusty, slate, sandy or violet.
            var casts = new[] { Palette.Hex(0x7A5E52), Palette.Hex(0x5E6470), Palette.Hex(0x7C7058), Palette.Hex(0x6A5A70) };
            var cast = casts[Mathf.Abs(cellId * 7 + 3) % casts.Length];
            var top = Palette.Get(Color.Lerp(Palette.Hex(0x605854), cast, 0.45f), Palette.Finish.Matte);
            var cliff = Palette.Get(Color.Lerp(Palette.Hex(0x4D4548), cast, 0.35f), Palette.Finish.Matte);
            var under = Palette.Get(Color.Lerp(Palette.Hex(0x4A4048), cast, 0.3f), Palette.Finish.Matte);
            var pad = Palette.Get(Color.Lerp(Palette.Hex(0x524A48), cast, 0.3f), Palette.Finish.Matte);
            var k = new MeshKit();
            k.Island(width, height, cellId, DeckTop, top, cliff, under);
            // A slightly darker graded pad marks the buildable grid on the plateau.
            k.Box(new Vector3(0, DeckTop + 0.005f, 0), new Vector3(width, 0.02f, height), pad);
            // Loose boulders on the rim, outside the grid.
            var rng = new System.Random(cellId * 31 + 7);
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Max(width, height) * 0.5f + 0.6f + (float)rng.NextDouble() * 1.2f;
                var p = new Vector3(Mathf.Cos(a) * r, DeckTop + 0.1f, Mathf.Sin(a) * r);
                p.x = Mathf.Clamp(p.x, -width * 0.5f - 2f, width * 0.5f + 2f);
                p.z = Mathf.Clamp(p.z, -height * 0.5f - 2f, height * 0.5f + 2f);
                if (Mathf.Abs(p.x) < width * 0.5f + 0.3f && Mathf.Abs(p.z) < height * 0.5f + 0.3f) continue;
                k.Rock(p, 0.25f + (float)rng.NextDouble() * 0.35f, cellId * 100 + i, 0.3f, cliff, 0.7f);
            }
            var go = k.Instantiate("Asteroid", deck.Root.transform, true);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // Beacon posts round the plateau, lit in the owner's colour.
            var posts = new MeshKit();
            var chrome = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var beacons = new MeshKit();
            var beaconMat = Palette.Get(Palette.Cream);
            float hw = width * 0.5f + 0.35f, hh = height * 0.5f + 0.35f;
            foreach (var c in new[] { new Vector2(-hw, -hh), new Vector2(hw, -hh), new Vector2(hw, hh), new Vector2(-hw, hh), new Vector2(0, -hh), new Vector2(0, hh), new Vector2(-hw, 0), new Vector2(hw, 0) })
            {
                posts.Cylinder(new Vector3(c.x, DeckTop, c.y), 0.06f, 0.55f, 8, chrome);
                beacons.Sphere(new Vector3(c.x, DeckTop + 0.62f, c.y), 0.11f, 10, beaconMat);
            }
            posts.Instantiate("Beacon Posts", deck.Root.transform, true);
            deck.Trim = beacons.Instantiate("Beacons", deck.Root.transform, false).GetComponent<MeshRenderer>();
        }
    }
}
