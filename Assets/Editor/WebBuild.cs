using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>Builds the Brotli-compressed Web player to Build/Web with Unity's decompression fallback.</summary>
    public static class WebBuild
    {
        public const string OutputDir = "Build/Web";
        public const string HeadersSource = "deploy/_headers";
        public const long CloudflareAssetLimitBytes = 25L * 1024 * 1024;

        [MenuItem("Tools/Orbital Foundry/Build Web")]
        public static void BuildWeb()
        {
            bool ok = Run();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Orbital Foundry", ok ? "Web build written to " + OutputDir + ". Serve it over HTTP and check headers before deploying." : "Web build failed; see the Console.", "OK");
        }

        /// <summary>Batch-mode entry: -executeMethod OrbitalFoundry.EditorTools.WebBuild.BuildWebBatch</summary>
        public static void BuildWebBatch()
        {
            EditorApplication.Exit(Run() ? 0 : 1);
        }

        static bool Run()
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || !scenes.Contains(ProjectSetup.ScenePath))
            {
                Debug.LogError("Main.unity is not an enabled build scene. Run Tools/Orbital Foundry/Create Prototype Scene first.");
                return false;
            }
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
            {
                Debug.LogError("Web Build Support is not installed for this editor.");
                return false;
            }
            if (!File.Exists(HeadersSource))
            {
                Debug.LogError(HeadersSource + " is missing.");
                return false;
            }

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            // Workers Static Assets may apply another transfer encoding to uploaded files.
            // Let Unity decode its .unityweb files after the browser handles that encoding.
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:OrbitalFoundry"; // full-window canvas
            // Prototype builds report every exception with a stack trace in the browser console.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;

            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("Web build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors).");
                return false;
            }
            if (!Directory.Exists(OutputDir) || !File.Exists(Path.Combine(OutputDir, "index.html")))
            {
                Debug.LogError("Web build produced no index.html in " + OutputDir + ".");
                return false;
            }

            File.Copy(HeadersSource, Path.Combine(OutputDir, "_headers"), true);

            bool sizesOk = true;
            long total = 0;
            foreach (var file in Directory.GetFiles(OutputDir, "*", SearchOption.AllDirectories))
            {
                long size = new FileInfo(file).Length;
                total += size;
                Debug.Log(string.Format("Web asset {0}: {1:0.00} MiB", file, size / 1048576.0));
                if (size > CloudflareAssetLimitBytes)
                {
                    Debug.LogError("Asset exceeds Cloudflare's 25 MiB static asset limit: " + file);
                    sizesOk = false;
                }
            }
            Debug.Log(string.Format("Web build total {0:0.00} MiB in {1}.", total / 1048576.0, OutputDir));
            if (sizesOk) Debug.Log("Size check passed. Before deploying, serve over HTTP and confirm that Unity loads its .unityweb assets.");
            return sizesOk;
        }
    }
}
