using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The board under each cell: a small asteroid sliced flat on top, a dusty plateau with a jagged rocky underside,
    /// loose boulders round the rim and safety lights round the edge that show who holds it.
    /// The deck surface is at y = 0 relative to the cell's layout origin.
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

        public static Deck Build(Transform parent, Vector3 center, int width, int height, int cellId)
        {
            var root = new GameObject("Deck");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            var deck = new Deck { Root = root };
            BuildIsland(deck, width, height, cellId);
            return deck;
        }

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
                posts.Cylinder(new Vector3(c.x, DeckTop, c.y), 0.06f, 0.65f, 8, chrome);
                posts.Cylinder(new Vector3(c.x, DeckTop + 0.65f, c.y), 0.19f, 0.08f, 12, housing);
                beacons.Sphere(new Vector3(c.x, DeckTop + 0.73f, c.y), 0.16f, 12, beaconMat, true);
                halos.Sphere(new Vector3(c.x, DeckTop + 0.78f, c.y), 0.42f, 12, haloMat);
            }
            posts.Instantiate("Beacon Posts", deck.Root.transform, true);
            deck.Trim = beacons.Instantiate("Beacons", deck.Root.transform, false).GetComponent<MeshRenderer>();
            deck.Halo = halos.Instantiate("Beacon Halos", deck.Root.transform, false).GetComponent<MeshRenderer>();
            deck.Halo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
