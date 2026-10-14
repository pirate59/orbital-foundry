using UnityEngine;

namespace OrbitalFoundry
{
    public enum ArtStyle
    {
        /// <summary>Cells are aligned truss platforms and every building is an orbital module docked to one.</summary>
        Station = 0,
        /// <summary>Cells are scattered flat-topped asteroids; buildings stand on the rock and drones link the islands.</summary>
        Belt = 1
    }

    /// <summary>
    /// Which art style this player shows. A web build bakes its default into Resources/art_style (see WebBuild's
    /// -artStyle); a <c>?style=station</c> or <c>?style=belt</c> URL parameter overrides it.
    /// </summary>
    public static class ArtDirection
    {
        static bool resolved;
        static ArtStyle current;

        public static ArtStyle Current
        {
            get
            {
                if (!resolved) Resolve();
                return current;
            }
            set { current = value; resolved = true; }
        }

        public static bool Belt => Current == ArtStyle.Belt;

        static void Resolve()
        {
            resolved = true;
            current = ArtStyle.Belt;
            var baked = Resources.Load<TextAsset>("art_style");
            ArtStyle parsed;
            if (baked != null && TryParse(baked.text, out parsed)) current = parsed;
            string url = Application.absoluteURL ?? "";
            int q = url.IndexOf("style=", System.StringComparison.OrdinalIgnoreCase);
            if (q >= 0)
            {
                string value = url.Substring(q + 6);
                int end = value.IndexOfAny(new[] { '&', '#' });
                if (end >= 0) value = value.Substring(0, end);
                if (TryParse(value, out parsed)) current = parsed;
            }
        }

        public static bool TryParse(string text, out ArtStyle style)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "station": style = ArtStyle.Station; return true;
                case "belt": style = ArtStyle.Belt; return true;
            }
            style = ArtStyle.Belt;
            return false;
        }
    }
}
