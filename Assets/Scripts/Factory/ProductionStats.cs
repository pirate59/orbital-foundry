using System;
using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// What each faction made, used and sold over the last minute, per item, plus the credits its sales earned, for
    /// the production overview. Runtime only: it is not saved, so after a load it fills again over a minute.
    /// Counts sit in ten-second buckets that roll over as the world's tick moves on.
    /// </summary>
    public sealed class ProductionStats
    {
        public const int Buckets = 6;
        public enum Kind { Made = 0, Used = 1, Sold = 2 }

        sealed class Counter
        {
            public readonly int[][] Counts = { new int[Buckets], new int[Buckets], new int[Buckets] };
        }

        readonly Dictionary<string, Counter>[] items = { new Dictionary<string, Counter>(), new Dictionary<string, Counter>(), new Dictionary<string, Counter>() };
        readonly long[][] credits = { new long[Buckets], new long[Buckets], new long[Buckets] };
        WorldState world;
        int bucketTicks = 100, startTick, bucket = -1, lastTick;

        /// <summary>Called each tick before anything is counted: starts afresh for a new world and clears old buckets.</summary>
        public void Advance(WorldState w, ContentCatalog c)
        {
            if (world != w || w.tick < lastTick)
            {
                world = w;
                startTick = w.tick;
                bucket = -1;
                foreach (var d in items) d.Clear();
                foreach (var cr in credits) Array.Clear(cr, 0, Buckets);
            }
            lastTick = w.tick;
            bucketTicks = Math.Max(1, 10000 / Math.Max(1, c.rules.tickMilliseconds)); // ten seconds
            int now = w.tick / bucketTicks;
            if (now == bucket) return;
            // Clear every bucket skipped since the last one (all of them after a long gap).
            int steps = bucket < 0 ? Buckets : Math.Min(Buckets, now - bucket);
            for (int i = 0; i < steps; i++)
            {
                int slot = ((now - i) % Buckets + Buckets) % Buckets;
                foreach (var d in items)
                    foreach (var counter in d.Values)
                        foreach (var k in counter.Counts) k[slot] = 0;
                foreach (var cr in credits) cr[slot] = 0;
            }
            bucket = now;
        }

        public void Add(FactionId faction, Kind kind, string itemId, int quantity)
        {
            if (quantity <= 0 || string.IsNullOrEmpty(itemId) || bucket < 0) return;
            var d = items[(int)faction];
            Counter counter;
            if (!d.TryGetValue(itemId, out counter)) d[itemId] = counter = new Counter();
            counter.Counts[(int)kind][bucket % Buckets] += quantity;
        }

        public void AddCredits(FactionId faction, int earned)
        {
            if (earned > 0 && bucket >= 0) credits[(int)faction][bucket % Buckets] += earned;
        }

        /// <summary>Seconds of history behind the rates: up to a minute, less just after a start or load.</summary>
        public float WindowSeconds(ContentCatalog c)
        {
            if (world == null) return 0f;
            float seconds = (world.tick - startTick) * c.rules.tickMilliseconds / 1000f;
            return Math.Min(Buckets * 10f, seconds);
        }

        /// <summary>Per minute, over the window (scaled up while the window is still filling).</summary>
        public float PerMinute(ContentCatalog c, FactionId faction, Kind kind, string itemId)
        {
            Counter counter;
            if (!items[(int)faction].TryGetValue(itemId, out counter)) return 0f;
            return Scale(c, Sum(counter.Counts[(int)kind]));
        }

        public float CreditsPerMinute(ContentCatalog c, FactionId faction)
        {
            long sum = 0;
            foreach (var v in credits[(int)faction]) sum += v;
            return Scale(c, sum);
        }

        /// <summary>Every item the faction has made, used or sold within the window.</summary>
        public IEnumerable<string> Items(FactionId faction)
        {
            return items[(int)faction].Keys;
        }

        float Scale(ContentCatalog c, long sum)
        {
            float window = WindowSeconds(c);
            return window < 1f ? 0f : sum * 60f / window;
        }

        static long Sum(int[] counts)
        {
            long s = 0;
            foreach (var v in counts) s += v;
            return s;
        }
    }
}
