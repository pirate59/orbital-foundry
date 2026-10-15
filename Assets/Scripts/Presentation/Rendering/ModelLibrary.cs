using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// A built model: its root, the renderers whose materials change with state, and the parts that turn.
    /// </summary>
    public sealed class Model
    {
        public GameObject Root;
        public readonly List<MeshRenderer> Renderers = new List<MeshRenderer>();
        public readonly List<Spinner> Spinners = new List<Spinner>();
        /// <summary>Other motion while the building works, given the frame time (a drill bit's spin and plunge).</summary>
        public readonly List<System.Action<float>> Motions = new List<System.Action<float>>();
        /// <summary>Renderers of parts that move (also in <see cref="Renderers"/>); left out of <see cref="Bounds"/> so the selection ring holds still.</summary>
        public readonly List<MeshRenderer> Moving = new List<MeshRenderer>();
        /// <summary>Glowing extras (beacon beams, halos) shown only while the model is shown working; not in <see cref="Renderers"/>.</summary>
        public readonly List<Renderer> Lights = new List<Renderer>();
        /// <summary>Particle effects that run only while the model is shown working (Normal state).</summary>
        public readonly List<ParticleSystem> Effects = new List<ParticleSystem>();
        Material[][] normal;
        Material[][] faded;
        Material[][] dimmed;
        int shownState = -1;

        public struct Spinner
        {
            public Transform Part;
            public Vector3 Axis;
            public float DegreesPerSecond;
        }

        public const int Normal = 0, Faded = 1, Dimmed = 2;

        /// <summary>Switches every renderer to the normal, Belt View (faded) or stopped (dimmed) materials.</summary>
        public void SetState(int state)
        {
            if (state == shownState) return;
            if (normal == null)
            {
                normal = new Material[Renderers.Count][];
                for (int i = 0; i < Renderers.Count; i++) normal[i] = Renderers[i].sharedMaterials;
            }
            var set = state == Faded ? (faded ?? (faded = Map(Palette.Faded))) : state == Dimmed ? (dimmed ?? (dimmed = Map(Palette.Dimmed))) : normal;
            for (int i = 0; i < Renderers.Count; i++)
            {
                Renderers[i].sharedMaterials = set[i];
                Renderers[i].shadowCastingMode = state == Faded ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            }
            shownState = state;
            foreach (var l in Lights) if (l != null) l.enabled = state == Normal;
            foreach (var e in Effects)
            {
                if (e == null) continue;
                if (state == Normal) { if (!e.isPlaying) e.Play(); }
                else if (e.isPlaying) e.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        Material[][] Map(System.Func<Material, Material> f)
        {
            var result = new Material[normal.Length][];
            for (int i = 0; i < normal.Length; i++)
            {
                result[i] = new Material[normal[i].Length];
                for (int j = 0; j < normal[i].Length; j++) result[i][j] = f(normal[i][j]);
            }
            return result;
        }

        public void Animate(float dt)
        {
            foreach (var s in Spinners) s.Part.Rotate(s.Axis, s.DegreesPerSecond * dt, Space.Self);
            foreach (var m in Motions) m(dt);
        }

        /// <summary>World bounds of every renderer, for picking.</summary>
        public Bounds Bounds
        {
            get
            {
                bool any = false;
                var b = new Bounds(Root.transform.position, Vector3.zero);
                foreach (var r in Renderers)
                {
                    if (Moving.Contains(r)) continue;
                    if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
                }
                return b;
            }
        }

        /// <summary>Height of the model's top above its origin.</summary>
        public float Height => Bounds.max.y - Root.transform.position.y;

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }
    }

    /// <summary>
    /// The atomic-age model set, built in code from <see cref="MeshKit"/> parts: Googie towers, tail-finned rockets,
    /// flying saucers, ray-gun turrets and chrome-banded tanks in cream, cherry, mint and chrome, with each faction's
    /// colour on its trim. Models stand on their tile centre with +Z as their front; one tile is one unit.
    /// </summary>
    public static partial class ModelLibrary
    {
        // Shared paint, resolved lazily so the palette is only touched once materials exist.
        static Material Cream => Palette.Get(Palette.Cream);
        static Material Ivory => Palette.Get(Palette.Ivory);
        static Material Cherry => Palette.Get(Palette.Cherry);
        static Material Mint => Palette.Get(Palette.Mint);
        static Material Seafoam => Palette.Get(Palette.Seafoam);
        static Material Teal => Palette.Get(Palette.Teal);
        static Material Mustard => Palette.Get(Palette.Mustard);
        static Material Chrome => Palette.Get(Palette.Chrome, Palette.Finish.Chrome);
        static Material Gunmetal => Palette.Get(Palette.Gunmetal, Palette.Finish.Chrome);
        static Material Bakelite => Palette.Get(Palette.Bakelite, Palette.Finish.Matte);
        /// <summary>Smoked canopy glass: dark, glossy and reflective, so it reads against any trim colour.</summary>
        static Material Glass => Palette.Get(Palette.Hex(0x2D4C74), Palette.Finish.Chrome);
        static Material Window => Palette.Get(Palette.Hex(0xFFD98A), Palette.Finish.Glow);
        static Material AtomGlow => Palette.Get(Palette.Atomic, Palette.Finish.Glow);
        static Material MintGlow => Palette.Get(Palette.Hex(0x7FF5E0), Palette.Finish.Glow);
        static Material Ember => Palette.Get(Palette.Hex(0xFF7A2E), Palette.Finish.Glow);
        static Material PanelBlue => Palette.Get(Palette.Hex(0x3A66B8), Palette.Finish.Chrome);

        static Material Accent(FactionId owner)
        {
            return owner == FactionId.Neutral ? Mustard : Palette.Get(FactionPaint(owner));
        }

        static Material AccentGlow(FactionId owner)
        {
            return Palette.Get(FactionPaint(owner), Palette.Finish.Glow);
        }

        /// <summary>Faction trim in period-appropriate enamel: the player's seafoam teal, the AI's cherry red.</summary>
        public static Color FactionPaint(FactionId owner)
        {
            switch (owner)
            {
                case FactionId.Player: return Palette.Hex(0x3FB7C9);
                case FactionId.Neutral: return Palette.Mustard;
                default: return Palette.Hex(0xD8423A);
            }
        }

        /// <summary>
        /// The 1.0-tile profile of a classic finned rocket, bottom to top, for lathing. <paramref name="scale"/>
        /// sizes the whole rocket.
        /// </summary>
        static Vector2[] RocketBody(float s)
        {
            return new[]
            {
                new Vector2(0, 0), new Vector2(0.09f * s, 0), new Vector2(0.12f * s, 0.1f * s), new Vector2(0.14f * s, 0.3f * s),
                new Vector2(0.14f * s, 0.5f * s), new Vector2(0.125f * s, 0.62f * s)
            };
        }

        static Vector2[] RocketNose(float s)
        {
            return new[]
            {
                new Vector2(0.125f * s, 0.62f * s), new Vector2(0.1f * s, 0.74f * s), new Vector2(0.06f * s, 0.86f * s),
                new Vector2(0.02f * s, 0.95f * s), new Vector2(0, 0.98f * s)
            };
        }

        static GameObject Part(MeshKit k, string name, Transform parent, Model model, bool shadows = true)
        {
            var go = k.Instantiate(name, parent, shadows);
            model.Renderers.Add(go.GetComponent<MeshRenderer>());
            return go;
        }

        static Model Begin(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return new Model { Root = root };
        }

        static void Spin(Model model, GameObject part, Vector3 axis, float degreesPerSecond)
        {
            model.Spinners.Add(new Model.Spinner { Part = part.transform, Axis = axis, DegreesPerSecond = degreesPerSecond });
        }

        // ---------------------------------------------------------------- buildings

        /// <summary>The model for a building type, or null when it has none (the sprite stays in use).</summary>
        public static Model Building(string typeId, FactionId owner, Transform parent)
        {
            if (!Palette.Ready) return null;
            switch (typeId)
            {
                case "headquarters": return Headquarters(owner, parent);
                case "extractor": return Extractor(owner, parent);
                case "refinery": return Refinery(owner, parent);
                case "purifier": return Purifier(owner, parent);
                case "assembler": return Assembler(owner, parent);
                case "sale_depot": return SaleDepot(owner, parent);
                case "shipyard": return Shipyard(owner, parent);
                case "fort": return Fort(owner, parent);
                case "fort_site": return FortSite(owner, parent);
                case "storage": return Storage(owner, parent);
                case "solar_array": return SolarArray(owner, parent);
                case "drone_pad": return DronePad(owner, parent);
            }
            return null;
        }

        /// <summary>A Googie space-needle: porthole drum, finned chrome stem, saucer deck with a glass dome, and a spinning atom.</summary>
        static Model Headquarters(FactionId owner, Transform parent)
        {
            var model = Begin("Headquarters", parent);
            var k = new MeshKit();
            k.Lathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.92f, 0), new Vector2(0.95f, 0.07f), new Vector2(0.86f, 0.14f),
                new Vector2(0.82f, 0.32f), new Vector2(0.62f, 0.42f), new Vector2(0, 0.42f)
            }, 40, Cream);
            k.Torus(new Vector3(0, 0.1f, 0), 0.93f, 0.035f, 48, 6, Chrome);
            k.Torus(new Vector3(0, 0.33f, 0), 0.8f, 0.03f, 48, 6, Accent(owner));
            k.Radial(10, 0, kk => kk.Sphere(new Vector3(0.85f, 0.23f, 0), 0.055f, 10, MintGlow));
            // Stem with three cherry fins.
            k.Cone(new Vector3(0, 0.42f, 0), 0.24f, 0.1f, 1.0f, 20, Chrome);
            k.Radial(3, 90, kk => kk.Push(new Vector3(0, 0.42f, 0), Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.12f, 0), new Vector2(0.55f, 0), new Vector2(0.48f, 0.12f), new Vector2(0.14f, 0.95f) }, 0.05f, Cherry).Pop());
            // Saucer observation deck.
            k.Push(new Vector3(0, 1.42f, 0));
            k.Lathe(new[] { new Vector2(0, -0.16f), new Vector2(0.3f, -0.15f), new Vector2(0.62f, -0.06f), new Vector2(0.74f, 0) }, 40, Accent(owner));
            k.Lathe(new[] { new Vector2(0.74f, 0), new Vector2(0.75f, 0.035f), new Vector2(0.62f, 0.1f), new Vector2(0.26f, 0.16f), new Vector2(0, 0.16f) }, 40, Ivory);
            k.Torus(new Vector3(0, 0.02f, 0), 0.72f, 0.025f, 48, 6, MintGlow);
            k.Sphere(new Vector3(0, 0.15f, 0), 0.26f, 24, Glass, true);
            k.Cylinder(new Vector3(0, 0.38f, 0), 0.025f, 0.55f, 8, Chrome);
            k.Pop();
            Part(k, "Tower", model.Root.transform, model);

            // The atom: a glowing nucleus and three tilted electron rings, slowly turning.
            var atom = new MeshKit();
            atom.Sphere(Vector3.zero, 0.085f, 14, AtomGlow);
            for (int i = 0; i < 3; i++)
                atom.Push(Vector3.zero, Quaternion.Euler(70, i * 60, 0)).Torus(Vector3.zero, 0.2f, 0.012f, 28, 5, Chrome).Pop();
            var atomGo = Part(atom, "Atom", model.Root.transform, model, false);
            atomGo.transform.localPosition = new Vector3(0, 2.42f, 0);
            Spin(model, atomGo, Vector3.up, 60f);
            return model;
        }

        /// <summary>A tripod rocket drill straddling its asteroid, with a striped bit that spins while it works.</summary>
        static Model Extractor(FactionId owner, Transform parent)
        {
            var model = Begin("Extractor", parent);
            var k = new MeshKit();
            k.Radial(3, 30, kk => kk.Strut(new Vector3(0.44f, 0, 0), new Vector3(0.13f, 1.0f, 0), 0.05f, Cherry)
                .Cylinder(new Vector3(0.44f, 0, 0), 0.07f, 0.04f, 10, Gunmetal));
            k.Torus(new Vector3(0, 0.62f, 0), 0.24f, 0.025f, 24, 5, Chrome);
            // Pod: an upside-down rocket body hanging from the crown.
            k.Push(new Vector3(0, 1.36f, 0), Quaternion.Euler(180, 0, 0));
            k.Lathe(new[] { new Vector2(0, -0.06f), new Vector2(0.11f, -0.06f), new Vector2(0.16f, 0.06f), new Vector2(0.17f, 0.2f), new Vector2(0.13f, 0.34f), new Vector2(0.08f, 0.4f) }, 20, Cream);
            k.Radial(3, 0, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.14f, 0.0f), new Vector2(0.3f, -0.04f), new Vector2(0.3f, 0.08f), new Vector2(0.16f, 0.22f) }, 0.035f, Accent(owner)).Pop());
            k.Pop();
            k.Sphere(new Vector3(0, 1.42f, 0), 0.07f, 12, AtomGlow, true);
            Part(k, "Rig", model.Root.transform, model);

            var bit = new MeshKit();
            bit.Cone(new Vector3(0, -0.14f, 0), 0.075f, 0.075f, 0.06f, 12, Chrome)
                .Push(new Vector3(0, -0.14f, 0), Quaternion.Euler(180, 0, 0))
                .Lathe(new[] { new Vector2(0.075f, 0), new Vector2(0.06f, 0.08f) }, 12, Cherry)
                .Lathe(new[] { new Vector2(0.06f, 0.08f), new Vector2(0.04f, 0.16f) }, 12, Ivory)
                .Lathe(new[] { new Vector2(0.04f, 0.16f), new Vector2(0.02f, 0.24f), new Vector2(0, 0.27f) }, 12, Cherry)
                .Pop();
            var bitGo = Part(bit, "Bit", model.Root.transform, model);
            bitGo.transform.localPosition = new Vector3(0, 0.96f, 0);
            Spin(model, bitGo, Vector3.up, 240f);
            return model;
        }

        /// <summary>A streamlined smelting capsule with a glowing furnace band and twin banded stacks.</summary>
        static Model Refinery(FactionId owner, Transform parent)
        {
            var model = Begin("Refinery", parent);
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.05f, 0), new Vector3(0.86f, 0.1f, 0.78f), Gunmetal);
            k.Box(new Vector3(0, 0.13f, 0), new Vector3(0.74f, 0.06f, 0.6f), Chrome);
            // Horizontal capsule along X.
            k.Push(new Vector3(0, 0.42f, 0.04f), Quaternion.Euler(0, 0, 90));
            k.SmoothLathe(new[]
            {
                new Vector2(0, -0.42f), new Vector2(0.12f, -0.4f), new Vector2(0.21f, -0.34f), new Vector2(0.25f, -0.22f),
                new Vector2(0.25f, 0.22f), new Vector2(0.21f, 0.34f), new Vector2(0.12f, 0.4f), new Vector2(0, 0.42f)
            }, 24, Cream);
            k.Push(new Vector3(0, -0.12f, 0)).Torus(Vector3.zero, 0.255f, 0.03f, 28, 6, Accent(owner)).Pop();
            k.Push(new Vector3(0, 0.06f, 0)).Lathe(new[] { new Vector2(0.258f, -0.07f), new Vector2(0.258f, 0.07f) }, 28, Ember).Pop();
            k.Push(new Vector3(0, 0.2f, 0)).Torus(Vector3.zero, 0.255f, 0.03f, 28, 6, Chrome).Pop();
            k.Pop();
            // Cradle legs.
            k.Box(new Vector3(-0.22f, 0.22f, 0.04f), new Vector3(0.06f, 0.18f, 0.42f), Gunmetal);
            k.Box(new Vector3(0.22f, 0.22f, 0.04f), new Vector3(0.06f, 0.18f, 0.42f), Gunmetal);
            // Stacks behind the capsule.
            foreach (float x in new[] { -0.2f, 0.2f })
            {
                k.Cylinder(new Vector3(x, 0.16f, -0.27f), 0.075f, 0.9f, 14, Cream);
                k.Torus(new Vector3(x, 0.55f, -0.27f), 0.078f, 0.018f, 16, 4, Cherry);
                k.Torus(new Vector3(x, 0.8f, -0.27f), 0.078f, 0.018f, 16, 4, Cherry);
                k.Cone(new Vector3(x, 1.06f, -0.27f), 0.1f, 0.05f, 0.08f, 14, Chrome);
            }
            Part(k, "Refinery", model.Root.transform, model);
            return model;
        }

        /// <summary>A 1950s water tower: a mint sphere on splayed legs with a glowing sight ring.</summary>
        static Model Purifier(FactionId owner, Transform parent)
        {
            var model = Begin("Purifier", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.44f, 0.06f, 24, Gunmetal);
            k.Radial(4, 45, kk => kk.Strut(new Vector3(0.36f, 0.04f, 0), new Vector3(0.16f, 0.66f, 0), 0.045f, Chrome));
            k.Torus(new Vector3(0, 0.36f, 0), 0.26f, 0.02f, 24, 4, Chrome);
            k.Cylinder(new Vector3(0, 0.04f, 0), 0.05f, 0.6f, 10, Chrome);
            k.Sphere(new Vector3(0, 0.88f, 0), 0.3f, 28, Mint);
            k.Torus(new Vector3(0, 0.88f, 0), 0.302f, 0.025f, 32, 6, MintGlow);
            k.Torus(new Vector3(0, 0.98f, 0), 0.285f, 0.018f, 32, 4, Accent(owner));
            k.Cone(new Vector3(0, 1.15f, 0), 0.1f, 0.02f, 0.14f, 14, Cream);
            k.Sphere(new Vector3(0, 1.31f, 0), 0.035f, 8, Window);
            Part(k, "Tower", model.Root.transform, model);
            return model;
        }

        /// <summary>A Quonset robot works with porthole windows, a stubby chimney and a turning radar dish.</summary>
        static Model Assembler(FactionId owner, Transform parent)
        {
            var model = Begin("Assembler", parent);
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.04f, 0), new Vector3(0.9f, 0.08f, 0.9f), Gunmetal);
            k.Box(new Vector3(0, 0.2f, 0), new Vector3(0.8f, 0.24f, 0.72f), Cream);
            k.Box(new Vector3(0, 0.1f, 0), new Vector3(0.82f, 0.04f, 0.74f), Chrome);
            // Half-cylinder roof along X.
            k.Push(new Vector3(0, 0.32f, 0), Quaternion.Euler(0, 0, 90));
            k.Lathe(new[] { new Vector2(0, -0.4f), new Vector2(0.34f, -0.4f), new Vector2(0.34f, 0.4f), new Vector2(0, 0.4f) }, 20, Teal, -90f, 180f);
            for (int i = -1; i <= 1; i++) k.Push(new Vector3(0, i * 0.26f, 0)).Lathe(new[] { new Vector2(0.345f, -0.02f), new Vector2(0.345f, 0.02f) }, 20, Chrome, -90f, 180f).Pop();
            k.Pop();
            // Front door and portholes.
            k.Box(new Vector3(0, 0.22f, 0.37f), new Vector3(0.2f, 0.26f, 0.03f), Accent(owner));
            foreach (float x in new[] { -0.26f, 0.26f })
            {
                k.Push(new Vector3(x, 0.22f, 0.36f), Quaternion.Euler(90, 0, 0)).Torus(Vector3.zero, 0.055f, 0.014f, 16, 4, Chrome).Pop();
                k.Box(new Vector3(x, 0.22f, 0.362f), new Vector3(0.07f, 0.07f, 0.01f), Window);
            }
            k.Cylinder(new Vector3(-0.25f, 0.5f, -0.15f), 0.05f, 0.32f, 10, Chrome);
            k.Cylinder(new Vector3(0.24f, 0.62f, 0f), 0.02f, 0.2f, 6, Chrome);
            Part(k, "Works", model.Root.transform, model);

            var dish = new MeshKit();
            dish.Push(Vector3.zero, Quaternion.Euler(-35, 0, 0))
                .Lathe(new[] { new Vector2(0, -0.02f), new Vector2(0.2f, 0.05f), new Vector2(0.19f, 0.07f), new Vector2(0, 0.01f) }, 20, Ivory)
                .Cylinder(Vector3.zero, 0.012f, 0.14f, 6, Chrome)
                .Sphere(new Vector3(0, 0.15f, 0), 0.025f, 8, AtomGlow)
                .Pop();
            var dishGo = Part(dish, "Dish", model.Root.transform, model);
            dishGo.transform.localPosition = new Vector3(0.24f, 0.84f, 0f);
            Spin(model, dishGo, Vector3.up, 45f);
            return model;
        }

        /// <summary>A Googie trading post: angled boomerang roof, glass frontage and a neon starburst sign.</summary>
        static Model SaleDepot(FactionId owner, Transform parent)
        {
            var model = Begin("Trading Post", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.46f, 0.05f, 28, Gunmetal);
            k.Box(new Vector3(-0.05f, 0.2f, -0.05f), new Vector3(0.62f, 0.3f, 0.44f), Cream);
            k.Box(new Vector3(-0.05f, 0.21f, 0.18f), new Vector3(0.56f, 0.18f, 0.02f), Window);
            k.Box(new Vector3(-0.05f, 0.08f, 0.19f), new Vector3(0.62f, 0.06f, 0.04f), Accent(owner));
            // Upswept roof slab.
            k.Push(new Vector3(-0.05f, 0.39f, 0.0f), Quaternion.Euler(-10, 0, 8)).Box(Vector3.zero, new Vector3(0.82f, 0.04f, 0.6f), Cherry).Pop();
            // Sign pylon.
            k.Push(new Vector3(0.34f, 0.04f, 0.18f), Quaternion.Euler(0, 0, -6))
                .Fin(new[] { new Vector2(-0.05f, 0), new Vector2(0.05f, 0), new Vector2(0.03f, 1.0f), new Vector2(-0.02f, 1.0f) }, 0.05f, Ivory)
                .Pop();
            k.Push(new Vector3(0.38f, 0.62f, 0.18f)).Cylinder(Vector3.zero, 0.09f, 0.03f, 18, Mustard).Pop();
            Part(k, "Post", model.Root.transform, model);

            var star = new MeshKit();
            star.Sphere(Vector3.zero, 0.05f, 10, AtomGlow);
            star.Radial(8, 0, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 0, 90)).Cone(Vector3.zero, 0.02f, 0.0f, 0.16f, 5, AtomGlow).Pop());
            var starGo = Part(star, "Starburst", model.Root.transform, model, false);
            starGo.transform.localPosition = new Vector3(0.28f, 1.08f, 0.18f);
            Spin(model, starGo, Vector3.up, 70f);
            return model;
        }

        /// <summary>A launch gantry cradling a tail-finned rocket, cream with a nose cone in faction colours.</summary>
        static Model Shipyard(FactionId owner, Transform parent)
        {
            var model = Begin("Shipyard", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.46f, 0.07f, 30, Gunmetal);
            k.Torus(new Vector3(0, 0.07f, 0), 0.4f, 0.02f, 36, 4, Mustard);
            // Gantry: four posts with cross braces, behind the rocket.
            float gx = 0.12f, gz = -0.3f;
            foreach (var p in new[] { new Vector2(-gx, gz - 0.08f), new Vector2(gx, gz - 0.08f), new Vector2(-gx, gz + 0.08f), new Vector2(gx, gz + 0.08f) })
                k.Box(new Vector3(p.x, 0.62f, p.y), new Vector3(0.035f, 1.1f, 0.035f), Cherry);
            for (int i = 0; i < 5; i++)
            {
                float y = 0.2f + i * 0.22f;
                k.Strut(new Vector3(-gx, y, gz + 0.08f), new Vector3(gx, y + 0.18f, gz + 0.08f), 0.02f, Cherry);
                k.Strut(new Vector3(-gx, y, gz - 0.08f), new Vector3(gx, y + 0.18f, gz - 0.08f), 0.02f, Cherry);
            }
            k.Box(new Vector3(0, 0.82f, -0.17f), new Vector3(0.05f, 0.04f, 0.2f), Chrome);
            k.Box(new Vector3(0, 0.45f, -0.17f), new Vector3(0.05f, 0.04f, 0.2f), Chrome);
            k.Sphere(new Vector3(0, 1.2f, gz), 0.04f, 8, Ember);
            // The rocket.
            k.Push(new Vector3(0, 0.08f, 0.02f));
            k.Lathe(RocketBody(1.1f), 24, Ivory);
            k.Lathe(RocketNose(1.1f), 24, Accent(owner));
            k.Radial(3, 90, kk => kk.Push(Vector3.zero, Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.1f, 0.0f), new Vector2(0.27f, -0.04f), new Vector2(0.27f, 0.08f), new Vector2(0.14f, 0.36f) }, 0.03f, Cherry).Pop());
            k.Push(new Vector3(0, 0.46f, 0.15f), Quaternion.Euler(90, 0, 0)).Torus(Vector3.zero, 0.04f, 0.012f, 14, 4, Chrome).Pop();
            k.Sphere(new Vector3(0, 0.46f, 0.145f), 0.032f, 8, Window);
            k.Pop();
            Part(k, "Shipyard", model.Root.transform, model);
            return model;
        }

        /// <summary>A domed bunker on a hex plinth with a ringed ray-gun cannon that sweeps the sky.</summary>
        static Model Fort(FactionId owner, Transform parent)
        {
            var model = Begin("Fort", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.5f, 0.12f, 6, Gunmetal);
            k.Cylinder(new Vector3(0, 0.12f, 0), 0.44f, 0.04f, 6, Chrome);
            k.Torus(new Vector3(0, 0.17f, 0), 0.36f, 0.035f, 30, 6, Accent(owner));
            k.Sphere(new Vector3(0, 0.16f, 0), 0.36f, 28, Teal, true);
            k.Radial(6, 0, kk => kk.Sphere(new Vector3(0.3f, 0.3f, 0), 0.04f, 8, MintGlow));
            Part(k, "Bunker", model.Root.transform, model);

            var turret = new MeshKit();
            turret.Sphere(Vector3.zero, 0.14f, 16, Cream);
            turret.Push(new Vector3(0, 0.02f, 0.04f), Quaternion.Euler(70, 0, 0));
            turret.Lathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.06f, 0), new Vector2(0.06f, 0.18f), new Vector2(0.1f, 0.2f), new Vector2(0.1f, 0.24f),
                new Vector2(0.05f, 0.26f), new Vector2(0.05f, 0.34f), new Vector2(0.085f, 0.36f), new Vector2(0.085f, 0.39f),
                new Vector2(0.035f, 0.41f), new Vector2(0.035f, 0.5f), new Vector2(0, 0.52f)
            }, 16, Chrome);
            turret.Sphere(new Vector3(0, 0.53f, 0), 0.03f, 8, AccentGlow(owner));
            turret.Pop();
            var turretGo = Part(turret, "Turret", model.Root.transform, model);
            turretGo.transform.localPosition = new Vector3(0, 0.5f, 0);
            Spin(model, turretGo, Vector3.up, 20f);
            return model;
        }

        /// <summary>A hazard-striped scaffold cradle with stacked crates and a blinking beacon.</summary>
        static Model FortSite(FactionId owner, Transform parent)
        {
            var model = Begin("Fort Site", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.48f, 0.05f, 6, Gunmetal);
            for (int i = 0; i < 6; i++)
            {
                var a = Quaternion.Euler(0, i * 60f, 0);
                var mat = i % 2 == 0 ? Mustard : Bakelite;
                k.Strut(a * new Vector3(0.42f, 0.05f, 0), a * new Vector3(0.36f, 0.6f, 0), 0.04f, mat);
                k.Strut(a * new Vector3(0.36f, 0.6f, 0), Quaternion.Euler(0, 60, 0) * a * new Vector3(0.36f, 0.6f, 0), 0.03f, Mustard);
            }
            k.Box(new Vector3(-0.12f, 0.12f, 0.08f), new Vector3(0.2f, 0.14f, 0.2f), Bakelite);
            k.Box(new Vector3(0.12f, 0.12f, -0.06f), new Vector3(0.22f, 0.14f, 0.18f), Cream);
            k.Box(new Vector3(0.0f, 0.26f, 0.0f), new Vector3(0.18f, 0.14f, 0.18f), Accent(owner));
            k.Cylinder(new Vector3(0, 0.6f, 0), 0.02f, 0.2f, 6, Chrome);
            k.Sphere(new Vector3(0, 0.84f, 0), 0.05f, 10, AtomGlow);
            Part(k, "Scaffold", model.Root.transform, model);
            return model;
        }

        /// <summary>A cluster of domed silos tied together by a chrome catwalk.</summary>
        static Model Storage(FactionId owner, Transform parent)
        {
            var model = Begin("Storage", parent);
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.03f, 0), new Vector3(0.9f, 0.06f, 0.9f), Gunmetal);
            var spots = new[] { new Vector3(-0.2f, 0, 0.16f), new Vector3(0.2f, 0, 0.16f), new Vector3(0, 0, -0.2f) };
            float[] heights = { 0.55f, 0.48f, 0.66f };
            for (int i = 0; i < 3; i++)
            {
                var p = spots[i] + Vector3.up * 0.06f;
                float h = heights[i];
                k.Cylinder(p, 0.18f, h, 22, Cream);
                k.Sphere(p + Vector3.up * h, 0.18f, 22, Cream, true);
                k.Torus(p + Vector3.up * (h * 0.35f), 0.182f, 0.02f, 24, 4, Cherry);
                k.Torus(p + Vector3.up * (h * 0.75f), 0.182f, 0.014f, 24, 4, Accent(owner));
                k.Sphere(p + Vector3.up * (h + 0.19f), 0.025f, 6, Chrome);
            }
            k.Torus(new Vector3(0, 0.46f, 0.02f), 0.36f, 0.012f, 36, 4, Chrome);
            Part(k, "Silos", model.Root.transform, model);
            return model;
        }

        /// <summary>A sunflower solar collector: petal panels around a glowing core that slowly tracks the sun.</summary>
        static Model SolarArray(FactionId owner, Transform parent)
        {
            var model = Begin("Solar Array", parent);
            var k = new MeshKit();
            k.Cylinder(Vector3.zero, 0.3f, 0.06f, 20, Gunmetal);
            k.Torus(new Vector3(0, 0.06f, 0), 0.26f, 0.02f, 24, 4, Accent(owner));
            k.Cone(new Vector3(0, 0.06f, 0), 0.08f, 0.04f, 0.7f, 12, Chrome);
            Part(k, "Mast", model.Root.transform, model);

            var flower = new MeshKit();
            flower.Push(Vector3.zero, Quaternion.Euler(-30, 0, 0));
            flower.Cylinder(new Vector3(0, -0.03f, 0), 0.11f, 0.06f, 18, Chrome);
            flower.Sphere(new Vector3(0, 0.03f, 0), 0.08f, 14, Palette.Get(Palette.Mustard, Palette.Finish.Glow), true);
            flower.Radial(8, 0, kk => kk.Push(new Vector3(0.12f, 0, 0), Quaternion.Euler(-90, 0, 12))
                .Fin(new[] { new Vector2(0, -0.07f), new Vector2(0.3f, -0.11f), new Vector2(0.34f, 0), new Vector2(0.3f, 0.11f), new Vector2(0, 0.07f) }, 0.02f, PanelBlue)
                .Pop());
            flower.Pop();
            var flowerGo = Part(flower, "Petals", model.Root.transform, model);
            flowerGo.transform.localPosition = new Vector3(0, 0.78f, 0);
            Spin(model, flowerGo, Vector3.up, 8f);
            return model;
        }

        // ---------------------------------------------------------------- craft

        /// <summary>The model for a craft type facing +Z, or null when it has none.</summary>
        public static Model Craft(string typeId, FactionId owner, Transform parent)
        {
            if (!Palette.Ready) return null;
            switch (typeId)
            {
                case "guard": return Guard(owner, parent);
                case "tank": return Tank(owner, parent);
                case "artillery": return Artillery(owner, parent);
                case "strike": return Strike(owner, parent);
            }
            return null;
        }

        /// <summary>A classic flying saucer with a bubble canopy and a glowing levitator ring.</summary>
        static Model Guard(FactionId owner, Transform parent)
        {
            var model = Begin("Guard", parent);
            var k = new MeshKit();
            k.Lathe(new[] { new Vector2(0, -0.07f), new Vector2(0.16f, -0.07f), new Vector2(0.34f, -0.02f), new Vector2(0.38f, 0) }, 32, Accent(owner));
            k.Lathe(new[] { new Vector2(0.38f, 0), new Vector2(0.37f, 0.03f), new Vector2(0.24f, 0.07f), new Vector2(0, 0.08f) }, 32, Ivory);
            k.Torus(new Vector3(0, -0.075f, 0), 0.12f, 0.022f, 24, 5, AccentGlow(owner));
            k.Torus(new Vector3(0, 0.055f, 0), 0.25f, 0.02f, 32, 5, Accent(owner));
            k.Torus(new Vector3(0, 0.075f, 0), 0.14f, 0.018f, 24, 5, Chrome);
            k.Sphere(new Vector3(0, 0.07f, 0), 0.13f, 20, Glass, true);
            k.Radial(8, 0, kk => kk.Sphere(new Vector3(0.36f, 0.005f, 0), 0.022f, 6, MintGlow));
            var go = Part(k, "Saucer", model.Root.transform, model);
            Spin(model, go, Vector3.up, 50f);
            return model;
        }

        /// <summary>A rounded hover tank with a ribbed skirt and a ringed ray-gun turret.</summary>
        static Model Tank(FactionId owner, Transform parent)
        {
            var model = Begin("Tank", parent);
            var k = new MeshKit();
            // Hull: a flattened capsule along Z.
            k.Push(new Vector3(0, 0.02f, 0), Quaternion.Euler(90, 0, 0), new Vector3(1.25f, 1f, 0.55f));
            k.SmoothLathe(new[]
            {
                new Vector2(0, -0.34f), new Vector2(0.12f, -0.32f), new Vector2(0.2f, -0.25f), new Vector2(0.22f, -0.1f),
                new Vector2(0.22f, 0.14f), new Vector2(0.18f, 0.28f), new Vector2(0.1f, 0.35f), new Vector2(0, 0.37f)
            }, 24, Accent(owner));
            k.Pop();
            k.Push(new Vector3(0, -0.08f, 0), Quaternion.identity, new Vector3(1f, 1f, 1.35f)).Torus(Vector3.zero, 0.25f, 0.035f, 30, 6, Gunmetal).Pop();
            k.Push(new Vector3(0, -0.11f, 0), Quaternion.identity, new Vector3(1f, 1f, 1.35f)).Torus(Vector3.zero, 0.2f, 0.018f, 30, 4, AccentGlow(owner)).Pop();
            // Turret and barrel.
            k.Sphere(new Vector3(0, 0.11f, -0.04f), 0.14f, 18, Ivory, true);
            k.Push(new Vector3(0, 0.15f, 0.05f), Quaternion.Euler(84, 0, 0));
            k.Lathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.035f, 0), new Vector2(0.035f, 0.16f), new Vector2(0.06f, 0.17f), new Vector2(0.06f, 0.2f),
                new Vector2(0.03f, 0.21f), new Vector2(0.03f, 0.3f), new Vector2(0.05f, 0.31f), new Vector2(0.05f, 0.33f), new Vector2(0, 0.34f)
            }, 12, Chrome);
            k.Pop();
            k.Sphere(new Vector3(0, 0.2f, -0.04f), 0.035f, 8, Glass);
            Part(k, "Tank", model.Root.transform, model);
            return model;
        }

        /// <summary>A long-barrelled coil cannon on a finned sled, barrel raised for indirect fire.</summary>
        static Model Artillery(FactionId owner, Transform parent)
        {
            var model = Begin("Artillery", parent);
            var k = new MeshKit();
            k.Box(new Vector3(0, -0.04f, -0.02f), new Vector3(0.34f, 0.1f, 0.5f), Cream);
            k.Box(new Vector3(0, -0.1f, -0.02f), new Vector3(0.38f, 0.03f, 0.56f), Accent(owner));
            foreach (float x in new[] { -0.19f, 0.19f })
                k.Push(new Vector3(x, -0.02f, -0.2f), Quaternion.Euler(0, 90, 0))
                    .Fin(new[] { new Vector2(0, 0), new Vector2(0.06f, -0.06f), new Vector2(0.06f, 0.1f), new Vector2(0, 0.1f) }, 0.02f, Cherry)
                    .Pop();
            k.Push(new Vector3(0, -0.1f, -0.02f), Quaternion.identity, new Vector3(1f, 1f, 1.5f)).Torus(Vector3.zero, 0.15f, 0.015f, 24, 4, AccentGlow(owner)).Pop();
            k.Sphere(new Vector3(0, 0.02f, -0.08f), 0.11f, 16, Gunmetal, true);
            // Barrel raised 30 degrees toward +Z, with three coils.
            k.Push(new Vector3(0, 0.06f, -0.08f), Quaternion.Euler(60, 0, 0));
            k.Cylinder(Vector3.zero, 0.035f, 0.62f, 12, Chrome);
            for (int i = 0; i < 3; i++) k.Torus(new Vector3(0, 0.26f + i * 0.11f, 0), 0.06f, 0.016f, 16, 4, Ember);
            k.Cone(new Vector3(0, 0.62f, 0), 0.05f, 0.025f, 0.05f, 12, Chrome);
            k.Pop();
            Part(k, "Artillery", model.Root.transform, model);
            return model;
        }

        /// <summary>A tail-finned rocket fighter with a bubble canopy and swept wings, nose toward +Z.</summary>
        static Model Strike(FactionId owner, Transform parent)
        {
            var model = Begin("Strike", parent);
            var k = new MeshKit();
            k.Push(new Vector3(0, 0, -0.32f), Quaternion.Euler(90, 0, 0));
            k.Lathe(RocketBody(0.75f), 20, Ivory);
            k.Lathe(RocketNose(0.75f), 20, Accent(owner));
            // Swept wings and a dorsal fin.
            foreach (float side in new[] { -1f, 1f })
                k.Push(Vector3.zero, Quaternion.Euler(0, side > 0 ? 0 : 180, 0))
                    .Fin(new[] { new Vector2(0.08f, 0.02f), new Vector2(0.34f, -0.06f), new Vector2(0.34f, 0.04f), new Vector2(0.09f, 0.3f) }, 0.025f, Cherry)
                    .Pop();
            k.Push(Vector3.zero, Quaternion.Euler(0, 90, 0))
                .Fin(new[] { new Vector2(0.08f, 0.0f), new Vector2(0.22f, -0.04f), new Vector2(0.2f, 0.04f), new Vector2(0.09f, 0.22f) }, 0.025f, Cherry)
                .Pop();
            k.Cylinder(new Vector3(0, -0.02f, 0), 0.06f, 0.03f, 12, Ember);
            k.Pop();
            k.Push(new Vector3(0, 0.07f, 0.06f), Quaternion.identity, new Vector3(1f, 0.8f, 1.8f)).Sphere(Vector3.zero, 0.065f, 14, Glass, true).Pop();
            Part(k, "Strike", model.Root.transform, model);
            return model;
        }

        // ---------------------------------------------------------------- resource nodes

        /// <summary>
        /// A squat mineral outcrop standing in an asteroid's surface: faceted rock tinted by its resource, with glowing
        /// crystals in the resource's colour growing upward. Ion is mostly crystal. Rich nodes are larger with more
        /// crystals.
        /// </summary>
        public static Model Asteroid(string itemId, Color itemColor, int seed, bool rich, Transform parent)
        {
            if (!Palette.Ready) return null;
            var model = Begin("Asteroid " + itemId, parent);
            var rng = new System.Random(seed * 7919 + itemId.GetHashCode());
            float size = rich ? 0.36f : 0.3f;
            bool crystalNode = itemId == "crystal" || itemId == "ice" || itemId == "quantum_core";
            var rockColor = Color.Lerp(Palette.Hex(0x5B5560), itemColor, crystalNode ? 0.15f : 0.35f);
            var rock = Palette.Get(rockColor, Palette.Finish.Matte);
            var shard = Palette.Get(itemColor, Palette.Finish.Glow);
            var k = new MeshKit();
            k.Rock(Vector3.zero, crystalNode ? size * 0.7f : size, seed, 0.28f, rock, 0.5f);
            int shards = (crystalNode ? 6 : 3) + (rich ? 3 : 0);
            for (int i = 0; i < shards; i++)
            {
                float yaw = (float)rng.NextDouble() * 360f;
                float tilt = 5f + (float)rng.NextDouble() * 40f;
                float len = (crystalNode ? 0.24f : 0.16f) + (float)rng.NextDouble() * (crystalNode ? 0.18f : 0.1f);
                float w = len * 0.3f;
                var dir = Quaternion.Euler(0, yaw, 0) * Quaternion.Euler(tilt, 0, 0);
                k.Push(dir * Vector3.up * size * (crystalNode ? 0.45f : 0.7f), dir);
                k.Lathe(new[] { new Vector2(0, -0.02f), new Vector2(w, len * 0.25f), new Vector2(w * 0.8f, len * 0.75f), new Vector2(0, len) }, 5, shard);
                k.Pop();
            }
            Part(k, "Rock", model.Root.transform, model);
            return model;
        }
    }
}
