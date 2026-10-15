using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Builds Resources/Materials/Dust.mat: URP's unlit particle shader, alpha-blended, with the soft DustDot texture,
    /// for drill dust and rock chips. Run once (menu or -executeMethod); the material is then an ordinary asset.
    /// </summary>
    public static class DustMaterial
    {
        const string MaterialPath = "Assets/Resources/Materials/Dust.mat";
        const string TexturePath = "Assets/Resources/Materials/DustDot.png";

        [MenuItem("Orbital Foundry/Build dust material")]
        public static void Build()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, MaterialPath);
            }
            m.shader = shader;
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            m.SetColor("_BaseColor", Color.white);
            // Transparent, alpha blended, no depth write: soft puffs that fade out.
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            Debug.Log("DUST MATERIAL built with " + shader.name);
        }
    }
}
