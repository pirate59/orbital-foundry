using System;

namespace OrbitalFoundry
{
    /// <summary>
    /// The trade win: sell goods you make at a set rate (credits a minute, by difficulty) and hold it for
    /// rules.tradeWinHoldSeconds. Every seat keeps its own ledger and can win this way, for its whole team. The rate looks at the last 30 seconds in six five-second slices. Each good counts
    /// only as far as it was also made in that window, so a stockpile emptied into the market adds nothing; a
    /// factory selling what it makes, through Storage or straight to a depot, counts in full.
    /// </summary>
    public static class TradeService
    {
        public const int Slices = 6, SliceSeconds = 5;

        static int SliceTicks(ContentCatalog c)
        {
            return Math.Max(1, SliceSeconds * 1000 / Math.Max(1, c.rules.tickMilliseconds));
        }

        /// <summary>Credits a minute a seat must reach at this world's difficulty.</summary>
        public static int Target(WorldState w, ContentCatalog c)
        {
            var list = c.rules.tradeWinPerMinute;
            if (list == null || list.Count == 0) return int.MaxValue;
            int i = Math.Max(0, Math.Min(list.Count - 1, (int)w.difficulty));
            return list[i];
        }

        public static int HoldSlices(ContentCatalog c)
        {
            return Math.Max(1, (c.rules.tradeWinHoldSeconds + SliceSeconds - 1) / SliceSeconds);
        }

        /// <summary>Seconds <paramref name="f"/> has held the target so far (0 when below it).</summary>
        public static int HeldSeconds(WorldState w, FactionId f)
        {
            var fs = WorldLookup.GetFaction(w, f);
            return fs != null ? fs.trade.heldSlices * SliceSeconds : 0;
        }

        /// <summary>
        /// Called once a factory tick: when a slice ends, the rate over the last 30 seconds is worked out and the
        /// hold counted (or reset), and the oldest slice is cleared for the next one.
        /// </summary>
        public static void Advance(WorldState w, ContentCatalog c)
        {
            foreach (var f in w.factions) Advance(w, c, f);
        }

        static void Advance(WorldState w, ContentCatalog c, FactionState f)
        {
            var t = f.trade;
            int now = w.tick / SliceTicks(c);
            if (t.bucket < 0) { t.bucket = now; Clear(t, now); return; }
            if (now == t.bucket) return;
            // The slice t.bucket has just ended: rate over the window that ends with it.
            t.rate = Rate(w, c, f.id);
            if (t.rate >= Target(w, c)) t.heldSlices++;
            else t.heldSlices = 0;
            if (t.heldSlices >= HoldSlices(c)) t.won = true;
            // Slices skipped (a gap with no ticks) are empty, which breaks a hold.
            if (now - t.bucket > 1) t.heldSlices = 0;
            for (int b = t.bucket + 1; b <= now && b - t.bucket <= Slices; b++) Clear(t, b);
            t.bucket = now;
        }

        static void Clear(TradeLedger t, int bucket)
        {
            int slot = bucket % Slices;
            foreach (var e in t.items) { e.made[slot] = 0; e.sold[slot] = 0; }
        }

        public static void RecordMade(WorldState w, FactionId owner, string itemId, int quantity)
        {
            var f = WorldLookup.GetFaction(w, owner);
            if (f == null || quantity <= 0 || f.trade.bucket < 0) return;
            Entry(f.trade, itemId).made[f.trade.bucket % Slices] += quantity;
        }

        public static void RecordSold(WorldState w, FactionId owner, string itemId, int quantity)
        {
            var f = WorldLookup.GetFaction(w, owner);
            if (f == null || quantity <= 0 || f.trade.bucket < 0) return;
            Entry(f.trade, itemId).sold[f.trade.bucket % Slices] += quantity;
        }

        static TradeItemCounts Entry(TradeLedger t, string itemId)
        {
            foreach (var e in t.items) if (e.itemId == itemId) return e;
            var made = new TradeItemCounts { itemId = itemId };
            t.items.Add(made);
            return made;
        }

        /// <summary>Credits a minute of goods <paramref name="seat"/> sold and made over the last 30 seconds, at its prices.</summary>
        public static int Rate(WorldState w, ContentCatalog c, FactionId seat)
        {
            var f = WorldLookup.GetFaction(w, seat);
            if (f == null) return 0;
            long credits = 0;
            foreach (var e in f.trade.items)
            {
                int made = 0, sold = 0;
                for (int i = 0; i < Slices; i++) { made += e.made[i]; sold += e.sold[i]; }
                int counted = Math.Min(made, sold);
                if (counted > 0) credits += (long)counted * EconomyService.SalePrice(c, f, CatalogRepository.GetItem(c, e.itemId));
            }
            long perMinute = credits * 60 / (Slices * SliceSeconds);
            return perMinute > int.MaxValue ? int.MaxValue : (int)perMinute;
        }
    }
}
