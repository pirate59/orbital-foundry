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
            EditorApplication.Exit(Run(OutputDir) ? 0 : 1);
        }

        static bool Run() { return Run(OutputDir); }

        static bool Run(string outputDir)
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
            // The themed loading page leads straight into the in-game title screen.
            PlayerSettings.SplashScreen.show = false;
            // Only exceptions the code throws itself are caught: full support with stack traces slows every managed
            // call in WebAssembly enough to drop frames on the ticks the simulation runs.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputDir,
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
            if (!Directory.Exists(outputDir) || !File.Exists(Path.Combine(outputDir, "index.html")))
            {
                Debug.LogError("Web build produced no index.html in " + outputDir + ".");
                return false;
            }

            File.Copy(HeadersSource, Path.Combine(outputDir, "_headers"), true);

            bool sizesOk = true;
            long total = 0;
            foreach (var file in Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories))
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
            Debug.Log(string.Format("Web build total {0:0.00} MiB in {1}.", total / 1048576.0, outputDir));
            if (sizesOk) Debug.Log("Size check passed. Before deploying, serve over HTTP and confirm that Unity loads its .unityweb assets.");
            return sizesOk;
        }
    }
}
