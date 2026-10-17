using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OrbitalFoundry.EditorTools
{
    /// <summary>
    /// Batch-mode entry point for the headless AI arena (tools/ai-arena.sh runs it):
    /// -executeMethod OrbitalFoundry.EditorTools.AiArenaCli.Run -arenaConfig &lt;file.json&gt; [-arenaOut &lt;dir&gt;]
    /// Results go to the output directory (default Logs/arena/&lt;config name&gt;-&lt;time&gt;).
    /// </summary>
    public static class AiArenaCli
    {
        public static void Run()
        {
            int code = 0;
            try
            {
                string config = Arg("-arenaConfig");
                if (string.IsNullOrEmpty(config) || !File.Exists(config)) throw new ArgumentException("Pass -arenaConfig <file.json> (see tools/arena/example.json).");
                string outDir = Arg("-arenaOut");
                if (string.IsNullOrEmpty(outDir))
                    outDir = Path.Combine("Logs", "arena", Path.GetFileNameWithoutExtension(config) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Debug.Log("[arena] config " + config + ", results in " + Path.GetFullPath(outDir));
                AiArena.RunFile(config, outDir, line => Debug.Log("[arena] " + line));
                Debug.Log("[arena] done: " + Path.GetFullPath(outDir));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
