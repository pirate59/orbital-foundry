using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// A small 3D marker that floats over the board and turns to face the camera: a status ring, a warning sign, an
    /// HP bar segment or a pip. Its colour comes from the shared palette (glowing or enamel), snapped to a few levels
    /// per channel so a smoothly changing colour, like an HP bar fading red, reuses a handful of materials.
    /// Meshes face -Z, like a sprite, so giving one the camera's rotation shows its face.
    /// </summary>
    public sealed class Marker
    {
        public enum Shape { Ring, Warning, Bar, Pip, Chevron }

        public readonly GameObject Root;
        readonly MeshRenderer renderer;
        readonly MeshRenderer accent;
        readonly bool glow;
        Color shown = new Color(-1, -1, -1);

        static readonly Dictionary<Shape, Mesh> meshes = new Dictionary<Shape, Mesh>();
        static Mesh bang;

        public Transform Transform => Root.transform;

        public bool Enabled
        {
            get { return renderer.enabled; }
            set
            {
                renderer.enabled = value;
                if (accent != null) accent.enabled = value;
            }
        }

        public Color Color
        {
            set
            {
                var c = new Color(Snap(value.r), Snap(value.g), Snap(value.b));
                if (c == shown) return;
                shown = c;
                renderer.sharedMaterial = Palette.Get(c, glow ? Palette.Finish.Glow : Palette.Finish.Enamel);
            }
        }

        static float Snap(float v) { return Mathf.Round(Mathf.Clamp01(v) * 8f) / 8f; }

        public Marker(string name, Shape shape, Transform parent, bool glowing = true)
        {
            glow = glowing;
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            Root.AddComponent<MeshFilter>().sharedMesh = MeshFor(shape);
            renderer = Root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Color = Color.white;
            if (shape == Shape.Warning)
            {
                // A dark exclamation mark standing proud of the sign's face.
                var mark = new GameObject("Mark");
                mark.transform.SetParent(Root.transform, false);
                mark.AddComponent<MeshFilter>().sharedMesh = BangMesh();
                accent = mark.AddComponent<MeshRenderer>();
                accent.sharedMaterial = Palette.Get(Palette.Hex(0x1A1420), Palette.Finish.Matte);
                accent.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        static Mesh MeshFor(Shape shape)
        {
            Mesh mesh;
            if (meshes.TryGetValue(shape, out mesh)) return mesh;
            var k = new MeshKit();
            var m = Palette.Get(Color.white);
            switch (shape)
            {
                case Shape.Ring:
                    // A thick ring of radius 0.5 in the XY plane.
                    k.Push(Vector3.zero, Quaternion.Euler(90, 0, 0)).Torus(Vector3.zero, 0.5f, 0.07f, 28, 8, m).Pop();
                    break;
                case Shape.Warning:
                    // A rounded warning triangle about one unit across.
                    k.Fin(new[] { new Vector2(-0.5f, -0.4f), new Vector2(0.5f, -0.4f), new Vector2(0f, 0.48f) }, 0.12f, m);
                    break;
                case Shape.Chevron:
                    // An upward chevron about one unit wide, for priority badges (turned over for low).
                    k.Fin(new[] { new Vector2(0f, 0.3f), new Vector2(0.5f, -0.1f), new Vector2(0.5f, -0.3f), new Vector2(0f, 0.08f) }, 0.12f, m);
                    k.Fin(new[] { new Vector2(0f, 0.3f), new Vector2(0f, 0.08f), new Vector2(-0.5f, -0.3f), new Vector2(-0.5f, -0.1f) }, 0.12f, m);
                    break;
                case Shape.Bar:
                    // A unit box, thin in depth; scale X for length and Y for thickness.
                    k.Box(Vector3.zero, new Vector3(1f, 1f, 0.6f), m);
                    break;
                default:
                    k.Sphere(Vector3.zero, 0.5f, 10, m);
                    break;
            }
            mesh = k.Build("Marker " + shape);
            meshes[shape] = mesh;
            return mesh;
        }

        static Mesh BangMesh()
        {
            if (bang != null) return bang;
            var k = new MeshKit();
            var m = Palette.Get(Color.black);
            k.Box(new Vector3(0, 0.02f, -0.08f), new Vector3(0.11f, 0.38f, 0.06f), m);
            k.Box(new Vector3(0, -0.27f, -0.08f), new Vector3(0.11f, 0.1f, 0.06f), m);
            bang = k.Build("Marker Bang");
            return bang;
        }
    }
}
