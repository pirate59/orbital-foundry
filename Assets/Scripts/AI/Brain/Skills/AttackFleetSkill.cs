using System.Collections.Generic;

namespace OrbitalFoundry
{
    /// <summary>
    /// Attack Fleet and Attack Timing: turns an attack target into a strike. It picks the strongest free, healthy
    /// ships until together they beat the target's defence by the profile's margin, chooses a rally cell next to the
    /// target (its own if it has one there), and says to launch once every ship has gathered and the moment is
    /// right: attacking or countering, past the grace period, and either the raid timer has run or the player's win
    /// must be stopped. What it still lacks becomes a strength shortfall that the budget and fleet composition build
    /// towards. Every ship in the cells next to the target joins the battle, so the whole group fights together.
    /// </summary>
    public sealed class AttackFleetSkill : IAiSkill
    {
        public string Name { get { return "Attack fleet & timing"; } }
        public AiSkillArea Area { get { return AiSkillArea.Military; } }
        public string Status { get; private set; }

        readonly List<CraftState> pool = new List<CraftState>();

        public int EveryThinks(AiContext ctx) { return 1; }

        public void Think(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            d.LaunchNow = false;
            d.StrikeAssembled = false;
            bool wanted = ctx.Catalog.rules.aiRaids && (d.Stance == AiStance.Attack || d.Stance == AiStance.Counter) && d.AttackCell >= 0;
            if (!wanted)
            {
                d.StrikeGroup.Clear();
                d.RallyCell = -1;
                d.StrikeShortfall = 0;
                d.StrikeReason = "";
                Status = "no strike planned";
                return;
            }

            // What it takes: the target's defence times the odds the profile wants.
            int defence = Defence(ctx, d.AttackCell);
            int needed = (int)(defence * ctx.Profile.NeededEase) + 1;

            // Where it gathers, worked out first: a ship that reaches the rally cell is made that cell's defender when it
            // has none, and must stay in the group all the same (else the gathering starts over, again and again).
            d.RallyCell = RallyCell(ctx, d.AttackCell);

            // Ships to choose from: free, healthy ones, and those already in the group (they may be on their way).
            pool.Clear();
            foreach (var k in AIController.FreeCraft(w, c))
                if (!AIController.WantsRepair(w, c, k)) pool.Add(k);
            foreach (int id in d.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k != null && k.owner == AiSide.Me && WorldLookup.IsCraftLive(k) && !pool.Contains(k)
                    && (!WorldLookup.IsDefender(w, k) || (d.RallyCell >= 0 && k.cellId == d.RallyCell))) pool.Add(k);
            }
            // Ships already in the group stay in it (so a gathering finishes), then the strongest.
            pool.Sort((a, b) =>
            {
                bool ia = d.StrikeGroup.Contains(a.id), ib = d.StrikeGroup.Contains(b.id);
                if (ia != ib) return ia ? -1 : 1;
                int sa = AiWorldModel.Strength(c, a), sb = AiWorldModel.Strength(c, b);
                return sa != sb ? sb.CompareTo(sa) : a.id.CompareTo(b.id);
            });
            d.StrikeGroup.Clear();
            int strength = 0;
            foreach (var k in pool)
            {
                if (strength >= needed) break;
                d.StrikeGroup.Add(k.id);
                strength += AiWorldModel.Strength(c, k);
            }
            d.StrikeShortfall = System.Math.Max(0, needed - strength);

            if (d.RallyCell < 0)
            {
                d.StrikeReason = "no cell next to cell " + d.AttackCell + " to gather in";
                Status = d.StrikeReason;
                return;
            }
            bool gathered = d.StrikeGroup.Count > 0;
            foreach (int id in d.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(w, id);
                if (k.cellId != d.RallyCell || k.worldPathCellIds.Count > 0) { gathered = false; break; }
            }
            d.StrikeAssembled = gathered && d.StrikeShortfall == 0;

            bool timeRight = w.tick >= ctx.Profile.EarliestAttackTick
                && (d.RaidNow || w.tick >= AiSide.StateOf(w).nextRaidTick);
            d.LaunchNow = d.StrikeAssembled && timeRight && AiSide.StateOf(w).raidCraftId == -1;
            d.StrikeReason = d.StrikeShortfall > 0
                ? d.StrikeGroup.Count + " ships, " + d.StrikeShortfall + " strength short of cell " + d.AttackCell
                : !d.StrikeAssembled ? "gathering " + d.StrikeGroup.Count + " ships in cell " + d.RallyCell
                : d.LaunchNow ? "launching " + d.StrikeGroup.Count + " ships at cell " + d.AttackCell
                : "assembled in cell " + d.RallyCell + ", waiting for the moment";
            Status = d.StrikeReason;
        }

        /// <summary>A cell's defence: the player's ships there and its Forts.</summary>
        public static int Defence(AiContext ctx, int cellId)
        {
            int defence = 0;
            foreach (var k in ctx.World.crafts)
                if (k.owner == AiSide.Foe && k.cellId == cellId) defence += AiWorldModel.Strength(ctx.Catalog, k);
            foreach (var b in ctx.World.buildings)
                if (b.owner == AiSide.Foe && b.cellId == cellId) defence += AiWorldModel.FortStrength(ctx.Catalog, b);
            return defence;
        }

        /// <summary>
        /// A cell next to the target to gather in: one of the AI's own if it has one there, else a neutral one with no
        /// player ships; the one nearest the AI's land. -1 when there is none.
        /// </summary>
        public static int RallyCell(AiContext ctx, int target)
        {
            int best = -1, bestScore = int.MaxValue;
            foreach (var cell in ctx.World.cells)
            {
                if (ctx.Distance(cell.id, target) != 1) continue;
                if (cell.owner == AiSide.Foe || WorldLookup.HasOpposingCraft(ctx.World, cell.id, AiSide.Me)) continue;
                int fromHome = ExpansionTargetSkill.DistanceFromAi(ctx, cell.id);
                if (fromHome < 0) continue;
                int score = fromHome * 10 + (cell.owner == AiSide.Me ? 0 : 5);
                if (score < bestScore) { bestScore = score; best = cell.id; }
            }
            return best;
        }
    }
}
