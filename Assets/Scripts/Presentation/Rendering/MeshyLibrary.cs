using System.Collections.Generic;
using UnityEngine;

namespace OrbitalFoundry
{
    /// <summary>
    /// The "Meshy" visual style: item models made with Meshy (meshy.ai), loaded from Resources/Meshy/Items/&lt;itemId&gt;
    /// (an imported FBX or OBJ, or a prefab). Each model is merged into one mesh, scaled to a cargo slot (about 0.26
    /// across, resting on y = 0) and given the game's lit material with its own texture, so it behaves exactly like a
    /// built-in cargo model. Items without a model keep the built-in one.
    /// </summary>
    public static class MeshyLibrary
    {
        public const string ItemFolder = "Meshy/Items/";
        const float CargoSize = 0.26f;

        /// <summary>True while the Meshy visual style is chosen.</summary>
        public static bool Enabled;

        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Material[]> materials = new Dictionary<string, Material[]>();
        static readonly HashSet<string> missing = new HashSet<string>();
        static readonly Dictionary<Texture, Material> textured = new Dictionary<Texture, Material>();

        /// <summary>True when a Meshy model exists for the item (whether or not the style is on).</summary>
        public static bool HasItem(string itemId) { return Load(itemId) != null; }

        /// <summary>How many of the catalog's items have a Meshy model.</summary>
        public static int CountItems(ContentCatalog c)
        {
            int n = 0;
            if (c != null) foreach (var item in c.items) if (HasItem(item.id)) n++;
            return n;
        }

        /// <summary>The item's Meshy cargo model (a pile of <paramref name="count"/> when stacked), or null to use the built-in one.</summary>
        public static GameObject Cargo(string itemId, Transform parent, int count, Vector3[] pileSpots, float pileScale)
        {
            if (!Enabled) return null;
            string key = count > 1 ? itemId + "x" + count : itemId;
            var mesh = Load(itemId);
            if (mesh == null) return null;
            if (count > 1 && !meshes.TryGetValue(key, out mesh))
            {
                // A pile: the single model repeated at the pile spots.
                var single = meshes[itemId];
                var parts = new List<CombineInstance>();
                for (int i = 0; i < count; i++)
                    for (int s = 0; s < single.subMeshCount; s++)
                        parts.Add(new CombineInstance { mesh = single, subMeshIndex = s, transform = Matrix4x4.TRS(pileSpots[i], Quaternion.Euler(0, i * 47f, 0), Vector3.one * pileScale) });
                mesh = Combine("Meshy " + key, parts);
                meshes[key] = mesh;
                var mats = new List<Material>();
                for (int i = 0; i < count; i++) mats.AddRange(materials[itemId]);
                materials[key] = mats.ToArray();
            }
            var go = new GameObject("Cargo " + itemId);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials[key];
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Mesh Combine(string name, List<CombineInstance> parts)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            // One submesh per source submesh and copy, so each keeps its material.
            mesh.CombineMeshes(parts.ToArray(), false, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Loads and normalises one model; null (remembered) when there is none.</summary>
        static Mesh Load(string itemId)
        {
            Mesh mesh;
            if (meshes.TryGetValue(itemId, out mesh)) return mesh;
            if (missing.Contains(itemId)) return null;
            var prefab = Resources.Load<GameObject>(ItemFolder + itemId);
            if (prefab == null) { missing.Add(itemId); return null; }

            // Gather every mesh in the model, in the model's own space.
            var parts = new List<CombineInstance>();
            var mats = new List<Material>();
            var rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (source == null || renderer == null) continue;
                var shared = renderer.sharedMaterials;
                for (int s = 0; s < source.subMeshCount; s++)
                {
                    parts.Add(new CombineInstance { mesh = source, subMeshIndex = s, transform = rootInverse * filter.transform.localToWorldMatrix });
                    mats.Add(Convert(s < shared.Length ? shared[s] : null));
                }
            }
            if (parts.Count == 0)
            {
                Debug.LogWarning("Meshy model for " + itemId + " has no meshes; using the built-in model.");
                missing.Add(itemId);
                return null;
            }
            Mesh merged;
            try { merged = Combine("Meshy " + itemId, parts); }
            catch (System.Exception e)
            {
                // Meshes must be readable; the importer in Assets/Editor/MeshyImport.cs turns that on for this folder.
                Debug.LogWarning("Meshy model for " + itemId + " could not be read (" + e.Message + "); using the built-in model.");
                missing.Add(itemId);
                return null;
            }

            // Fit it to a cargo slot: largest side CargoSize, centred, resting on y = 0.
            var b = merged.bounds;
            float scale = CargoSize / Mathf.Max(0.0001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            var fit = Matrix4x4.Scale(Vector3.one * scale) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            var fitted = new List<CombineInstance>();
            for (int s = 0; s < merged.subMeshCount; s++) fitted.Add(new CombineInstance { mesh = merged, subMeshIndex = s, transform = fit });
            mesh = Combine("Meshy " + itemId, fitted);
            if (Application.isPlaying) Object.Destroy(merged); else Object.DestroyImmediate(merged);
            meshes[itemId] = mesh;
            materials[itemId] = mats.ToArray();
            return mesh;
        }

        /// <summary>The game's lit material carrying the imported material's texture and tint.</summary>
        static Material Convert(Material source)
        {
            var white = Palette.Get(Color.white, Palette.Finish.Matte);
            if (source == null) return white;
            Texture tex = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : null;
            if (tex == null && source.HasProperty("_MainTex")) tex = source.GetTexture("_MainTex");
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.color : Color.white;
            if (tex == null) return Palette.Get(new Color(tint.r, tint.g, tint.b), Palette.Finish.Matte);
            Material m;
            if (textured.TryGetValue(tex, out m)) return m;
            m = new Material(white) { name = "Meshy " + tex.name };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
            textured[tex] = m;
            return m;
        }
    }
}
