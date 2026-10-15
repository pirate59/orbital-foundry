using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// Shared materials for the 3D board, cloned from the templates in Resources/Materials and cached by look, so
    /// every model with the same paint shares one material. Each opaque material has a faded twin for Belt View.
    /// </summary>
    public static class Palette
    {
        // Atomic-age paint chips.
        public static readonly Color Cream = Hex(0xEFE4C8);
        public static readonly Color Ivory = Hex(0xF7F0DC);
        public static readonly Color Mint = Hex(0x8FD6C0);
        public static readonly Color Seafoam = Hex(0x5FB8A8);
        public static readonly Color Teal = Hex(0x1E6A70);
        public static readonly Color DeepTeal = Hex(0x123E46);
        public static readonly Color Cherry = Hex(0xD8423A);
        public static readonly Color Atomic = Hex(0xF29A3A);
        public static readonly Color Mustard = Hex(0xE8C04A);
        public static readonly Color Chrome = Hex(0xC9D2D6);
        public static readonly Color Gunmetal = Hex(0x3A434B);
        public static readonly Color Bakelite = Hex(0x5A2E22);
        public static readonly Color Glass = Hex(0x9FE6FF);

        public enum Finish { Enamel, Chrome, Matte, Glow }

        struct Key
        {
            public Color32 Color;
            public Finish Finish;
            public bool Faded;
            public override int GetHashCode() { return (Color.r << 24 | Color.g << 16 | Color.b << 8 | Color.a) ^ ((int)Finish * 31 + (Faded ? 7 : 0)); }
            public override bool Equals(object o) { var k = (Key)o; return k.Color.Equals(Color) && k.Finish == Finish && k.Faded == Faded; }
        }

        static readonly Dictionary<Key, Material> cache = new Dictionary<Key, Material>();
        static readonly Dictionary<Material, Material> fadedTwin = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, Material> dimmedTwin = new Dictionary<Material, Material>();
        static readonly Dictionary<Material, Key> keyOf = new Dictionary<Material, Key>();
        static Material lit, litEmissive, litFade, litFadeEmissive, unlit, glow;

        /// <summary>Alpha of buildings in Belt View.</summary>
        public const float FadeAlpha = 0.18f;

        public static bool Ready => Template("Lit") != null;

        static Material Template(string name)
        {
            switch (name)
            {
                case "Lit": return lit ?? (lit = Resources.Load<Material>("Materials/Lit"));
                case "LitEmissive": return litEmissive ?? (litEmissive = Resources.Load<Material>("Materials/LitEmissive"));
                case "LitFade": return litFade ?? (litFade = Resources.Load<Material>("Materials/LitFade"));
                case "LitFadeEmissive": return litFadeEmissive ?? (litFadeEmissive = Resources.Load<Material>("Materials/LitFadeEmissive"));
                case "Unlit": return unlit ?? (unlit = Resources.Load<Material>("Materials/Unlit"));
                case "UnlitGlow": return glow ?? (glow = Resources.Load<Material>("Materials/UnlitGlow"));
            }
            return null;
        }

        public static Material Get(Color color, Finish finish = Finish.Enamel) { return Get(color, finish, false); }

        static Material Get(Color color, Finish finish, bool faded)
        {
            var key = new Key { Color = color, Finish = finish, Faded = faded };
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            bool glowing = finish == Finish.Glow;
            var template = Template(faded ? (glowing ? "LitFadeEmissive" : "LitFade") : (glowing ? "LitEmissive" : "Lit"));
            m = new Material(template) { name = finish + " " + ColorUtility.ToHtmlStringRGB(color) + (faded ? " faded" : "") };
            var c = color;
            if (faded) c.a = FadeAlpha;
            m.SetColor("_BaseColor", c);
            switch (finish)
            {
                case Finish.Chrome: m.SetFloat("_Metallic", 0.85f); m.SetFloat("_Smoothness", 0.82f); break;
                case Finish.Matte: m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.18f); break;
                case Finish.Glow: m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.6f); m.SetColor("_EmissionColor", color * 2.4f); break;
                default: m.SetFloat("_Metallic", 0.05f); m.SetFloat("_Smoothness", 0.62f); break;
            }
            cache[key] = m;
            keyOf[m] = key;
            if (!faded) fadedTwin[m] = Get(color, finish, true);
            return m;
        }

        static readonly Dictionary<Texture, Material> textured = new Dictionary<Texture, Material>();

        /// <summary>
        /// Lit material carrying an imported model's texture (a Meshy model's), with Belt View and stopped twins so
        /// <see cref="Faded"/> and <see cref="Dimmed"/> treat it like any palette paint.
        /// </summary>
        public static Material Textured(Texture texture, Color tint)
        {
            Material m;
            if (textured.TryGetValue(texture, out m)) return m;
            tint.a = 1f;
            m = new Material(Template("Lit")) { name = "Textured " + texture.name };
            m.SetTexture("_BaseMap", texture);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", 0.05f);
            m.SetFloat("_Smoothness", 0.45f);
            var f = new Material(Template("LitFade")) { name = m.name + " faded" };
            f.SetTexture("_BaseMap", texture);
            f.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, FadeAlpha));
            var d = new Material(m) { name = m.name + " dimmed" };
            float grey = tint.grayscale;
            d.SetColor("_BaseColor", Color.Lerp(tint, new Color(grey, grey, grey), 0.6f) * 0.55f);
            fadedTwin[m] = f;
            dimmedTwin[m] = d;
            textured[texture] = m;
            return m;
        }

        /// <summary>The Belt View twin of a palette material (itself when it has none).</summary>
        public static Material Faded(Material m)
        {
            Material f;
            return m != null && fadedTwin.TryGetValue(m, out f) ? f : m;
        }

        /// <summary>
        /// The powered-down look of a palette material: lights go out (glow becomes dark enamel) and paint greys a
        /// little, for stopped buildings.
        /// </summary>
        public static Material Dimmed(Material m)
        {
            if (m == null) return null;
            Material d;
            if (dimmedTwin.TryGetValue(m, out d)) return d;
            Key key;
            if (!keyOf.TryGetValue(m, out key)) return m;
            Color c = key.Color;
            float grey = c.grayscale;
            var dim = Color.Lerp(c, new Color(grey, grey, grey), 0.6f) * (key.Finish == Finish.Glow ? 0.3f : 0.6f);
            dim.a = 1f;
            d = Get(dim, key.Finish == Finish.Glow ? Finish.Enamel : key.Finish);
            dimmedTwin[m] = d;
            return d;
        }

        /// <summary>Unlit colour, optionally additive for halos and beams.</summary>
        public static Material Flat(Color color, bool additive = false)
        {
            var key = new Key { Color = color, Finish = additive ? (Finish)100 : (Finish)101 };
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            m = new Material(Template(additive ? "UnlitGlow" : "Unlit")) { name = (additive ? "Glow " : "Flat ") + ColorUtility.ToHtmlStringRGBA(color) };
            m.SetColor("_BaseColor", color);
            cache[key] = m;
            return m;
        }

        public static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }
    }
}
