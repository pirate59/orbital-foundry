using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Creates the URP pipeline asset and the shared materials the 3D board clones at runtime. Materials live in
    /// Resources so their shaders and keyword variants are always in the player build.
    /// </summary>
    public static class RenderingSetup
    {
        const string SettingsDir = "Assets/Settings/Rendering";
        const string PipelinePath = SettingsDir + "/OrbitalFoundry_URP.asset";
        const string RendererPath = SettingsDir + "/OrbitalFoundry_Renderer.asset";
        const string MaterialsDir = "Assets/Resources/Materials";
        const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

        [MenuItem("Tools/Orbital Foundry/Configure Rendering")]
        public static void ConfigureMenu() { Run(); }

        /// <summary>Batch-mode entry: -executeMethod OrbitalFoundry.EditorTools.RenderingSetup.ConfigureBatch</summary>
        public static void ConfigureBatch() { EditorApplication.Exit(Run() ? 0 : 1); }

        public static bool Run()
        {
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(MaterialsDir);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            if (renderer.postProcessData == null)
            {
                Debug.LogError("URP post-process data was not found at " + PostProcessDataPath + ".");
                return false;
            }

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 4;
            pipeline.shadowDistance = 140f;
            pipeline.shadowCascadeCount = 2;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var sky = Shader.Find("OrbitalFoundry/RetroSky");
            if (lit == null || unlit == null || sky == null)
            {
                Debug.LogError("A rendering shader is missing (URP Lit/Unlit or OrbitalFoundry/RetroSky).");
                return false;
            }

            SaveMaterial("Lit", lit, m => { m.SetFloat("_Smoothness", 0.5f); });
            SaveMaterial("LitEmissive", lit, m =>
            {
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            SaveMaterial("LitFade", lit, m => MakeTransparent(m));
            SaveMaterial("LitFadeEmissive", lit, m =>
            {
                MakeTransparent(m);
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            });
            SaveMaterial("Unlit", unlit, m => { });
            SaveMaterial("UnlitGlow", unlit, m =>
            {
                MakeTransparent(m);
                m.SetFloat("_Blend", 2f); // additive
            });
            SaveMaterial("RetroSky", sky, m => { });

            AssetDatabase.SaveAssets();
            Debug.Log("Orbital Foundry rendering configured: " + PipelinePath);
            return true;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
        }

        static void SaveMaterial(string name, Shader shader, System.Action<Material> configure)
        {
            string path = MaterialsDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = m == null;
            if (created) m = new Material(shader);
            m.shader = shader;
            configure(m);
            if (shader.name.StartsWith("Universal Render Pipeline")) UnityEditor.BaseShaderGUI.SetMaterialKeywords(m);
            if (created) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
        }
    }
}
