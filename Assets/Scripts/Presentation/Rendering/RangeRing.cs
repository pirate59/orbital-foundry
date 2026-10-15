using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// A drone pad's reach: a thin glowing circle lying flat at the pad's height, with a faint wash inside it, sized to
    /// the pad's drone range. Range is measured flat across the field, so the circle shows exactly what is in reach.
    /// </summary>
    public sealed class RangeRing
    {
        readonly GameObject root;
        static Mesh ringMesh, discMesh;

        public RangeRing(Transform parent)
        {
            root = new GameObject("Drone Range");
            root.transform.SetParent(parent, false);
            if (ringMesh == null)
            {
                var k = new MeshKit();
                k.Torus(Vector3.zero, 1f, 0.006f, 128, 6, Palette.Get(Color.white));
                ringMesh = k.Build("Range Ring");
                var d = new MeshKit();
                d.Cylinder(Vector3.zero, 1f, 0.001f, 96, Palette.Get(Color.white));
                discMesh = d.Build("Range Disc");
            }
            var ring = Part("Ring", ringMesh);
            ring.sharedMaterial = Palette.Get(new Color(0.5f, 1f, 0.85f), Palette.Finish.Glow);
            var disc = Part("Disc", discMesh);
            disc.sharedMaterial = Palette.Flat(new Color(0.012f, 0.04f, 0.033f), true);
            root.SetActive(false);
        }

        MeshRenderer Part(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        public void Show(Vector3 center, float radius)
        {
            root.SetActive(true);
            root.transform.position = center + Vector3.up * 0.15f;
            // Scale flat: the band stays thin however wide the circle grows.
            root.transform.localScale = new Vector3(radius, 1f, radius);
        }

        public void Hide()
        {
            root.SetActive(false);
        }
    }
}
