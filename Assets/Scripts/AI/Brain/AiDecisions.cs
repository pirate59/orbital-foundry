namespace OrbitalFoundry
{
    /// <summary>The AI's broad intent, set by the Grand Strategy skill.</summary>
    public enum AiStance
    {
        /// <summary>Grow the factory; defend what it holds.</summary>
        Build,
        /// <summary>A cell is threatened beyond its defence: ships and money go to holding it.</summary>
        Defend,
        /// <summary>Take more ground for the economy.</summary>
        Expand,
        /// <summary>Strong enough to take the player's cells.</summary>
        Attack,
        /// <summary>The player is close to a trade win: hit their economy.</summary>
        Counter
    }

    /// <summary>
    /// The blackboard the skills write and the controller and planners read: stance, budget and targets. Runtime only.
    /// Each decision carries a short reason, for the debug readout.
    /// </summary>
    public sealed class AiDecisions
    {
        public AiStance Stance = AiStance.Build;
        public string StanceReason = "starting out";
        // A stance waiting out the profile's reaction time before it takes effect.
        public AiStance PendingStance = AiStance.Build;
        public int PendingSinceTick = -1;

        // Budget (the Budget skill).
        /// <summary>Whether research may be bought now.</summary>
        public bool ResearchAllowed = true;
        /// <summary>Credits kept in hand when ordering ships.</summary>
        public int CraftReserve = 120;
        /// <summary>Fleet size wanted in a military game.</summary>
        public int MaxCraft = 6;
        /// <summary>Raid now, whatever the raid timer says (attacking or countering).</summary>
        public bool RaidNow;
        public string BudgetReason = "";

        // Targets (the targeting skills). -1 when there is none worth taking.
        public int ExpansionCell = -1;
        public string ExpansionReason = "";
        public int AttackCell = -1;
        public float AttackEase;
        /// <summary>More ships it needs before the planned attack is one it would win (0: ready, or no plan).</summary>
        public int AttackShipsNeeded;
        /// <summary>The ship type that is the cheapest way to make the planned strike ready ("" for none).</summary>
        public string StrikeBuildType = "";
        /// <summary>The industry gate's last reckoning (could it lose the attack fleet and recover in time?).</summary>
        public string IndustryReason = "";
        public string AttackReason = "";

        // Attack fleet (the Attack Fleet skill): the ships picked to strike, where they gather, and whether to go.
        public readonly System.Collections.Generic.List<int> StrikeGroup = new System.Collections.Generic.List<int>();
        public int RallyCell = -1;
        /// <summary>Strength still missing for the strike (0 when the group is strong enough).</summary>
        public int StrikeShortfall;
        public bool StrikeAssembled, LaunchNow;
        public string StrikeReason = "";

        // Fleet composition (the Fleet Composition skill): ship types to order next, best first.
        public readonly System.Collections.Generic.List<string> BuildOrder = new System.Collections.Generic.List<string>();
        public string BuildReason = "";

        // Defence posture (the Defence Posture skill): its cells, most worth guarding first, and where reserves gather.
        public readonly System.Collections.Generic.List<int> GuardOrder = new System.Collections.Generic.List<int>();
        public int ReserveCell = -1;

        // Economy (the Needs, Product Selection, Research Goals, Bottleneck and Power skills).
        public readonly System.Collections.Generic.List<string> ProductOrder = new System.Collections.Generic.List<string>();
        public string ProductReason = "";
        public string ResearchTarget = "", ResearchReason = "";
        public int IonWanted, PowerSpare;
        public readonly System.Collections.Generic.HashSet<string> MissingOres = new System.Collections.Generic.HashSet<string>();
        public bool PartsShort;
        public string NeedsReason = "";
        public int LowPowerMachines, StarvedMachines, BackedUpMachines;
        public bool ReplanNow;
        public string ReplanReason = "", BottleneckReason = "";
        public int PowerMarginMw;
        public bool PowerFirst;
        /// <summary>Demand is well over supply: hold off new product machines until power catches up.</summary>
        public bool HoldNewMachines;
        // Solar supply chain: delivery priorities to set now, and why.
        public readonly System.Collections.Generic.List<GameCommand> PriorityCommands = new System.Collections.Generic.List<GameCommand>();
        public string SolarReason = "";
        public string PowerReason = "";

        // Growth (the Throughput Balancing and Upgrade vs Expand skills).
        /// <summary>AI machines working flat out: the planner builds more beside them rather than loading them further.</summary>
        public readonly System.Collections.Generic.HashSet<int> Saturated = new System.Collections.Generic.HashSet<int>();
        public string ThroughputReason = "";
        /// <summary>The asteroid the next new chain starts on (-1: the planner's own choice, its headquarters).</summary>
        public int NewChainCell = -1;
        // Cross-platform supply: asteroids that refine flown-in ore for chains elsewhere.
        public readonly System.Collections.Generic.HashSet<int> SmeltCells = new System.Collections.Generic.HashSet<int>();
        public string SupplyReason = "";
        /// <summary>The ready strike's simulated battle (combat readiness): rounds to win and health kept.</summary>
        public AiEngagement.Result StrikeOdds;
        public readonly System.Collections.Generic.List<GameCommand> SupplyCommands = new System.Collections.Generic.List<GameCommand>();
        public string GrowthReason = "";

        // Logistics (the Belt Upgrades skill): a belt whose line to upgrade now (-1 for none).
        public int UpgradeBeltId = -1;
        public string BeltReason = "";

        // Layout (the Belt Refactoring and Sale Depot Management skills): orders to carry out between plans.
        public readonly System.Collections.Generic.List<GameCommand> RefactorCommands = new System.Collections.Generic.List<GameCommand>();
        public string RefactorReason = "";
        public readonly System.Collections.Generic.List<GameCommand> OutletCommands = new System.Collections.Generic.List<GameCommand>();
        public string OutletReason = "";
        // Belt style: a mixed line untangled (dedicated) or an overflow depot for a shared one (sushi).
        public readonly System.Collections.Generic.List<GameCommand> BeltStyleCommands = new System.Collections.Generic.List<GameCommand>();
        public string BeltStyleReason = "";

        public readonly System.Collections.Generic.List<GameCommand> HubCommands = new System.Collections.Generic.List<GameCommand>();
        public string HubReason = "";
        public readonly System.Collections.Generic.List<GameCommand> RelayoutCommands = new System.Collections.Generic.List<GameCommand>();
        public string RelayoutReason = "";
        public string PriorityReason = "";
        // Storage management: accept-list changes, carried out at once like priorities (free and instant).
        public readonly System.Collections.Generic.List<GameCommand> InstantCommands = new System.Collections.Generic.List<GameCommand>();
        public string StorageReason = "";

        // Losses (the Rebuild after Loss skill): cells it lost with machines on them, and how many.
        public readonly System.Collections.Generic.Dictionary<int, int> LostCells = new System.Collections.Generic.Dictionary<int, int>();
        public string LostReason = "";

        // Expansion pacing (the Expansion Pacing skill).
        public bool ExpandNow;
        public int MaxExpansions = 1;
        public string ExpandReason = "";
    }
}
