using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OrbitalFoundry
{
    /// <summary>
    /// "Enhanced comic" craft for the battle scenes: chunkier, more detailed cartoon versions of the four units (pilots
    /// in open cockpits, panel lines, rivets, antennas, insignia, exhaust flames) drawn with the ComicToon shader's
    /// cel bands, halftone shadows and ink outlines. <see cref="Portrait"/> renders one off-screen into a cached sprite
    /// facing right, which the battle scene uses in place of the pixel-art ship.
    /// </summary>
    public static class ComicCraft
    {
        const int Size = 448;
        const int Layer = 30;
        static readonly Vector3 Stage = new Vector3(0f, -2000f, 0f);
        static readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Material> paints = new Dictionary<string, Material>();
        static Material template;
        static bool missing;

        // ---------------------------------------------------------------- paint

        static Material Paint(Color color, float gloss = 0.5f, bool glow = false, float outline = 0.012f)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + gloss + glow + outline;
            Material m;
            if (paints.TryGetValue(key, out m)) return m;
            m = new Material(template) { name = "Comic " + key };
            m.SetColor("_BaseColor", color);
            // Shadows shift toward violet, as in printed comics.
            m.SetColor("_ShadeColor", Color.Lerp(color * 0.45f, new Color(0.17f, 0.1f, 0.32f), 0.38f));
            m.SetFloat("_Gloss", gloss);
            m.SetFloat("_Glow", glow ? 1f : 0f);
            m.SetFloat("_OutlineWidth", glow ? outline * 0.4f : outline);
            paints[key] = m;
            return m;
        }

        static Material Ivory => Paint(Palette.Hex(0xF7EED6), 0.7f);
        static Material Cream => Paint(Palette.Hex(0xEAD9B4), 0.5f);
        static Material Chrome => Paint(Palette.Hex(0xC8D4DC), 1f);
        static Material Gunmetal => Paint(Palette.Hex(0x4A5560), 0.8f);
        static Material Cherry => Paint(Palette.Hex(0xE04A3C), 0.6f);
        static Material Skin => Paint(Palette.Hex(0xF2C49B), 0.3f);
        static Material Visor => Paint(Palette.Hex(0x28406A), 1f);
        static Material Glass => Paint(Palette.Hex(0x9EDCF2), 1f);
        static Material Flame => Paint(Palette.Hex(0xFF8A2A), 0f, true);
        static Material FlameCore => Paint(Palette.Hex(0xFFE27A), 0f, true);
        static Material Lamp => Paint(Palette.Hex(0xFFE9A8), 0f, true);
        static Material Accent(FactionId owner) => Paint(ModelLibrary.FactionPaint(owner), 0.7f);
        static Material AccentGlow(FactionId owner) => Paint(Color.Lerp(ModelLibrary.FactionPaint(owner), Color.white, 0.3f), 0f, true);

        // ---------------------------------------------------------------- shared details

        /// <summary>A helmeted pilot's head and shoulders, looking along +Z.</summary>
        static void Pilot(MeshKit k, Vector3 at, FactionId owner)
        {
            k.Cylinder(at + Vector3.down * 0.1f, 0.075f, 0.07f, 12, Accent(owner));
            k.Sphere(at, 0.065f, 14, Skin);
            k.Push(at + new Vector3(0, 0.012f, -0.004f)).Sphere(Vector3.zero, 0.074f, 14, Ivory, true).Pop();
            k.Torus(at + new Vector3(0, 0.02f, 0), 0.072f, 0.012f, 16, 4, Accent(owner));
            k.Push(at + new Vector3(0, 0.005f, 0.05f), Quaternion.Euler(-8, 0, 0)).Box(Vector3.zero, new Vector3(0.1f, 0.035f, 0.03f), Visor).Pop();
            k.Strut(at + new Vector3(0.05f, 0.06f, -0.02f), at + new Vector3(0.09f, 0.14f, -0.06f), 0.01f, Chrome);
            k.Sphere(at + new Vector3(0.09f, 0.145f, -0.06f), 0.015f, 6, Lamp);
        }

        static void Rivets(MeshKit k, Vector3 center, float radius, int count, Material mat)
        {
            k.Radial(count, 0, kk => kk.Sphere(center + new Vector3(radius, 0, 0), 0.011f, 5, mat));
        }

        /// <summary>A roundel: white disc, faction centre and a dark ring, facing along the push's +Y.</summary>
        static void Roundel(MeshKit k, Vector3 at, Quaternion facing, float r, FactionId owner)
        {
            k.Push(at, facing);
            k.Cylinder(Vector3.zero, r, 0.008f, 18, Ivory);
            k.Cylinder(new Vector3(0, 0.004f, 0), r * 0.62f, 0.008f, 18, Accent(owner));
            k.Cylinder(new Vector3(0, 0.008f, 0), r * 0.22f, 0.006f, 10, Ivory);
            k.Pop();
        }

        /// <summary>A two-layer exhaust flame trailing along -Z from <paramref name="at"/>.</summary>
        static void Exhaust(MeshKit k, Vector3 at, float radius, float length)
        {
            k.Push(at, Quaternion.Euler(-90, 0, 0));
            k.Lathe(new[] { new Vector2(0, 0), new Vector2(radius, 0), new Vector2(radius * 0.8f, length * 0.35f), new Vector2(0, length) }, 10, Flame);
            k.Lathe(new[] { new Vector2(0, 0.01f), new Vector2(radius * 0.55f, 0.01f), new Vector2(radius * 0.4f, length * 0.25f), new Vector2(0, length * 0.55f) }, 8, FlameCore);
            k.Pop();
        }

        // ---------------------------------------------------------------- the craft

        static MeshKit Build(string typeId, FactionId owner)
        {
            switch (typeId)
            {
                case "guard": return Guard(owner);
                case "tank": return Tank(owner);
                case "artillery": return Artillery(owner);
                case "strike": return Strike(owner);
            }
            return null;
        }

        /// <summary>A chunky saucer on stubby legs with a pilot in an open cockpit behind a windscreen.</summary>
        static MeshKit Guard(FactionId owner)
        {
            var k = new MeshKit();
            k.Push(new Vector3(0, 0.34f, 0));
            k.Lathe(new[] { new Vector2(0, -0.17f), new Vector2(0.24f, -0.16f), new Vector2(0.5f, -0.06f), new Vector2(0.58f, 0) }, 36, Accent(owner));
            k.Lathe(new[] { new Vector2(0.58f, 0), new Vector2(0.6f, 0.02f), new Vector2(0.6f, 0.055f), new Vector2(0.57f, 0.075f) }, 36, Chrome);
            k.Lathe(new[] { new Vector2(0.57f, 0.075f), new Vector2(0.45f, 0.13f), new Vector2(0.25f, 0.18f), new Vector2(0, 0.19f) }, 36, Ivory);
            Rivets(k, new Vector3(0, 0.038f, 0), 0.603f, 22, Gunmetal);
            k.Radial(8, 22.5f, kk => kk.Sphere(new Vector3(0.49f, 0.105f, 0), 0.03f, 8, Lamp));
            k.Torus(new Vector3(0, 0.16f, 0), 0.32f, 0.012f, 32, 4, Gunmetal);
            k.Torus(new Vector3(0, -0.17f, 0), 0.2f, 0.035f, 24, 6, AccentGlow(owner));
            k.Cylinder(new Vector3(0, -0.19f, 0), 0.12f, 0.02f, 16, AccentGlow(owner));
            k.Radial(3, 90, kk => kk.Strut(new Vector3(0.3f, -0.1f, 0), new Vector3(0.42f, -0.3f, 0), 0.03f, Gunmetal)
                .Cylinder(new Vector3(0.42f, -0.32f, 0), 0.05f, 0.025f, 10, Chrome));
            // Open cockpit: a chrome collar, a glass windscreen and the pilot.
            k.Torus(new Vector3(0, 0.19f, 0), 0.15f, 0.028f, 24, 6, Chrome);
            k.Cylinder(new Vector3(0, 0.15f, 0), 0.14f, 0.05f, 20, Gunmetal);
            k.Push(new Vector3(0, 0.2f, 0.12f), Quaternion.Euler(-28, 0, 0))
                .Fin(new[] { new Vector2(-0.13f, 0), new Vector2(0.13f, 0), new Vector2(0.1f, 0.13f), new Vector2(-0.1f, 0.13f) }, 0.012f, Glass)
                .Pop();
            Pilot(k, new Vector3(0, 0.28f, -0.02f), owner);
            k.Strut(new Vector3(-0.2f, 0.17f, -0.25f), new Vector3(-0.3f, 0.55f, -0.38f), 0.014f, Chrome);
            k.Sphere(new Vector3(-0.3f, 0.56f, -0.38f), 0.03f, 8, AccentGlow(owner));
            k.Push(new Vector3(0, 0.1f, -0.42f), Quaternion.Euler(0, -90, 0))
                .Fin(new[] { new Vector2(0.12f, 0), new Vector2(-0.12f, 0), new Vector2(-0.18f, 0.24f), new Vector2(-0.04f, 0.24f) }, 0.025f, Cherry)
                .Pop();
            Roundel(k, new Vector3(0, 0.04f, 0.6f), Quaternion.Euler(90, 0, 0), 0.06f, owner);
            k.Pop();
            return k;
        }

        /// <summary>A bulbous hover tank: ribbed skirt, headlamps, a ringed ray-gun, a commander in the hatch and a pennant.</summary>
        static MeshKit Tank(FactionId owner)
        {
            var k = new MeshKit();
            // Hull along Z.
            k.Push(new Vector3(0, 0.3f, -0.02f), Quaternion.Euler(90, 0, 0), new Vector3(1.3f, 1.25f, 0.62f));
            k.SmoothLathe(new[]
            {
                new Vector2(0, -0.36f), new Vector2(0.13f, -0.34f), new Vector2(0.21f, -0.27f), new Vector2(0.23f, -0.12f),
                new Vector2(0.23f, 0.14f), new Vector2(0.19f, 0.28f), new Vector2(0.1f, 0.36f), new Vector2(0, 0.38f)
            }, 28, Accent(owner));
            k.Torus(new Vector3(0, -0.05f, 0), 0.232f, 0.014f, 28, 4, Gunmetal);
            k.Torus(new Vector3(0, 0.15f, 0), 0.226f, 0.014f, 28, 4, Gunmetal);
            k.Pop();
            // Skirt with ribs and a hover glow.
            k.Push(new Vector3(0, 0.15f, 0), Quaternion.identity, new Vector3(1f, 1f, 1.4f));
            k.Torus(Vector3.zero, 0.33f, 0.065f, 32, 8, Gunmetal);
            k.Radial(16, 0, kk => kk.Box(new Vector3(0.33f, 0.0f, 0), new Vector3(0.14f, 0.11f, 0.025f), Chrome));
            k.Torus(new Vector3(0, -0.07f, 0), 0.26f, 0.03f, 28, 5, AccentGlow(owner));
            k.Pop();
            foreach (float x in new[] { -0.17f, 0.17f })
            {
                k.Push(new Vector3(x, 0.3f, 0.47f), Quaternion.Euler(90, 0, 0)).Cylinder(Vector3.zero, 0.055f, 0.03f, 14, Chrome).Pop();
                k.Sphere(new Vector3(x, 0.3f, 0.5f), 0.042f, 10, Lamp);
                k.Push(new Vector3(x, 0.32f, -0.46f), Quaternion.Euler(-90, 0, 0)).Cylinder(Vector3.zero, 0.05f, 0.06f, 12, Gunmetal).Pop();
                Exhaust(k, new Vector3(x, 0.32f, -0.52f), 0.04f, 0.22f);
            }
            // Turret, hatch and commander.
            k.Sphere(new Vector3(0, 0.42f, -0.06f), 0.22f, 22, Ivory, true);
            k.Torus(new Vector3(0, 0.43f, -0.06f), 0.222f, 0.022f, 28, 5, Accent(owner));
            Rivets(k, new Vector3(0, 0.47f, -0.06f), 0.205f, 14, Gunmetal);
            k.Cylinder(new Vector3(0, 0.6f, -0.12f), 0.09f, 0.04f, 16, Gunmetal);
            k.Push(new Vector3(0, 0.66f, -0.22f), Quaternion.Euler(-65, 0, 0)).Cylinder(Vector3.zero, 0.09f, 0.02f, 16, Ivory).Pop();
            Pilot(k, new Vector3(0, 0.71f, -0.12f), owner);
            // The ray gun.
            k.Push(new Vector3(0, 0.5f, 0.1f), Quaternion.Euler(82, 0, 0));
            k.Lathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.05f, 0), new Vector2(0.05f, 0.18f), new Vector2(0.085f, 0.2f), new Vector2(0.085f, 0.24f),
                new Vector2(0.045f, 0.26f), new Vector2(0.045f, 0.36f), new Vector2(0.075f, 0.38f), new Vector2(0.075f, 0.41f),
                new Vector2(0.04f, 0.43f), new Vector2(0.04f, 0.5f), new Vector2(0.06f, 0.52f), new Vector2(0, 0.53f)
            }, 16, Chrome);
            k.Sphere(new Vector3(0, 0.55f, 0), 0.04f, 10, AccentGlow(owner));
            k.Pop();
            // Whip antenna with a pennant.
            k.Strut(new Vector3(-0.16f, 0.5f, -0.26f), new Vector3(-0.2f, 1.05f, -0.36f), 0.012f, Chrome);
            k.Push(new Vector3(-0.2f, 0.98f, -0.36f), Quaternion.Euler(0, -90, 0))
                .Fin(new[] { new Vector2(0, -0.06f), new Vector2(0, 0.06f), new Vector2(-0.18f, 0.0f) }, 0.01f, Accent(owner))
                .Pop();
            Roundel(k, new Vector3(0.3f, 0.3f, 0f), Quaternion.Euler(0, 0, -90), 0.07f, owner);
            Roundel(k, new Vector3(-0.3f, 0.3f, 0f), Quaternion.Euler(0, 0, 90), 0.07f, owner);
            return k;
        }

        /// <summary>A hover sled with a seated gunner behind a shield and a long coil cannon raised to fire.</summary>
        static MeshKit Artillery(FactionId owner)
        {
            var k = new MeshKit();
            k.Box(new Vector3(0, 0.25f, -0.05f), new Vector3(0.5f, 0.18f, 0.86f), Ivory);
            k.Box(new Vector3(0, 0.25f, -0.05f), new Vector3(0.52f, 0.06f, 0.88f), Accent(owner));
            k.Box(new Vector3(0, 0.16f, -0.05f), new Vector3(0.46f, 0.04f, 0.8f), Gunmetal);
            k.Push(new Vector3(0, 0.25f, 0.38f), Quaternion.Euler(0, 0, 90)).Lathe(new[] { new Vector2(0, -0.25f), new Vector2(0.09f, -0.25f), new Vector2(0.09f, 0.25f), new Vector2(0, 0.25f) }, 12, Ivory, -90f, 180f).Pop();
            foreach (float x in new[] { -0.2f, 0.2f })
                foreach (float z in new[] { -0.35f, 0.25f })
                {
                    k.Torus(new Vector3(x, 0.12f, z), 0.07f, 0.022f, 16, 5, Gunmetal);
                    k.Sphere(new Vector3(x, 0.11f, z), 0.055f, 10, AccentGlow(owner));
                }
            foreach (float x in new[] { -0.26f, 0.26f })
                k.Push(new Vector3(x, 0.3f, -0.42f), Quaternion.Euler(0, -90, 0))
                    .Fin(new[] { new Vector2(0.1f, 0), new Vector2(-0.12f, 0), new Vector2(-0.16f, 0.2f), new Vector2(-0.04f, 0.2f) }, 0.02f, Cherry)
                    .Pop();
            // Ammo crates.
            k.Box(new Vector3(-0.12f, 0.4f, -0.36f), new Vector3(0.16f, 0.12f, 0.12f), Gunmetal);
            k.Box(new Vector3(0.1f, 0.4f, -0.38f), new Vector3(0.14f, 0.1f, 0.1f), Cherry);
            k.Box(new Vector3(0.1f, 0.455f, -0.38f), new Vector3(0.15f, 0.015f, 0.11f), Ivory);
            // Gun mount and coil cannon.
            k.Sphere(new Vector3(0, 0.36f, 0.1f), 0.13f, 16, Gunmetal, true);
            k.Push(new Vector3(0, 0.42f, 0.1f), Quaternion.Euler(52, 0, 0));
            k.Cylinder(Vector3.zero, 0.05f, 0.95f, 14, Chrome);
            for (int i = 0; i < 4; i++) k.Torus(new Vector3(0, 0.28f + i * 0.13f, 0), 0.085f, 0.022f, 16, 5, Flame);
            k.Box(new Vector3(0, 0.15f, 0), new Vector3(0.22f, 0.12f, 0.02f), Gunmetal);
            k.Cone(new Vector3(0, 0.95f, 0), 0.08f, 0.04f, 0.08f, 14, Chrome);
            k.Sphere(new Vector3(0, 1.04f, 0), 0.035f, 8, FlameCore);
            k.Pop();
            // Gunner behind a curved shield.
            k.Push(new Vector3(0, 0.36f, -0.1f), Quaternion.identity)
                .Fin(new[] { new Vector2(-0.16f, 0), new Vector2(0.16f, 0), new Vector2(0.13f, 0.16f), new Vector2(-0.13f, 0.16f) }, 0.02f, Accent(owner))
                .Pop();
            Pilot(k, new Vector3(0, 0.55f, -0.2f), owner);
            Roundel(k, new Vector3(0.262f, 0.25f, -0.15f), Quaternion.Euler(0, 0, -90), 0.06f, owner);
            Roundel(k, new Vector3(-0.262f, 0.25f, -0.15f), Quaternion.Euler(0, 0, 90), 0.06f, owner);
            return k;
        }

        /// <summary>A bulbous rocket fighter with swept wings, wingtip pods, twin tails, a pilot and a long flame.</summary>
        static MeshKit Strike(FactionId owner)
        {
            var k = new MeshKit();
            k.Push(new Vector3(0, 0.36f, -0.5f), Quaternion.Euler(90, 0, 0));
            k.SmoothLathe(new[]
            {
                new Vector2(0, 0), new Vector2(0.1f, 0.01f), new Vector2(0.16f, 0.1f), new Vector2(0.19f, 0.32f),
                new Vector2(0.18f, 0.55f), new Vector2(0.14f, 0.72f)
            }, 24, Ivory);
            k.SmoothLathe(new[] { new Vector2(0.14f, 0.72f), new Vector2(0.1f, 0.84f), new Vector2(0.05f, 0.95f), new Vector2(0, 1.0f) }, 24, Accent(owner));
            k.Torus(new Vector3(0, 0.72f, 0), 0.142f, 0.016f, 24, 4, Chrome);
            k.Torus(new Vector3(0, 0.3f, 0), 0.19f, 0.014f, 24, 4, Gunmetal);
            k.Cone(new Vector3(0, -0.05f, 0), 0.08f, 0.11f, 0.07f, 16, Gunmetal);
            k.Pop();
            Exhaust(k, new Vector3(0, 0.36f, -0.55f), 0.075f, 0.5f);
            // Intakes.
            foreach (float x in new[] { -0.17f, 0.17f })
                k.Push(new Vector3(x, 0.4f, 0.05f), Quaternion.Euler(90, 0, 0)).Cylinder(Vector3.zero, 0.045f, 0.16f, 12, Gunmetal).Pop();
            // Cockpit.
            k.Torus(new Vector3(0, 0.53f, 0.12f), 0.1f, 0.02f, 20, 5, Chrome);
            k.Push(new Vector3(0, 0.53f, 0.2f), Quaternion.Euler(-35, 0, 0))
                .Fin(new[] { new Vector2(-0.09f, 0), new Vector2(0.09f, 0), new Vector2(0.06f, 0.1f), new Vector2(-0.06f, 0.1f) }, 0.01f, Glass)
                .Pop();
            Pilot(k, new Vector3(0, 0.6f, 0.1f), owner);
            // Swept wings with wingtip pods and roundels.
            foreach (float side in new[] { -1f, 1f })
            {
                k.Push(new Vector3(side * 0.12f, 0.34f, -0.05f), Quaternion.Euler(90, 0, 0))
                    .Fin(new[] { new Vector2(0, 0.2f), new Vector2(side * 0.52f, -0.12f), new Vector2(side * 0.54f, -0.26f), new Vector2(0, -0.2f) }, 0.035f, Cherry)
                    .Pop();
                k.Push(new Vector3(side * 0.68f, 0.34f, -0.2f), Quaternion.Euler(90, 0, 0), new Vector3(1f, 2.4f, 1f)).Sphere(Vector3.zero, 0.05f, 12, Ivory).Pop();
                k.Sphere(new Vector3(side * 0.68f, 0.34f, -0.08f), 0.03f, 8, AccentGlow(owner));
                Roundel(k, new Vector3(side * 0.42f, 0.36f, -0.16f), Quaternion.identity, 0.055f, owner);
                // Twin tail fins.
                k.Push(new Vector3(side * 0.09f, 0.42f, -0.38f), Quaternion.Euler(0, -90, side * 12f))
                    .Fin(new[] { new Vector2(0.14f, 0), new Vector2(-0.08f, 0), new Vector2(-0.16f, 0.26f), new Vector2(-0.06f, 0.26f) }, 0.025f, Accent(owner))
                    .Pop();
            }
            return k;
        }

        // ---------------------------------------------------------------- portraits

        /// <summary>
        /// The craft drawn as a comic portrait facing right, cached per type and faction; null when the comic
        /// material is unavailable or the type has no comic model (callers keep their old sprite).
        /// </summary>
        public static Sprite Portrait(string typeId, FactionId owner)
        {
            string key = typeId + "_" + owner;
            Sprite sprite;
            if (portraits.TryGetValue(key, out sprite)) return sprite;
            if (missing) return null;
            if (template == null) template = Resources.Load<Material>("Materials/ComicToon");
            if (template == null || !template.shader.isSupported) { missing = true; return null; }
            var kit = Build(typeId, owner);
            if (kit == null) return null;
            sprite = Render(kit, typeId + " " + owner);
            portraits[key] = sprite;
            return sprite;
        }

        static Sprite Render(MeshKit kit, string name)
        {
            var root = new GameObject("Comic " + name) { layer = Layer };
            root.transform.position = Stage;
            // Nose toward screen-right and a little toward the viewer.
            root.transform.rotation = Quaternion.Euler(0, 112f, 0);
            root.AddComponent<MeshFilter>().sharedMesh = kit.Build(name, true);
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = kit.Materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var bounds = renderer.bounds;

            var camGo = new GameObject("Comic Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.fieldOfView = 24f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            var rot = Quaternion.Euler(22f, 0f, 0f);
            float radius = bounds.extents.magnitude;
            cam.transform.rotation = rot;
            cam.transform.position = bounds.center - rot * Vector3.forward * (radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.98f);

            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
            cam.targetTexture = rt;
            // Render on black and on white and recover alpha from the difference, so the result is transparent
            // whatever the pipeline does with the camera's alpha channel.
            var onBlack = Grab(cam, rt, Color.black);
            var onWhite = Grab(cam, rt, Color.white);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(root);

            var b = onBlack.GetPixels32();
            var w = onWhite.GetPixels32();
            var result = new Color32[b.Length];
            int minX = Size, minY = Size, maxX = -1, maxY = -1;
            for (int i = 0; i < b.Length; i++)
            {
                int diff = ((w[i].r - b[i].r) + (w[i].g - b[i].g) + (w[i].b - b[i].b)) / 3;
                int a = Mathf.Clamp(255 - diff, 0, 255);
                if (a < 8) { result[i] = new Color32(0, 0, 0, 0); continue; }
                int x = i % Size, y = i / Size;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                result[i] = new Color32(
                    (byte)Mathf.Min(255, b[i].r * 255 / a),
                    (byte)Mathf.Min(255, b[i].g * 255 / a),
                    (byte)Mathf.Min(255, b[i].b * 255 / a),
                    (byte)a);
            }
            Object.Destroy(onBlack);
            Object.Destroy(onWhite);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Comic " + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(result);
            tex.Apply(false, false);
            // Crop to the craft (with a little margin) so it fills the battle slot.
            var rect = new Rect(0, 0, Size, Size);
            if (maxX >= minX)
            {
                int pad = 6;
                int x0 = Mathf.Max(0, minX - pad), y0 = Mathf.Max(0, minY - pad);
                rect = new Rect(x0, y0, Mathf.Min(Size, maxX + pad + 1) - x0, Mathf.Min(Size, maxY + pad + 1) - y0);
            }
            return Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
        }

        static Texture2D Grab(Camera cam, RenderTexture rt, Color background)
        {
            cam.backgroundColor = background;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }
    }
}
