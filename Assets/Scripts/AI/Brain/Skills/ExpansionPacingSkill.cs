namespace OrbitalFoundry
{
    /// <summary>
    /// Expansion Execution and Aggressiveness: whether to send a ship to claim the expansion target now, and how
    /// many claims may be under way at once (one, two for a bold profile). It holds back while defending, keeps a
    /// ship at home when something is threatened, and a cautious profile waits until its economy can pay for a
    /// Fort zone to hold what it claims.
    /// </summary>
    public sealed class ExpansionPacingSkill : IAiSkill
    {
        public string Name { get { return "Expansion pacing"; } }
        public AiSkillArea Area { get { return AiSkillArea.Territory; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return 1; }

        public void Think(AiContext ctx)
        {
            var d = ctx.Decisions;
            var p = ctx.Profile;
            d.MaxExpansions = 1 + (int)(p.Aggression * 2f); // Easy 1, Medium 1, Hard 2
            if (!ctx.Catalog.rules.aiExpands) { d.ExpandNow = false; d.ExpandReason = "expansion is off"; Status = d.ExpandReason; return; }
            if (d.Stance == AiStance.Defend) { d.ExpandNow = false; d.ExpandReason = "defending first"; Status = d.ExpandReason; return; }
            if (d.ExpansionCell < 0) { d.ExpandNow = false; d.ExpandReason = "nothing worth claiming"; Status = d.ExpandReason; return; }
            // A cautious AI first saves for the Fort zone that will hold the new cell.
            int fortZone = 0;
            foreach (var def in ctx.Catalog.buildings) if (def.kind == BuildingKind.ConstructionZone) fortZone = def.cost;
            if (p.Level < 1 && ctx.Faction.currency < fortZone * 2)
            {
                d.ExpandNow = false;
                d.ExpandReason = "saving to hold a new cell";
                Status = d.ExpandReason;
                return;
            }
            d.ExpandNow = true;
            d.ExpandReason = "claim " + d.ExpansionReason;
            Status = d.ExpandReason;
        }
    }
}
