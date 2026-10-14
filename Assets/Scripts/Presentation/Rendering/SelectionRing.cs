using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The selection marker: a glowing 3D ring lying on the deck around whatever is selected, sized to clear its
    /// footprint so a building's base never hides it, with four inward-pointing ticks that turn slowly and a soft
    /// pulse so the eye finds it.
    /// </summary>
    public sealed class SelectionRing
    {
        readonly GameObject root;
        readonly Transform spinner;
        readonly MeshRenderer ring, ticks;
        Color shown = new Color(-1, -1, -1);
        float radius = 0.6f;

        static Mesh ringMesh, tickMesh;

        public SelectionRing(Transform parent)
        {
            root = new GameObject("Selection");
            root.transform.SetParent(parent, false);
            ring = Part(root.transform, "Ring", RingMesh());
            var spin = new GameObject("Ticks");
            spin.transform.SetParent(root.transform, false);
            spinner = spin.transform;
            ticks = Part(spinner, "Ticks", TickMesh());
            Hide();
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>A ring of radius 1 lying flat, thin enough to read as a line once scaled to a footprint.</summary>
        static Mesh RingMesh()
        {
            if (ringMesh != null) return ringMesh;
            var k = new MeshKit();
            k.Torus(Vector3.zero, 1f, 0.045f, 64, 8, Palette.Get(Color.white));
            ringMesh = k.Build("Selection Ring");
            return ringMesh;
        }

        /// <summary>Four small arrowheads just outside the ring, pointing in at the selection.</summary>
        static Mesh TickMesh()
        {
            if (tickMesh != null) return tickMesh;
            var k = new MeshKit();
            k.Radial(4, 45f, kk => kk.Push(new Vector3(1.22f, 0.02f, 0), Quaternion.Euler(90, 0, 90))
                .Fin(new[] { new Vector2(0, -0.12f), new Vector2(0.13f, 0.1f), new Vector2(-0.13f, 0.1f) }, 0.05f, Palette.Get(Color.white))
                .Pop());
            tickMesh = k.Build("Selection Ticks");
            return tickMesh;
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        /// <summary>Shows the ring on the deck at <paramref name="ground"/> with the given radius and colour.</summary>
        public void Show(Vector3 ground, float ringRadius, Color color)
        {
            root.SetActive(true);
            radius = ringRadius;
            root.transform.position = ground + Vector3.up * (ModelLibrary.BeltSurface + 0.04f); // above conveyors crossing it
            if (color != shown)
            {
                shown = color;
                var m = Palette.Get(color, Palette.Finish.Glow);
                ring.sharedMaterial = m;
                ticks.sharedMaterial = m;
            }
        }

        /// <summary>Pulses the ring and turns the ticks; call every frame.</summary>
        public void Animate(float time)
        {
            if (!root.activeSelf) return;
            float pulse = 1f + Mathf.Sin(time * 4f) * 0.04f;
            root.transform.localScale = new Vector3(radius * pulse, radius, radius * pulse);
            spinner.localRotation = Quaternion.Euler(0, time * 30f, 0);
        }
    }
}
