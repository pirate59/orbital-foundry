using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Links for belts that cross from one cell to the next. Belt style: a pair of drone pads with a dotted flight path
    /// between the asteroids and a cargo drone shuttling along it. Station style: a raised transfer rail across the
    /// bridge between platforms.
    /// </summary>
    public static partial class ModelLibrary
    {
        /// <summary>Height of the flight path above the straight line between two pads, <paramref name="t"/> in 0-1.</summary>
        public static Vector3 LinkArc(Vector3 from, Vector3 to, float t)
        {
            if (!ArtDirection.Belt) return Vector3.Lerp(from, to, t) + Vector3.up * 0.42f;
            float arc = 0.9f + Vector3.Distance(from, to) * 0.12f;
            return Vector3.Lerp(from, to, t) + Vector3.up * (0.35f + Mathf.Sin(t * Mathf.PI) * arc);
        }

        /// <summary>Two landing pads (at <paramref name="from"/> and <paramref name="to"/>) and the dotted path between them.</summary>
        public static Model DroneLink(Vector3 from, Vector3 to, FactionId owner, Transform parent)
        {
            if (!Palette.Ready) return null;
            if (!ArtDirection.Belt) return TransferRail(from, to, owner, parent);
            var model = Begin("Drone Link", parent);
            model.Root.transform.position = from;
            var k = new MeshKit();
            foreach (var p in new[] { Vector3.zero, to - from })
            {
                k.Cylinder(p, 0.36f, 0.06f, 18, Gunmetal);
                k.Torus(p + Vector3.up * 0.065f, 0.28f, 0.03f, 20, 4, AccentGlow(owner));
                k.Cylinder(p + new Vector3(0.28f, 0, 0.28f), 0.025f, 0.45f, 6, Chrome);
                k.Sphere(p + new Vector3(0.28f, 0.48f, 0.28f), 0.045f, 8, AtomGlow);
            }
            int dots = Mathf.Max(6, Mathf.RoundToInt(Vector3.Distance(from, to) * 1.6f));
            var dotMat = Palette.Get(Color.Lerp(FactionPaint(owner), Color.white, 0.4f), Palette.Finish.Glow);
            for (int i = 1; i < dots; i++)
            {
                float t = (float)i / dots;
                k.Sphere(LinkArc(from, to, t) - from, 0.035f, 6, dotMat);
            }
            Part(k, "Pads", model.Root.transform, model, false);
            return model;
        }

        /// <summary>Two chrome rails on posts with a lit strip between, a little above the bridge deck.</summary>
        static Model TransferRail(Vector3 from, Vector3 to, FactionId owner, Transform parent)
        {
            var model = Begin("Transfer Rail", parent);
            model.Root.transform.position = from;
            var k = new MeshKit();
            var d = to - from;
            var side = Vector3.Cross(Vector3.up, d.normalized) * 0.12f;
            var lift = Vector3.up * 0.36f;
            k.Strut(lift + side, d + lift + side, 0.03f, Chrome);
            k.Strut(lift - side, d + lift - side, 0.03f, Chrome);
            k.Strut(lift - Vector3.up * 0.03f, d + lift - Vector3.up * 0.03f, 0.05f, AccentGlow(owner));
            int posts = Mathf.Max(2, Mathf.RoundToInt(d.magnitude));
            for (int i = 0; i <= posts; i++)
            {
                var p = d * ((float)i / posts);
                k.Strut(p, p + lift, 0.04f, Gunmetal);
                k.Strut(p + lift - side * 1.3f, p + lift + side * 1.3f, 0.03f, Gunmetal);
            }
            Part(k, "Rail", model.Root.transform, model, true);
            return model;
        }

        /// <summary>A little cargo drone: a cream pod on four rotor arms with a glowing belly light.</summary>
        public static Model Drone(FactionId owner, Transform parent)
        {
            if (!Palette.Ready) return null;
            var model = Begin("Drone", parent);
            var k = new MeshKit();
            k.Sphere(Vector3.zero, 0.11f, 14, Cream);
            k.Torus(Vector3.zero, 0.112f, 0.018f, 18, 4, Accent(owner));
            k.Sphere(new Vector3(0, -0.09f, 0), 0.045f, 8, AtomGlow);
            k.Radial(4, 45, kk => kk.Strut(new Vector3(0.08f, 0.02f, 0), new Vector3(0.22f, 0.05f, 0), 0.025f, Chrome)
                .Cylinder(new Vector3(0.22f, 0.05f, 0), 0.08f, 0.015f, 12, Gunmetal));
            var go = Part(k, "Drone", model.Root.transform, model);
            Spin(model, go, Vector3.up, 90f);
            return model;
        }
    }
}
