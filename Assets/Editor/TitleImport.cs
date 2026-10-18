using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// The title screen's ship (Assets/Resources/Title/): the detailed Meshy Mobile Guard, kept at full quality, unlike
    /// the board's copies (MeshyImport squeezes those to 512-pixel textures and compressed meshes, as ten of them can
    /// fly in one fleet). The mesh is not compressed; the colour, normal and metallic maps stay at their full size; the
    /// normal map is imported as one. <see cref="Build"/> packs the separate metallic and roughness maps into the one
    /// map URP's Lit shader reads (metal in red, smoothness in alpha) and makes the ship's material, so the build keeps
    /// the shader variants it needs.
    /// </summary>
    public sealed class TitleImport : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/Title/";

        public override uint GetVersion() { return 1; }

        bool IsTitle => assetPath.Replace('\\', '/').StartsWith(Folder);

        void OnPreprocessModel()
        {
            if (!IsTitle) return;
            var importer = (ModelImporter)assetImporter;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None; // its material is made by Build
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.animationType = ModelImporterAnimationType.None;
        }

        void OnPreprocessTexture()
        {
            if (!IsTitle) return;
            var importer = (TextureImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath);
            importer.maxTextureSize = 4096; // never smaller than the maps themselves
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.anisoLevel = 4;
            if (name.EndsWith("_normal")) importer.textureType = TextureImporterType.NormalMap;
            // The metallic and roughness maps are data, read by Build to make the mask.
            if (name.EndsWith("_metallic") || name.EndsWith("_roughness") || name.EndsWith("_mask")) importer.sRGBTexture = false;
            if (name.EndsWith("_metallic") || name.EndsWith("_roughness")) importer.isReadable = true;
        }

        /// <summary>Packs the mask and makes Assets/Resources/Title/guard.mat (run once, or after replacing the maps).</summary>
        [MenuItem("Orbital Foundry/Build title ship material")]
        public static void Build()
        {
            var metallic = Load(Folder + "guard_metallic.png");
            var roughness = Load(Folder + "guard_roughness.png");
            if (metallic == null || roughness == null) { Debug.LogError("Title ship: the metallic or roughness map is missing."); return; }
            int w = metallic.width, h = metallic.height;
            var m = metallic.GetPixels32();
            var r = roughness.width == w && roughness.height == h ? roughness.GetPixels32() : null;
            var mask = new Color32[m.Length];
            for (int i = 0; i < m.Length; i++)
            {
                byte rough = r != null ? r[i].r : (byte)128;
                mask[i] = new Color32(m[i].r, 0, 0, (byte)(255 - rough)); // metal, -, -, smoothness
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(mask);
            tex.Apply();
            File.WriteAllBytes(Folder + "guard_mask.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Folder + "guard_mask.png", ImportAssetOptions.ForceUpdate);

            var lit = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/Lit.mat");
            if (lit == null) { Debug.LogError("Title ship: Materials/Lit.mat is missing."); return; }
            var mat = new Material(lit) { name = "guard" };
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "guard_color.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "guard_normal.png"));
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "guard_mask.png"));
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness from the mask's alpha
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            AssetDatabase.CreateAsset(mat, Folder + "guard.mat");
            // The separate maps are folded into the mask: they need not ship with the game.
            AssetDatabase.DeleteAsset(Folder + "guard_metallic.png");
            AssetDatabase.DeleteAsset(Folder + "guard_roughness.png");
            AssetDatabase.SaveAssets();
            Debug.Log("Title ship material built: " + w + "x" + h + " mask.");
        }

        static Texture2D Load(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
        }
    }
}
