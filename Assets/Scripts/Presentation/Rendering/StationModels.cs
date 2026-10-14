using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Station-style buildings: 1950s orbital modules (pressurised cylinders and spheres, solar wings, radiators,
    /// docking collars and truss frames) mounted on the platform by a docking pylon, in the same paint as the rest of
    /// the set. A von Braun wheel serves as the Headquarters.
    /// </summary>
    public static partial class ModelLibrary
    {
        static Material Foil => Palette.Get(Palette.Hex(0xC9A24A), Palette.Finish.Chrome);
        static Material Radiator => Palette.Get(Palette.Hex(0xE9E6DA), Palette.Finish.Matte);

        static Model StationModule(string typeId, FactionId owner, Transform parent)
        {
            switch (typeId)
            {
                case "headquarters": return WheelStation(owner, parent);
                case "extractor": return MiningTug(owner, parent);
                case "refinery": return SmelterModule(owner, parent);
                case "purifier": return TankModule(owner, parent);
                case "assembler": return WorkshopModule(owner, parent);
                case "sale_depot": return DockingPort(owner, parent);
                case "shipyard": return Drydock(owner, parent);
                case "fort": return DefenceSphere(owner, parent);
                case "fort_site": return FrameSite(owner, parent);
                case "storage": return TankFarm(owner, parent);
                case "solar_array": return SolarWing(owner, parent);
            }
            return null;
        }

        /// <summary>A docking pylon from the deck plate up to <paramref name="height"/>, ending in a collar.</summary>
        static void Pylon(MeshKit k, float height, FactionId owner)
        {
            k.Cylinder(Vector3.zero, 0.3f, 0.04f, 20, Gunmetal);
            k.Torus(new Vector3(0, 0.04f, 0), 0.27f, 0.018f, 20, 4, Accent(owner));
            k.Cone(new Vector3(0, 0.04f, 0), 0.1f, 0.06f, height - 0.04f, 12, Chrome);
            k.Torus(new Vector3(0, height, 0), 0.09f, 0.025f, 16, 5, Foil);
        }

        /// <summary>A pressurised cylinder along X with rounded ends and a docking collar at each end.</summary>
        static void Hull(MeshKit k, Vector3 center, float length, float radius, Material body, Material band)
        {
            k.Push(center, Quaternion.Euler(0, 0, 90));
            float half = length * 0.5f;
            k.SmoothLathe(new[]
            {
                new Vector2(0, -half - radius * 0.35f), new Vector2(radius * 0.6f, -half - radius * 0.2f), new Vector2(radius * 0.95f, -half),
                new Vector2(radius, -half + radius * 0.2f), new Vector2(radius, half - radius * 0.2f), new Vector2(radius * 0.95f, half),
                new Vector2(radius * 0.6f, half + radius * 0.2f), new Vector2(0, half + radius * 0.35f)
            }, 20, body);
            k.Torus(new Vector3(0, -half * 0.45f, 0), radius * 1.01f, radius * 0.08f, 22, 4, band);
            k.Torus(new Vector3(0, half * 0.45f, 0), radius * 1.01f, radius * 0.08f, 22, 4, band);
            k.Cylinder(new Vector3(0, half + radius * 0.3f, 0), radius * 0.4f, radius * 0.25f, 14, Chrome);
            k.Pop();
        }

        /// <summary>A solar wing on a short boom, sticking out along <paramref name="direction"/>.</summary>
        static void Wing(MeshKit k, Vector3 root, Vector3 direction, float length, float span)
        {
            var end = root + direction * length;
            k.Strut(root, end, 0.025f, Chrome);
            var mid = root + direction * (length * 0.55f + 0.04f);
            var rot = Quaternion.LookRotation(direction, Vector3.up);
            k.Push(mid, rot);
            k.Box(Vector3.zero, new Vector3(span, 0.012f, length * 0.9f), PanelBlue);
            for (int i = -1; i <= 1; i++) k.Box(new Vector3(0, 0.008f, i * length * 0.3f), new Vector3(span + 0.02f, 0.012f, 0.012f), Foil);
            k.Pop();
        }

        /// <summary>The von Braun wheel: a turning habitat ring on three spokes round a hub, with a spire and atom.</summary>
        static Model WheelStation(FactionId owner, Transform parent)
        {
            var model = Begin("Wheel Station", parent);
            var k = new MeshKit();
            Pylon(k, 0.55f, owner);
            k.Cylinder(new Vector3(0, 0.55f, 0), 0.24f, 0.6f, 20, Cream);
            k.Torus(new Vector3(0, 0.62f, 0), 0.25f, 0.03f, 20, 4, Accent(owner));
            k.Torus(new Vector3(0, 1.08f, 0), 0.25f, 0.03f, 20, 4, Foil);
            k.Cone(new Vector3(0, 1.15f, 0), 0.24f, 0.08f, 0.25f, 20, Ivory);
            k.Cylinder(new Vector3(0, 1.4f, 0), 0.03f, 0.75f, 8, Chrome);
            Wing(k, new Vector3(0, 1.75f, 0), Vector3.right, 0.6f, 0.24f);
            Wing(k, new Vector3(0, 1.75f, 0), Vector3.left, 0.6f, 0.24f);
            Part(k, "Hub", model.Root.transform, model);

            var wheel = new MeshKit();
            wheel.Torus(Vector3.zero, 1.0f, 0.15f, 56, 10, Ivory);
            wheel.Torus(new Vector3(0, 0.1f, 0), 1.0f, 0.06f, 56, 6, Accent(owner));
            wheel.Radial(20, 0, kk => kk.Sphere(new Vector3(1.145f, 0, 0), 0.035f, 6, Window));
            wheel.Radial(3, 0, kk => kk.Strut(new Vector3(0.24f, 0, 0), new Vector3(0.88f, 0, 0), 0.07f, Chrome)
                .Cylinder(new Vector3(0.56f, -0.05f, 0), 0.05f, 0.1f, 8, Cream));
            var wheelGo = Part(wheel, "Wheel", model.Root.transform, model);
            wheelGo.transform.localPosition = new Vector3(0, 0.85f, 0);
            Spin(model, wheelGo, Vector3.up, 12f);

            var atom = new MeshKit();
            atom.Sphere(Vector3.zero, 0.085f, 14, AtomGlow);
            for (int i = 0; i < 3; i++)
                atom.Push(Vector3.zero, Quaternion.Euler(70, i * 60, 0)).Torus(Vector3.zero, 0.2f, 0.012f, 28, 5, Chrome).Pop();
            var atomGo = Part(atom, "Atom", model.Root.transform, model, false);
            atomGo.transform.localPosition = new Vector3(0, 2.3f, 0);
            Spin(model, atomGo, Vector3.up, 60f);
            return model;
        }

        /// <summary>A mining tug hovering over its asteroid, gripping it with three claws and a cutting beam.</summary>
        static Model MiningTug(FactionId owner, Transform parent)
        {
            var model = Begin("Mining Tug", parent);
            var k = new MeshKit();
            k.Push(new Vector3(0, 0.82f, 0));
            k.SmoothLathe(new[]
            {
                new Vector2(0, -0.1f), new Vector2(0.12f, -0.08f), new Vector2(0.18f, 0.02f), new Vector2(0.19f, 0.22f),
                new Vector2(0.15f, 0.34f), new Vector2(0.08f, 0.4f), new Vector2(0, 0.42f)
            }, 20, Cream);
            k.Torus(new Vector3(0, 0.12f, 0), 0.19f, 0.025f, 22, 4, Accent(owner));
            k.Cone(new Vector3(0, -0.18f, 0), 0.05f, 0.1f, 0.1f, 12, Gunmetal);
            k.Sphere(new Vector3(0, 0.44f, 0), 0.04f, 8, AtomGlow);
            k.Radial(3, 30, kk => kk.Strut(new Vector3(0.15f, 0.02f, 0), new Vector3(0.36f, -0.32f, 0), 0.035f, Chrome)
                .Strut(new Vector3(0.36f, -0.32f, 0), new Vector3(0.24f, -0.56f, 0), 0.03f, Chrome)
                .Sphere(new Vector3(0.36f, -0.32f, 0), 0.03f, 6, Cherry));
            Wing(k, new Vector3(0, 0.22f, 0), Vector3.forward, 0.38f, 0.16f);
            Wing(k, new Vector3(0, 0.22f, 0), Vector3.back, 0.38f, 0.16f);
            k.Pop();
            Part(k, "Tug", model.Root.transform, model);

            var beam = new MeshKit();
            beam.Cone(Vector3.zero, 0.02f, 0.06f, 0.42f, 10, Ember);
            var beamGo = Part(beam, "Cutting Beam", model.Root.transform, model, false);
            beamGo.transform.localPosition = new Vector3(0, 0.22f, 0);
            Spin(model, beamGo, Vector3.up, 300f);
            return model;
        }

        /// <summary>A smelter module: a long cylinder with a glowing furnace band and white radiator panels.</summary>
        static Model SmelterModule(FactionId owner, Transform parent)
        {
            var model = Begin("Smelter Module", parent);
            var k = new MeshKit();
            Pylon(k, 0.42f, owner);
            Hull(k, new Vector3(0, 0.62f, 0), 0.62f, 0.2f, Cream, Accent(owner));
            k.Push(new Vector3(0, 0.62f, 0), Quaternion.Euler(0, 0, 90)).Lathe(new[] { new Vector2(0.205f, -0.06f), new Vector2(0.205f, 0.06f) }, 22, Ember).Pop();
            foreach (float z in new[] { -1f, 1f })
            {
                k.Strut(new Vector3(0, 0.62f, z * 0.18f), new Vector3(0, 0.62f, z * 0.32f), 0.03f, Chrome);
                k.Box(new Vector3(0, 0.62f, z * 0.52f), new Vector3(0.7f, 0.015f, 0.36f), Radiator);
                for (int i = -2; i <= 2; i++) k.Box(new Vector3(i * 0.14f, 0.63f, z * 0.52f), new Vector3(0.012f, 0.012f, 0.36f), Chrome);
            }
            Part(k, "Smelter", model.Root.transform, model);
            return model;
        }

        /// <summary>A water tank module: a mint sphere in a chrome cage with a glowing gauge ring.</summary>
        static Model TankModule(FactionId owner, Transform parent)
        {
            var model = Begin("Tank Module", parent);
            var k = new MeshKit();
            Pylon(k, 0.36f, owner);
            k.Sphere(new Vector3(0, 0.68f, 0), 0.3f, 26, Mint);
            k.Torus(new Vector3(0, 0.68f, 0), 0.305f, 0.022f, 30, 5, MintGlow);
            k.Torus(new Vector3(0, 0.82f, 0), 0.27f, 0.015f, 30, 4, Chrome);
            k.Torus(new Vector3(0, 0.54f, 0), 0.27f, 0.015f, 30, 4, Chrome);
            k.Radial(4, 45, kk => kk.Strut(new Vector3(0.33f, 0.42f, 0), new Vector3(0.33f, 0.96f, 0), 0.025f, Chrome));
            k.Cylinder(new Vector3(0, 0.96f, 0), 0.06f, 0.1f, 12, Foil);
            Wing(k, new Vector3(0.0f, 1.05f, 0), Vector3.right, 0.42f, 0.14f);
            Part(k, "Tank", model.Root.transform, model);
            return model;
        }

        /// <summary>A workshop module: a boxy hab with portholes, a robot arm, twin solar wings and a turning dish.</summary>
        static Model WorkshopModule(FactionId owner, Transform parent)
        {
            var model = Begin("Workshop Module", parent);
            var k = new MeshKit();
            Pylon(k, 0.34f, owner);
            k.Box(new Vector3(0, 0.6f, 0), new Vector3(0.5f, 0.44f, 0.5f), Cream);
            k.Box(new Vector3(0, 0.6f, 0), new Vector3(0.52f, 0.06f, 0.52f), Accent(owner));
            foreach (var c in new[] { new Vector3(-0.25f, 0, -0.25f), new Vector3(0.25f, 0, -0.25f), new Vector3(0.25f, 0, 0.25f), new Vector3(-0.25f, 0, 0.25f) })
                k.Box(c + new Vector3(0, 0.6f, 0), new Vector3(0.05f, 0.46f, 0.05f), Chrome);
            foreach (float x in new[] { -0.12f, 0.12f })
                k.Push(new Vector3(x, 0.66f, 0.255f), Quaternion.Euler(90, 0, 0)).Torus(Vector3.zero, 0.05f, 0.012f, 14, 4, Chrome).Pop()
                    .Box(new Vector3(x, 0.66f, 0.258f), new Vector3(0.06f, 0.06f, 0.01f), Window);
            Wing(k, new Vector3(0.26f, 0.72f, 0), Vector3.right, 0.5f, 0.22f);
            Wing(k, new Vector3(-0.26f, 0.72f, 0), Vector3.left, 0.5f, 0.22f);
            // Robot arm reaching forward from the front corner.
            k.Strut(new Vector3(0.15f, 0.42f, 0.25f), new Vector3(0.3f, 0.34f, 0.45f), 0.035f, Cherry);
            k.Strut(new Vector3(0.3f, 0.34f, 0.45f), new Vector3(0.2f, 0.18f, 0.55f), 0.03f, Cherry);
            k.Sphere(new Vector3(0.3f, 0.34f, 0.45f), 0.035f, 6, Chrome);
            Part(k, "Workshop", model.Root.transform, model);

            var dish = new MeshKit();
            dish.Push(Vector3.zero, Quaternion.Euler(-35, 0, 0))
                .Lathe(new[] { new Vector2(0, -0.02f), new Vector2(0.16f, 0.04f), new Vector2(0.15f, 0.06f), new Vector2(0, 0.01f) }, 18, Ivory)
                .Cylinder(Vector3.zero, 0.01f, 0.12f, 6, Chrome)
                .Sphere(new Vector3(0, 0.13f, 0), 0.02f, 8, AtomGlow)
                .Pop();
            var dishGo = Part(dish, "Dish", model.Root.transform, model);
            dishGo.transform.localPosition = new Vector3(0, 0.9f, 0);
            Spin(model, dishGo, Vector3.up, 45f);
            return model;
        }

        /// <summary>A trading port: a hull with a docking ring at one end and a cargo rocket moored to it.</summary>
        static Model DockingPort(FactionId owner, Transform parent)
        {
            var model = Begin("Docking Port", parent);
            var k = new MeshKit();
            Pylon(k, 0.38f, owner);
            Hull(k, new Vector3(-0.08f, 0.56f, 0), 0.4f, 0.17f, Cream, Accent(owner));
            k.Push(new Vector3(0.22f, 0.56f, 0), Quaternion.Euler(0, 0, 90)).Torus(Vector3.zero, 0.2f, 0.04f, 24, 6, Foil).Pop();
            // The moored freighter, nose outward along +X.
            k.Push(new Vector3(0.26f, 0.56f, 0), Quaternion.Euler(0, 0, -90));
            k.Lathe(RocketBody(0.5f), 18, Ivory);
            k.Lathe(RocketNose(0.5f), 18, Cherry);
            k.Radial(3, 90, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.05f, 0.0f), new Vector2(0.13f, -0.02f), new Vector2(0.13f, 0.04f), new Vector2(0.07f, 0.16f) }, 0.018f, Cherry).Pop());
            k.Pop();
            Part(k, "Port", model.Root.transform, model);

            var star = new MeshKit();
            star.Sphere(Vector3.zero, 0.04f, 10, AtomGlow);
            star.Radial(8, 0, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 0, 90)).Cone(Vector3.zero, 0.016f, 0f, 0.13f, 5, AtomGlow).Pop());
            var starGo = Part(star, "Beacon", model.Root.transform, model, false);
            starGo.transform.localPosition = new Vector3(-0.08f, 0.9f, 0);
            Spin(model, starGo, Vector3.up, 70f);
            return model;
        }

        /// <summary>An open truss drydock with a rocket under construction inside.</summary>
        static Model Drydock(FactionId owner, Transform parent)
        {
            var model = Begin("Drydock", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.44f, 0.04f, 4, Gunmetal);
            float s = 0.36f, h = 1.2f;
            var corners = new[] { new Vector3(-s, 0, -s), new Vector3(s, 0, -s), new Vector3(s, 0, s), new Vector3(-s, 0, s) };
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % 4];
                k.Strut(a + Vector3.up * 0.04f, a + Vector3.up * h, 0.04f, Cherry);
                foreach (float y in new[] { 0.06f, 0.6f, h })
                    k.Strut(a + Vector3.up * y, b + Vector3.up * y, 0.03f, Cherry);
                k.Strut(a + Vector3.up * 0.06f, b + Vector3.up * 0.6f, 0.02f, Chrome);
                k.Strut(a + Vector3.up * 0.6f, b + Vector3.up * h, 0.02f, Chrome);
            }
            k.Sphere(new Vector3(-s, h + 0.04f, -s), 0.04f, 8, Ember);
            k.Push(new Vector3(0, 0.08f, 0));
            k.Lathe(RocketBody(1.05f), 22, Ivory);
            k.Lathe(RocketNose(1.05f), 22, Accent(owner));
            k.Radial(3, 90, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.1f, 0.0f), new Vector2(0.24f, -0.04f), new Vector2(0.24f, 0.08f), new Vector2(0.13f, 0.32f) }, 0.03f, Cherry).Pop());
            k.Pop();
            Part(k, "Drydock", model.Root.transform, model);
            return model;
        }

        /// <summary>An armoured defence sphere on a pylon with a belt of plates and a ray-gun turret on top.</summary>
        static Model DefenceSphere(FactionId owner, Transform parent)
        {
            var model = Begin("Defence Sphere", parent);
            var k = new MeshKit();
            Pylon(k, 0.3f, owner);
            k.Sphere(new Vector3(0, 0.6f, 0), 0.32f, 26, Teal);
            k.Torus(new Vector3(0, 0.6f, 0), 0.325f, 0.045f, 30, 6, Accent(owner));
            k.Radial(8, 0, kk => kk.Sphere(new Vector3(0.3f, 0.7f, 0), 0.03f, 6, MintGlow));
            Part(k, "Sphere", model.Root.transform, model);

            var turret = new MeshKit();
            turret.Sphere(Vector3.zero, 0.12f, 14, Cream);
            turret.Push(new Vector3(0, 0.02f, 0.03f), Quaternion.Euler(75, 0, 0));
            turret.Lathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.05f, 0), new Vector2(0.05f, 0.16f), new Vector2(0.085f, 0.18f), new Vector2(0.085f, 0.21f),
                new Vector2(0.04f, 0.23f), new Vector2(0.04f, 0.3f), new Vector2(0.07f, 0.32f), new Vector2(0.07f, 0.35f),
                new Vector2(0.03f, 0.37f), new Vector2(0.03f, 0.45f), new Vector2(0, 0.47f)
            }, 14, Chrome);
            turret.Sphere(new Vector3(0, 0.48f, 0), 0.026f, 8, AccentGlow(owner));
            turret.Pop();
            var turretGo = Part(turret, "Turret", model.Root.transform, model);
            turretGo.transform.localPosition = new Vector3(0, 0.92f, 0);
            Spin(model, turretGo, Vector3.up, 20f);
            return model;
        }

        /// <summary>A half-built frame: girders, a few plates and a blinking beacon.</summary>
        static Model FrameSite(FactionId owner, Transform parent)
        {
            var model = Begin("Frame Site", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.42f, 0.04f, 4, Gunmetal);
            float s = 0.32f;
            k.Strut(new Vector3(-s, 0.04f, -s), new Vector3(-s, 0.7f, -s), 0.035f, Mustard);
            k.Strut(new Vector3(s, 0.04f, -s), new Vector3(s, 0.5f, -s), 0.035f, Mustard);
            k.Strut(new Vector3(s, 0.04f, s), new Vector3(s, 0.35f, s), 0.035f, Mustard);
            k.Strut(new Vector3(-s, 0.5f, -s), new Vector3(s, 0.5f, -s), 0.03f, Bakelite);
            k.Strut(new Vector3(s, 0.35f, -s), new Vector3(s, 0.35f, s), 0.03f, Bakelite);
            k.Box(new Vector3(0, 0.08f, 0.05f), new Vector3(0.3f, 0.08f, 0.3f), Cream);
            k.Box(new Vector3(-0.05f, 0.18f, -0.05f), new Vector3(0.18f, 0.12f, 0.18f), Accent(owner));
            k.Sphere(new Vector3(-s, 0.76f, -s), 0.05f, 8, AtomGlow);
            Part(k, "Frame", model.Root.transform, model);
            return model;
        }

        /// <summary>A tank farm: four spheres on a cross of girders round a central pylon.</summary>
        static Model TankFarm(FactionId owner, Transform parent)
        {
            var model = Begin("Tank Farm", parent);
            var k = new MeshKit();
            Pylon(k, 0.45f, owner);
            k.Strut(new Vector3(-0.3f, 0.5f, 0), new Vector3(0.3f, 0.5f, 0), 0.04f, Chrome);
            k.Strut(new Vector3(0, 0.5f, -0.3f), new Vector3(0, 0.5f, 0.3f), 0.04f, Chrome);
            k.Radial(4, 0, kk => kk.Sphere(new Vector3(0.28f, 0.5f, 0), 0.16f, 20, Cream)
                .Torus(new Vector3(0.28f, 0.5f, 0), 0.162f, 0.018f, 20, 4, Cherry));
            k.Sphere(new Vector3(0, 0.62f, 0), 0.1f, 14, Accent(owner));
            Part(k, "Tanks", model.Root.transform, model);
            return model;
        }

        /// <summary>A solar array: a mast carrying two long, slowly tracking wings.</summary>
        static Model SolarWing(FactionId owner, Transform parent)
        {
            var model = Begin("Solar Wing", parent);
            var k = new MeshKit();
            Pylon(k, 0.6f, owner);
            Part(k, "Mast", model.Root.transform, model);
            var wings = new MeshKit();
            wings.Cylinder(new Vector3(0, -0.05f, 0), 0.06f, 0.1f, 12, Foil);
            Wing(wings, Vector3.zero, Vector3.right, 0.9f, 0.34f);
            Wing(wings, Vector3.zero, Vector3.left, 0.9f, 0.34f);
            var wingsGo = Part(wings, "Wings", model.Root.transform, model);
            wingsGo.transform.localPosition = new Vector3(0, 0.66f, 0);
            Spin(model, wingsGo, Vector3.up, 6f);
            return model;
        }
    }
}
