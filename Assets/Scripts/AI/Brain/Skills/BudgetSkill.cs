namespace OrbitalFoundry
{
    /// <summary>
    /// Goal Arbitration / Budget: turns the stance into spending rules the controller and planner follow. Whether
    /// research may be bought, how many credits stay in hand when ordering ships, how big a fleet to keep, and whether
    /// to raid now. Defending and countering put ships first; building puts the factory and research first.
    /// </summary>
    public sealed class BudgetSkill : IAiSkill
    {
        public const int BaseCraftReserve = 120;
        /// <summary>Credits above which research goes on whatever the stance: there is more than ships can use.</summary>
        public const int PlentyOfCredits = 2000;

        public string Name { get { return "Budget"; } }
        public AiSkillArea Area { get { return AiSkillArea.Strategy; } }
        public string Status { get; private set; }

        public int EveryThinks(AiContext ctx) { return ctx.Profile.AssessEveryThinks; } // follows the stance

        public void Think(AiContext ctx)
        {
            var d = ctx.Decisions;
            var p = ctx.Profile;
            var r = ctx.Catalog.rules;
            bool military = r.aiExpands || r.aiRaids;
            int cells = ctx.Model.AiCells;
            // In a military game the profile's fleet is what it can strike with: one ship per cell held stays on guard
            // on top of it. (A fleet the size of its garrison never had a ship to spare for an attack.)
            int fleet = military ? p.MaxCraft + cells : p.MaxCraft;
            switch (d.Stance)
            {
                case AiStance.Defend:
                    // Ships before research (unless money is piling up anyway); spend down to a small reserve; a fleet
                    // big enough to hold every cell.
                    d.ResearchAllowed = ctx.Faction.currency > PlentyOfCredits;
                    d.CraftReserve = BaseCraftReserve / 2;
                    d.MaxCraft = military ? fleet : System.Math.Min(p.MaxCraft, System.Math.Max(cells, 1) + 1);
                    d.RaidNow = false;
                    d.BudgetReason = "budget: ships first to hold cell " + ctx.Model.MostThreatenedCell;
                    break;
                case AiStance.Counter:
                    d.ResearchAllowed = false;
                    d.CraftReserve = BaseCraftReserve / 2;
                    d.MaxCraft = fleet;
                    d.RaidNow = r.aiRaids;
                    d.BudgetReason = "budget: everything into raiding the player's economy";
                    break;
                case AiStance.Attack:
                    d.ResearchAllowed = ctx.Faction.currency > 2 * BaseCraftReserve;
                    d.CraftReserve = BaseCraftReserve;
                    d.MaxCraft = fleet;
                    d.RaidNow = false; // raids keep to the (shortened) raid timer
                    d.BudgetReason = "budget: fleet and attacks, research with what is left";
                    break;
                default:
                    // Build and Expand: the factory and research come first; ships only as the rules call for them.
                    d.ResearchAllowed = true;
                    d.CraftReserve = BaseCraftReserve;
                    d.MaxCraft = military ? fleet : System.Math.Min(p.MaxCraft, cells);
                    d.RaidNow = false;
                    d.BudgetReason = "budget: factory and research first";
                    break;
            }
            // A strike that is short of strength asks for one more ship, within the profile's fleet size.
            if (d.StrikeShortfall > 0 && military)
            {
                d.MaxCraft = System.Math.Min(fleet, System.Math.Max(d.MaxCraft, ctx.Model.AiCraft + 1));
                d.BudgetReason += "; building up the strike";
            }
            Status = d.BudgetReason;
        }
    }
}
