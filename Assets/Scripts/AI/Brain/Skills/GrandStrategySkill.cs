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

        /// <summary>
        /// Whether it could lose its attack fleet and recover within the profile's RecoupMinutes: income over that time
        /// pays for the fleet (credits, and Ion counted double), and its shipyards could rebuild it. The fleet is the
        /// strike it is planning (the group, and the ships it still means to build), else its free ships.
        /// </summary>
        public static bool CanRecoup(AiContext ctx)
        {
            var w = ctx.World;
            var c = ctx.Catalog;
            var d = ctx.Decisions;
            float price = 0f;
            int buildTicks = 0, ships = 0;
            System.Action<CraftDefinition> count = def =>
            {
                if (def == null) return;
                price += AiEngagement.Price(def);
                buildTicks += def.buildTicks;
                ships++;
            };
            if (d.StrikeGroup.Count > 0)
                foreach (int id in d.StrikeGroup)
                {
                    var k = WorldLookup.GetCraft(w, id);
                    if (k != null) count(CatalogRepository.GetCraft(c, k.typeId));
                }
            else
                foreach (var k in AIController.FreeCraft(w, c))
                    if (!AIController.IsLastHeadquartersGuard(w, k)) count(CatalogRepository.GetCraft(c, k.typeId));
            if (d.StrikeShortfall > 0 && !string.IsNullOrEmpty(d.StrikeBuildType))
            {
                var planned = CatalogRepository.GetCraft(c, d.StrikeBuildType);
                for (int i = 0; i < d.StrikeShortfall; i++) count(planned);
            }
            int yards = 0;
            foreach (var b in w.buildings)
            {
                if (b.owner != AiSide.Me || !WorldLookup.IsBuildingActive(w, b)) continue;
                var def = CatalogRepository.GetBuilding(c, b.typeId);
                if (def != null && def.kind == BuildingKind.Shipyard) yards++;
            }
            float minutes = System.Math.Max(0.5f, ctx.Profile.RecoupMinutes);
            float earns = ctx.Model.AiSalesPerMinute * minutes;
            float rebuild = yards > 0 ? buildTicks / (float)yards * c.rules.tickMilliseconds / 60000f : float.MaxValue;
            bool ok = ships > 0 && yards > 0 && earns >= price && rebuild <= minutes;
            d.IndustryReason = ships == 0 ? "no attack fleet yet"
                : "fleet of " + ships + " (" + (int)price + "): earns " + (int)earns + " and rebuilds in " + (yards > 0 ? rebuild.ToString("0.0") : "-")
                  + " min at " + yards + " yards, within " + minutes.ToString("0") + " min: " + (ok ? "can press forward" : "not yet");
            return ok;
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
            // The industry gate: only when it could lose the attack fleet and recover in time (income and shipyards).
            bool mayAttack = ctx.World.tick >= p.EarliestAttackTick && ctx.Faction.cumulativeSales >= AIFactoryPlanner.ShipyardAfterSales
                && CanRecoup(ctx);
            if ((r.aiRaids || r.aiExpands) && mayAttack && p.Level >= 1 && m.AiCraft > 0 && m.MilitaryBalance >= lead)
            {
                reason = "military lead " + m.MilitaryBalance.ToString("0.0") + "x";
                return AiStance.Attack;
            }
            // Expand only with a cell to claim that it could fortify (see ExpansionTargetSkill); with none, it builds up.
            if (r.aiExpands && ctx.Decisions.ExpansionCell >= 0 && (m.AiCells <= m.PlayerCells || m.EconomyBalance < 1f))
            {
                reason = m.AiCells <= m.PlayerCells ? "holds no more ground than the player" : "economy behind";
                return AiStance.Expand;
            }
            reason = "growing the factory";
            return AiStance.Build;
        }
    }
}
