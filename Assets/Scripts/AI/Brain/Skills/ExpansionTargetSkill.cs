using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Expansion Target (factory goal): picks the neutral cell most worth claiming for the economy. Deposits count,
    /// more so ores the AI does not mine yet and rich ones; Ion counts while the AI is short of it. Distance from its
    /// land costs, and so does a player fleet next door, less for a bolder profile.
    /// </summary>
    public sealed class ExpansionTargetSkill : IAiSkill
    {
        public string Name { get { return "Expansion target (factory)"; } }
        public AiSkillArea Area { get { return AiSkillArea.Territory; } }
        public string Status { get; private set; }

        /// <summary>Ion below which an Ion deposit is worth more to claim.</summary>
        public const int IonShort = 300;

        readonly HashSet<string> mined = new HashSet<string>();

        public int EveryThinks(AiContext ctx) { return ctx.Every(5f); } // the map changes slowly

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            mined.Clear();
            foreach (var n in w.nodes)
                if (n.extractorBuildingId >= 0)
                {
                    var b = WorldLookup.GetBuilding(w, n.extractorBuildingId);
                    if (b != null && b.owner == AiSide.Me) mined.Add(n.itemId);
                }

            int best = -1, bestScore = int.MinValue;
            string bestWhy = "";
            foreach (var cell in w.cells)
            {
                if (cell.owner != FactionId.Neutral) continue;
                int distance = DistanceFromAi(ctx, cell.id);
                if (distance < 0) continue;
                int value = 0, newOres = 0, rich = 0;
                foreach (var n in w.nodes)
                {
                    if (n.cellId != cell.id) continue;
                    if (n.itemId == c.rules.ionItemId) { value += ctx.Faction.ion < IonShort ? 8 : 3; continue; }
                    value += 4;
                    if (!mined.Contains(n.itemId)) { value += 8; newOres++; }
                    if (d.MissingOres.Contains(n.itemId)) value += 12; // an ore its target products need
                    if (WorldLookup.IsRichNode(w, n)) { value += 4; rich++; }
                }
                int lost;
                if (d.LostCells.TryGetValue(cell.id, out lost)) value += lost * 3; // lost earlier: reclaim it
                // A player fleet next door makes it risky to hold; a bold AI minds less.
                int danger = 0;
                foreach (var k in w.crafts)
                    if (AiSide.IsEnemy(w, k.owner) && WorldLookup.IsCraftLive(k) && ctx.Distance(k.cellId, cell.id) <= 1) danger++;
                int score = value * 10 - distance * 25 - (int)(danger * 30 * (1f - ctx.Profile.Aggression))
                    + (int)(AiRandom.Unit(w, AiSide.Me, AiRandom.ExpansionTie + cell.id) * ctx.Profile.Variety * 100f); // near-ties go either way
                if (WorldLookup.HasOpposingCraft(w, cell.id, AiSide.Me)) continue; // held by enemy craft: an attack, not an expansion
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell.id;
                    bestWhy = "cell " + cell.id + (newOres > 0 ? " (" + newOres + " new ores" : " (") + (rich > 0 ? ", " + rich + " rich" : "") + ", " + distance + " away)";
                }
            }
            d.ExpansionCell = best;
            d.ExpansionReason = bestWhy;
            Status = best >= 0 ? "expand to " + bestWhy : "nothing to expand into";
        }

        /// <summary>Cells from the nearest AI cell, or -1 when it holds none.</summary>
        public static int DistanceFromAi(AiContext ctx, int cellId)
        {
            int best = -1;
            foreach (var cell in ctx.World.cells)
            {
                if (cell.owner != AiSide.Me) continue;
                int d = ctx.Distance(cell.id, cellId);
                if (d >= 0 && (best < 0 || d < best)) best = d;
            }
            return best;
        }
    }
}
