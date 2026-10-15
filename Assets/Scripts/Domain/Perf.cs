using System.Diagnostics;
using System.Text;

namespace OrbitalFoundry
{
    /// <summary>
    /// A cheap always-on timer for the testing overlay: simulation stages and frame work add their time here, and once
    /// a second it turns the totals into frames per second, ticks per second and milliseconds per stage.
    /// Runtime only; it never touches game state, so it cannot change what the simulation does.
    /// </summary>
    public static class Perf
    {
        public enum Stage
        {
            Expire, Construction, Power, Extractors, Recipes, Requests, Dispatch, Tokens, Sales, Capture, Ai, Raids,
            /// <summary>Route searches and sorting inside Dispatch (also counted in Dispatch).</summary>
            Reach, Search, Sort,
            View, Hud, Input, Count
        }

        /// <summary>The first stage that runs once per frame rather than once per tick.</summary>
        public const Stage FirstFrameStage = Stage.View;

        static readonly long[] ticksIn = new long[(int)Stage.Count];
        static readonly double[] lastMs = new double[(int)Stage.Count];
        static int frames, simTicks;
        static double window;

        public static float Fps { get; private set; }
        public static float Tps { get; private set; }
        /// <summary>Average whole-frame time, in milliseconds, over the last second.</summary>
        public static float FrameMs { get; private set; }
        /// <summary>Average simulation time per tick, in milliseconds, over the last second.</summary>
        public static float TickMs { get; private set; }
        /// <summary>Average CPU time spent in measured work per frame (simulation plus view), in milliseconds.</summary>
        public static float WorkMs { get; private set; }

        public static long Begin() { return Stopwatch.GetTimestamp(); }

        public static void End(Stage stage, long start) { ticksIn[(int)stage] += Stopwatch.GetTimestamp() - start; }

        public static void Tick() { simTicks++; }

        /// <summary>Call once a frame with the frame's real duration.</summary>
        public static void Frame(float deltaSeconds)
        {
            frames++;
            window += deltaSeconds;
            if (window < 1.0) return;
            Fps = (float)(frames / window);
            Tps = (float)(simTicks / window);
            FrameMs = (float)(window * 1000.0 / frames);
            double sim = 0, all = 0;
            for (int i = 0; i < ticksIn.Length; i++)
            {
                double ms = ticksIn[i] * 1000.0 / Stopwatch.Frequency;
                lastMs[i] = ms / window; // milliseconds per second of play
                if (i < (int)FirstFrameStage) sim += ms;
                all += ms;
                ticksIn[i] = 0;
            }
            TickMs = simTicks > 0 ? (float)(sim / simTicks) : 0f;
            WorkMs = (float)(all / frames);
            frames = 0;
            simTicks = 0;
            window = 0;
        }

        /// <summary>Milliseconds spent in a stage per second of play, over the last full second.</summary>
        public static double MsPerSecond(Stage stage) { return lastMs[(int)stage]; }

        /// <summary>A short report for the overlay: rates, then the costliest stages.</summary>
        public static string Report(bool detail)
        {
            var sb = new StringBuilder();
            sb.Append("FPS ").Append(Fps.ToString("0")).Append("  (").Append(FrameMs.ToString("0.0")).Append(" ms)   TPS ").Append(Tps.ToString("0.0"))
              .Append("   sim ").Append(TickMs.ToString("0.00")).Append(" ms/tick");
            if (!detail) return sb.ToString();
            // The five costliest stages, as milliseconds of CPU per second of play.
            var order = new int[(int)Stage.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => lastMs[b].CompareTo(lastMs[a]));
            sb.Append('\n');
            for (int k = 0; k < 5; k++)
            {
                int i = order[k];
                if (lastMs[i] < 0.05) break;
                if (k > 0) sb.Append("  ·  ");
                sb.Append((Stage)i).Append(' ').Append(lastMs[i].ToString("0.0"));
            }
            sb.Append("  ms/s");
            return sb.ToString();
        }
    }
}
