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

        readonly List<CraftState> group = new List<CraftState>();

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
            // Ships already in the group stay in it (so a gathering finishes); then attack and support ships, the core of
            // a strike; then defence ships, which only add weight; strongest first within each.
            pool.RemoveAll(k => AIController.IsLastHeadquartersGuard(w, k)); // the headquarters keeps its guard
            pool.Sort((a, b) =>
            {
                bool ia = d.StrikeGroup.Contains(a.id), ib = d.StrikeGroup.Contains(b.id);
                if (ia != ib) return ia ? -1 : 1;
                bool ca = AiEngagement.StrikeCore(c, a), cb = AiEngagement.StrikeCore(c, b);
                if (ca != cb) return ca ? -1 : 1;
                int sa = AiWorldModel.Strength(c, a), sb = AiWorldModel.Strength(c, b);
                return sa != sb ? sb.CompareTo(sa) : a.id.CompareTo(b.id);
            });
            // Add ships until the battle, played out, is one it would win keeping what the profile insists on
            // (combat readiness): enough attack value for the whole engagement, not the first round.
            d.StrikeGroup.Clear();
            group.Clear();
            bool emergency = d.Stance == AiStance.Counter;
            bool ready = false;
            foreach (var k in pool)
            {
                group.Add(k);
                d.StrikeGroup.Add(k.id);
                var sim = AiEngagement.Simulate(w, c, group, d.AttackCell);
                if (AiEngagement.Ready(c, group, sim, ctx.Profile, emergency)) { ready = true; d.StrikeOdds = sim; break; }
            }
            // Short: the ships still needed, of the type it builds next, worked out from the battle played out. The
            // budget builds them all (attack ships first, by Fleet composition), up to the profile's fleet size.
            // Is going now the best value? A ready group of defence ships alone is weighed against building attack
            // ships: when those win the battle for less (their price plus the expected losses), it builds them instead.
            int room = System.Math.Max(1, ctx.Profile.MaxCraft + ctx.Model.AiCells - ctx.Model.AiCraft);
            var option = AiEngagement.Cheapest(w, c, group, d.AttackCell, ctx.Profile, emergency, AiEngagement.Buildable(ctx), room);
            if (ready && (!option.Found || option.Ships == 0 || emergency || group.Exists(k => AiEngagement.StrikeCore(c, k))))
            {
                d.StrikeShortfall = 0;
                d.StrikeBuildType = "";
            }
            else
            {
                d.StrikeShortfall = option.Found && option.Ships > 0 ? option.Ships : 1;
                d.StrikeBuildType = option.Found ? option.TypeId : "";
            }

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
                ? d.StrikeGroup.Count + " ships, not yet enough to win a full battle for cell " + d.AttackCell
                : !d.StrikeAssembled ? "gathering " + d.StrikeGroup.Count + " ships in cell " + d.RallyCell
                : d.LaunchNow ? "launching " + d.StrikeGroup.Count + " ships (" + CoreCount(ctx, d) + " attack or support) at cell " + d.AttackCell + " (expects to win in " + d.StrikeOdds.Rounds
                    + " rounds keeping " + (int)(d.StrikeOdds.Kept * 100) + "%)"
                : "assembled in cell " + d.RallyCell + ", waiting for the moment";
            Status = d.StrikeReason;
        }

        static int CoreCount(AiContext ctx, AiDecisions d)
        {
            int n = 0;
            foreach (int id in d.StrikeGroup)
            {
                var k = WorldLookup.GetCraft(ctx.World, id);
                if (k != null && AiEngagement.StrikeCore(ctx.Catalog, k)) n++;
            }
            return n;
        }

        /// <summary>Whether its ships can fly to the cell from its own land without crossing enemy-held cells.</summary>
        static bool ReachableFromHome(AiContext ctx, int cellId)
        {
            var w = ctx.World;
            if (WorldLookup.GetCell(w, cellId).owner == AiSide.Me) return true;
            // From its nearest few cells (a path search each): enough to know whether the front is open there.
            starts.Clear();
            foreach (var cell in w.cells) if (cell.owner == AiSide.Me && ctx.Distance(cell.id, cellId) >= 0) starts.Add(cell.id);
            starts.Sort((a, b) => ctx.Distance(a, cellId).CompareTo(ctx.Distance(b, cellId)));
            for (int i = 0; i < starts.Count && i < 3; i++)
                if (ctx.Services.Movement.FindWorldPath(w, ctx.Map, AiSide.Me, starts[i], cellId) != null) return true;
            return false;
        }

        static readonly List<int> starts = new List<int>();

        /// <summary>A cell's defence: the player's ships there and its Forts.</summary>
        public static int Defence(AiContext ctx, int cellId)
        {
            int defence = 0;
            foreach (var k in ctx.World.crafts)
                if (AiSide.IsEnemy(ctx.World, k.owner) && k.cellId == cellId) defence += AiWorldModel.Strength(ctx.Catalog, k);
            foreach (var b in ctx.World.buildings)
                if (AiSide.IsEnemy(ctx.World, b.owner) && b.cellId == cellId) defence += AiWorldModel.FortStrength(ctx.Catalog, b);
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
                if (AiSide.IsEnemy(ctx.World, cell.owner) || WorldLookup.HasOpposingCraft(ctx.World, cell.id, AiSide.Me)) continue;
                int fromHome = ExpansionTargetSkill.DistanceFromAi(ctx, cell.id);
                if (fromHome < 0 || !ReachableFromHome(ctx, cell.id)) continue; // front-line movement: no way through enemy land
                int score = fromHome * 10 + (cell.owner == AiSide.Me ? 0 : 5);
                if (score < bestScore) { bestScore = score; best = cell.id; }
            }
            return best;
        }
    }
}
