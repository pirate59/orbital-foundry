using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The board itself: each cell is a floating enamel deck with a chrome lip, a faction trim band, and a finned
    /// keel pod with a glowing levitator ring underneath, so tilting the camera shows the platforms hanging in space.
    /// </summary>
    public static class BoardArt
    {
        public const float DeckTop = -0.04f;
        const float Inset = 0.14f;
        const float Thickness = 0.55f;

        static Mesh deckMesh;
        static Material[] deckMaterials;

        public sealed class Deck
        {
            public GameObject Root;
            public MeshRenderer Trim;
        }

        public static Deck Build(Transform parent, Vector3 center, int width, int height)
        {
            var root = new GameObject("Deck");
            root.transform.SetParent(parent, false);
            root.transform.position = center;

            var hull = new GameObject("Hull");
            hull.transform.SetParent(root.transform, false);
            hull.AddComponent<MeshFilter>().sharedMesh = DeckMesh(width, height);
            var hr = hull.AddComponent<MeshRenderer>();
            hr.sharedMaterials = deckMaterials;

            float w = width - Inset * 2, h = height - Inset * 2;
            var trim = new MeshKit();
            var trimMat = Palette.Get(Palette.Cream);
            float y = DeckTop - Thickness * 0.55f;
            trim.Box(new Vector3(0, y, h * 0.5f + 0.02f), new Vector3(w + 0.08f, 0.09f, 0.06f), trimMat)
                .Box(new Vector3(0, y, -h * 0.5f - 0.02f), new Vector3(w + 0.08f, 0.09f, 0.06f), trimMat)
                .Box(new Vector3(w * 0.5f + 0.02f, y, 0), new Vector3(0.06f, 0.09f, h + 0.08f), trimMat)
                .Box(new Vector3(-w * 0.5f - 0.02f, y, 0), new Vector3(0.06f, 0.09f, h + 0.08f), trimMat);
            var trimGo = trim.Instantiate("Trim", root.transform, false);
            return new Deck { Root = root, Trim = trimGo.GetComponent<MeshRenderer>() };
        }

        static Mesh DeckMesh(int width, int height)
        {
            if (deckMesh != null) return deckMesh;
            var hullMat = Palette.Get(Palette.DeepTeal);
            var topMat = Palette.Get(Palette.Hex(0x1A4A52), Palette.Finish.Matte);
            var chrome = Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
            var dark = Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
            var glow = Palette.Get(Palette.Hex(0x7FF5E0), Palette.Finish.Glow);
            var k = new MeshKit();
            float w = width - Inset * 2, h = height - Inset * 2;
            // Deck plate, a chrome lip, and the hull slab tapering underneath.
            k.Box(new Vector3(0, DeckTop - 0.03f, 0), new Vector3(w, 0.06f, h), topMat);
            k.Box(new Vector3(0, DeckTop - 0.1f, 0), new Vector3(w + 0.1f, 0.1f, h + 0.1f), chrome);
            k.Box(new Vector3(0, DeckTop - 0.15f - Thickness * 0.4f, 0), new Vector3(w - 0.1f, Thickness * 0.8f, h - 0.1f), hullMat);
            k.Box(new Vector3(0, DeckTop - 0.15f - Thickness * 0.95f, 0), new Vector3(w - 1.2f, Thickness * 0.3f, h - 1.2f), dark);
            // Keel pod: a streamlined teardrop hanging under the centre, with four fins and a levitator ring.
            float podTop = DeckTop - 0.15f - Thickness * 1.1f;
            k.Push(new Vector3(0, podTop, 0), Quaternion.Euler(180, 0, 0), Vector3.one);
            k.SmoothLathe(new[]
            {
                new Vector2(0.0f, 0f), new Vector2(2.4f, 0.05f), new Vector2(2.6f, 0.4f), new Vector2(2.2f, 1.2f),
                new Vector2(1.4f, 2.3f), new Vector2(0.6f, 3.2f), new Vector2(0.15f, 3.7f), new Vector2(0f, 3.8f)
            }, 28, hullMat);
            k.Radial(4, 45, kk => kk.Push(new Vector3(0, 0.6f, 0), Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(1.6f, 0f), new Vector2(3.4f, -0.2f), new Vector2(3.6f, 1.2f), new Vector2(1.4f, 2.6f) }, 0.14f, chrome).Pop());
            k.Pop();
            k.Torus(new Vector3(0, podTop - 0.55f, 0), 2.45f, 0.13f, 40, 8, glow);
            k.Torus(new Vector3(0, podTop - 2.2f, 0), 1.55f, 0.09f, 32, 6, glow);
            deckMesh = k.Build("Deck " + width + "x" + height);
            deckMaterials = k.Materials;
            return deckMesh;
        }
    }
}
