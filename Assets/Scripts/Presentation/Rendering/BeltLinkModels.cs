using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Drone links between asteroids: the Drone Pad building, the dotted flight path a link draws between two pads, and
    /// the cargo drone that flies it.
    /// </summary>
    public static partial class ModelLibrary
    {
        /// <summary>Height of the flight path above the straight line between two pads, <paramref name="t"/> in 0-1.</summary>
        public static Vector3 LinkArc(Vector3 from, Vector3 to, float t)
        {
            float arc = 0.9f + Vector3.Distance(from, to) * 0.12f;
            return Vector3.Lerp(from, to, t) + Vector3.up * (0.35f + Mathf.Sin(t * Mathf.PI) * arc);
        }

        /// <summary>The dotted flight path between two pads (at <paramref name="from"/> and <paramref name="to"/>).</summary>
        public static Model DroneLink(Vector3 from, Vector3 to, FactionId owner, Transform parent)
        {
            if (!Palette.Ready) return null;
            var model = Begin("Drone Link", parent);
            model.Root.transform.position = from;
            var k = new MeshKit();
            int dots = Mathf.Max(6, Mathf.RoundToInt(Vector3.Distance(from, to) * 1.6f));
            var dotMat = Palette.Get(Color.Lerp(FactionPaint(owner), Color.white, 0.4f), Palette.Finish.Glow);
            for (int i = 1; i < dots; i++)
            {
                float t = (float)i / dots;
                k.Sphere(LinkArc(from, to, t) - from, 0.035f, 6, dotMat);
            }
            Part(k, "Path", model.Root.transform, model, false);
            return model;
        }

        /// <summary>A landing pad: a ringed deck with an approach-light ring in the owner's colour, corner lamps and a beacon mast.</summary>
        static Model DronePad(FactionId owner, Transform parent)
        {
            var model = Begin("Drone Pad", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.46f, 0.08f, 28, Gunmetal);
            k.Cylinder(new Vector3(0, 0.08f, 0), 0.4f, 0.03f, 28, Cream);
            k.Torus(new Vector3(0, 0.11f, 0), 0.33f, 0.025f, 32, 5, AccentGlow(owner));
            // A landing cross on the deck.
            k.Box(new Vector3(0, 0.115f, 0), new Vector3(0.36f, 0.01f, 0.06f), Accent(owner));
            k.Box(new Vector3(0, 0.115f, 0), new Vector3(0.06f, 0.01f, 0.36f), Accent(owner));
            k.Radial(4, 45, kk => kk.Sphere(new Vector3(0.43f, 0.09f, 0), 0.035f, 8, AtomGlow));
            k.Cylinder(new Vector3(0.34f, 0.08f, -0.34f), 0.025f, 0.6f, 8, Chrome);
            Part(k, "Pad", model.Root.transform, model);

            var beacon = new MeshKit();
            beacon.Sphere(Vector3.zero, 0.05f, 10, AtomGlow);
            beacon.Push(new Vector3(0, 0.02f, 0), Quaternion.Euler(-30, 0, 0))
                .Lathe(new[] { new Vector2(0, -0.01f), new Vector2(0.12f, 0.03f), new Vector2(0.11f, 0.045f), new Vector2(0, 0.005f) }, 16, Ivory)
                .Pop();
            var beaconGo = Part(beacon, "Beacon", model.Root.transform, model, false);
            beaconGo.transform.localPosition = new Vector3(0.34f, 0.72f, -0.34f);
            Spin(model, beaconGo, Vector3.up, 90f);
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
