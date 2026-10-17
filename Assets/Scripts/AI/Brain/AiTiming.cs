using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace OrbitalFoundry
{
    /// <summary>
    /// Time spent in each AI skill and step: runs, total and longest run. Runtime only and never read by the AI
    /// itself, so it cannot change what the AI does; the benchmark and the debug readout report it.
    /// </summary>
    public static class AiTiming
    {
        sealed class Entry { public long Total, Peak; public int Runs; }

        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static readonly List<string> order = new List<string>();

        public static long Begin() { return Stopwatch.GetTimestamp(); }

        public static void End(string name, long start)
        {
            if (AiSide.TimingTag.Length > 0) name = AiSide.TimingTag + name;
            long spent = Stopwatch.GetTimestamp() - start;
            Entry e;
            if (!entries.TryGetValue(name, out e)) { entries[name] = e = new Entry(); order.Add(name); }
            e.Total += spent;
            e.Runs++;
            if (spent > e.Peak) e.Peak = spent;
        }

        public static void Reset()
        {
            entries.Clear();
            order.Clear();
        }

        static double Ms(long ticks) { return ticks * 1000.0 / Stopwatch.Frequency; }

        /// <summary>One measured skill or step: total and longest run in milliseconds, and how often it ran.</summary>
        public struct Line { public string Name; public double TotalMs, PeakMs; public int Runs; }

        /// <summary>Every skill and step measured whose name starts with <paramref name="prefix"/>, prefix removed.</summary>
        public static List<Line> Lines(string prefix)
        {
            var list = new List<Line>();
            foreach (var name in order)
            {
                if (!name.StartsWith(prefix, System.StringComparison.Ordinal)) continue;
                if (prefix.Length == 0 && name.StartsWith("[", System.StringComparison.Ordinal)) continue; // another side's
                var e = entries[name];
                list.Add(new Line { Name = name.Substring(prefix.Length), TotalMs = Ms(e.Total), PeakMs = Ms(e.Peak), Runs = e.Runs });
            }
            return list;
        }

        /// <summary>One line per skill or step: milliseconds per game tick on average, per run, and the longest run.</summary>
        public static string Report(int gameTicks)
        {
            var sb = new StringBuilder();
            foreach (var name in order)
            {
                var e = entries[name];
                sb.Append("  ").Append(name).Append(": ")
                  .Append((Ms(e.Total) / System.Math.Max(1, gameTicks)).ToString("0.0000")).Append(" ms/tick, ")
                  .Append((Ms(e.Total) / System.Math.Max(1, e.Runs)).ToString("0.000")).Append(" ms a run (")
                  .Append(e.Runs).Append(" runs), worst ").Append(Ms(e.Peak).ToString("0.00")).Append(" ms\n");
            }
            return sb.ToString();
        }
    }
}
