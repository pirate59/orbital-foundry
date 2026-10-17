namespace OrbitalFoundry
{
    /// <summary>
    /// Grand Strategy: chooses the AI's stance from the situation and its profile. Defend when a cell is threatened
    /// beyond its defence; Counter when the player nears a trade win; Attack when clearly stronger and bold enough;
    /// Expand while it holds little ground; otherwise Build. A new stance must hold for the profile's reaction time
    /// before it takes over, except Defend, which is immediate. Stances that need expansion or raids only appear when
    /// the rules allow them.
    /// </summary>
    public sealed class GrandStrategySkill : IAiSkill
    {
        public string Name { get { return "Grand strategy"; } }
        public AiSkillArea Area { get { return AiSkillArea.Strategy; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Profile.AssessEveryThinks; }

        public void Think(AiContext ctx)
        {
            var p = ctx.Profile;
            var d = ctx.Decisions;
            string reason;
            var want = Choose(ctx, out reason);

            if (want == d.Stance) { d.PendingSinceTick = -1; d.StanceReason = reason; }
            else if (want == AiStance.Defend)
            {
                d.Stance = want;
                d.StanceReason = reason;
                d.PendingSinceTick = -1;
            }
            else
            {
                if (d.PendingSinceTick < 0 || d.PendingStance != want) { d.PendingStance = want; d.PendingSinceTick = ctx.World.tick; }
                if (ctx.World.tick - d.PendingSinceTick >= p.ReactionTicks)
                {
                    d.Stance = want;
                    d.StanceReason = reason;
                    d.PendingSinceTick = -1;
                }
            }
            Status = d.Stance + (d.PendingSinceTick >= 0 ? " (considering " + d.PendingStance + ")" : "");
        }

        static AiStance Choose(AiContext ctx, out string reason)
        {
            var m = ctx.Model;
            var p = ctx.Profile;
            var r = ctx.Catalog.rules;
            if (r.aiDefends && m.MostThreatenedCell >= 0)
            {
                reason = "cell " + m.MostThreatenedCell + " threatened";
                return AiStance.Defend;
            }
            if (r.aiRaids && m.AiCraft > 0 && (m.PlayerWinProgress >= p.CounterAt || (m.PlayerHolding && p.CounterAt <= 1f)))
            {
                reason = "player near a trade win (" + (int)(m.PlayerWinProgress * 100) + "%)";
                return AiStance.Counter;
            }
            // Bolder profiles attack on a smaller lead, and only once past the grace period with an earning economy.
            float lead = 1f + (1f - p.Aggression);
            bool mayAttack = ctx.World.tick >= p.EarliestAttackTick && ctx.Faction.cumulativeSales >= AIFactoryPlanner.ShipyardAfterSales;
            if ((r.aiRaids || r.aiExpands) && mayAttack && p.Level >= 1 && m.AiCraft > 0 && m.MilitaryBalance >= lead)
            {
                reason = "military lead " + m.MilitaryBalance.ToString("0.0") + "x";
                return AiStance.Attack;
            }
            if (r.aiExpands && (m.AiCells <= m.PlayerCells || m.EconomyBalance < 1f))
            {
                reason = m.AiCells <= m.PlayerCells ? "holds no more ground than the player" : "economy behind";
                return AiStance.Expand;
            }
            reason = "growing the factory";
            return AiStance.Build;
        }
    }
}
